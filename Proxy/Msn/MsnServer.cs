// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Security.Cryptography;
using VintageHive.Network;

namespace VintageHive.Proxy.Msn;

// A self-hosted MSN Messenger server for period MSNP2-7 clients (MSN Messenger 4.x-5.0), covering login
// (MD5 challenge auth, no Passport), presence, and 1:1 IM. The Notification Server and Switchboard roles
// share one port (1863); a connection's opening verb decides its role (VER = NS, USR/ANS = SB), and the
// ring that invites a callee is delivered in-process over the callee's own NS connection.
public sealed class MsnServer : Listener
{
    // Authenticated notification-server sessions, keyed by lowercased account.
    public static readonly ConcurrentDictionary<string, MsnSession> NsSessions = new();

    // Switchboard tickets issued by XFR/RNG, keyed by cookie.
    static readonly ConcurrentDictionary<string, SbInvite> PendingInvites = new();

    // Active switchboard sessions, keyed by session id.
    static readonly ConcurrentDictionary<string, Switchboard> Switchboards = new();

    static long _cookieCounter;

    // Upper bound on a switchboard MSG payload so a bogus length cannot allocate unbounded memory.
    const int MaxPayloadBytes = 64 * 1024;

    // Per-peer write timeout so one stalled (non-reading) client cannot freeze a broadcast for everyone.
    const int BroadcastWriteTimeoutMs = 5000;

    // A switchboard ticket is redeemable once, only by the account it was minted for, and only briefly;
    // without a lifetime every abandoned XFR or unanswered ring would sit in PendingInvites forever and
    // its cookie would stay redeemable indefinitely.
    static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(5);

    static readonly string[] SupportedVersions = { "MSNP7", "MSNP6", "MSNP5", "MSNP4", "MSNP3", "MSNP2" };

    // Sends to one peer with a bounded timeout, swallowing failures so a dead/slow peer cannot break a broadcast.
    static async Task SafeSendAsync(MsnSession target, string line)
    {
        try
        {
            using var cts = new CancellationTokenSource(BroadcastWriteTimeoutMs);

            await target.SendLineAsync(line, cts.Token);
        }
        catch
        {
            // Peer is gone or stalled; skip it.
        }
    }

    public MsnServer(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, false) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var traceId = connection.TraceId.ToString();
        var remoteAddress = connection.RemoteAddress;

        Log.WriteLine(Log.LEVEL_INFO, nameof(MsnServer), $"Client connected from {remoteAddress}", traceId);

        var session = new MsnSession(connection);

        try
        {
            var first = await session.Reader.ReadLineAsync();

            if (first != null)
            {
                var verb = Verb(first);

                if (verb == "VER")
                {
                    session.Role = MsnRole.Notification;

                    await HandleVerAsync(session, first);
                    await RunNotificationLoopAsync(session, traceId);
                }
                else if (verb == "USR" || verb == "ANS")
                {
                    session.Role = MsnRole.Switchboard;

                    await HandleSwitchboardOpeningAsync(session, first, verb, traceId);
                    await RunSwitchboardLoopAsync(session, traceId);
                }
                else
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(MsnServer), $"Unknown opening verb: {verb}", traceId);
                }
            }
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(MsnServer), ex, traceId);
        }
        finally
        {
            Teardown(session);

            Log.WriteLine(Log.LEVEL_INFO, nameof(MsnServer), $"Client disconnected from {remoteAddress}", traceId);
        }

        return null;
    }

    // -- Notification Server ----------------------------------------------

    async Task RunNotificationLoopAsync(MsnSession session, string traceId)
    {
        while (session.Client.IsConnected)
        {
            var line = await session.Reader.ReadLineAsync();

            if (line == null)
            {
                break;
            }

            var parts = line.Split(' ');
            var verb = parts[0];

            switch (verb)
            {
                case "CVR": await HandleCvrAsync(session, parts); break;
                case "INF": await HandleInfAsync(session, parts); break;
                case "USR": await HandleUsrAsync(session, parts, traceId); break;
                case "SYN": await HandleSynAsync(session, parts); break;
                case "CHG": await HandleChgAsync(session, parts); break;
                case "XFR": await HandleXfrAsync(session, parts); break;
                case "PNG": await session.SendLineAsync("QNG 50"); break;
                case "OUT": session.Client.RawSocket.Close(); break;
                default: Log.WriteLine(Log.LEVEL_DEBUG, nameof(MsnServer), $"Unhandled NS command: {verb}", traceId); break;
            }
        }
    }

    async Task HandleVerAsync(MsnSession session, string line)
    {
        var parts = line.Split(' ');
        var trid = parts.Length > 1 ? parts[1] : "0";

        var chosen = ChooseVersion(parts.Skip(2));

        await session.SendLineAsync($"VER {trid} {chosen} CVR0");
    }

    static async Task HandleCvrAsync(MsnSession session, string[] parts)
    {
        var trid = parts.Length > 1 ? parts[1] : "0";

        // Recommend the client keep its own version; the last arg is the account.
        await session.SendLineAsync($"CVR {trid} 5.0.0544 5.0.0544 4.7.2009 http://download.microsoft.com/ http://messenger.msn.com/");
    }

    static async Task HandleInfAsync(MsnSession session, string[] parts)
    {
        var trid = parts.Length > 1 ? parts[1] : "0";

        await session.SendLineAsync($"INF {trid} MD5");
    }

    async Task HandleUsrAsync(MsnSession session, string[] parts, string traceId)
    {
        // USR <trid> MD5 I <account>  |  USR <trid> MD5 S <hash> - both phases need the 5th token.
        if (parts.Length < 5)
        {
            return;
        }

        var trid = parts[1];
        var phase = parts[3];

        if (phase == "I")
        {
            session.Account = parts[4];
            session.AuthChallenge = NextCookie();

            await session.SendLineAsync($"USR {trid} MD5 S {session.AuthChallenge}");

            return;
        }

        if (phase == "S")
        {
            var clientHash = parts[4];
            var user = Mind.Db?.UserFetch(session.Account ?? string.Empty);

            if (user == null || !string.Equals(Md5Response(session.AuthChallenge, user.Password), clientHash, StringComparison.OrdinalIgnoreCase))
            {
                // 911 = authentication failed.
                await session.SendLineAsync($"911 {trid}");

                return;
            }

            session.IsAuthenticated = true;
            session.DisplayName = session.Account;

            var key = session.Account.ToLowerInvariant();

            // A duplicate login supersedes the prior connection; close it so it doesn't linger as a ghost.
            if (NsSessions.TryGetValue(key, out var existing) && !ReferenceEquals(existing, session))
            {
                try { existing.Client.RawSocket.Close(); } catch { }
            }

            NsSessions[key] = session;

            Mind.Db?.RequestsTrack(session.Client, "N/A", "MSN", $"logon {session.Account}", nameof(MsnServer));

            await session.SendLineAsync($"USR {trid} OK {session.Account} {UrlEncode(session.DisplayName)} 1 0");
        }
    }

    static async Task HandleSynAsync(MsnSession session, string[] parts)
    {
        var trid = parts.Length > 1 ? parts[1] : "0";
        const string version = "1";

        var contacts = OtherAccounts(session.Account);

        await session.SendLineAsync($"SYN {trid} {version}");
        await session.SendLineAsync($"GTC {trid} {version} A");
        await session.SendLineAsync($"BLP {trid} {version} AL");

        // MSNP2-7 clients expect ALL FOUR lists in the SYN response, each terminated by its item#/total
        // pair (0 0 when empty), before they consider contact sync complete. Everyone is a buddy of
        // everyone on the hive, so FL and RL carry the full account list; AL mirrors FL (everyone is
        // pre-allowed, so GTC A never triggers a prompt storm) and BL stays empty.
        await SendContactListAsync(session, trid, version, "FL", contacts);
        await SendContactListAsync(session, trid, version, "AL", contacts);
        await SendContactListAsync(session, trid, version, "BL", new List<string>());
        await SendContactListAsync(session, trid, version, "RL", contacts);
    }

    static async Task SendContactListAsync(MsnSession session, string trid, string version, string list, List<string> contacts)
    {
        if (contacts.Count == 0)
        {
            await session.SendLineAsync($"LST {trid} {list} {version} 0 0");

            return;
        }

        for (var i = 0; i < contacts.Count; i++)
        {
            await session.SendLineAsync($"LST {trid} {list} {version} {i + 1} {contacts.Count} {contacts[i]} {UrlEncode(contacts[i])}");
        }
    }

    async Task HandleChgAsync(MsnSession session, string[] parts)
    {
        if (parts.Length < 3)
        {
            return;
        }

        var trid = parts[1];
        var status = parts[2];

        if (!MsnStatus.IsValid(status))
        {
            status = MsnStatus.Online;
        }

        session.Status = status;

        // Track when the client went idle so the presence registry (Finger) reports a real duration;
        // repeated CHG IDL keepalives must not reset the clock.
        if (status == MsnStatus.Idle)
        {
            if (session.IdleSince == DateTimeOffset.MinValue)
            {
                session.IdleSince = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            session.IdleSince = DateTimeOffset.MinValue;
        }

        await session.SendLineAsync($"CHG {trid} {status}");

        // Initial presence dump: one ILN per already-online contact.
        foreach (var other in NsSessions.Values.ToArray())
        {
            if (!other.IsAuthenticated || ReferenceEquals(other, session) || other.Status is MsnStatus.Offline or MsnStatus.Hidden)
            {
                continue;
            }

            await SafeSendAsync(session, $"ILN {trid} {other.Status} {other.Account} {UrlEncode(other.DisplayName)}");
        }

        // Going hidden must still tell peers to drop the user (FLN); otherwise announce the live status.
        await BroadcastPresenceAsync(session, status == MsnStatus.Hidden ? MsnStatus.Offline : status);
    }

    static async Task BroadcastPresenceAsync(MsnSession session, string status)
    {
        foreach (var other in NsSessions.Values.ToArray())
        {
            if (!other.IsAuthenticated || ReferenceEquals(other, session))
            {
                continue;
            }

            var line = status == MsnStatus.Offline
                ? $"FLN {session.Account}"
                : $"NLN {status} {session.Account} {UrlEncode(session.DisplayName)}";

            await SafeSendAsync(other, line);
        }
    }

    async Task HandleXfrAsync(MsnSession session, string[] parts)
    {
        if (parts.Length < 3 || parts[2] != "SB")
        {
            return;
        }

        var trid = parts[1];
        var sessionId = NextCookie();
        var cookie = NextCookie();

        PruneExpiredInvites();

        // The requester itself redeems this ticket over its new SB connection (USR).
        PendingInvites[cookie] = new SbInvite(cookie, sessionId, session.Account, DateTimeOffset.UtcNow);

        await session.SendLineAsync($"XFR {trid} SB {AdvertisedAddress(session)} CKI {cookie}");
    }

    // -- Switchboard ------------------------------------------------------

    async Task HandleSwitchboardOpeningAsync(MsnSession session, string firstLine, string verb, string traceId)
    {
        var parts = firstLine.Split(' ');

        if (verb == "USR")
        {
            // USR <trid> <account> <cookie> - the caller joining the SB it requested. The cookie is
            // consumed even on rejection so a mismatched account burns the ticket instead of probing it.
            if (parts.Length < 4 || !PendingInvites.TryRemove(parts[3], out var invite) || !IsRedeemableBy(invite, parts[2]))
            {
                await session.SendLineAsync("911 " + (parts.Length > 1 ? parts[1] : "0"));

                return;
            }

            session.Account = parts[2];
            session.DisplayName = parts[2];
            session.IsAuthenticated = true;
            session.SwitchboardId = invite.SessionId;

            GetOrCreateSwitchboard(invite.SessionId).Participants[session.Account.ToLowerInvariant()] = session;

            await session.SendLineAsync($"USR {parts[1]} OK {session.Account} {UrlEncode(session.DisplayName)}");
        }
        else if (verb == "ANS")
        {
            // ANS <trid> <account> <cookie> <sessionId> - the callee answering the ring; the ticket only
            // admits the account it was minted for.
            if (parts.Length < 5 || !PendingInvites.TryRemove(parts[3], out var invite) || invite.SessionId != parts[4] || !IsRedeemableBy(invite, parts[2]))
            {
                await session.SendLineAsync("911 " + (parts.Length > 1 ? parts[1] : "0"));

                return;
            }

            var trid = parts[1];

            session.Account = parts[2];
            session.DisplayName = parts[2];
            session.IsAuthenticated = true;
            session.SwitchboardId = invite.SessionId;

            var sb = GetOrCreateSwitchboard(invite.SessionId);
            var existing = sb.Participants.Values.ToArray();

            // Tell the joiner about everyone already present.
            var index = 1;

            foreach (var participant in existing)
            {
                await session.SendLineAsync($"IRO {trid} {index} {existing.Length} {participant.Account} {UrlEncode(participant.DisplayName)}");

                index++;
            }

            await session.SendLineAsync($"ANS {trid} OK");

            sb.Participants[session.Account.ToLowerInvariant()] = session;

            // Announce the joiner to the others.
            foreach (var participant in existing)
            {
                await SafeSendAsync(participant, $"JOI {session.Account} {UrlEncode(session.DisplayName)}");
            }
        }
    }

    async Task RunSwitchboardLoopAsync(MsnSession session, string traceId)
    {
        while (session.Client.IsConnected)
        {
            var line = await session.Reader.ReadLineAsync();

            if (line == null)
            {
                break;
            }

            var parts = line.Split(' ');
            var verb = parts[0];

            switch (verb)
            {
                case "CAL": await HandleCalAsync(session, parts); break;
                case "MSG": await HandleSwitchboardMessageAsync(session, parts); break;
                case "BYE":
                case "OUT": session.Client.RawSocket.Close(); break;
                default: Log.WriteLine(Log.LEVEL_DEBUG, nameof(MsnServer), $"Unhandled SB command: {verb}", traceId); break;
            }
        }
    }

    async Task HandleCalAsync(MsnSession session, string[] parts)
    {
        if (parts.Length < 3)
        {
            return;
        }

        var trid = parts[1];
        var callee = parts[2];

        // Hidden (and authenticated-but-pre-CHG, still-FLN) callees must look offline to a caller: ringing
        // them would leak the concealed presence via RINGING-vs-217 and pop an RNG the FLN broadcast said
        // could not happen. 217 = principal not online.
        if (!NsSessions.TryGetValue(callee.ToLowerInvariant(), out var calleeNs) || !calleeNs.IsAuthenticated || calleeNs.Status is MsnStatus.Offline or MsnStatus.Hidden)
        {
            await session.SendLineAsync($"217 {trid}");

            return;
        }

        await session.SendLineAsync($"CAL {trid} RINGING {session.SwitchboardId}");

        var cookie = NextCookie();

        PruneExpiredInvites();

        // The CALLEE redeems this ticket when answering the ring (ANS).
        PendingInvites[cookie] = new SbInvite(cookie, session.SwitchboardId, callee, DateTimeOffset.UtcNow);

        // The ring is delivered over the callee's notification connection, not the switchboard.
        await SafeSendAsync(calleeNs, $"RNG {session.SwitchboardId} {AdvertisedAddress(session)} CKI {cookie} {session.Account} {UrlEncode(session.DisplayName)}");
    }

    async Task HandleSwitchboardMessageAsync(MsnSession session, string[] parts)
    {
        // MSG <trid> <ack> <length>
        if (parts.Length < 4 || !int.TryParse(parts[3], out var length))
        {
            return;
        }

        // Reject a bogus/oversized length before allocating, so an unauthenticated or hostile client cannot
        // exhaust memory with one MSG line.
        if (!session.IsAuthenticated || session.SwitchboardId == null || length < 0 || length > MaxPayloadBytes)
        {
            try { session.Client.RawSocket.Close(); } catch { }

            return;
        }

        var trid = parts[1];
        var ack = parts[2];

        var payload = await session.Reader.ReadBytesAsync(length);

        if (payload == null)
        {
            return;
        }

        var delivered = false;

        if (Switchboards.TryGetValue(session.SwitchboardId ?? string.Empty, out var sb))
        {
            foreach (var participant in sb.Participants.Values.ToArray())
            {
                if (ReferenceEquals(participant, session))
                {
                    continue;
                }

                try
                {
                    using var cts = new CancellationTokenSource(BroadcastWriteTimeoutMs);

                    await participant.SendPayloadAsync($"MSG {session.Account} {UrlEncode(session.DisplayName)} {length}", payload, cts.Token);

                    delivered = true;
                }
                catch
                {
                    // Participant is gone or stalled; report as undelivered to the sender.
                }
            }
        }

        // Ack semantics: A = always ack, N = ack only on failure, U = never ack.
        if (ack == "A" || (ack == "N" && !delivered))
        {
            await session.SendLineAsync($"{(delivered ? "ACK" : "NAK")} {trid}");
        }
    }

    // -- Shared helpers ---------------------------------------------------

    static Switchboard GetOrCreateSwitchboard(string sessionId)
    {
        return Switchboards.GetOrAdd(sessionId, id => new Switchboard(id));
    }

    static bool IsRedeemableBy(SbInvite invite, string account)
    {
        return string.Equals(invite.Account, account, StringComparison.OrdinalIgnoreCase) && DateTimeOffset.UtcNow - invite.IssuedAt <= InviteLifetime;
    }

    // Opportunistic sweep on invite creation (no timer thread needed): abandoned XFRs and unanswered rings
    // must not accumulate in the static dictionary for the life of the process.
    static void PruneExpiredInvites()
    {
        foreach (var invite in PendingInvites.Values.ToArray())
        {
            if (DateTimeOffset.UtcNow - invite.IssuedAt > InviteLifetime)
            {
                PendingInvites.TryRemove(invite.Cookie, out _);
            }
        }
    }

    void Teardown(MsnSession session)
    {
        if (session.Role == MsnRole.Notification && session.IsAuthenticated && session.Account != null)
        {
            var key = session.Account.ToLowerInvariant();

            // Only evict/announce if THIS session is still the registered one; a duplicate login may have
            // already replaced it, and removing then would ghost the live session.
            if (NsSessions.TryGetValue(key, out var current) && ReferenceEquals(current, session))
            {
                NsSessions.TryRemove(key, out _);

                // Best-effort offline broadcast.
                _ = BroadcastPresenceAsync(session, MsnStatus.Offline);

                Mind.Db?.RequestsTrack(session.Client, "N/A", "MSN", $"logoff {session.Account}", nameof(MsnServer));
            }
        }
        else if (session.Role == MsnRole.Switchboard && session.SwitchboardId != null && Switchboards.TryGetValue(session.SwitchboardId, out var sb))
        {
            if (session.Account != null)
            {
                sb.Participants.TryRemove(session.Account.ToLowerInvariant(), out _);
            }

            foreach (var participant in sb.Participants.Values.ToArray())
            {
                _ = SafeSendAsync(participant, $"BYE {session.Account}");
            }

            if (sb.Participants.IsEmpty)
            {
                Switchboards.TryRemove(session.SwitchboardId, out _);
            }
        }
    }

    string AdvertisedAddress(MsnSession session)
    {
        // Behind Docker/NAT the socket-local IP is unreachable; prefer the operator-configured IP when set.
        var configured = Mind.Db?.ConfigGet<string>(ConfigNames.IpAddress);
        var host = !string.IsNullOrWhiteSpace(configured) && configured != IPAddress.Any.ToString()
            ? configured
            : session.Client.LocalIP;

        return $"{host}:{Port}";
    }

    internal static string ChooseVersion(IEnumerable<string> offered)
    {
        var offeredSet = offered.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var version in SupportedVersions)
        {
            if (offeredSet.Contains(version))
            {
                return version;
            }
        }

        // The client offered nothing we speak; answer our highest so an MD5-capable client can still proceed.
        return SupportedVersions[0];
    }

    internal static string Md5Response(string challenge, string password)
    {
        var hash = MD5.HashData(Encoding.ASCII.GetBytes((challenge ?? string.Empty) + (password ?? string.Empty)));

        return Convert.ToHexStringLower(hash);
    }

    static string NextCookie()
    {
        var id = Interlocked.Increment(ref _cookieCounter);

        return $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.{id}";
    }

    static List<string> OtherAccounts(string account)
    {
        var users = Mind.Db?.UserList() ?? new List<Data.Types.HiveUser>();

        return users
            .Select(u => u.Username)
            .Where(u => !string.Equals(u, account, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    static string Verb(string line)
    {
        var space = line.IndexOf(' ');

        return space < 0 ? line : line[..space];
    }

    // Display names are URL-encoded on the wire (%20 for space, etc.).
    static string UrlEncode(string value)
    {
        return Uri.EscapeDataString(value ?? string.Empty);
    }

    // Account = the one account allowed to redeem this ticket (the XFR requester for USR, the ring's
    // callee for ANS), so a leaked or guessed cookie cannot admit an arbitrary self-asserted name.
    sealed record SbInvite(string Cookie, string SessionId, string Account, DateTimeOffset IssuedAt);

    sealed class Switchboard
    {
        public Switchboard(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        public ConcurrentDictionary<string, MsnSession> Participants { get; } = new();
    }
}
