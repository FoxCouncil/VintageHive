// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using VintageHive.Network;
using VintageHive.Proxy.Presence;

namespace VintageHive.Proxy.Yahoo;

// A self-hosted Yahoo! Messenger (YMSG) server for period YM 5.x clients: login, presence, and 1:1 IM.
// VintageHive is the whole auth server (same trust model as OSCAR), so the v9/v10 challenge/response
// crypt is not reproduced; we mint a challenge, then accept any response for a username that exists in
// the shared user table. See the login handler for the exact stance.
internal sealed class YmsgServer : Listener
{
    public static readonly ConcurrentDictionary<uint, YmsgSession> Sessions = new();

    const int MaxBodyBytes = 65535;

    // Cap relayed IM text so an encoded packet body cannot exceed the 16-bit YMSG length field.
    const int MaxMessageChars = 16000;

    const int BroadcastWriteTimeoutMs = 5000;

    // Sends to one peer with a bounded timeout, swallowing failures so a dead/slow peer cannot break a broadcast.
    static async Task SafeSendAsync(YmsgSession target, YmsgPacket packet)
    {
        try
        {
            using var cts = new CancellationTokenSource(BroadcastWriteTimeoutMs);

            await target.SendAsync(packet, cts.Token);
        }
        catch
        {
            // Peer is gone or stalled; skip it.
        }
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
                await BroadcastLogoffAsync(session);

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
        session.IsAuthenticated = true;

        Mind.Db?.RequestsTrack(session.Client, "N/A", "YMSG", $"logon {username}", nameof(YmsgServer));

        await SendListAsync(session);
        await SendInitialPresenceAsync(session);
        await BroadcastPresenceAsync(session);
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
            // Offline delivery is not implemented for the MVP; the message is dropped.
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(YmsgServer), $"Message to offline user {to} dropped", traceId);

            return;
        }

        // On the wire the sender is rewritten from field 1 (sender's own id) to field 4 (from) on delivery.
        var delivery = new YmsgPacket(YmsgService.Message, 0, target.SessionId)
            .Add(0, target.Username)
            .Add(1, target.Username)
            .Add(5, target.Username)
            .Add(4, session.Username)
            .Add(14, text)
            .Add(15, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            .Add(97, "1");

        await target.SendAsync(delivery);
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
