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
                    Mind.Db.UserCreate(user, "secret");
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

    // Runs the VERIFY/AUTH/AUTHRESP handshake and drains the resulting LIST + initial LOGON.
    public async Task LoginAsync(string username)
    {
        await SendAsync(new YmsgPacket(YmsgService.Verify, 0, 0));
        await ReadAsync(); // VERIFY echo

        await SendAsync(new YmsgPacket(YmsgService.Auth, 0, 0).Add(1, username));
        await ReadAsync(); // AUTH challenge

        await SendAsync(new YmsgPacket(YmsgService.AuthResp, 0, 0).Add(0, username).Add(6, "x").Add(96, "y"));

        var list = await ReadAsync();

        Assert.AreEqual(YmsgService.List, list.Service, "Expected buddy LIST after auth");

        var logon = await ReadAsync();

        Assert.AreEqual(YmsgService.Logon, logon.Service, "Expected initial LOGON presence after LIST");
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
        Assert.AreEqual(YmsgStatus.OfflineMessage, delivered.Status, "Offline delivery must carry the offline header status");
        Assert.AreEqual("alice", delivered.Get(4), "Sender must be in field 4");
        Assert.IsNull(delivered.Get(1), "Field 1 must be absent or period clients attribute the message to the recipient");
        Assert.AreEqual("read this later", delivered.Get(14));

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

        // 22 is the client's TYPING header status; it must be echoed through to the recipient.
        await alice.SendAsync(new YmsgPacket(YmsgService.Notify, 22, 0).Add(1, "alice").Add(5, "bob").Add(49, "TYPING").Add(13, "1").Add(14, " "));

        var notify = await bob.ReadAsync();

        Assert.AreEqual(YmsgService.Notify, notify.Service);
        Assert.AreEqual(22u, notify.Status, "Header status must be relayed");
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
