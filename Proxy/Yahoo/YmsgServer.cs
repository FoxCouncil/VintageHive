// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using VintageHive.Network;
using VintageHive.Proxy.Presence;

namespace VintageHive.Proxy.Yahoo;

// A self-hosted Yahoo! Messenger (YMSG) server for period YM 5.x clients: login, presence, and 1:1 IM.
// VintageHive is the whole auth server (same trust model as OSCAR), so the v9/v10 challenge/response
// crypt is not reproduced; we mint a challenge, then accept any response for a username that exists in
// the shared user table. See the login handler for the exact stance.
public sealed class YmsgServer : Listener
{
    public static readonly ConcurrentDictionary<uint, YmsgSession> Sessions = new();

    // Serializes the duplicate-login supersede decision. Without it, two simultaneous logins for the same
    // account can each mark themselves authenticated, then each see the OTHER as the ghost and mutually
    // kick, leaving zero survivors.
    static readonly SemaphoreSlim AuthGate = new(1, 1);

    const int MaxBodyBytes = 65535;

    // Cap relayed IM text so an encoded packet body cannot exceed the 16-bit YMSG length field.
    const int MaxMessageChars = 16000;

    const int BroadcastWriteTimeoutMs = 5000;

    // Sends to one peer with a bounded timeout; returns false when the peer is gone or stalled so the
    // caller can fall back (offline storage) instead of letting the failure take down its own session.
    static async Task<bool> TrySendAsync(YmsgSession target, YmsgPacket packet)
    {
        try
        {
            using var cts = new CancellationTokenSource(BroadcastWriteTimeoutMs);

            await target.SendAsync(packet, cts.Token);

            return true;
        }
        catch
        {
            return false;
        }
    }

    // Sends to one peer with a bounded timeout, swallowing failures so a dead/slow peer cannot break a broadcast.
    static async Task SafeSendAsync(YmsgSession target, YmsgPacket packet)
    {
        await TrySendAsync(target, packet);
    }

    public YmsgServer(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, false) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var traceId = connection.TraceId.ToString();
        var remoteAddress = connection.RemoteAddress;

        Log.WriteLine(Log.LEVEL_INFO, nameof(YmsgServer), $"Client connected from {remoteAddress}", traceId);

        var session = new YmsgSession(connection);

        Sessions[session.SessionId] = session;

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
            Sessions.TryRemove(session.SessionId, out _);

            if (session.IsAuthenticated)
            {
                // A superseding duplicate login may already own this username; announcing a logoff then
                // would tell peers the (still-online) user left.
                if (GetByUsername(session.Username) == null)
                {
                    await BroadcastLogoffAsync(session);
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

        // Send the auth challenge. We never validate the client's crypt response, so the challenge content
        // is immaterial; the client only needs a non-empty seed in field 94.
        var response = new YmsgPacket(YmsgService.Auth, 0, session.SessionId)
            .Add(1, username)
            .Add(94, MakeChallenge(session.SessionId))
            .Add(13, "1");

        await session.SendAsync(response);
    }

    async Task HandleAuthRespAsync(YmsgSession session, YmsgPacket packet, string traceId)
    {
        var username = packet.Get(0) ?? packet.Get(1) ?? session.Username;

        // We are the auth server and cannot reproduce Yahoo's crypt, so we gate on the account existing in
        // the shared user table and accept any response for it (self-host trust model, same as OSCAR).
        if (string.IsNullOrEmpty(username) || Mind.Db?.UserExistsByUsername(username) != true)
        {
            var error = new YmsgPacket(YmsgService.AuthResp, YmsgStatus.LoginError, session.SessionId)
                .Add(66, "3"); // 3 = bad username

            await session.SendAsync(error);

            return;
        }

        session.Username = username;

        // A duplicate login supersedes the prior connection (mirrors MsnServer): notify it with a
        // duplicate-status logoff and close it so it doesn't linger as a ghost that eats relayed messages.
        // Marking this session authenticated and evicting the ghost happen atomically under AuthGate, and
        // the (bounded) notify/close sends run after release so the gate never blocks other logins on a
        // stalled ghost socket. The evicted session's teardown then sees a live owner for the username and
        // skips its logoff broadcast.
        var superseded = new List<YmsgSession>();

        await AuthGate.WaitAsync();

        try
        {
            session.IsAuthenticated = true;

            foreach (var other in Sessions.Values.ToArray())
            {
                if (other.SessionId != session.SessionId && other.IsAuthenticated && string.Equals(other.Username, username, StringComparison.OrdinalIgnoreCase))
                {
                    Sessions.TryRemove(other.SessionId, out _);

                    superseded.Add(other);
                }
            }
        }
        finally
        {
            AuthGate.Release();
        }

        foreach (var other in superseded)
        {
            await TrySendAsync(other, new YmsgPacket(YmsgService.Logoff, YmsgStatus.Duplicate, other.SessionId).Add(0, other.Username));

            // Shutdown(Send) flushes the notice and FINs first; a bare Close with the old handler's read
            // still pending is an abortive reset that discards the notice in flight.
            try { other.Client.RawSocket.Shutdown(SocketShutdown.Send); } catch { }
            try { other.Client.RawSocket.Close(); } catch { }
        }

        Mind.Db?.RequestsTrack(session.Client, "N/A", "YMSG", $"logon {username}", nameof(YmsgServer));

        await SendListAsync(session);
        await SendInitialPresenceAsync(session);
        await BroadcastPresenceAsync(session);
        await DeliverOfflineMessagesAsync(session);
    }

    async Task SendListAsync(YmsgSession session)
    {
        var others = OtherUsernames(session.Username);

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
        var online = Sessions.Values.Where(s => s.IsAuthenticated && s.SessionId != session.SessionId && !IsInvisibleTo(s, session)).ToList();

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

    async Task BroadcastPresenceAsync(YmsgSession session)
    {
        foreach (var other in Sessions.Values.ToArray())
        {
            if (!other.IsAuthenticated || other.SessionId == session.SessionId)
            {
                continue;
            }

            if (IsInvisibleTo(session, other))
            {
                continue;
            }

            var presence = new YmsgPacket(YmsgService.Logon, 0, other.SessionId)
                .Add(0, other.Username)
                .Add(7, session.Username)
                .Add(10, session.YahooStatus.ToString())
                .Add(11, session.SessionId.ToString("X"))
                .Add(13, "1");

            if (session.YahooStatus == YmsgStatus.Custom && !string.IsNullOrEmpty(session.CustomStatusMessage))
            {
                presence.Add(19, session.CustomStatusMessage);
                presence.Add(47, "1");
            }

            await SafeSendAsync(other, presence);
        }
    }

    async Task BroadcastLogoffAsync(YmsgSession session)
    {
        foreach (var other in Sessions.Values.ToArray())
        {
            if (!other.IsAuthenticated || other.SessionId == session.SessionId)
            {
                continue;
            }

            var logoff = new YmsgPacket(YmsgService.Logoff, 0, other.SessionId)
                .Add(0, other.Username)
                .Add(7, session.Username)
                .Add(10, YmsgStatus.Available.ToString())
                .Add(11, session.SessionId.ToString("X"))
                .Add(13, "0");

            await SafeSendAsync(other, logoff);
        }
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
            await BroadcastLogoffAsync(session);
        }
        else
        {
            await BroadcastPresenceAsync(session);
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

        var target = GetByUsername(to);

        if (target == null)
        {
            StoreOfflineMessage(session.Username, to, text, traceId);

            return;
        }

        // A dead or stalled recipient must not kill (or indefinitely hang) the SENDER's session; on a
        // failed relay the message falls back to offline storage for the recipient's next login.
        if (await TrySendAsync(target, BuildImDelivery(target, session.Username, text)))
        {
            return;
        }

        // The failed (possibly cancelled mid-frame) write may have left the recipient's length-prefixed
        // stream misframed; close it so the client reconnects cleanly instead of lingering as a ghost
        // that parses every subsequent packet as garbage.
        try { target.Client.RawSocket.Close(); } catch { }

        // The recipient may have been superseded by a fresh login mid-send; retry once against the current
        // live session before deferring the message to a future login.
        var retry = GetByUsername(to);

        if (retry != null && retry.SessionId != target.SessionId)
        {
            if (await TrySendAsync(retry, BuildImDelivery(retry, session.Username, text)))
            {
                return;
            }

            try { retry.Client.RawSocket.Close(); } catch { }
        }

        StoreOfflineMessage(session.Username, to, text, traceId);
    }

    // Real server-to-client Message packets carry 4 (from), 5 (to), 14 (text), 15 (timestamp), 97 (utf8).
    // Fields 0/1 must NOT be present: libyahoo2-lineage clients treat the FIRST of key 1 or 4 as the
    // sender, so a leading field 1 would attribute the message to the recipient themselves.
    static YmsgPacket BuildImDelivery(YmsgSession target, string from, string text)
    {
        return new YmsgPacket(YmsgService.Message, 0, target.SessionId)
            .Add(4, from)
            .Add(5, target.Username)
            .Add(14, text)
            .Add(15, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            .Add(97, "1");
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

        var target = GetByUsername(to);

        if (target == null)
        {
            return;
        }

        var notify = new YmsgPacket(YmsgService.Notify, packet.Status, target.SessionId)
            .Add(4, session.Username)
            .Add(5, target.Username)
            .Add(49, packet.Get(49) ?? "TYPING")
            .Add(13, packet.Get(13) ?? "1");

        await SafeSendAsync(target, notify);
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

    static bool IsInvisibleTo(YmsgSession subject, YmsgSession observer)
    {
        // Invisible users are hidden from everyone; extend here if per-buddy privacy is added later.
        _ = observer;

        return subject.YahooStatus == YmsgStatus.Invisible;
    }

    static YmsgSession GetByUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        foreach (var session in Sessions.Values.ToArray())
        {
            if (session.IsAuthenticated && string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }

        return null;
    }

    static List<string> OtherUsernames(string username)
    {
        var users = Mind.Db?.UserList() ?? new List<Data.Types.HiveUser>();

        return users
            .Select(u => u.Username)
            .Where(u => !string.Equals(u, username, StringComparison.OrdinalIgnoreCase))
            .ToList();
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

    internal static string MakeChallenge(uint sessionId)
    {
        // A deterministic non-empty seed; content is irrelevant because we do not validate the crypt response.
        return $"c={sessionId:X8}$vintagehive$";
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
