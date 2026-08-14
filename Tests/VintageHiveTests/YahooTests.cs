// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Network;
using VintageHive.Proxy.Presence;
using VintageHive.Proxy.Yahoo;

namespace Yahoo;

internal static class YmsgTestEnv
{
    private static readonly object Gate = new();
    private static bool _ready;

    // The password the seeded test accounts are created with. Logins must now actually prove knowledge of it.
    public const string Password = "secret";

    public static void Ensure()
    {
        lock (Gate)
        {
            if (_ready)
            {
                return;
            }

            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "vfs", "data"));

            if (Mind.Db == null)
            {
                var setter = typeof(Mind).GetProperty(nameof(Mind.Db))!.GetSetMethod(nonPublic: true)!;
                setter.Invoke(null, new object[] { new HiveDbContext() });
            }

            foreach (var user in new[] { "alice", "bob" })
            {
                if (!Mind.Db!.UserExistsByUsername(user))
                {
                    Mind.Db.UserCreate(user, Password);
                }
            }

            _ready = true;
        }
    }
}

// Drives the real YmsgServer.ProcessConnection over a loopback socket, mirroring the SOCKS/Gopher harnesses.
internal sealed class YmsgConn : IDisposable
{
    private readonly Socket _client;
    private readonly Socket _server;
    private readonly Task _handler;

    public NetworkStream ClientStream { get; }

    public YmsgConn(YmsgServer server)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        var acceptTask = listener.AcceptSocketAsync();

        _client.Connect(IPAddress.Loopback, port);

        _server = acceptTask.GetAwaiter().GetResult();

        listener.Stop();

        ClientStream = new NetworkStream(_client);

        var connection = new ListenerSocket
        {
            RawSocket = _server,
            Stream = new NetworkStream(_server),
        };

        _handler = Task.Run(() => server.ProcessConnection(connection));
    }

    public async Task SendAsync(YmsgPacket packet)
    {
        await ClientStream.WriteAsync(packet.Encode());
    }

    public async Task<YmsgPacket> ReadAsync(int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        var header = new byte[YmsgPacket.HeaderSize];

        await ReadExact(header, header.Length, cts.Token);

        var bodyLength = YmsgPacket.BodyLength(header);
        var body = new byte[bodyLength];

        if (bodyLength > 0)
        {
            await ReadExact(body, bodyLength, cts.Token);
        }

        return YmsgPacket.Decode(header, body);
    }

    private async Task ReadExact(byte[] buffer, int count, CancellationToken ct)
    {
        var offset = 0;

        while (offset < count)
        {
            var read = await ClientStream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);

            if (read <= 0)
            {
                throw new IOException("Connection closed mid-packet");
            }

            offset += read;
        }
    }

    // Runs the VERIFY/AUTH/AUTHRESP handshake and drains the resulting LIST + initial LOGON, handing the
    // LIST packet back so roster-shape tests can assert on field 87. The AUTHRESP is a real crypt response
    // computed from the server's own challenge, the way a period client would answer - the server verifies
    // it now, so junk placeholders no longer get in.
    public async Task<YmsgPacket> LoginAsync(string username, string password = YmsgTestEnv.Password)
    {
        await SendAsync(new YmsgPacket(YmsgService.Verify, 0, 0));
        await ReadAsync(); // VERIFY echo

        await SendAsync(new YmsgPacket(YmsgService.Auth, 0, 0).Add(1, username));

        var challenge = await ReadAsync();

        var seed = challenge.Get(94);

        Assert.IsFalse(string.IsNullOrEmpty(seed), "Server did not send a challenge seed in field 94");

        var response = YmsgCrypt.ComputeAuthResponse(seed, password);

        Assert.IsNotNull(response, $"Server issued a challenge a real client could not process: {seed}");

        await SendAsync(new YmsgPacket(YmsgService.AuthResp, 0, 0).Add(0, username).Add(6, response.Resp6).Add(96, response.Resp96));

        var list = await ReadAsync();

        Assert.AreEqual(YmsgService.List, list.Service, "Expected buddy LIST after auth");

        var logon = await ReadAsync();

        Assert.AreEqual(YmsgService.Logon, logon.Service, "Expected initial LOGON presence after LIST");

        return list;
    }

    public void Dispose()
    {
        // Close the client so the server loop hits EOF, then wait for the handler (and its logoff broadcast)
        // to finish before the next test so static session state doesn't leak across tests.
        try { ClientStream.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
        try { _handler.Wait(3000); } catch { }
        try { _server.Dispose(); } catch { }
    }
}

[TestClass]
public class YmsgPacketTests
{
    [TestMethod]
    public void Encode_ProducesYmsgMagicAndCorrectBodyLength()
    {
        var bytes = new YmsgPacket(YmsgService.Message, 0, 0x1234).Add(5, "bob").Add(14, "hi").Encode();

        Assert.AreEqual("YMSG", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.IsTrue(YmsgPacket.HasMagic(bytes));

        // Body length in the header must exclude the 20-byte header.
        Assert.AreEqual(bytes.Length - YmsgPacket.HeaderSize, YmsgPacket.BodyLength(bytes));
    }

    [TestMethod]
    public void EncodeDecode_RoundTripsHeaderAndFields()
    {
        var original = new YmsgPacket(YmsgService.Logon, 7, 0xABCD, 0x000A)
            .Add(0, "alice")
            .Add(7, "bob")
            .Add(10, "0");

        var bytes = original.Encode();
        var header = bytes[..YmsgPacket.HeaderSize];
        var body = bytes[YmsgPacket.HeaderSize..];

        var decoded = YmsgPacket.Decode(header, body);

        Assert.AreEqual(YmsgService.Logon, decoded.Service);
        Assert.AreEqual(7u, decoded.Status);
        Assert.AreEqual(0xABCDu, decoded.SessionId);
        Assert.AreEqual((ushort)0x000A, decoded.Version);
        Assert.AreEqual("alice", decoded.Get(0));
        Assert.AreEqual("bob", decoded.Get(7));
        Assert.AreEqual("0", decoded.Get(10));
    }

    [TestMethod]
    public void Decode_UsesC080SeparatorAndPreservesUtf8Values()
    {
        var bytes = new YmsgPacket(YmsgService.Message, 0, 1).Add(14, "café ☕").Encode();

        // The separator is the two-byte 0xC0 0x80 overlong NUL.
        Assert.IsTrue(ContainsSequence(bytes, new byte[] { 0xC0, 0x80 }));

        var decoded = YmsgPacket.Decode(bytes[..YmsgPacket.HeaderSize], bytes[YmsgPacket.HeaderSize..]);

        Assert.AreEqual("café ☕", decoded.Get(14));
    }

    [TestMethod]
    public void Decode_DuplicateKeys_ArePreservedInOrder()
    {
        var bytes = new YmsgPacket(YmsgService.Logon, 0, 1).Add(7, "alice").Add(7, "bob").Encode();

        var decoded = YmsgPacket.Decode(bytes[..YmsgPacket.HeaderSize], bytes[YmsgPacket.HeaderSize..]);

        var buddies = decoded.Fields.Where(f => f.Key == 7).Select(f => f.Value).ToList();

        CollectionAssert.AreEqual(new[] { "alice", "bob" }, buddies);
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}

[TestClass]
public class YmsgHelperTests
{
    [TestMethod]
    public void BuildRosterField_GroupsBuddiesUnderHive()
    {
        Assert.AreEqual("Hive:bob,carol\n", YmsgServer.BuildRosterField(new[] { "bob", "carol" }));
    }

    [TestMethod]
    public void BuildRosterField_Empty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, YmsgServer.BuildRosterField(System.Array.Empty<string>()));
    }

    [TestMethod]
    public void BuildStoredRosterField_PreservesGroupNamesAndOrder()
    {
        var groups = new List<(string Group, List<string> Buddies)>
        {
            ("Friends", new List<string> { "bob", "carol" }),
            ("Work Pals", new List<string> { "dave" }),
        };

        Assert.AreEqual("Friends:bob,carol\nWork Pals:dave\n", YmsgServer.BuildStoredRosterField(groups));
    }

    [TestMethod]
    public void BuildStoredRosterField_Empty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, YmsgServer.BuildStoredRosterField(new List<(string Group, List<string> Buddies)>()));
    }

    [TestMethod]
    public void MapToPresenceStatus_MapsYahooCodes()
    {
        Assert.AreEqual(PresenceStatus.Online, YmsgServer.MapToPresenceStatus(YmsgStatus.Available));
        Assert.AreEqual(PresenceStatus.Busy, YmsgServer.MapToPresenceStatus(YmsgStatus.Busy));
        Assert.AreEqual(PresenceStatus.OnThePhone, YmsgServer.MapToPresenceStatus(YmsgStatus.OnPhone));
        Assert.AreEqual(PresenceStatus.Invisible, YmsgServer.MapToPresenceStatus(YmsgStatus.Invisible));
        Assert.AreEqual(PresenceStatus.Idle, YmsgServer.MapToPresenceStatus(YmsgStatus.Idle));
        Assert.AreEqual(PresenceStatus.Away, YmsgServer.MapToPresenceStatus(YmsgStatus.OnVacation));
    }
}

[TestClass]
public class YmsgServerTests
{
    [TestInitialize]
    public void Setup()
    {
        YmsgTestEnv.Ensure();
        YmsgServer.Sessions.Clear();

        // Offline-message queues are per-account DB state; flush them so one test's undelivered messages
        // don't surface in another test's login.
        Mind.Db!.YahooDeleteOfflineMessages("alice");
        Mind.Db!.YahooDeleteOfflineMessages("bob");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Login_UnknownUser_IsRejected()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var conn = new YmsgConn(server);

        await conn.SendAsync(new YmsgPacket(YmsgService.Auth, 0, 0).Add(1, "nobody"));
        await conn.ReadAsync(); // AUTH challenge

        await conn.SendAsync(new YmsgPacket(YmsgService.AuthResp, 0, 0).Add(0, "nobody").Add(6, "x").Add(96, "y"));

        var reply = await conn.ReadAsync();

        Assert.AreEqual(YmsgService.AuthResp, reply.Service);
        Assert.AreEqual(YmsgStatus.LoginError, reply.Status, "Unknown account must get a login error");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Login_KnownUser_GetsListAndPresence()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var conn = new YmsgConn(server);

        // LoginAsync asserts LIST then LOGON arrive.
        await conn.LoginAsync("alice");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Login_AutoRosterOff_ListPacketIsPresentAndEmptyAndSignOnCompletes()
    {
        Mind.Db!.ConfigSet(VintageHive.Data.Types.ConfigNames.YahooAutoRoster, false);

        try
        {
            var server = new YmsgServer(IPAddress.Loopback, 0);

            using var conn = new YmsgConn(server);

            await conn.SendAsync(new YmsgPacket(YmsgService.Verify, 0, 0));
            await conn.ReadAsync();

            await conn.SendAsync(new YmsgPacket(YmsgService.Auth, 0, 0).Add(1, "alice"));

            var challenge = await conn.ReadAsync();
            var answer = YmsgCrypt.ComputeAuthResponse(challenge.Get(94), YmsgTestEnv.Password);

            await conn.SendAsync(new YmsgPacket(YmsgService.AuthResp, 0, 0).Add(0, "alice").Add(6, answer.Resp6).Add(96, answer.Resp96));

            // The buddy-list packet must still be SENT - a missing LIST hangs period clients - just with an
            // empty roster field.
            var list = await conn.ReadAsync();

            Assert.AreEqual(YmsgService.List, list.Service, "The LIST packet must still be sent with the roster off");
            Assert.IsTrue(string.IsNullOrEmpty(list.Get(87)), $"The roster field must be empty with the roster off, got: {list.Get(87)}");

            // ...and sign-on still completes: the initial presence snapshot follows as always.
            var logon = await conn.ReadAsync();

            Assert.AreEqual(YmsgService.Logon, logon.Service, "Sign-on must still complete with the roster off");
        }
        finally
        {
            Mind.Db!.ConfigSet(VintageHive.Data.Types.ConfigNames.YahooAutoRoster, true);
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task SecondUserLogin_BroadcastsPresenceToFirst()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");

        // Alice must receive a LOGON announcing bob.
        var presence = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.Logon, presence.Service);
        Assert.AreEqual("bob", presence.Get(7));
        Assert.AreEqual("1", presence.Get(13), "Online flag");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Message_IsRelayedToTargetWithSenderInField4()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");

        await alice.ReadAsync(); // drain bob's arrival LOGON

        await alice.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "bob").Add(14, "hello bob"));

        var delivered = await bob.ReadAsync();

        Assert.AreEqual(YmsgService.Message, delivered.Service);
        Assert.AreEqual("alice", delivered.Get(4), "Sender must be rewritten into field 4");
        Assert.IsNull(delivered.Get(1), "Field 1 must be absent or period clients attribute the message to the recipient");
        Assert.AreEqual("bob", delivered.Get(5));
        Assert.AreEqual("hello bob", delivered.Get(14));
        Assert.AreEqual(";0", delivered.Get(63), "Delivery must carry the IMVironment name even when the sender omitted it");
        Assert.AreEqual("0", delivered.Get(64), "Delivery must carry the IMVironment flag even when the sender omitted it");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Message_RelayEchoesTheSendersImvironmentPair()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");

        await alice.ReadAsync(); // drain bob's arrival LOGON

        await alice.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "bob").Add(14, "doodle with me").Add(97, "1").Add(63, "doodle;11").Add(64, "1"));

        var delivered = await bob.ReadAsync();

        Assert.AreEqual(YmsgService.Message, delivered.Service);
        Assert.AreEqual("doodle;11", delivered.Get(63), "The sender's IMVironment name must be relayed, not replaced with the plain-window default");
        Assert.AreEqual("1", delivered.Get(64), "The sender's IMVironment flag must be relayed, not replaced with the plain-window default");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task GoingInvisible_BroadcastsLogoffToPeers()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");

        await alice.ReadAsync(); // drain bob's arrival LOGON

        // Bob goes invisible; alice must be told he went offline (not left showing him online).
        await bob.SendAsync(new YmsgPacket(YmsgService.IsAway, 0, 0).Add(10, YmsgStatus.Invisible.ToString()));

        var notice = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.Logoff, notice.Service, "Invisible transition must broadcast a LOGOFF to peers");
        Assert.AreEqual("bob", notice.Get(7));
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AuthenticatedUser_AppearsInPresenceRegistry()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        PresenceRegistry.Register(new YahooPresenceProvider());

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        var entry = PresenceRegistry.Find("alice");

        Assert.IsNotNull(entry, "Yahoo user should be visible via the shared presence registry");
        Assert.AreEqual("Yahoo", entry.Network);
        Assert.AreEqual(PresenceStatus.Online, entry.Status);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Message_ToOfflineUser_IsQueuedAndDeliveredOnLogin()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using (var alice = new YmsgConn(server))
        {
            await alice.LoginAsync("alice");

            // Bob exists but is offline; the message must queue instead of vanishing.
            await alice.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "bob").Add(14, "read this later"));

            // A ping round-trip proves the message was processed before we move on.
            await alice.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
            await alice.ReadAsync();
        }

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");

        var delivered = await bob.ReadAsync();

        Assert.AreEqual(YmsgService.Message, delivered.Service);
        // The one delivery that is NOT status 1. A live Message must be 1 or YM 5.5 discards it, but the
        // at-login offline flush keeps 5, matching Escargot's OIM-at-login shape. Mid-session status 5 is
        // dropped by the client; at-login is a different, accepted window. Do not "unify" these two.
        Assert.AreEqual(YmsgStatus.OfflineMessage, delivered.Status, "Offline delivery must carry the offline header status");
        Assert.AreEqual("alice", delivered.Get(4), "Sender must be in field 4");
        Assert.IsNull(delivered.Get(1), "Field 1 must be absent or period clients attribute the message to the recipient");
        Assert.AreEqual("read this later", delivered.Get(14));
        Assert.AreEqual(";0", delivered.Get(63), "The offline flush must carry the IMVironment pair too");
        Assert.AreEqual("0", delivered.Get(64), "The offline flush must carry the IMVironment pair too");

        // The server deletes the queue AFTER the send we just read, so synchronize on a ping round-trip
        // (the handler loop is sequential) before asserting the flush committed.
        await bob.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        await bob.ReadAsync();

        // The queue must be flushed after delivery so the message is not redelivered on the next login.
        Assert.AreEqual(0, Mind.Db!.YahooGetOfflineMessages("bob").Count);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task DuplicateLogin_SupersedesThePriorSession()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var first = new YmsgConn(server);

        await first.LoginAsync("alice");

        using var second = new YmsgConn(server);

        await second.LoginAsync("alice");

        // The first connection is told it was superseded, then dropped.
        var notice = await first.ReadAsync();

        Assert.AreEqual(YmsgService.Logoff, notice.Service);
        Assert.AreEqual(YmsgStatus.Duplicate, notice.Status, "The superseded session must get a duplicate-login logoff");

        // Exactly one live alice session remains, so IMs cannot route to the zombie.
        Assert.AreEqual(1, YmsgServer.Sessions.Values.Count(s => s.IsAuthenticated && string.Equals(s.Username, "alice", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Message_ToDeadRecipient_DoesNotKillTheSender_AndFallsBackToOfflineStorage()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        // A dead-but-still-registered recipient: no handler task owns this session, so it cannot be torn
        // down before the relay attempts the send, and its disposed socket fails the write immediately.
        // This pins the TrySendAsync relay path deterministically (a real crashed peer would race its own
        // teardown and could dodge the relay entirely).
        var zombie = MakeDeadSession("bob");

        YmsgServer.Sessions[zombie.SessionId] = zombie;

        await alice.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "bob").Add(14, "anyone home?"));

        // The sender's session must survive the failed relay: a ping still round-trips, and the handler
        // loop is sequential so the pong also proves the message was fully processed.
        await alice.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));

        var pong = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.Ping, pong.Service, "Sender must remain connected after messaging a dead recipient");

        // The undeliverable message must fall back to offline storage instead of vanishing.
        var queued = Mind.Db!.YahooGetOfflineMessages("bob");

        Assert.AreEqual(1, queued.Count);
        Assert.AreEqual("alice", queued[0].FromUsername);
        Assert.AreEqual("anyone home?", queued[0].Message);
    }

    private static YmsgSession MakeDeadSession(string username)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var acceptTask = listener.AcceptSocketAsync();

        client.Connect(IPAddress.Loopback, port);

        var serverSocket = acceptTask.GetAwaiter().GetResult();

        listener.Stop();

        var connection = new ListenerSocket
        {
            RawSocket = serverSocket,
            Stream = new NetworkStream(serverSocket),
        };

        var session = new YmsgSession(connection)
        {
            Username = username,
            IsAuthenticated = true,
        };

        // Kill both ends so any write to the session fails immediately and deterministically.
        connection.Stream.Dispose();
        serverSocket.Dispose();
        client.Dispose();

        return session;
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Notify_TypingIndicator_IsRelayedToTarget()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");
        await alice.ReadAsync(); // drain bob's arrival LOGON

        // 22 (0x16) is what a 5.5 client stamps on its OUTGOING typing notification. This test used to assert
        // that value was echoed through to the recipient, which pinned the bug rather than the behaviour: an
        // A/B probe against a signed-on 5.5.0.1244 session showed the client silently discards any
        // server-to-client delivery whose header status is not 1, so the "faithfully relayed" 22 was landing
        // on the floor. Escargot sends BRB(1) on every live Notify for the same reason.
        await alice.SendAsync(new YmsgPacket(YmsgService.Notify, 22, 0).Add(1, "alice").Add(5, "bob").Add(49, "TYPING").Add(13, "1").Add(14, " "));

        var notify = await bob.ReadAsync();

        Assert.AreEqual(YmsgService.Notify, notify.Service);
        Assert.AreEqual(YmsgStatus.LiveDelivery, notify.Status, "A live Notify must go out as status 1 regardless of what the sender stamped, or YM 5.5 drops it.");
        Assert.AreEqual("alice", notify.Get(4), "Sender must be rewritten into field 4");
        Assert.AreEqual("TYPING", notify.Get(49));
        Assert.AreEqual("1", notify.Get(13));
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AddBuddy_IsAcknowledgedWithSuccess()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        await alice.SendAsync(new YmsgPacket(YmsgService.AddBuddy, 0, 0).Add(1, "alice").Add(7, "bob").Add(65, "Hive"));

        var ack = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.AddBuddy, ack.Service);
        Assert.AreEqual("bob", ack.Get(7));
        Assert.AreEqual("Hive", ack.Get(65));
        Assert.AreEqual("0", ack.Get(66), "Field 66 = 0 means success");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task InvisibleUser_IsHiddenFromThePresenceRegistry()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        PresenceRegistry.Register(new YahooPresenceProvider());

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        Assert.IsNotNull(PresenceRegistry.Find("alice"), "Sanity: alice is visible while Available");

        await alice.SendAsync(new YmsgPacket(YmsgService.IsAway, 0, 0).Add(10, YmsgStatus.Invisible.ToString()));

        // A ping round-trip guarantees the status change was processed before asserting.
        await alice.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        await alice.ReadAsync();

        Assert.IsNull(PresenceRegistry.Find("alice"), "Invisible users must not be exposed via the presence registry");
        Assert.IsFalse(PresenceRegistry.Online().Any(e => e.Network == "Yahoo" && e.Username == "alice"), "Invisible users must not appear in the online list");
    }
}

[TestClass]
public class YmsgPersistListsTests
{
    [TestInitialize]
    public void Setup()
    {
        YmsgTestEnv.Ensure();
        YmsgServer.Sessions.Clear();

        // Same hygiene as the MSN twin: the list store is on-disk sqlite that outlives the process, so
        // rows and serials left by a prior run must not leak in, and stale offline messages must not
        // surface into a login read.
        foreach (var owner in new[] { "alice", "bob" })
        {
            Mind.Db!.YahooListReset(owner);
            Mind.Db!.YahooDeleteOfflineMessages(owner);
        }

        Mind.Db!.ConfigSet(VintageHive.Data.Types.ConfigNames.YahooPersistLists, true);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Mind.Db!.ConfigSet(VintageHive.Data.Types.ConfigNames.YahooPersistLists, false);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AddBuddy_PersistsAndSurvivesReconnect()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using (var alice = new YmsgConn(server))
        {
            await alice.LoginAsync("alice");

            await alice.SendAsync(new YmsgPacket(YmsgService.AddBuddy, 0, 0).Add(1, "alice").Add(7, "bob").Add(65, "Friends"));

            var ack = await alice.ReadAsync();

            Assert.AreEqual(YmsgService.AddBuddy, ack.Service);
            Assert.AreEqual("bob", ack.Get(7));
            Assert.AreEqual("Friends", ack.Get(65), "The ack must echo the client's own group, never a server-invented default");
            Assert.AreEqual("0", ack.Get(66));
        }

        // A fresh connection: the roster must come back from storage, not from the server-built list.
        using var again = new YmsgConn(server);

        var list = await again.LoginAsync("alice");

        Assert.AreEqual("Friends:bob\n", list.Get(87), "The stored roster must be served back on the next login");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task GroupName_RoundTripsExactlyAsSent()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using (var alice = new YmsgConn(server))
        {
            await alice.LoginAsync("alice");

            await alice.SendAsync(new YmsgPacket(YmsgService.AddBuddy, 0, 0).Add(1, "alice").Add(7, "bob").Add(65, "My School Pals"));

            var ack = await alice.ReadAsync();

            Assert.AreEqual("My School Pals", ack.Get(65), "The group must round-trip exactly as the client sent it");
        }

        using var again = new YmsgConn(server);

        var list = await again.LoginAsync("alice");

        Assert.AreEqual("My School Pals:bob\n", list.Get(87), "The stored group name must survive verbatim into the roster field");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task RemoveBuddy_DeletesTheStoredEntry()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using (var alice = new YmsgConn(server))
        {
            await alice.LoginAsync("alice");

            await alice.SendAsync(new YmsgPacket(YmsgService.AddBuddy, 0, 0).Add(1, "alice").Add(7, "bob").Add(65, "Friends"));
            await alice.ReadAsync(); // ADD ack

            await alice.SendAsync(new YmsgPacket(YmsgService.RemoveBuddy, 0, 0).Add(1, "alice").Add(7, "bob").Add(65, "Friends"));

            var ack = await alice.ReadAsync();

            Assert.AreEqual(YmsgService.RemoveBuddy, ack.Service);
            Assert.AreEqual("bob", ack.Get(7));
            Assert.AreEqual("0", ack.Get(66));
        }

        Assert.AreEqual(0, Mind.Db!.YahooListGet("alice").Count, "The remove must delete the stored row");

        using var again = new YmsgConn(server);

        var list = await again.LoginAsync("alice");

        Assert.IsTrue(string.IsNullOrEmpty(list.Get(87)), $"A removed buddy must not come back at the next login, got: {list.Get(87)}");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AddOnlineBuddy_PushesTheirPresenceImmediately()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");

        // Bob's ping round-trip proves his sign-on fan-out already ran (and skipped alice, whose list
        // omits him). Without it, a delayed arrival broadcast could re-read alice's list AFTER the add
        // lands and deliver a LOGON that races ahead of the ADD ack.
        await bob.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        await bob.ReadAsync();

        // The next packets alice reads are the ADD ack and then the immediate presence push - the YMSG
        // twin of MSN's ILN-on-add.
        await alice.SendAsync(new YmsgPacket(YmsgService.AddBuddy, 0, 0).Add(1, "alice").Add(7, "bob").Add(65, "Friends"));

        var ack = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.AddBuddy, ack.Service);
        Assert.AreEqual("0", ack.Get(66));

        var presence = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.Logon, presence.Service, "A live buddy must light up at ADD, not at their next status change");
        Assert.AreEqual("bob", presence.Get(7));
        Assert.AreEqual("1", presence.Get(13), "Online flag");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Presence_NotDeliveredWhenTheListOmitsThePrincipal()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");

        // Bob's ping round-trip proves his handler finished the sign-on fan-out; had a broadcast wrongly
        // gone to alice it would already sit in her stream ahead of the pong she reads next.
        await bob.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        await bob.ReadAsync();

        await alice.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));

        var reply = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.Ping, reply.Service, "A session whose list omits the principal must not receive their presence");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AddUnknownName_AcksIdenticallyToARealButOfflineOne()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        // The unknown name and the real-but-offline member must be observationally identical: same ack
        // shape, and in both cases the next reply after a ping is the pong - no presence, no error.
        await alice.SendAsync(new YmsgPacket(YmsgService.AddBuddy, 0, 0).Add(1, "alice").Add(7, "ghost").Add(65, "Friends"));

        var ghostAck = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.AddBuddy, ghostAck.Service);
        Assert.AreEqual("ghost", ghostAck.Get(7));
        Assert.AreEqual("Friends", ghostAck.Get(65));
        Assert.AreEqual("0", ghostAck.Get(66), "An unknown name must ack in exactly the shape a real one does");

        await alice.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        Assert.AreEqual(YmsgService.Ping, (await alice.ReadAsync()).Service, "Nothing may follow the ack that distinguishes the unknown name");

        await alice.SendAsync(new YmsgPacket(YmsgService.AddBuddy, 0, 0).Add(1, "alice").Add(7, "bob").Add(65, "Friends"));

        var bobAck = await alice.ReadAsync();

        Assert.AreEqual("bob", bobAck.Get(7));
        Assert.AreEqual("Friends", bobAck.Get(65));
        Assert.AreEqual("0", bobAck.Get(66));

        await alice.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        Assert.AreEqual(YmsgService.Ping, (await alice.ReadAsync()).Service, "An offline real member looks exactly like the unknown one");

        var stored = Mind.Db!.YahooListGet("alice");

        Assert.IsTrue(stored.Any(e => e.Buddy == "ghost"), "The unknown name is stored like any other entry");
        Assert.IsTrue(stored.Any(e => e.Buddy == "bob"));
    }

    // The compatibility contract: with the flag off, stored rows must be invisible everywhere - the LIST
    // serves the server-built roster, ADDBUDDY goes back to the ack-only shape with the legacy "Hive"
    // default, and nothing new lands in storage.
    [TestMethod]
    [Timeout(15000)]
    public async Task FlagOff_ReproducesTodaysBehavior()
    {
        Mind.Db!.ConfigSet(VintageHive.Data.Types.ConfigNames.YahooPersistLists, false);

        // A row left over from some flag-on life must not surface.
        Mind.Db!.YahooListUpsert("alice", "Friends", "ghost");

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        var list = await alice.LoginAsync("alice");
        var roster = list.Get(87) ?? string.Empty;

        Assert.IsTrue(roster.StartsWith("Hive:"), $"Flag off must serve the server-built single-group roster, got: {roster}");
        Assert.IsFalse(roster.Contains("ghost"), "Stored rows must be invisible with the flag off");

        // No field 65: the legacy ack invents the Hive group, exactly as before the feature existed.
        await alice.SendAsync(new YmsgPacket(YmsgService.AddBuddy, 0, 0).Add(1, "alice").Add(7, "bob"));

        var ack = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.AddBuddy, ack.Service);
        Assert.AreEqual("bob", ack.Get(7));
        Assert.AreEqual("Hive", ack.Get(65), "The flag-off ack must keep the legacy Hive default byte for byte");
        Assert.AreEqual("0", ack.Get(66));

        Assert.AreEqual(1, Mind.Db!.YahooListGet("alice").Count, "Flag off must persist nothing - only the planted row remains");
    }

    // The Pager transport serves its BUDDYLIST from the same stored rows, so the two Yahoo! dialects keep
    // showing the same buddy list with the flag on - and the ignored YahooAutoRoster cannot leak the
    // server-built roster in through the HTTP side.
    [TestMethod]
    public void PagerAccountBlock_ServesTheStoredRosterWithTheFlagOn()
    {
        Mind.Db!.YahooListUpsert("alice", "Friends", "bob");
        Mind.Db!.YahooListUpsert("alice", "Work", "carol");

        var block = VintageHive.Processors.YahooPagerProcessor.BuildAccountBlock("alice");

        StringAssert.Contains(block, "Friends:bob\r\n", "The Pager BUDDYLIST must serve the stored rows");
        StringAssert.Contains(block, "Work:carol\r\n", "Stored group names must survive into the Pager BUDDYLIST");
        Assert.IsFalse(block.Contains("Hive:"), "The server-built roster must not leak through the Pager with the flag on");
    }
}
