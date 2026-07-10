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
}
