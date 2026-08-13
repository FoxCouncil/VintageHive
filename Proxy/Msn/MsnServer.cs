// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Security.Cryptography;
using VintageHive.Data.Types;
using VintageHive.Network;

namespace VintageHive.Proxy.Msn;

// A self-hosted MSN Messenger server for period MSNP2-7 clients (MSN Messenger 4.x-5.0), covering login
// (MD5 challenge auth, no Passport), presence, and 1:1 IM. The Notification Server and Switchboard roles
// share one port (1863); a connection's opening verb decides its role (VER = NS, USR/ANS = SB), and the
// ring that invites a callee is delivered in-process over the callee's own NS connection.
public sealed class MsnServer : Listener
{

    // ProcessConnection below drives the whole session; there is nothing for the base read loop to do.
    protected override bool OwnsConnection => true;
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

    // Everything else the notification loop accepts (CVR, INF, USR, PNG, OUT) is either part of the sign-on
    // exchange itself or carries no account state, so it stays reachable before authentication. ADD/REM edit
    // the account's lists and REA edits its stored display name, so they gate with the rest.
    static readonly HashSet<string> RequiresAuthentication = new(StringComparer.Ordinal) { "SYN", "CHG", "XFR", "ADD", "REM", "REA" };

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

            // SYN answers with the full contact list, which on this hive is every registered account, and
            // CHG/XFR act on the account's identity. None may run before USR has actually verified a password:
            // an anonymous VER-then-SYN used to enumerate every member on the box, because HandleSynAsync
            // calls OtherAccounts(session.Account) and a null account excludes nobody.
            if (!session.IsAuthenticated && RequiresAuthentication.Contains(verb))
            {
                Log.WriteLine(Log.LEVEL_INFO, nameof(MsnServer), $"Refused {verb} on an unauthenticated session", traceId);

                // 911 is the authentication-failed code this server already answers a bad USR with.
                await session.SendLineAsync($"911 {(parts.Length > 1 ? parts[1] : "0")}");

                continue;
            }

            switch (verb)
            {
                case "CVR": await HandleCvrAsync(session, parts); break;
                case "INF": await HandleInfAsync(session, parts); break;
                case "USR": await HandleUsrAsync(session, parts, traceId); break;
                case "SYN": await HandleSynAsync(session, parts); break;
                case "CHG": await HandleChgAsync(session, parts); break;
                case "XFR": await HandleXfrAsync(session, parts); break;
                case "ADD": await HandleListEditAsync(session, parts); break;
                case "REM": await HandleListEditAsync(session, parts); break;
                case "GTC": await HandleListSettingAsync(session, parts, traceId); break;
                case "BLP": await HandleListSettingAsync(session, parts, traceId); break;
                case "REA": await HandleReaAsync(session, parts); break;
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

        // HandleSynAsync varies its reply shape by dialect (MSNP7 adds contact groups), so the
        // negotiated version must outlive this exchange.
        if (chosen.StartsWith("MSNP", StringComparison.Ordinal) && int.TryParse(chosen.AsSpan(4), out var dialect))
        {
            session.Dialect = dialect;
        }

        await session.SendLineAsync($"VER {trid} {chosen} CVR0");
    }

    static async Task HandleCvrAsync(MsnSession session, string[] parts)
    {
        var trid = parts.Length > 1 ? parts[1] : "0";

        // CVR <trid> <localeid> <osType> <osVer> <arch> <clientName> <clientVer> <MSMSGS> <account>.
        // Echo the client's own version into every slot: the reply's third version is a MINIMUM the client
        // obeys literally, so any value above the connecting client is an upgrade wall ("A newer version is
        // available...") pointing at an update this server cannot host.
        var clientVersion = parts.Length > 7 && IsVersionToken(parts[7]) ? parts[7] : null;
        var recommended = clientVersion ?? "5.0.0544";

        // With no parseable client version, the minimum drops to a floor no real client is below.
        var minimum = clientVersion ?? "1.0.0000";

        await session.SendLineAsync($"CVR {trid} {recommended} {recommended} {minimum} http://{HiveDomains.Intranet}/download.html http://{HiveDomains.Intranet}/");
    }

    // A plausible dotted client version like "4.5.0121": digits and dots only, ending on a digit; anything
    // else falls back so the reply can never assert a minimum above the client that asked.
    static bool IsVersionToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 16 || token[0] == '.' || token[^1] == '.')
        {
            return false;
        }

        foreach (var c in token)
        {
            if (!char.IsAsciiDigit(c) && c != '.')
            {
                return false;
            }
        }

        return true;
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
            var presented = session.Account ?? string.Empty;
            var user = Mind.Db?.UserFetch(presented);

            // MSN Messenger only ever sends a Passport-style name@domain (the client refuses to submit a
            // bare name), so on a miss resolve it the way the mail plane resolves POP3/IMAP/SMTP logins:
            // validate the domain against the hosted list and fetch the local part. The whole-string fetch
            // above always wins, so an account literally named with an '@' keeps working, and an unhosted
            // domain stays a 911 rather than being silently stripped to its local part.
            if (user == null && presented.Contains('@') && MailDomains.TryResolveLogin(presented, out var localPart, out _))
            {
                user = Mind.Db?.UserFetch(localPart);
            }

            if (user == null || !string.Equals(Md5Response(session.AuthChallenge, user.Password), clientHash, StringComparison.OrdinalIgnoreCase))
            {
                // 911 = authentication failed.
                await session.SendLineAsync($"911 {trid}");

                return;
            }

            session.IsAuthenticated = true;
            session.AccountUsername = user.Username;

            // A name the member chose (REA) survives sign-ons; without one the sign-in address is the
            // display name, exactly as before the profile store existed.
            var storedName = Mind.Db?.UserGetDisplayName(user.Username);

            session.DisplayName = string.IsNullOrWhiteSpace(storedName) ? session.Account : storedName;

            // Key on the RESOLVED username, not the presented string, so `alice` and `alice@<hosted>` are
            // one entry and a duplicate login supersedes instead of ghosting alongside.
            var key = session.AccountUsername.ToLowerInvariant();

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

        string version;
        string gtc;
        string blp;
        List<MsnListEntry> forwardList, allowList, blockList, reverseList;

        if (PersistLists)
        {
            // Members own their lists: serve exactly what is stored, under the serial that survives a
            // process restart (so a client's cached version number stays meaningful). MsnAutoRoster is
            // ignored on this path by contract.
            var state = Mind.Db.MsnListStateGet(session.AccountUsername);

            version = state.Serial.ToString();
            gtc = state.Gtc;
            blp = state.Blp;
            forwardList = Mind.Db.MsnListGet(session.AccountUsername, "FL");
            allowList = Mind.Db.MsnListGet(session.AccountUsername, "AL");
            blockList = Mind.Db.MsnListGet(session.AccountUsername, "BL");
            reverseList = Mind.Db.MsnListGet(session.AccountUsername, "RL");
        }
        else
        {
            // The server-built roster is the operator's call, not the server's: MsnAutoRoster off starts every
            // member with all four lists empty (the 0 0 form) so members build their own via ADD/REM. Re-read
            // per SYN, so a ConfigSet applies without restart. The "everyone except me" filter compares stored
            // usernames, so it must use the resolved username; the presented alice@<hosted> would exclude
            // nobody and list the member to themselves.
            var autoRoster = Mind.Db?.ConfigGet<bool>(ConfigNames.MsnAutoRoster) ?? true;
            var contacts = autoRoster ? OtherAccounts(session.AccountUsername) : new List<string>();
            var entries = contacts.Select(c => new MsnListEntry(c, c, 0)).ToList();

            // Everyone is a buddy of everyone on the hive, so FL and RL carry the full account list; AL
            // mirrors FL (everyone is pre-allowed, so GTC A never triggers a prompt storm) and BL stays empty.
            version = "1";
            gtc = "A";
            blp = "AL";
            forwardList = entries;
            allowList = entries;
            blockList = new List<MsnListEntry>();
            reverseList = entries;
        }

        await session.SendLineAsync($"SYN {trid} {version}");
        await session.SendLineAsync($"GTC {trid} {version} {gtc}");
        await session.SendLineAsync($"BLP {trid} {version} {blp}");

        // MSNP7 adds contact groups to the sync, and the client stalls on "Signing In..." forever without
        // them: at least one LSG line always precedes the lists (the implicit "Other Contacts" group,
        // id 0, counted in the group total), and every FL entry carries a trailing comma-separated
        // group-id field naming the groups it belongs to. The hive has no group storage, so the single
        // implicit group holding every contact is the complete answer, not a stub. Pre-7 dialects use
        // neither shape and get the exact reply they always did.
        var msnp7 = session.Dialect >= 7;

        if (msnp7)
        {
            await session.SendLineAsync($"LSG {trid} {version} 1 1 0 {UrlEncode("Other Contacts")} 0");
        }

        // MSNP2-7 clients expect ALL FOUR lists in the SYN response, each terminated by its item#/total
        // pair (0 0 when empty), before they consider contact sync complete.
        await SendContactListAsync(session, trid, version, "FL", forwardList, appendGroupField: msnp7);
        await SendContactListAsync(session, trid, version, "AL", allowList);
        await SendContactListAsync(session, trid, version, "BL", blockList);
        await SendContactListAsync(session, trid, version, "RL", reverseList);
    }

    // appendGroupField applies only to the FL on MSNP7+; the AL/BL/RL never carry group ids in any dialect.
    static async Task SendContactListAsync(MsnSession session, string trid, string version, string list, List<MsnListEntry> contacts, bool appendGroupField = false)
    {
        if (contacts.Count == 0)
        {
            await session.SendLineAsync($"LST {trid} {list} {version} 0 0");

            return;
        }

        for (var i = 0; i < contacts.Count; i++)
        {
            var entry = contacts[i];
            var display = string.IsNullOrEmpty(entry.DisplayName) ? entry.Contact : entry.DisplayName;
            var line = $"LST {trid} {list} {version} {i + 1} {contacts.Count} {entry.Contact} {UrlEncode(display)}";

            if (appendGroupField)
            {
                line += $" {entry.GroupId}";
            }

            await session.SendLineAsync(line);
        }
    }

    // ADD <trid> <list> <account> <displayname>[ <groupid>] and REM <trid> <list> <account>: acknowledged by
    // echoing the request with the next list serial, which is all the client needs to commit the change into
    // its own lists. With MsnPersistLists off nothing is persisted (the flag-off contract); with it on the
    // edit lands in the owner's stored lists and an ADD FL grows its reverse side (NotifyForwardAddAsync).
    // Either way there is deliberately no does-the-account-exist check: a "no such user" error here would
    // confirm which names exist, the very disclosure the roster switch closes.
    static async Task HandleListEditAsync(MsnSession session, string[] parts)
    {
        if (parts.Length < 4)
        {
            return;
        }

        if (!PersistLists)
        {
            await session.SendLineAsync($"{parts[0]} {parts[1]} {parts[2]} {session.NextListSerial()} {string.Join(' ', parts.Skip(3))}");

            return;
        }

        var verb = parts[0];
        var trid = parts[1];
        var list = parts[2];
        var contact = parts[3];

        // Only the four real lists persist; anything else is not a shape a period client emits.
        if (list is not ("FL" or "AL" or "BL" or "RL"))
        {
            return;
        }

        if (verb == "ADD")
        {
            // The DECODED display name persists (wire encoding is applied exactly once, on the way out); a
            // name that fails REA's validity rules falls back to the account token rather than refusing the
            // add, because the ack must stay uniform regardless of what was asked for (see the no-oracle
            // rule above).
            var display = contact;

            if (parts.Length > 4)
            {
                try
                {
                    var decoded = Uri.UnescapeDataString(parts[4]);

                    if (!string.IsNullOrWhiteSpace(decoded) && IsValidDisplayName(decoded))
                    {
                        display = decoded;
                    }
                }
                catch (UriFormatException)
                {
                    // Keep the account-token fallback.
                }
            }

            var groupId = parts.Length > 5 && int.TryParse(parts[5], out var parsedGroup) && parsedGroup >= 0 ? parsedGroup : 0;

            Mind.Db.MsnListUpsert(session.AccountUsername, list, contact, display, groupId);

            await session.SendLineAsync($"ADD {trid} {list} {Mind.Db.MsnListBumpSerial(session.AccountUsername)} {string.Join(' ', parts.Skip(3))}");

            if (list == "FL")
            {
                await NotifyForwardAddAsync(session, trid, contact);
            }
        }
        else
        {
            // REM FL deliberately leaves the target's RL entry alone (real MSN semantics: withdrawing your
            // subscription does not erase the fact you once asked), so only the owner's own row goes.
            Mind.Db.MsnListRemove(session.AccountUsername, list, contact);

            await session.SendLineAsync($"REM {trid} {list} {Mind.Db.MsnListBumpSerial(session.AccountUsername)} {string.Join(' ', parts.Skip(3))}");
        }
    }

    // The reverse half of ADD FL, the piece that makes the client's authorization prompt possible under
    // GTC A: the adder lands on the target's RL, a live target hears about it as ADD 0 RL, and the adder
    // gets the target's presence immediately instead of staying dark until the contact's next CHG. A name
    // that resolves to no account simply has no reverse side - it was stored and acked upstream identically
    // to a real one, so nothing here may emit anything that distinguishes the two cases to the adder.
    static async Task NotifyForwardAddAsync(MsnSession session, string trid, string contact)
    {
        var targetUsername = ResolveAccountUsername(contact);

        if (targetUsername == null || targetUsername.Equals(session.AccountUsername, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Only a NEW reverse-list entry notifies: re-adding someone who already holds the RL entry (say,
        // after a REM FL) must not re-ring their authorization prompt.
        if (Mind.Db.MsnListUpsert(targetUsername, "RL", session.Account, session.DisplayName, 0))
        {
            var targetSerial = Mind.Db.MsnListBumpSerial(targetUsername);

            if (NsSessions.TryGetValue(targetUsername.ToLowerInvariant(), out var live) && live.IsAuthenticated)
            {
                await SafeSendAsync(live, $"ADD 0 RL {targetSerial} {session.Account} {UrlEncode(session.DisplayName)}");
            }
        }

        // Presence on add, under the same veto as the fan-out: a target whose lists refuse the adder
        // (Block, BLP BL, GTC N) stays exactly as dark as an offline or nonexistent one.
        if (NsSessions.TryGetValue(targetUsername.ToLowerInvariant(), out var target)
            && target.IsAuthenticated
            && target.Status is not (MsnStatus.Offline or MsnStatus.Hidden)
            && CanContact(targetUsername, session))
        {
            await session.SendLineAsync($"ILN {trid} {target.Status} {target.Account} {UrlEncode(target.DisplayName)}");
        }
    }

    // GTC <trid> <A|N> and BLP <trid> <AL|BL>: the client's list-behavior settings, persisted so SYN can
    // serve them back. Only meaningful with persistent lists; without the flag they stay the unhandled
    // no-ops they always were (hence not in RequiresAuthentication - a 911 there would change the flag-off
    // behavior - so the auth gate lives here instead).
    static async Task HandleListSettingAsync(MsnSession session, string[] parts, string traceId)
    {
        if (!PersistLists)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(MsnServer), $"Unhandled NS command: {parts[0]}", traceId);

            return;
        }

        if (parts.Length < 3)
        {
            return;
        }

        var verb = parts[0];
        var trid = parts[1];
        var value = parts[2];

        if (!session.IsAuthenticated)
        {
            await session.SendLineAsync($"911 {trid}");

            return;
        }

        if (verb == "GTC" ? value is not ("A" or "N") : value is not ("AL" or "BL"))
        {
            return;
        }

        var serial = Mind.Db.MsnListBumpSerial(session.AccountUsername);

        if (verb == "GTC")
        {
            Mind.Db.MsnListSetGtc(session.AccountUsername, value);
        }
        else
        {
            Mind.Db.MsnListSetBlp(session.AccountUsername, value);
        }

        await session.SendLineAsync($"{verb} {trid} {serial} {value}");
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

        // Initial presence dump: one ILN per already-online contact. With persistent lists this is
        // list-scoped: only contacts the member's own FL names, and only when the contact's own lists
        // answer that this member may perceive them (the same CanContact the switchboard consults).
        var persist = PersistLists;
        var forwardList = persist ? Mind.Db.MsnListGet(session.AccountUsername, "FL") : null;

        foreach (var other in NsSessions.Values.ToArray())
        {
            if (!other.IsAuthenticated || ReferenceEquals(other, session) || other.Status is MsnStatus.Offline or MsnStatus.Hidden)
            {
                continue;
            }

            if (persist && (!ListNames(forwardList, other) || !CanContact(other.AccountUsername, session)))
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
        var persist = PersistLists;

        foreach (var other in NsSessions.Values.ToArray())
        {
            if (!other.IsAuthenticated || ReferenceEquals(other, session))
            {
                continue;
            }

            // With persistent lists the fan-out is list-scoped: presence reaches only members whose FL
            // names the principal AND whom the principal's own lists admit (BL veto, BLP default, GTC N
            // hold - the same CanContact the switchboard consults, so seeing and reaching never disagree).
            if (persist && (!ListNames(Mind.Db.MsnListGet(other.AccountUsername, "FL"), session) || !CanContact(session.AccountUsername, other)))
            {
                continue;
            }

            var line = status == MsnStatus.Offline
                ? $"FLN {session.Account}"
                : $"NLN {status} {session.Account} {UrlEncode(session.DisplayName)}";

            await SafeSendAsync(other, line);
        }
    }

    // REA <trid> <account> <urlencodedName>, answered REA <trid> <serial> <account> <urlencodedName>: the
    // MSNP2-9 rename. A member may only rename THEMSELVES - accepting any other account here would let one
    // member rename another. The DECODED name is what persists; the wire encoding is applied exactly once,
    // by UrlEncode on every outbound surface. Error codes: 201 = invalid parameter, 209 = invalid friendly name.
    async Task HandleReaAsync(MsnSession session, string[] parts)
    {
        if (parts.Length < 4)
        {
            return;
        }

        var trid = parts[1];

        // A raw space in the name token means the client never URL-encoded it; storing the truncated
        // fragment tokenization left in parts[3] would persist something the member did not send.
        if (parts.Length > 4)
        {
            await session.SendLineAsync($"209 {trid}");

            return;
        }

        // "Themselves" is a PRINCIPAL, not a string: the bare and Passport forms of one account both name
        // it (the same resolution every other account argument goes through), so a client presenting the
        // form it did not sign in with is still renaming itself. Any other member's name, in either form,
        // stays the 201.
        var target = ResolveAccountUsername(parts[2]) ?? parts[2];

        if (!string.Equals(target, session.AccountUsername, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendLineAsync($"201 {trid}");

            return;
        }

        string decoded;

        try
        {
            decoded = Uri.UnescapeDataString(parts[3]);
        }
        catch (UriFormatException)
        {
            await session.SendLineAsync($"209 {trid}");

            return;
        }

        if (!IsValidDisplayName(decoded))
        {
            await session.SendLineAsync($"209 {trid}");

            return;
        }

        // Empty or whitespace-only clears the stored name: the sign-in address becomes the display name
        // again, rather than contacts seeing a blank entry.
        var cleared = string.IsNullOrWhiteSpace(decoded);

        Mind.Db?.UserSetDisplayName(session.AccountUsername, cleared ? null : decoded);

        session.DisplayName = cleared ? session.Account : decoded;

        await session.SendLineAsync($"REA {trid} {NextListSerial(session)} {session.Account} {UrlEncode(session.DisplayName)}");

        // Already-connected contacts see the new name immediately: the NLN broadcast carries
        // UrlEncode(session.DisplayName). A session that never went online (or is hidden) announces
        // nothing, exactly as its status demands - the name still lands at the next sign-on/CHG.
        if (session.Status is not (MsnStatus.Offline or MsnStatus.Hidden))
        {
            await BroadcastPresenceAsync(session, session.Status);
        }
    }

    // The decoded form is shown to other members, so it gets the same discipline as any member-supplied
    // display text: a period-honest length cap (clients of the era handled ~129 chars) and no control
    // characters, which covers the CR/LF that would break MSNP's line framing. Spaces and delimiters are
    // fine here because every outbound path re-encodes with UrlEncode.
    internal const int MaxDisplayNameLength = 129;

    internal static bool IsValidDisplayName(string name)
    {
        if (name.Length > MaxDisplayNameLength)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
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

            // The switchboard session must answer list lookups (CanContact/MayRing) under the same owner
            // key the notification side stores them with, so the presented form resolves exactly as
            // sign-on does. Unconditional, so a flag flipped on mid-session finds already-open
            // switchboard sessions carrying the right key rather than failing open.
            session.AccountUsername = ResolveAccountUsername(session.Account) ?? session.Account;

            AdoptNotificationIdentity(session);

            GetOrCreateSwitchboard(invite.SessionId).Participants[session.AccountUsername.ToLowerInvariant()] = session;

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

            // Same owner-key resolution as the USR branch, for the same lookups, unconditional for the
            // same mid-session flag-flip reason.
            session.AccountUsername = ResolveAccountUsername(session.Account) ?? session.Account;

            AdoptNotificationIdentity(session);

            Switchboards.TryGetValue(invite.SessionId, out var pending);

            var existing = pending?.Participants.Values.ToArray() ?? Array.Empty<MsnSession>();

            // The invite was screened at CAL time, but a block can land in the gap before ANS redeems it
            // (and the flag can flip on in that gap): re-screen against the room as it exists NOW, in both
            // directions, answering the same 911 an expired or stolen ticket gets so the refusal discloses
            // nothing. This runs before the IRO roster hands the joiner anyone's identity, and before the
            // session is marked a room member - a refused connection must tear down like one that never
            // joined, not broadcast a BYE for a member who was never announced.
            if (PersistLists)
            {
                foreach (var participant in existing)
                {
                    if (!CanContact(participant.AccountUsername, session) || !CanContact(session.AccountUsername, participant))
                    {
                        await session.SendLineAsync($"911 {trid}");

                        return;
                    }
                }
            }

            session.IsAuthenticated = true;
            session.SwitchboardId = invite.SessionId;

            var sb = GetOrCreateSwitchboard(invite.SessionId);

            // Tell the joiner about everyone already present.
            var index = 1;

            foreach (var participant in existing)
            {
                await session.SendLineAsync($"IRO {trid} {index} {existing.Length} {participant.Account} {UrlEncode(participant.DisplayName)}");

                index++;
            }

            await session.SendLineAsync($"ANS {trid} OK");

            sb.Participants[session.AccountUsername.ToLowerInvariant()] = session;

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

        // A real client dials the Passport form - the contact list and the RNG/ILN payloads all carry
        // name@domain - while NsSessions keys on the RESOLVED username, so the callee token goes through
        // the same resolution as sign-on and both forms land on the same entry. A name that resolves to
        // nothing keeps its presented form, which misses the lookup exactly like a real-but-offline member.
        var calleeUsername = ResolveAccountUsername(callee) ?? callee;

        // Hidden (and authenticated-but-pre-CHG, still-FLN) callees must look offline to a caller: ringing
        // them would leak the concealed presence via RINGING-vs-217 and pop an RNG the FLN broadcast said
        // could not happen. 217 = principal not online. With persistent lists, a callee the ring may not
        // reach (Block, BLP BL, GTC N - see MayRing) answers from this SAME condition: blocked and offline
        // must be byte-identical - the same 217, no RINGING, no invite minted, no RNG - or the refusal
        // becomes a did-they-block-me oracle, the mirror of the enumeration oracle ADD refuses to be.
        if (!NsSessions.TryGetValue(calleeUsername.ToLowerInvariant(), out var calleeNs) || !calleeNs.IsAuthenticated || calleeNs.Status is MsnStatus.Offline or MsnStatus.Hidden || (PersistLists && !MayRing(session, calleeNs)))
        {
            await session.SendLineAsync($"217 {trid}");

            return;
        }

        await session.SendLineAsync($"CAL {trid} RINGING {session.SwitchboardId}");

        var cookie = NextCookie();

        PruneExpiredInvites();

        // The CALLEE redeems this ticket when answering the ring (ANS). It is minted for the callee's own
        // sign-in identity, not the form the caller dialed: the callee's client answers with the name it
        // signed in as, and dialing the other form of the same account must not produce a ticket its own
        // callee cannot redeem.
        PendingInvites[cookie] = new SbInvite(cookie, session.SwitchboardId, calleeNs.Account, DateTimeOffset.UtcNow);

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
        var persist = PersistLists;

        if (Switchboards.TryGetValue(session.SwitchboardId ?? string.Empty, out var sb))
        {
            foreach (var participant in sb.Participants.Values.ToArray())
            {
                if (ReferenceEquals(participant, session))
                {
                    continue;
                }

                // A block takes effect on the OPEN switchboard, in both directions: every relay consults
                // the stored lists, so an ADD BL on the notification connection stops the very next
                // message - the blocked party's toward the blocker AND the blocker's own toward the
                // blocked, because blocking someone ends the conversation rather than demoting them to a
                // listener. Skipped delivery counts as undelivered, so a lone refused recipient surfaces
                // to the sender as the same NAK any dead peer produces.
                if (persist && (!CanContact(participant.AccountUsername, session) || !CanContact(session.AccountUsername, participant)))
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

    // Re-read per operation (like MsnAutoRoster) so a ConfigSet applies without restart.
    static bool PersistLists => Mind.Db?.ConfigGet<bool>(ConfigNames.MsnPersistLists) ?? false;

    // With persistent lists the serial lives in storage, shared by every edit surface (ADD/REM/REA/GTC/BLP)
    // so the client's cached version number never runs backwards; otherwise it stays the session counter it
    // always was.
    static int NextListSerial(MsnSession session)
    {
        return PersistLists ? Mind.Db.MsnListBumpSerial(session.AccountUsername) : session.NextListSerial();
    }

    // The same resolution sign-on uses: whole-string fetch first (an account literally named with an '@'
    // wins), then the Passport-style name@domain form against the hosted mail domains. Null when nothing
    // matches - which callers must treat exactly like a real-but-offline member.
    static string ResolveAccountUsername(string presented)
    {
        var user = Mind.Db?.UserFetch(presented);

        if (user == null && presented.Contains('@') && MailDomains.TryResolveLogin(presented, out var localPart, out _))
        {
            user = Mind.Db?.UserFetch(localPart);
        }

        return user?.Username;
    }

    // A member's switchboard identity is the identity their notification session announces: peers key
    // conversation windows on the form presence carried (ILN/NLN/RNG), so IRO/JOI/MSG must emit that same
    // form - and the member's chosen display name, not the account token - no matter which form this
    // switchboard connection presented. With no live notification session (signed off in the ring gap)
    // the presented form stands, resolved username and all.
    static void AdoptNotificationIdentity(MsnSession session)
    {
        if (NsSessions.TryGetValue(session.AccountUsername.ToLowerInvariant(), out var ns) && ns.IsAuthenticated)
        {
            session.Account = ns.Account;
            session.DisplayName = ns.DisplayName;
        }
    }

    // Whether this switchboard may ring the callee at all: the callee and every person already in the
    // room - the caller included - must admit each other in BOTH directions, because a join hands every
    // pair each other's presence (RNG/IRO/JOI, relayed MSG identity) and a Block in either direction
    // denies exactly that. One rule, no self-inflicted exception: blocking someone means the conversation
    // is over until you unblock them, not "over unless the blocker dials". The refusal shares CAL's
    // offline 217, so the inviter learns nothing a busy callee would not already show.
    static bool MayRing(MsnSession caller, MsnSession callee)
    {
        if (!CanContact(callee.AccountUsername, caller) || !CanContact(caller.AccountUsername, callee))
        {
            return false;
        }

        if (caller.SwitchboardId != null && Switchboards.TryGetValue(caller.SwitchboardId, out var sb))
        {
            foreach (var participant in sb.Participants.Values.ToArray())
            {
                if (ReferenceEquals(participant, caller))
                {
                    continue;
                }

                if (!CanContact(participant.AccountUsername, callee) || !CanContact(callee.AccountUsername, participant))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // The one answer to "may peer reach or perceive principal", shared by presence (the CHG ILN dump, the
    // NLN/FLN fan-out, the ILN-on-add) and the switchboard (CAL refusal, MSG relay), so blocked-ness can
    // never disagree between what a member sees and what can reach them. The principal's stored lists
    // decide: the BL always vetoes; the AL always grants; a stranger falls to the BLP default (BL means
    // allow-list-only); and GTC N holds an unauthorized adder (on the RL but not yet the AL) at the door
    // until the member authorizes them. The flag-on defaults (GTC A, BLP AL, empty lists) answer true for
    // everyone, which is exactly the pre-enforcement behavior.
    static bool CanContact(string principalUsername, MsnSession peer)
    {
        if (ListNames(Mind.Db.MsnListGet(principalUsername, "BL"), peer))
        {
            return false;
        }

        if (ListNames(Mind.Db.MsnListGet(principalUsername, "AL"), peer))
        {
            return true;
        }

        var state = Mind.Db.MsnListStateGet(principalUsername);

        if (state.Blp == "BL")
        {
            return false;
        }

        if (state.Gtc == "N" && ListNames(Mind.Db.MsnListGet(principalUsername, "RL"), peer))
        {
            return false;
        }

        return true;
    }

    // Whether a stored list names this session's member. Entries hold whatever the member's client sent,
    // so a bare name and the Passport form of the same account must both match: the presented wire
    // identity, the resolved username, and a hosted name@domain resolving to that username all count.
    static bool ListNames(List<MsnListEntry> entries, MsnSession who)
    {
        foreach (var entry in entries)
        {
            if (entry.Contact.Equals(who.Account, StringComparison.OrdinalIgnoreCase) || entry.Contact.Equals(who.AccountUsername, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry.Contact.Contains('@') && MailDomains.TryResolveLogin(entry.Contact, out var localPart, out _) && localPart.Equals(who.AccountUsername, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static Switchboard GetOrCreateSwitchboard(string sessionId)
    {
        return Switchboards.GetOrAdd(sessionId, id => new Switchboard(id));
    }

    // The account gate compares PRINCIPALS, not strings: the redeemer may present either form of the
    // account the ticket was minted for (bare or Passport on a hosted domain), because which form a client
    // sends depends on how it signed in, not on how the ticket was minted. Names that resolve to no
    // account never match each other this way - an unresolvable ticket stays redeemable only by its exact
    // string - so the widening admits no principal the exact match would have refused.
    static bool IsRedeemableBy(SbInvite invite, string account)
    {
        if (DateTimeOffset.UtcNow - invite.IssuedAt > InviteLifetime)
        {
            return false;
        }

        if (string.Equals(invite.Account, account, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var invitedUsername = ResolveAccountUsername(invite.Account);

        return invitedUsername != null && string.Equals(invitedUsername, ResolveAccountUsername(account), StringComparison.OrdinalIgnoreCase);
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
            // Same resolved-username key HandleUsrAsync registered under.
            var key = session.AccountUsername.ToLowerInvariant();

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
            // Participants key on the resolved username (one member = one slot, whichever form each
            // connection presented), so evict and announce only if THIS session still holds the slot: a
            // rejoin by the same member supersedes the entry, and tearing the superseded connection down
            // must not remove the live session or broadcast a BYE for a member still in the room.
            var removed = false;

            if (session.AccountUsername != null)
            {
                var key = session.AccountUsername.ToLowerInvariant();

                if (sb.Participants.TryGetValue(key, out var current) && ReferenceEquals(current, session))
                {
                    removed = sb.Participants.TryRemove(key, out _);
                }
            }

            if (removed)
            {
                foreach (var participant in sb.Participants.Values.ToArray())
                {
                    _ = SafeSendAsync(participant, $"BYE {session.Account}");
                }
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
