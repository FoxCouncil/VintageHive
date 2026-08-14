// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;

namespace VintageHive.Proxy.Yahoo;

/// <summary>
/// The single table of signed-on Yahoo! identities, shared by every transport.
/// <para>
/// ONE TABLE, DELIBERATELY. It is keyed by session id and matched by username, never by transport. A YMSG
/// client on port 5050 and an HTTP Pager client polling /notify/ are the same account when they carry the same
/// username, so they supersede each other, see each other's presence, and exchange messages. Partitioning this
/// per transport is the failure this class exists to prevent: on a small network the two users ARE the whole
/// population, and two half-visible presence worlds is the exact opposite of the point.
/// </para>
/// </summary>
public static class YahooSessionRegistry
{
    public static readonly ConcurrentDictionary<uint, YahooSession> Sessions = new();

    // Serializes the duplicate-login supersede decision. Without it, two simultaneous logins for the same
    // account can each mark themselves authenticated, then each see the OTHER as the ghost and mutually
    // kick, leaving zero survivors. It spans transports for the same reason it spans connections.
    static readonly SemaphoreSlim AuthGate = new(1, 1);

    public static void Add(YahooSession session)
    {
        Sessions[session.SessionId] = session;
    }

    public static void Remove(YahooSession session)
    {
        Sessions.TryRemove(session.SessionId, out _);
    }

    /// <summary>The live session owning a username, or null. Authenticated sessions only.</summary>
    public static YahooSession GetByUsername(string username)
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

    /// <summary>Every authenticated session except <paramref name="excluding"/>.</summary>
    public static List<YahooSession> Peers(YahooSession excluding)
    {
        var peers = new List<YahooSession>();

        foreach (var session in Sessions.Values.ToArray())
        {
            if (!session.IsAuthenticated || session.SessionId == excluding?.SessionId)
            {
                continue;
            }

            peers.Add(session);
        }

        return peers;
    }

    /// <summary>
    /// Marks <paramref name="session"/> authenticated and evicts any other session holding the same username,
    /// whatever transport it is on. Returns the evicted sessions so the caller can notify and tear them down
    /// AFTER the gate is released - a stalled ghost socket must never block other members signing in.
    /// </summary>
    public static async Task<List<YahooSession>> ClaimIdentityAsync(YahooSession session, string username)
    {
        var superseded = new List<YahooSession>();

        await AuthGate.WaitAsync();

        try
        {
            session.Username = username;
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

        return superseded;
    }

    /// <summary>Notifies and tears down the sessions <see cref="ClaimIdentityAsync"/> evicted.</summary>
    public static async Task SupersedeAllAsync(IEnumerable<YahooSession> superseded)
    {
        foreach (var other in superseded)
        {
            await other.SupersedeAsync();
        }
    }

    // Invisible users are hidden from everyone; extend here if per-buddy privacy is added later.
    public static bool IsInvisibleTo(YahooSession subject, YahooSession observer)
    {
        _ = observer;

        return subject.YahooStatus == YmsgStatus.Invisible;
    }

    // Re-read per operation (like YahooAutoRoster) so a ConfigSet applies without restart.
    internal static bool PersistLists => Mind.Db?.ConfigGet<bool>(Data.Types.ConfigNames.YahooPersistLists) ?? false;

    /// <summary>
    /// The one answer to "may observer perceive subject's presence", shared by the login snapshot on both
    /// transports and the presence fan-out, so what a member sees at sign-on and what reaches them later
    /// can never disagree. Invisibility always hides; with persistent lists the observer's own stored list
    /// must also name the subject (the Yahoo! model: you see the people YOU added - the same predicate
    /// shape as MSN's CanContact/ListNames, so the two protocols cannot disagree about who sees whom).
    /// Flag off keeps the relay-to-all behavior byte for byte.
    /// </summary>
    public static bool MaySee(YahooSession observer, YahooSession subject)
    {
        if (IsInvisibleTo(subject, observer))
        {
            return false;
        }

        return !PersistLists || ListNames(observer.Username, subject.Username);
    }

    // Whether the owner's stored list names this username. Entries hold whatever the member's client sent,
    // so a bare name and a hosted name@domain form of the same account must both match - mirroring
    // MsnServer.ListNames, resolution at use rather than at storage.
    static bool ListNames(string ownerUsername, string subjectUsername)
    {
        foreach (var entry in Mind.Db?.YahooListGet(ownerUsername) ?? new List<Data.Types.YahooListEntry>())
        {
            if (entry.Buddy.Equals(subjectUsername, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry.Buddy.Contains('@') && Data.Types.MailDomains.TryResolveLogin(entry.Buddy, out var localPart, out _) && localPart.Equals(subjectUsername, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The same resolution MSN's surfaces use: whole-string fetch first (an account literally
    /// named with an '@' wins), then the name@domain form against the hosted mail domains. Null when
    /// nothing matches - which callers must treat exactly like a real-but-offline member.</summary>
    public static string ResolveAccountUsername(string presented)
    {
        var user = Mind.Db?.UserFetch(presented);

        if (user == null && presented.Contains('@') && Data.Types.MailDomains.TryResolveLogin(presented, out var localPart, out _))
        {
            user = Mind.Db?.UserFetch(localPart);
        }

        return user?.Username;
    }

    /// <summary>The member's stored roster as (group, buddies) pairs, groups in first-appearance order and
    /// names exactly as the client presented them. Shared by the YMSG LIST field and the Pager account
    /// block, so the two transports keep showing the same buddy list with persistent lists on.</summary>
    public static List<(string Group, List<string> Buddies)> StoredRosterGroups(string username)
    {
        var groups = new List<(string Group, List<string> Buddies)>();

        foreach (var entry in Mind.Db?.YahooListGet(username) ?? new List<Data.Types.YahooListEntry>())
        {
            var slot = groups.FindIndex(g => string.Equals(g.Group, entry.Group, StringComparison.Ordinal));

            if (slot < 0)
            {
                groups.Add((entry.Group, new List<string> { entry.Buddy }));
            }
            else
            {
                groups[slot].Buddies.Add(entry.Buddy);
            }
        }

        return groups;
    }

    /// <summary>Announces <paramref name="subject"/> to every other signed-on session, on any transport.
    /// With persistent lists the fan-out is list-scoped via <see cref="MaySee"/>.</summary>
    public static async Task BroadcastPresenceAsync(YahooSession subject)
    {
        foreach (var other in Peers(subject))
        {
            if (!MaySee(other, subject))
            {
                continue;
            }

            await other.DeliverPresenceAsync(subject);
        }
    }

    /// <summary>Announces <paramref name="subject"/>'s departure to every other signed-on session. Scoped
    /// by the stored list like presence (a member who could never see the subject hears nothing), but NOT
    /// by invisibility - going invisible is announced exactly as a departure.</summary>
    public static async Task BroadcastLogoffAsync(YahooSession subject)
    {
        foreach (var other in Peers(subject))
        {
            if (PersistLists && !ListNames(other.Username, subject.Username))
            {
                continue;
            }

            await other.DeliverLogoffAsync(subject);
        }
    }

    /// <summary>
    /// Delivers an IM to whichever session currently owns <paramref name="to"/>, on whatever transport, and
    /// reports whether it landed. False means the caller should queue it for the recipient's next login.
    /// </summary>
    public static async Task<bool> RelayMessageAsync(string from, string to, string text, string imvironment = null, string imvironmentFlag = null)
    {
        var target = GetByUsername(to);

        if (target == null)
        {
            return false;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // A dead or stalled recipient must not kill (or indefinitely hang) the SENDER's session.
        if (await target.DeliverMessageAsync(from, text, timestamp, offline: false, imvironment, imvironmentFlag))
        {
            return true;
        }

        // The failed (possibly cancelled mid-frame) write may have left the recipient's stream misframed, so
        // drop it rather than leaving a ghost that parses every subsequent packet as garbage.
        target.Drop();

        // The recipient may have been superseded by a fresh login mid-send - including a login that arrived on
        // a different transport - so retry once against the current live session before deferring.
        var retry = GetByUsername(to);

        if (retry != null && retry.SessionId != target.SessionId)
        {
            if (await retry.DeliverMessageAsync(from, text, timestamp, offline: false, imvironment, imvironmentFlag))
            {
                return true;
            }

            retry.Drop();
        }

        return false;
    }

    /// <summary>Relays a typing notification. Ephemeral, so an offline or dead target just drops it.</summary>
    public static async Task RelayTypingAsync(YahooSession from, string to, string kind, string flag, uint status)
    {
        var target = GetByUsername(to);

        if (target == null)
        {
            return;
        }

        await target.DeliverTypingAsync(from, kind, flag, status);
    }

    /// <summary>Every registered account except the caller's own - the server-built hive roster, shared by
    /// YMSG and the HTTP Pager. YahooAutoRoster off turns the server-built roster off at this single seam:
    /// every caller gets an empty answer, and the member's list is what they added. The list packets
    /// themselves still go out (well-formed and empty) because a missing list hangs period clients where an
    /// empty one does not. With persistent lists on, the server-built roster is not a thing at all
    /// (YahooAutoRoster is ignored; the stored rows via <see cref="StoredRosterGroups"/> are the roster),
    /// so this answers empty then too - a caller that forgets to branch serves an empty roster, never the
    /// server-built one. Re-read per call, so a ConfigSet applies without restart.</summary>
    public static List<string> OtherUsernames(string username)
    {
        if (PersistLists)
        {
            return new List<string>();
        }

        if (!(Mind.Db?.ConfigGet<bool>(Data.Types.ConfigNames.YahooAutoRoster) ?? true))
        {
            return new List<string>();
        }

        var users = Mind.Db?.UserList() ?? new List<Data.Types.HiveUser>();

        return users
            .Select(u => u.Username)
            .Where(u => !string.Equals(u, username, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
