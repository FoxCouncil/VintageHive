// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Security.Cryptography;
using VintageHive.Network;
using VintageHive.Proxy.Chat;
using VintageHive.Proxy.Presence;

namespace VintageHive.Proxy.Yahoo;

// A self-hosted Yahoo! Messenger (YMSG) server for period YM 5.x clients: login, presence, and 1:1 IM.
// VintageHive is the whole auth server, so it mints the challenge and verifies the client's answer to it
// against the stored password - see YmsgCrypt for the v9 "0x0b" crypt and the login handler for the checks.
//
// This class owns the YMSG wire only. Who is signed on, who supersedes whom, and where a message goes are
// YahooSessionRegistry's, shared with every other Yahoo! transport.
public sealed class YmsgServer : Listener
{
    // The shared table, exposed under its historical name. Not a YMSG-private table: an HTTP Pager session
    // for the same account lands in here too, and supersedes this one.
    public static ConcurrentDictionary<uint, YahooSession> Sessions => YahooSessionRegistry.Sessions;

    const int MaxBodyBytes = 65535;

    // Cap relayed IM text so an encoded packet body cannot exceed the 16-bit YMSG length field.
    const int MaxMessageChars = 16000;

    public YmsgServer(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, false) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var traceId = connection.TraceId.ToString();
        var remoteAddress = connection.RemoteAddress;

        Log.WriteLine(Log.LEVEL_INFO, nameof(YmsgServer), $"Client connected from {remoteAddress}", traceId);

        var session = new YmsgSession(connection);

        YahooSessionRegistry.Add(session);

        try
        {
            while (connection.IsConnected)
            {
                var packet = await ReadPacketAsync(connection);

                if (packet == null)
                {
                    break;
                }

                session.Version = packet.Version == 0 ? session.Version : packet.Version;

                await HandlePacketAsync(session, packet, traceId);
            }
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(YmsgServer), ex, traceId);
        }
        finally
        {
            YahooSessionRegistry.Remove(session);

            if (session.IsAuthenticated)
            {
                // A superseding duplicate login may already own this username - possibly on another
                // transport; announcing a logoff then would tell peers the (still-online) user left.
                if (YahooSessionRegistry.GetByUsername(session.Username) == null)
                {
                    await YahooSessionRegistry.BroadcastLogoffAsync(session);
                }

                Mind.Db?.RequestsTrack(connection, "N/A", "YMSG", $"logoff {session.Username}", nameof(YmsgServer));
            }

            Log.WriteLine(Log.LEVEL_INFO, nameof(YmsgServer), $"Client disconnected from {remoteAddress}", traceId);
        }

        return null;
    }

    static async Task<YmsgPacket> ReadPacketAsync(ListenerSocket connection)
    {
        var header = new byte[YmsgPacket.HeaderSize];

        if (!await ReadExactAsync(connection.Stream, header, header.Length))
        {
            return null;
        }

        if (!YmsgPacket.HasMagic(header))
        {
            return null;
        }

        var bodyLength = YmsgPacket.BodyLength(header);

        if (bodyLength > MaxBodyBytes)
        {
            return null;
        }

        var body = new byte[bodyLength];

        if (bodyLength > 0 && !await ReadExactAsync(connection.Stream, body, bodyLength))
        {
            return null;
        }

        return YmsgPacket.Decode(header, body);
    }

    static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;

        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));

            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    async Task HandlePacketAsync(YmsgSession session, YmsgPacket packet, string traceId)
    {
        switch (packet.Service)
        {
            case YmsgService.Verify:
            {
                // Echo the handshake so YM 5.5+ clients proceed to AUTH.
                await session.SendAsync(new YmsgPacket(YmsgService.Verify, 0, session.SessionId));
            }
            break;

            case YmsgService.Auth:
            {
                await HandleAuthAsync(session, packet);
            }
            break;

            case YmsgService.AuthResp:
            {
                await HandleAuthRespAsync(session, packet, traceId);
            }
            break;

            case YmsgService.Message:
            {
                await HandleMessageAsync(session, packet, traceId);
            }
            break;

            case YmsgService.IsAway:
            case YmsgService.IsBack:
            {
                await HandleStatusChangeAsync(session, packet);
            }
            break;

            case YmsgService.Logoff:
            {
                // Client is signing off; the finally block broadcasts departure.
                session.Client.RawSocket.Close();
            }
            break;

            case YmsgService.Ping:
            {
                await session.SendAsync(new YmsgPacket(YmsgService.Ping, 0, session.SessionId));
            }
            break;

            case YmsgService.KeepAlive:
            {
                // No response required.
            }
            break;

            case YmsgService.Notify:
            {
                await HandleNotifyAsync(session, packet);
            }
            break;

            case YmsgService.AddBuddy:
            case YmsgService.RemoveBuddy:
            {
                await HandleBuddyEditAsync(session, packet);
            }
            break;

            case YmsgService.UserStat:
            {
                // Period clients only consume UserStat when it arrives server-sent alongside presence
                // packets; no client-initiated semantics are documented, so this is a deliberate no-op
                // rather than an unknown service.
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(YmsgServer), $"Unhandled service 0x{(ushort)packet.Service:X2}", traceId);
            }
            break;
        }
    }

    async Task HandleAuthAsync(YmsgSession session, YmsgPacket packet)
    {
        var username = packet.Get(1) ?? packet.Get(0);

        session.Username = username;

        // Field 13 = "1" tells the client to answer with the v9 "0x0b" crypt, which we verify in AUTHRESP - so
        // the seed's content is load-bearing twice over. It must be drawn from the crypt's lookup alphabets (a
        // client fed anything else spins forever, because its parser does not advance on an unknown character),
        // and it must embed a (depth, table) pair the client's MD5 search actually finds - a real Messenger 5.x
        // answers a seed that fails that search with empty crypt fields, which reads here as a bad password.
        var seed = MakeChallenge();

        session.ChallengeSeed = seed;
        session.Challenge = YmsgCrypt.PrepareChallenge(seed);

        var response = new YmsgPacket(YmsgService.Auth, 0, session.SessionId)
            .Add(1, username)
            .Add(94, seed)
            .Add(13, "1");

        await session.SendAsync(response);
    }

    async Task HandleAuthRespAsync(YmsgSession session, YmsgPacket packet, string traceId)
    {
        var username = packet.Get(0) ?? packet.Get(1) ?? session.Username;

        if (string.IsNullOrEmpty(username) || Mind.Db?.UserExistsByUsername(username) != true)
        {
            var error = new YmsgPacket(YmsgService.AuthResp, YmsgStatus.LoginError, session.SessionId)
                .Add(66, "3"); // 3 = bad username

            await session.SendAsync(error);

            return;
        }

        // Verify the crypt response against the stored password. This used to accept ANY response for a known
        // account, which meant anyone who could reach the port could sign in as any member - OSCAR was cited as
        // the precedent for skipping it, but OscarServer.ProcessChannelOneAuth has always compared the roasted
        // password, so it was the counter-example rather than the precedent.
        if (!IsAuthResponseValid(session, packet, username, traceId))
        {
            var error = new YmsgPacket(YmsgService.AuthResp, YmsgStatus.LoginError, session.SessionId)
                .Add(66, "13"); // 13 = bad password

            await session.SendAsync(error);

            return;
        }

        // A duplicate login supersedes the prior session (mirrors MsnServer), and "prior session" spans
        // transports: an HTTP Pager sign-on for this account is evicted here exactly like another YMSG socket.
        // Marking this session authenticated and evicting the ghost happen atomically inside the registry, and
        // the (bounded) notify/close sends run after the gate releases so a stalled ghost never blocks other
        // logins. The evicted session's teardown then sees a live owner for the username and skips its
        // logoff broadcast.
        var superseded = await YahooSessionRegistry.ClaimIdentityAsync(session, username);

        await YahooSessionRegistry.SupersedeAllAsync(superseded);

        Mind.Db?.RequestsTrack(session.Client, "N/A", "YMSG", $"logon {username}", nameof(YmsgServer));

        await SendListAsync(session);
        await SendInitialPresenceAsync(session);
        await YahooSessionRegistry.BroadcastPresenceAsync(session);
        await DeliverOfflineMessagesAsync(session);
    }

    async Task SendListAsync(YmsgSession session)
    {
        var others = YahooSessionRegistry.OtherUsernames(session.Username);

        var list = new YmsgPacket(YmsgService.List, 0, session.SessionId)
            .Add(87, BuildRosterField(others))
            .Add(88, string.Empty)
            .Add(89, session.Username)
            .Add(59, "Y\tv=1;\nT\tv=1;\n")
            .Add(0, session.Username);

        await session.SendAsync(list);
    }

    async Task SendInitialPresenceAsync(YmsgSession session)
    {
        var online = YahooSessionRegistry.Peers(session).Where(s => !YahooSessionRegistry.IsInvisibleTo(s, session)).ToList();

        var presence = new YmsgPacket(YmsgService.Logon, 0, session.SessionId)
            .Add(0, session.Username)
            .Add(8, online.Count.ToString());

        foreach (var other in online)
        {
            presence.Add(7, other.Username);
            presence.Add(10, other.YahooStatus.ToString());
            presence.Add(11, other.SessionId.ToString("X"));
            presence.Add(13, "1");
        }

        await session.SendAsync(presence);
    }

    async Task HandleStatusChangeAsync(YmsgSession session, YmsgPacket packet)
    {
        if (uint.TryParse(packet.Get(10), out var status))
        {
            session.YahooStatus = status;
        }
        else
        {
            session.YahooStatus = packet.Service == YmsgService.IsBack ? YmsgStatus.Available : YmsgStatus.BeRightBack;
        }

        session.CustomStatusMessage = packet.Get(19) ?? string.Empty;

        if (session.YahooStatus == YmsgStatus.Available)
        {
            session.IdleSince = DateTimeOffset.MinValue;
        }
        else if (session.YahooStatus == YmsgStatus.Idle)
        {
            session.IdleSince = DateTimeOffset.UtcNow;
        }

        // Going invisible must tell peers to drop the user; BroadcastPresence suppresses invisible sessions,
        // so a plain presence broadcast would leave them showing the user as still online.
        if (session.YahooStatus == YmsgStatus.Invisible)
        {
            await YahooSessionRegistry.BroadcastLogoffAsync(session);
        }
        else
        {
            await YahooSessionRegistry.BroadcastPresenceAsync(session);
        }
    }

    async Task HandleMessageAsync(YmsgSession session, YmsgPacket packet, string traceId)
    {
        if (!session.IsAuthenticated)
        {
            return;
        }

        var to = packet.Get(5);
        var text = packet.Get(14) ?? string.Empty;

        if (string.IsNullOrEmpty(to))
        {
            return;
        }

        if (text.Length > MaxMessageChars)
        {
            text = text[..MaxMessageChars];
        }

        Mind.Db?.RequestsTrack(session.Client, "N/A", "YMSG", $"msg {session.Username} -> {to}", nameof(YmsgServer));

        // The relay finds the recipient on whatever transport they are signed on with, and reports failure
        // instead of throwing, so an unreachable peer costs the sender nothing but a queued message.
        if (await YahooSessionRegistry.RelayMessageAsync(session.Username, to, text))
        {
            return;
        }

        // An embedder-registered service name ("YahooHelper") answers here, before the unknown-recipient
        // handling. Replies are attributed to the name as the CLIENT wrote it so its IM window threads them.
        var serviceReplies = await ChatServiceRegistry.TryHandleAsync(to, "Yahoo", session.Username, text);

        if (serviceReplies != null)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var line in serviceReplies)
            {
                await session.DeliverMessageAsync(to, line, timestamp, offline: false);
            }

            return;
        }

        StoreOfflineMessage(session.Username, to, text, traceId);
    }

    // Queues a message for a real account's next login; a typo'd recipient must not accrete DB rows.
    static void StoreOfflineMessage(string from, string to, string text, string traceId)
    {
        if (Mind.Db?.UserExistsByUsername(to) != true)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(YmsgServer), $"Message to unknown user {to} dropped", traceId);

            return;
        }

        Mind.Db.YahooStoreOfflineMessage(from, to, text);

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(YmsgServer), $"Message to offline user {to} queued for next login", traceId);
    }

    // Flushes queued offline messages after login, mirroring OSCAR's offline ICBM delivery. Header status
    // OfflineMessage (5) marks them as offline-delivered IMs. Deletion is by the ids actually flushed and
    // only after all sends succeed: a mid-flush disconnect leaves them queued for redelivery rather than
    // lost, and a message stored concurrently (a failed live relay falling back mid-flush) is untouched.
    //
    // Deliberately NOT routed through DeliverMessageAsync: this is the login flush, and it must let a write
    // failure propagate so the rows stay queued. The Deliver* path reports failure instead of throwing,
    // which here would look like a successful flush and delete messages nobody received.
    async Task DeliverOfflineMessagesAsync(YmsgSession session)
    {
        var messages = Mind.Db?.YahooGetOfflineMessages(session.Username);

        if (messages == null || messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            var delivery = new YmsgPacket(YmsgService.Message, YmsgStatus.OfflineMessage, session.SessionId)
                .Add(4, message.FromUsername)
                .Add(5, session.Username)
                .Add(14, message.Message)
                .Add(15, message.Timestamp.ToString())
                .Add(97, "1");

            await session.SendAsync(delivery);
        }

        Mind.Db.YahooDeleteOfflineMessages(messages.Select(m => m.Id));
    }

    // Relays a typing (or similar) notification to its target, rewriting the sender into field 4 like IM
    // delivery. Best-effort: notifications are ephemeral, so an offline or dead target just drops it.
    async Task HandleNotifyAsync(YmsgSession session, YmsgPacket packet)
    {
        if (!session.IsAuthenticated)
        {
            return;
        }

        var to = packet.Get(5);

        if (string.IsNullOrEmpty(to))
        {
            return;
        }

        await YahooSessionRegistry.RelayTypingAsync(session, to, packet.Get(49) ?? "TYPING", packet.Get(13) ?? "1", packet.Status);
    }

    // The hive roster is server-built (every account is everyone's buddy), so add/remove are acknowledged
    // with field 66 = 0 (success) to complete the client's dialog but not persisted; the next login
    // rebuilds the full roster anyway.
    async Task HandleBuddyEditAsync(YmsgSession session, YmsgPacket packet)
    {
        if (!session.IsAuthenticated)
        {
            return;
        }

        var buddy = packet.Get(7);

        if (string.IsNullOrEmpty(buddy))
        {
            return;
        }

        var ack = new YmsgPacket(packet.Service, 0, session.SessionId)
            .Add(1, session.Username)
            .Add(7, buddy)
            .Add(65, packet.Get(65) ?? "Hive")
            .Add(66, "0");

        await session.SendAsync(ack);
    }

    // The YMSG buddy roster is one string: "Group:buddy1,buddy2\nGroup2:buddy3\n". For the hive we put every
    // other registered account under a single group so any two users can see and message each other.
    internal static string BuildRosterField(IEnumerable<string> buddies)
    {
        var list = buddies.ToList();

        if (list.Count == 0)
        {
            return string.Empty;
        }

        return $"Hive:{string.Join(",", list)}\n";
    }

    internal static string MakeChallenge()
    {
        return YmsgCrypt.MakeChallenge();
    }

    // True only when the client's fields 6 and 96 match what the stored password produces for this session's
    // challenge. Every other outcome - no challenge issued, an unusable challenge, a missing field, an unknown
    // account - is a refusal. There is deliberately no branch here that accepts something it could not check.
    static bool IsAuthResponseValid(YmsgSession session, YmsgPacket packet, string username, string traceId)
    {
        if (session.Challenge == null)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(YmsgServer), $"Auth failed (no challenge issued): {username}", traceId);

            return false;
        }

        var resp6 = packet.Get(6);
        var resp96 = packet.Get(96);

        if (string.IsNullOrEmpty(resp6) || string.IsNullOrEmpty(resp96))
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(YmsgServer), $"Auth failed (no crypt response): {username}", traceId);

            return false;
        }

        var user = Mind.Db?.UserFetch(username);

        if (user == null || string.IsNullOrEmpty(user.Password))
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(YmsgServer), $"Auth failed (no stored password): {username}", traceId);

            return false;
        }

        var expected = YmsgCrypt.ComputeResponses(session.Challenge, user.Password);

        if (expected == null)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(YmsgServer), $"Auth failed (challenge not verifiable): {username}", traceId);

            return false;
        }

        // Fixed-time compare so a wrong password cannot be narrowed down by timing the reply.
        var match = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected.Resp6), Encoding.UTF8.GetBytes(resp6))
            & CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected.Resp96), Encoding.UTF8.GetBytes(resp96));

        if (!match)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(YmsgServer), $"Auth failed (bad password): {username}", traceId);

            return false;
        }

        return true;
    }

    internal static PresenceStatus MapToPresenceStatus(uint yahooStatus)
    {
        return yahooStatus switch
        {
            YmsgStatus.Available => PresenceStatus.Online,
            YmsgStatus.BeRightBack => PresenceStatus.BeRightBack,
            YmsgStatus.Busy => PresenceStatus.Busy,
            YmsgStatus.OnPhone => PresenceStatus.OnThePhone,
            YmsgStatus.OutToLunch => PresenceStatus.OutToLunch,
            YmsgStatus.Invisible => PresenceStatus.Invisible,
            YmsgStatus.Idle => PresenceStatus.Idle,
            YmsgStatus.NotAtHome or YmsgStatus.NotAtDesk or YmsgStatus.NotInOffice or YmsgStatus.OnVacation or YmsgStatus.SteppedOut => PresenceStatus.Away,
            YmsgStatus.Custom => PresenceStatus.Away,
            _ => PresenceStatus.Online
        };
    }
}
