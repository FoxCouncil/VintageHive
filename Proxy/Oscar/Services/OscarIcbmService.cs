// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

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
                var msgIdCookie = OscarUtils.ToUInt64(snac.RawData[..8]);
                var msgChannel = OscarUtils.ToUInt16(snac.RawData[8..10]);

                var screenNameLength = (ushort)snac.RawData[10];
                var screenName = Encoding.ASCII.GetString(snac.RawData[11..(11 + screenNameLength)]);

                var tlvData = snac.RawData[(11 + screenNameLength)..];
                var tlvs = OscarUtils.DecodeTlvs(tlvData);

                var userSession = OscarServer.Sessions.GetByScreenName(screenName);

                if (userSession == null)
                {
                    // User is offline — store for offline delivery (channel 1 only)
                    if (msgChannel == 1 && Mind.Db.UserExistsByUsername(screenName))
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
                        sendClientMessageSnac.WriteTlv(new Tlv(0x02, tlvs.GetTlv(0x02).Value));
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

                if (targetSession != null)
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
