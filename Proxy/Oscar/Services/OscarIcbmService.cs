// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;
using VintageHive.Proxy.Chat;

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarIcbmService : IOscarService
{
    public const ushort FAMILY_ID = 0x04;

    public const ushort CLI_SRV_ERROR = 0x01;
    public const ushort CLI_SET_ICBM_PARAMS = 0x02;
    public const ushort CLI_ICBM_PARAM_REQ = 0x04;
    public const ushort SRV_ICBM_PARAMS = 0x05;
    public const ushort CLI_SEND_ICBM = 0x06;
    public const ushort SRV_CLIENT_ICBM = 0x07;
    public const ushort CLI_EVIL_REQUEST = 0x08;
    public const ushort SRV_EVIL_REPLY = 0x09;
    public const ushort SRV_MISSED_CALLS = 0x0A;
    public const ushort CLI_CLIENT_ERR = 0x0B;
    public const ushort SRV_MSG_ACK = 0x0C;
    public const ushort CLI_MTN = 0x14;
    public const ushort SRV_MTN = 0x14;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarIcbmService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_SET_ICBM_PARAMS:
            {
                var channelId = OscarUtils.ToUInt16(snac.RawData[..2]);
                var messageFlags = OscarUtils.ToUInt32(snac.RawData[2..6]);
                var maxMessageSnacSize = OscarUtils.ToUInt16(snac.RawData[6..8]);
                var maxSenderWarningLevel = OscarUtils.ToUInt16(snac.RawData[8..10]);
                var maxReceiverWarningLevel = OscarUtils.ToUInt16(snac.RawData[10..12]);
                var minimumMessageIntervalSecs = OscarUtils.ToUInt16(snac.RawData[12..14]);

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcbmService), $"ICBM params set: ch={channelId} flags=0x{messageFlags:X} maxSize={maxMessageSnacSize}", traceId);
            }
            break;

            case CLI_ICBM_PARAM_REQ:
            {
                var icbmParamsReply = snac.NewReply(Family, SRV_ICBM_PARAMS);

                icbmParamsReply.WriteUInt16(0);     // Channel
                icbmParamsReply.WriteUInt32(0x0B);  // Flags: IM + missed calls + typing notifications
                icbmParamsReply.WriteUInt16(8000);  // Max SNAC size
                icbmParamsReply.WriteUInt16(999);   // Max sender warning
                icbmParamsReply.WriteUInt16(999);   // Max receiver warning
                icbmParamsReply.WriteUInt16(0);     // Min message interval
                icbmParamsReply.WriteUInt16(1000);  // Unknown

                await session.SendSnac(icbmParamsReply);
            }
            break;

            case CLI_SEND_ICBM:
            {
                // Every slice below is driven by a client-supplied length. The guarded siblings
                // (OscarBuddyListService.ParseBuddyList, IcqUserMetaRequest) bounds-check first; this one did
                // not, so a truncated frame threw ArgumentOutOfRangeException into the per-SNAC catch and the
                // request disappeared with only a log line to show for it.
                if (snac.RawData.Length < 11)
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcbmService), "Dropped a truncated CLI_SEND_ICBM (shorter than its fixed header)", traceId);

                    return;
                }

                var msgIdCookie = OscarUtils.ToUInt64(snac.RawData[..8]);
                var msgChannel = OscarUtils.ToUInt16(snac.RawData[8..10]);

                var screenNameLength = (ushort)snac.RawData[10];

                if (snac.RawData.Length < 11 + screenNameLength)
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcbmService), "Dropped a CLI_SEND_ICBM whose screen-name length ran past the frame", traceId);

                    return;
                }

                var screenName = Encoding.ASCII.GetString(snac.RawData[11..(11 + screenNameLength)]);

                var tlvData = snac.RawData[(11 + screenNameLength)..];
                var tlvs = OscarUtils.DecodeTlvs(tlvData);

                var userSession = OscarServer.Sessions.GetByScreenName(screenName);

                if (userSession == null)
                {
                    // An embedder-registered service name answers here, before the unknown-recipient handling.
                    // Channel 1 only: there is nothing to rendezvous with. Replies are attributed to the name
                    // as the CLIENT wrote it so its IM window threads them.
                    var serviceReplies = msgChannel == 1
                        ? await ChatServiceRegistry.TryHandleAsync(screenName, "OSCAR", session.ScreenName, ExtractPlainText(tlvs))
                        : null;

                    if (serviceReplies != null)
                    {
                        // ACK the send like a normal delivery so the sender's client considers it sent.
                        var serviceAckSnac = new Snac(Family, SRV_MSG_ACK);

                        serviceAckSnac.WriteUInt64(msgIdCookie);
                        serviceAckSnac.WriteUInt16(msgChannel);

                        serviceAckSnac.WriteUInt8((byte)screenName.Length);
                        serviceAckSnac.WriteString(screenName);

                        await session.SendSnac(serviceAckSnac);

                        foreach (var line in serviceReplies)
                        {
                            await SendServiceReply(session, screenName, line);
                        }
                    }
                    // User is offline - store for offline delivery (channel 1 only). A configured numeric
                    // alias is a valid recipient too: the store stays keyed on the presented number, which
                    // is the name the owning session signs on with and flushes.
                    else if (msgChannel == 1 && (Mind.Db.UserExistsByUsername(screenName) || Mind.Db.UserResolveAlias(screenName) != null))
                    {
                        // Store the message TLVs for later delivery
                        var messageTlv = tlvs.GetTlv(0x02);

                        if (messageTlv != null)
                        {
                            var storedTlvData = new Tlv(0x02, messageTlv.Value).Encode();

                            Mind.Db.OscarStoreOfflineMessage(session.ScreenName, screenName, msgChannel, storedTlvData);

                            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarIcbmService), $"Stored offline message from {session.ScreenName} to {screenName}", traceId);

                            // Still send ACK to sender
                            var ackSnac = new Snac(Family, SRV_MSG_ACK);

                            ackSnac.WriteUInt64(msgIdCookie);
                            ackSnac.WriteUInt16(msgChannel);

                            ackSnac.WriteUInt8((byte)screenName.Length);
                            ackSnac.WriteString(screenName);

                            await session.SendSnac(ackSnac);
                        }
                    }
                    else
                    {
                        var notFoundSnac = snac.NewReply(Family, CLI_SRV_ERROR);

                        notFoundSnac.WriteUInt16(0x0004); // Not logged on

                        await session.SendSnac(notFoundSnac);
                    }
                }
                else if (!userSession.IsVisibleTo(session.ScreenName))
                {
                    // Privacy list: the recipient has this sender denied or not permitted. AIM convention is that a
                    // blocked user appears offline to the blocked party, so reply "not logged on" and deliver nothing.
                    var blockedSnac = snac.NewReply(Family, CLI_SRV_ERROR);

                    blockedSnac.WriteUInt16(0x0004); // Not logged on

                    await session.SendSnac(blockedSnac);
                }
                else
                {
                    var sendClientMessageSnac = new Snac(Family, SRV_CLIENT_ICBM);

                    sendClientMessageSnac.WriteUInt64(msgIdCookie);
                    sendClientMessageSnac.WriteUInt16(msgChannel);

                    sendClientMessageSnac.WriteUInt8((byte)session.ScreenName.Length);
                    sendClientMessageSnac.WriteString(session.ScreenName);

                    sendClientMessageSnac.WriteUInt16(session.WarningLevel);

                    var sendTlvs = new List<Tlv>
                    {
                        new Tlv(0x01, OscarUtils.GetBytes(0)),
                        new Tlv(0x06, OscarUtils.GetBytes((uint)session.Status)),
                        new Tlv(0x0F, OscarUtils.GetBytes((uint)session.SignOnTime.ToUnixTimeSeconds())),
                        new Tlv(0x03, OscarUtils.GetBytes((uint)OscarServer.ServerTime.ToUnixTimeSeconds()))
                    };

                    sendClientMessageSnac.WriteUInt16((ushort)sendTlvs.Count);

                    foreach (Tlv tlv in sendTlvs)
                    {
                        sendClientMessageSnac.Write(tlv.Encode());
                    }

                    if (msgChannel == 1)
                    {
                        // The offline-store path already null-checks this TLV; the live path did not, so a
                        // channel-1 ICBM that omitted the message block threw an NRE that the per-SNAC catch
                        // swallowed - the IM vanished and neither party was told anything.
                        var liveMessageTlv = tlvs.GetTlv(0x02);

                        if (liveMessageTlv?.Value == null)
                        {
                            Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcbmService), "Dropped a channel-1 ICBM carrying no message TLV (0x02)", traceId);

                            return;
                        }

                        sendClientMessageSnac.WriteTlv(new Tlv(0x02, liveMessageTlv.Value));
                    }
                    else if (msgChannel == 2)
                    {
                        // Forward rendezvous data (file transfer, direct connect, etc.)
                        var rendezvousTlv = tlvs.GetTlv(0x05);

                        if (rendezvousTlv != null)
                        {
                            sendClientMessageSnac.WriteTlv(new Tlv(0x05, rendezvousTlv.Value));
                        }
                    }

                    await userSession.SendSnac(sendClientMessageSnac);

                    // Send ACK to sender
                    var ackSentMessageSnac = new Snac(Family, SRV_MSG_ACK);

                    ackSentMessageSnac.WriteUInt64(msgIdCookie);
                    ackSentMessageSnac.WriteUInt16(msgChannel);

                    ackSentMessageSnac.WriteUInt8((byte)screenName.Length);
                    ackSentMessageSnac.WriteString(screenName);

                    await session.SendSnac(ackSentMessageSnac);
                }
            }
            break;

            case CLI_EVIL_REQUEST:
            {
                await ProcessWarning(session, snac);
            }
            break;

            case CLI_MTN:
            {
                // Typing notification: channel(2) + screenname_len(1) + screenname + event(2)
                var channel = OscarUtils.ToUInt16(snac.RawData[..2]);
                var targetNameLength = snac.RawData[2];
                var targetName = Encoding.ASCII.GetString(snac.RawData[3..(3 + targetNameLength)]);
                var typingEvent = OscarUtils.ToUInt16(snac.RawData[(3 + targetNameLength)..(5 + targetNameLength)]);

                var targetSession = OscarServer.Sessions.GetByScreenName(targetName);

                // Privacy list: don't leak typing activity to a target who has this sender denied/not permitted.
                if (targetSession != null && targetSession.IsVisibleTo(session.ScreenName))
                {
                    // Forward typing notification to target
                    var mtnSnac = new Snac(Family, SRV_MTN);

                    mtnSnac.WriteUInt16(channel);
                    mtnSnac.WriteUInt8((byte)session.ScreenName.Length);
                    mtnSnac.WriteString(session.ScreenName);
                    mtnSnac.WriteUInt16(typingEvent);

                    await targetSession.SendSnac(mtnSnac);
                }
            }
            break;

            case CLI_CLIENT_ERR:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcbmService), "Client error report received", traceId);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcbmService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }

    // Keeps a service reply's message fragment within the ICBM budget this server advertises (8000-byte max
    // SNAC size), with headroom for the header and sender TLVs. Also what keeps the fragment's 16-bit length
    // field honest for any text the embedder hands back.
    private const int MaxServiceReplyChars = 2000;

    // Delivers one service reply line to the sender as a fresh channel-1 ICBM from the service name,
    // mirroring the live-relay delivery shape above (and the offline flush in OscarGenericServiceControls).
    private static async Task SendServiceReply(OscarSession session, string fromName, string text)
    {
        if (text.Length > MaxServiceReplyChars)
        {
            text = text[..MaxServiceReplyChars];
        }

        var replySnac = new Snac(FAMILY_ID, SRV_CLIENT_ICBM);

        replySnac.WriteUInt64((ulong)Random.Shared.NextInt64());
        replySnac.WriteUInt16(1); // Channel 1: plain IM

        replySnac.WriteUInt8((byte)fromName.Length);
        replySnac.WriteString(fromName);

        replySnac.WriteUInt16(0); // Warning level

        var senderTlvs = new List<Tlv>
        {
            new Tlv(0x01, OscarUtils.GetBytes(0)),
            new Tlv(0x06, OscarUtils.GetBytes((uint)OscarSessionOnlineStatus.Online)),
            new Tlv(0x0F, OscarUtils.GetBytes((uint)0)),
            new Tlv(0x03, OscarUtils.GetBytes((uint)OscarServer.ServerTime.ToUnixTimeSeconds()))
        };

        replySnac.WriteUInt16((ushort)senderTlvs.Count);

        foreach (Tlv tlv in senderTlvs)
        {
            replySnac.Write(tlv.Encode());
        }

        replySnac.WriteTlv(BuildMessageBlockTlv(text));

        await session.SendSnac(replySnac);
    }

    // The channel-1 message TLV (0x02) is a run of fragments: id(1) + version(1) + length(2, BE) + payload.
    // Fragment 0x05 carries capabilities, fragment 0x01 the message block: charset(2) + subset(2) + text.
    private static Tlv BuildMessageBlockTlv(string text)
    {
        var isAscii = text.All(c => c < 0x80);

        // Charset 0 (ASCII) when possible, UCS-2BE (charset 2) otherwise - period clients handle both.
        var charset = (ushort)(isAscii ? 0 : 2);
        var textBytes = isAscii ? Encoding.ASCII.GetBytes(text) : Encoding.BigEndianUnicode.GetBytes(text);

        using var value = new MemoryStream();

        // Fragment 0x05 v1: the bare "plain text" capability.
        value.Write(new byte[] { 0x05, 0x01, 0x00, 0x01, 0x01 });

        var blockLength = (ushort)(4 + textBytes.Length);

        value.Write(new byte[] { 0x01, 0x01, (byte)(blockLength >> 8), (byte)blockLength });
        value.Write(new byte[] { (byte)(charset >> 8), (byte)charset, 0x00, 0x00 });
        value.Write(textBytes);

        return new Tlv(0x02, value.ToArray());
    }

    // What the member actually typed, out of the fragment structure and stripped of the HTML wrapper AIM
    // clients put around every message - a service handler should compare against "help", not
    // "<HTML><BODY>help</BODY></HTML>".
    internal static string ExtractPlainText(Tlv[] tlvs)
    {
        var messageTlv = tlvs.GetTlv(0x02);

        if (messageTlv == null)
        {
            return string.Empty;
        }

        var block = messageTlv.Value;
        var text = new StringBuilder();

        var offset = 0;

        while (offset + 4 <= block.Length)
        {
            var fragmentId = block[offset];
            var fragmentLength = OscarUtils.ToUInt16(block[(offset + 2)..(offset + 4)]);

            offset += 4;

            if (offset + fragmentLength > block.Length)
            {
                break;
            }

            if (fragmentId == 0x01 && fragmentLength >= 4)
            {
                var charset = OscarUtils.ToUInt16(block[offset..(offset + 2)]);
                var textBytes = block[(offset + 4)..(offset + fragmentLength)];

                // 0 = ASCII (decoded as UTF-8, a safe superset), 2 = UCS-2BE, 3 = ISO-8859-1.
                text.Append(charset switch
                {
                    2 => Encoding.BigEndianUnicode.GetString(textBytes),
                    3 => Encoding.Latin1.GetString(textBytes),
                    _ => Encoding.UTF8.GetString(textBytes),
                });
            }

            offset += fragmentLength;
        }

        return WebUtility.HtmlDecode(Regex.Replace(text.ToString(), "<[^>]*>", string.Empty));
    }

    private async Task ProcessWarning(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        var isAnonymous = OscarUtils.ToUInt16(snac.RawData[..2]) == 0x01;

        var screenNameLength = (ushort)snac.RawData[2];
        var screenName = Encoding.ASCII.GetString(snac.RawData[3..(3 + screenNameLength)]);

        var targetSession = OscarServer.Sessions.GetByScreenName(screenName);

        if (targetSession == null)
        {
            var errorSnac = snac.NewReply(Family, CLI_SRV_ERROR);

            errorSnac.WriteUInt16(0x0004); // Not logged on

            await session.SendSnac(errorSnac);

            return;
        }

        // Privacy list: a sender the target has denied/not-permitted cannot warn them - treat as not available.
        if (!targetSession.IsVisibleTo(session.ScreenName))
        {
            var blockedSnac = snac.NewReply(Family, CLI_SRV_ERROR);

            blockedSnac.WriteUInt16(0x0004); // Not logged on

            await session.SendSnac(blockedSnac);

            return;
        }

        // Apply warning to target
        targetSession.ApplyWarning(isAnonymous);

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarIcbmService), $"{session.ScreenName} warned {screenName} ({(isAnonymous ? "anonymous" : "normal")}), new level: {targetSession.WarningLevel}", traceId);

        // Send reply to warner with new warning level
        var replySnac = snac.NewReply(Family, SRV_EVIL_REPLY);

        replySnac.WriteUInt16(targetSession.WarningLevel);

        await session.SendSnac(replySnac);

        // Notify target of being warned
        var targetSnac = new Snac(Family, SRV_EVIL_REPLY);

        targetSnac.WriteUInt16(targetSession.WarningLevel);

        if (!isAnonymous)
        {
            targetSnac.WriteUInt8((byte)session.ScreenName.Length);
            targetSnac.WriteString(session.ScreenName);
        }

        await targetSession.SendSnac(targetSnac);

        // Broadcast updated warning level to watchers
        await targetSession.BroadcastStatusToWatchers();
    }
}
