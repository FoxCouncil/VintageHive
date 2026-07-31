// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Net;
using VintageHive;
using VintageHive.Proxy.Presence;
using VintageHive.Proxy.Yahoo;

namespace Yahoo;

// A second Yahoo! transport that is not YMSG, standing in for the HTTP Pager before it exists.
//
// The whole point of hoisting Sessions/AuthGate/supersede out of YmsgServer is that identity is shared across
// transports: one screen name, one presence, one delivery path, whichever wire the member is on. That property
// is invisible to a suite where every session is a YmsgSession, because a registry keyed per transport would
// pass all of those tests. This class makes it testable now rather than after the Pager lands and the bug is
// live - a member on YMSG and a member on the Pager unable to see each other is exactly the failure the seam
// exists to prevent.
internal sealed class FakeTransportSession : YahooSession
{
    public override string Transport => "FAKE";

    public ConcurrentQueue<string> Delivered { get; } = new();

    public bool WasSuperseded { get; private set; }

    public bool WasDropped { get; private set; }

    // Set to make every delivery fail, standing in for a peer that has gone away mid-broadcast.
    public bool FailDeliveries { get; set; }

    // Runs at the moment a delivery is attempted, so a test can land a competing login inside that window.
    public Action OnDeliverAttempt { get; set; }

    public override Task<bool> DeliverMessageAsync(string from, string text, long timestamp, bool offline, string imvironment = null, string imvironmentFlag = null)
    {
        return Record($"msg:{from}:{text}:{(offline ? "offline" : "live")}");
    }

    public override Task<bool> DeliverPresenceAsync(YahooSession subject)
    {
        return Record($"presence:{subject.Username}:{subject.YahooStatus}");
    }

    public override Task<bool> DeliverLogoffAsync(YahooSession subject)
    {
        return Record($"logoff:{subject.Username}");
    }

    public override Task<bool> DeliverTypingAsync(YahooSession from, string kind, string flag, uint status)
    {
        return Record($"typing:{from.Username}:{kind}:{flag}:{status}");
    }

    public override Task SupersedeAsync()
    {
        WasSuperseded = true;

        return Task.CompletedTask;
    }

    public override void Drop()
    {
        WasDropped = true;
    }

    Task<bool> Record(string what)
    {
        OnDeliverAttempt?.Invoke();

        if (FailDeliveries)
        {
            return Task.FromResult(false);
        }

        Delivered.Enqueue(what);

        return Task.FromResult(true);
    }

    public bool Got(string prefix) => Delivered.Any(x => x.StartsWith(prefix, StringComparison.Ordinal));

    public static FakeTransportSession SignOn(string username, uint status = YmsgStatus.Available)
    {
        var session = new FakeTransportSession
        {
            Username = username,
            IsAuthenticated = true,
            YahooStatus = status,
        };

        YahooSessionRegistry.Add(session);

        return session;
    }
}

[TestClass]
public class YahooSessionRegistryTests
{
    [TestInitialize]
    public void Setup()
    {
        YmsgTestEnv.Ensure();
        YahooSessionRegistry.Sessions.Clear();

        Mind.Db!.YahooDeleteOfflineMessages("alice");
        Mind.Db!.YahooDeleteOfflineMessages("bob");
    }

    // Session ids are minted on the base class rather than per transport. Two transports handing out their own
    // id spaces would collide in the one flat table and silently evict each other's live sessions.
    [TestMethod]
    public void SessionIds_AreUniqueAcrossTransports()
    {
        var ids = new HashSet<uint>();

        for (var i = 0; i < 25; i++)
        {
            Assert.IsTrue(ids.Add(new FakeTransportSession().SessionId), "A transport reused a session id already handed out.");
        }

        var socketSession = new YmsgSession(null);

        Assert.IsTrue(ids.Add(socketSession.SessionId), "A YMSG session collided with another transport's session id.");
    }

    [TestMethod]
    public void GetByUsername_FindsASessionOnAnyTransport()
    {
        var pager = FakeTransportSession.SignOn("alice");

        var found = YahooSessionRegistry.GetByUsername("alice");

        Assert.AreSame(pager, found, "The registry must resolve a username regardless of which transport holds it.");
        Assert.AreSame(pager, YahooSessionRegistry.GetByUsername("ALICE"), "Username matching must stay case insensitive.");
    }

    [TestMethod]
    public void GetByUsername_IgnoresUnauthenticatedSessions()
    {
        var pending = new FakeTransportSession { Username = "alice", IsAuthenticated = false };

        YahooSessionRegistry.Add(pending);

        Assert.IsNull(YahooSessionRegistry.GetByUsername("alice"), "A session that has not authenticated must not own the account.");
    }

    // The headline invariant Fox asked to be explicit rather than emergent.
    [TestMethod]
    public async Task ClaimIdentity_SupersedesASessionOnADifferentTransport()
    {
        var pager = FakeTransportSession.SignOn("alice");

        var arriving = new FakeTransportSession();

        YahooSessionRegistry.Add(arriving);

        var superseded = await YahooSessionRegistry.ClaimIdentityAsync(arriving, "alice");

        Assert.AreEqual(1, superseded.Count, "A login must evict the account's session on the other transport.");
        Assert.AreSame(pager, superseded[0]);

        await YahooSessionRegistry.SupersedeAllAsync(superseded);

        Assert.IsTrue(pager.WasSuperseded, "The evicted session was never told it lost the account.");
        Assert.AreSame(arriving, YahooSessionRegistry.GetByUsername("alice"));
        Assert.AreEqual(1, YahooSessionRegistry.Sessions.Values.Count(s => s.IsAuthenticated && s.Username == "alice"), "Two sessions both look live for one account.");
    }

    // Same invariant, driven from the real YMSG login path: a period client signing on evicts a session that
    // arrived over a completely different transport.
    [TestMethod]
    [Timeout(15000)]
    public async Task YmsgLogin_SupersedesAnotherTransportsSessionForTheSameAccount()
    {
        var pager = FakeTransportSession.SignOn("alice");

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("alice");

        Assert.IsTrue(pager.WasSuperseded, "A YMSG login did not evict the same account's session on another transport.");
        Assert.IsFalse(YahooSessionRegistry.Sessions.ContainsKey(pager.SessionId), "The evicted session is still in the registry and will eat relayed messages.");
        Assert.AreEqual(1, YahooSessionRegistry.Sessions.Values.Count(s => s.IsAuthenticated && string.Equals(s.Username, "alice", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ClaimIdentity_LeavesOtherAccountsAlone()
    {
        var bob = FakeTransportSession.SignOn("bob");

        var arriving = new FakeTransportSession();

        YahooSessionRegistry.Add(arriving);

        var superseded = await YahooSessionRegistry.ClaimIdentityAsync(arriving, "alice");

        Assert.AreEqual(0, superseded.Count);
        Assert.IsFalse(bob.WasSuperseded, "Signing in as alice evicted bob.");
    }

    // Presence has to cross the transport boundary in both directions, or the two members on a small network
    // each see an empty buddy list.
    [TestMethod]
    [Timeout(15000)]
    public async Task Presence_CrossesTheTransportBoundaryInBothDirections()
    {
        var pager = FakeTransportSession.SignOn("bob");

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        // alice signs on over YMSG: the other transport's session must be told.
        await socket.LoginAsync("alice");

        // LoginAsync returns once LIST and the initial roster snapshot have been read, but the outbound
        // presence broadcast is sent AFTER those. The handler loop is sequential, so a ping round-trip is what
        // proves sign-on finished rather than racing the assertion against it.
        await socket.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        await socket.ReadAsync();

        Assert.IsTrue(pager.Got("presence:alice"), "A YMSG sign-on was not announced to the session on the other transport.");

        // ...and the YMSG client's own initial roster snapshot must have counted the other transport's session.
        // LoginAsync drains LIST then the initial LOGON, so re-drive it here to read that packet's contents.
        using var second = new YmsgConn(server);

        await second.SendAsync(new YmsgPacket(YmsgService.Verify, 0, 0));
        await second.ReadAsync();

        await second.SendAsync(new YmsgPacket(YmsgService.Auth, 0, 0).Add(1, "bob"));

        var challenge = await second.ReadAsync();
        var answer = YmsgCrypt.ComputeAuthResponse(challenge.Get(94), YmsgTestEnv.Password);

        await second.SendAsync(new YmsgPacket(YmsgService.AuthResp, 0, 0).Add(0, "bob").Add(6, answer.Resp6).Add(96, answer.Resp96));

        await second.ReadAsync(); // LIST

        var roster = await second.ReadAsync();

        Assert.AreEqual(YmsgService.Logon, roster.Service);
        Assert.AreEqual("alice", roster.Get(7), "The initial presence snapshot did not include the peer signed on over YMSG.");
    }

    [TestMethod]
    public async Task BroadcastPresence_SkipsInvisibleSubjectsOnEveryTransport()
    {
        var watcher = FakeTransportSession.SignOn("bob");

        var ghost = FakeTransportSession.SignOn("alice", YmsgStatus.Invisible);

        await YahooSessionRegistry.BroadcastPresenceAsync(ghost);

        Assert.IsFalse(watcher.Got("presence:alice"), "An invisible member was announced to a peer on another transport.");
    }

    [TestMethod]
    public async Task BroadcastLogoff_ReachesEveryTransportButTheSubject()
    {
        var watcher = FakeTransportSession.SignOn("bob");

        var leaving = FakeTransportSession.SignOn("alice");

        await YahooSessionRegistry.BroadcastLogoffAsync(leaving);

        Assert.IsTrue(watcher.Got("logoff:alice"));
        Assert.IsFalse(leaving.Got("logoff:alice"), "A departing session was told about its own departure.");
    }

    // Delivery, the other half of "the sessions want to be the same sessions".
    [TestMethod]
    [Timeout(15000)]
    public async Task Message_FromYmsg_ReachesARecipientOnAnotherTransport()
    {
        var pager = FakeTransportSession.SignOn("bob");

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("alice");

        await socket.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "bob").Add(14, "hello over there"));

        // The handler loop is sequential, so a ping round-trip proves the message was fully processed.
        await socket.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        await socket.ReadAsync();

        Assert.IsTrue(pager.Got("msg:alice:hello over there:live"), "A YMSG member's IM never reached the recipient on the other transport.");
        Assert.AreEqual(0, Mind.Db!.YahooGetOfflineMessages("bob").Count, "The IM was queued offline even though the recipient was signed on, just on another transport.");
    }

    [TestMethod]
    public async Task RelayMessage_ToAnOfflineAccount_ReportsFailureSoTheCallerCanQueue()
    {
        Assert.IsFalse(await YahooSessionRegistry.RelayMessageAsync("alice", "bob", "nobody home"));
    }

    // A failed delivery drops the dead session and retries against whatever now owns the account. The window
    // this covers is narrow and real: the recipient is superseded by a fresh login WHILE the relay is mid-send,
    // so the session the relay resolved a moment ago is already the ghost. The competing login is fired from
    // inside the delivery attempt to reproduce that ordering deterministically, and it arrives on a different
    // transport - a Pager sign-on landing during a YMSG relay is precisely the shape this has to survive.
    [TestMethod]
    public async Task RelayMessage_RetriesAgainstASessionThatSupersededTheDeadOneMidSend()
    {
        var dying = FakeTransportSession.SignOn("bob");

        dying.FailDeliveries = true;

        var replacement = new FakeTransportSession();

        YahooSessionRegistry.Add(replacement);

        dying.OnDeliverAttempt = () => YahooSessionRegistry.ClaimIdentityAsync(replacement, "bob").GetAwaiter().GetResult();

        var delivered = await YahooSessionRegistry.RelayMessageAsync("alice", "bob", "retry me");

        Assert.IsTrue(dying.WasDropped, "The failed session was left in place to misframe every later packet.");
        Assert.IsTrue(delivered, "The relay gave up instead of retrying against the session that superseded the dead one.");
        Assert.IsTrue(replacement.Got("msg:alice:retry me"), "The retry did not land on the live session.");
    }

    // The other side of that coin, pinned so the fallback is not mistaken for a bug later: when the failed
    // session is still the registered owner (its own teardown has not run yet), there is nothing to retry
    // against, so the message defers to offline storage rather than being dropped.
    [TestMethod]
    public async Task RelayMessage_ToAFailingSessionThatStillOwnsTheAccount_DefersToOfflineStorage()
    {
        var dying = FakeTransportSession.SignOn("bob");

        dying.FailDeliveries = true;

        Assert.IsFalse(await YahooSessionRegistry.RelayMessageAsync("alice", "bob", "queue me"), "A failed delivery with no live replacement must report failure so the caller queues the message.");
        Assert.IsTrue(dying.WasDropped);
    }

    [TestMethod]
    public async Task RelayTyping_CrossesTransportsAndCarriesTheHeaderStatus()
    {
        var pager = FakeTransportSession.SignOn("bob");

        var from = FakeTransportSession.SignOn("alice");

        await YahooSessionRegistry.RelayTypingAsync(from, "bob", "TYPING", "1", 22);

        Assert.IsTrue(pager.Got("typing:alice:TYPING:1:22"), "The typing relay lost the originating header status or never crossed transports.");
    }

    [TestMethod]
    public async Task RelayTyping_ToAnOfflineAccount_IsSilentlyDropped()
    {
        var from = FakeTransportSession.SignOn("alice");

        await YahooSessionRegistry.RelayTypingAsync(from, "bob", "TYPING", "1", 0);
    }

    // Finger and the dashboard read the shared registry, so a member on any transport has to show up there.
    [TestMethod]
    public void PresenceRegistry_SeesSessionsFromEveryTransport()
    {
        PresenceRegistry.Register(new YahooPresenceProvider());

        FakeTransportSession.SignOn("alice");

        var entry = PresenceRegistry.Find("alice");

        Assert.IsNotNull(entry, "A member signed on over a non-YMSG transport is invisible to cross-protocol consumers.");
        Assert.AreEqual("Yahoo", entry.Network);
        Assert.AreEqual(PresenceStatus.Online, entry.Status);
    }

    [TestMethod]
    public void PresenceRegistry_StillHidesInvisibleSessionsOnEveryTransport()
    {
        PresenceRegistry.Register(new YahooPresenceProvider());

        FakeTransportSession.SignOn("bob", YmsgStatus.Invisible);

        Assert.IsNull(PresenceRegistry.Find("bob"), "An invisible member on another transport leaked into the presence registry.");
    }

    // Two simultaneous logins for one account must leave exactly one survivor. Without the gate they can each
    // see the OTHER as the ghost and mutually kick, leaving zero - and the gate now has to span transports.
    [TestMethod]
    public async Task ConcurrentLoginsAcrossTransports_LeaveExactlyOneSurvivor()
    {
        var contenders = Enumerable.Range(0, 8).Select(_ => new FakeTransportSession()).ToList();

        foreach (var contender in contenders)
        {
            YahooSessionRegistry.Add(contender);
        }

        var claims = contenders.Select(c => Task.Run(async () =>
        {
            await YahooSessionRegistry.SupersedeAllAsync(await YahooSessionRegistry.ClaimIdentityAsync(c, "alice"));
        }));

        await Task.WhenAll(claims);

        Assert.AreEqual(1, YahooSessionRegistry.Sessions.Values.Count(s => s.IsAuthenticated && s.Username == "alice"), "Concurrent logins left zero or multiple survivors for one account.");
        Assert.IsNotNull(YahooSessionRegistry.GetByUsername("alice"));
    }
}
