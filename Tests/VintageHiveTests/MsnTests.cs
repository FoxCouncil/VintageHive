// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Network;
using VintageHive.Proxy.Msn;
using VintageHive.Proxy.Presence;

namespace Msn;

internal static class MsnTestEnv
{
    private static readonly object Gate = new();
    private static bool _ready;

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

// Drives the real MsnServer.ProcessConnection over a loopback socket.
internal sealed class MsnConn : IDisposable
{
    private readonly Socket _client;
    private readonly Socket _server;
    private readonly Task _handler;
    private readonly MsnStreamReader _reader;

    public NetworkStream ClientStream { get; }

    public MsnConn(MsnServer server)
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
        _reader = new MsnStreamReader(ClientStream);

        var connection = new ListenerSocket
        {
            RawSocket = _server,
            Stream = new NetworkStream(_server),
        };

        _handler = Task.Run(() => server.ProcessConnection(connection));
    }

    public async Task SendAsync(string line)
    {
        await ClientStream.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"));
    }

    public async Task SendPayloadAsync(string line, byte[] payload)
    {
        await ClientStream.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"));
        await ClientStream.WriteAsync(payload);
    }

    public Task<string> ReadLineAsync() => _reader.ReadLineAsync();

    public Task<byte[]> ReadBytesAsync(int count) => _reader.ReadBytesAsync(count);

    // Runs VER/CVR/USR-MD5 login and returns after the USR OK.
    public async Task LoginAsync(string account)
    {
        await SendAsync("VER 1 MSNP7 MSNP6 CVR0");

        var ver = await ReadLineAsync();

        Assert.IsTrue(ver.StartsWith("VER 1 MSNP7"), $"Unexpected VER reply: {ver}");

        await SendAsync($"CVR 2 0x0409 win 5.0 i386 MSNMSGR 5.0.0544 MSMSGS {account}");
        await ReadLineAsync(); // CVR

        await SendAsync($"USR 3 MD5 I {account}");

        var challengeLine = await ReadLineAsync();
        var challenge = challengeLine.Split(' ')[4];
        var hash = MsnServer.Md5Response(challenge, MsnTestEnv.Password);

        await SendAsync($"USR 4 MD5 S {hash}");

        var ok = await ReadLineAsync();

        Assert.IsTrue(ok.StartsWith("USR 4 OK"), $"Login failed: {ok}");
    }

    public void Dispose()
    {
        // Close the client so the server's read loop hits EOF, then wait for the handler (and its presence
        // teardown broadcast) to finish before the next test, so static session state doesn't leak across tests.
        try { ClientStream.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
        try { _handler.Wait(3000); } catch { }
        try { _server.Dispose(); } catch { }
    }
}

[TestClass]
public class MsnStreamReaderTests
{
    [TestMethod]
    public async Task ReadLine_And_ReadBytes_ShareBufferedOverread()
    {
        // A payload command line, then its raw body, with the body over-read while scanning for the line CRLF.
        var stream = new MemoryStream(Encoding.ASCII.GetBytes("USR 1 alice cookie\r\nMSG 2 A 5\r\nhello"));
        var reader = new MsnStreamReader(stream);

        Assert.AreEqual("USR 1 alice cookie", await reader.ReadLineAsync());
        Assert.AreEqual("MSG 2 A 5", await reader.ReadLineAsync());

        var body = await reader.ReadBytesAsync(5);

        Assert.AreEqual("hello", Encoding.ASCII.GetString(body));
    }

    [TestMethod]
    public async Task ReadLine_ReturnsNullOnEof()
    {
        var reader = new MsnStreamReader(new MemoryStream(Encoding.ASCII.GetBytes("PARTIAL")));

        Assert.IsNull(await reader.ReadLineAsync());
    }
}

[TestClass]
public class MsnHelperTests
{
    [TestMethod]
    public void ChooseVersion_PicksHighestSupported()
    {
        Assert.AreEqual("MSNP7", MsnServer.ChooseVersion(new[] { "MSNP8", "MSNP7", "MSNP2", "CVR0" }));
        Assert.AreEqual("MSNP4", MsnServer.ChooseVersion(new[] { "MSNP4", "MSNP3" }));
    }

    [TestMethod]
    public void ChooseVersion_NoneSupported_FallsBackToHighest()
    {
        Assert.AreEqual("MSNP7", MsnServer.ChooseVersion(new[] { "MSNP18", "MSNP15" }));
    }

    [TestMethod]
    public void Md5Response_IsLowercaseHexOfChallengePlusPassword()
    {
        // Known vector: MD5("test" + "secret").
        var expected = System.Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(Encoding.ASCII.GetBytes("testsecret")));

        Assert.AreEqual(expected, MsnServer.Md5Response("test", "secret"));
        Assert.AreEqual(32, MsnServer.Md5Response("test", "secret").Length);
    }
}

[TestClass]
public class MsnServerTests
{
    [TestInitialize]
    public void Setup()
    {
        MsnTestEnv.Ensure();
        MsnServer.NsSessions.Clear();
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Login_WrongPassword_Gets911()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var conn = new MsnConn(server);

        await conn.SendAsync("VER 1 MSNP7 CVR0");
        await conn.ReadLineAsync();

        await conn.SendAsync("USR 3 MD5 I alice");

        var challengeLine = await conn.ReadLineAsync();
        var challenge = challengeLine.Split(' ')[4];

        // Hash of the WRONG password.
        await conn.SendAsync($"USR 4 MD5 S {MsnServer.Md5Response(challenge, "wrongpw")}");

        var reply = await conn.ReadLineAsync();

        Assert.IsTrue(reply.StartsWith("911"), $"Expected auth failure 911, got: {reply}");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Login_CorrectPassword_Succeeds()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var conn = new MsnConn(server);

        await conn.LoginAsync("alice");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Chg_BroadcastsNlnToOtherUsers()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 NLN");

        Assert.AreEqual("CHG 5 NLN", await alice.ReadLineAsync());

        using var bob = new MsnConn(server);

        await bob.LoginAsync("bob");
        await bob.SendAsync("CHG 5 NLN");

        Assert.AreEqual("CHG 5 NLN", await bob.ReadLineAsync());
        Assert.IsTrue((await bob.ReadLineAsync()).StartsWith("ILN 5 NLN alice"), "Bob should get an ILN for the already-online alice");

        // Alice's notification connection receives bob's arrival.
        var nln = await alice.ReadLineAsync();

        Assert.IsTrue(nln.StartsWith("NLN NLN bob"), $"Alice should receive bob's NLN, got: {nln}");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Switchboard_OneToOneMessage_IsDelivered()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var aliceNs = new MsnConn(server);
        using var bobNs = new MsnConn(server);

        await aliceNs.LoginAsync("alice");
        await aliceNs.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await aliceNs.ReadLineAsync());

        await bobNs.LoginAsync("bob");
        await bobNs.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await bobNs.ReadLineAsync());
        await bobNs.ReadLineAsync(); // ILN alice
        await aliceNs.ReadLineAsync(); // NLN bob

        // Alice requests a switchboard.
        await aliceNs.SendAsync("XFR 10 SB");

        var xfr = (await aliceNs.ReadLineAsync()).Split(' ');

        Assert.AreEqual("XFR", xfr[0]);
        Assert.AreEqual("SB", xfr[2]);

        var callerCookie = xfr[5];

        // Alice opens the switchboard and invites bob.
        using var aliceSb = new MsnConn(server);

        await aliceSb.SendAsync($"USR 1 alice {callerCookie}");
        Assert.IsTrue((await aliceSb.ReadLineAsync()).StartsWith("USR 1 OK alice"));

        await aliceSb.SendAsync("CAL 2 bob");

        var cal = await aliceSb.ReadLineAsync();

        Assert.IsTrue(cal.StartsWith("CAL 2 RINGING"), $"Expected RINGING, got: {cal}");

        var sessionId = cal.Split(' ')[3];

        // Bob's notification connection receives the ring.
        var rng = (await bobNs.ReadLineAsync()).Split(' ');

        Assert.AreEqual("RNG", rng[0]);

        var bobCookie = rng[4];

        // Bob answers on a new switchboard connection.
        using var bobSb = new MsnConn(server);

        await bobSb.SendAsync($"ANS 1 bob {bobCookie} {sessionId}");
        Assert.IsTrue((await bobSb.ReadLineAsync()).StartsWith("IRO 1 1 1 alice"), "Bob should be told alice is present");
        Assert.AreEqual("ANS 1 OK", await bobSb.ReadLineAsync());

        // Alice's switchboard sees bob join.
        Assert.IsTrue((await aliceSb.ReadLineAsync()).StartsWith("JOI bob"), "Alice should see bob join");

        // Alice sends a message; bob receives it.
        var body = Encoding.ASCII.GetBytes("MIME-Version: 1.0\r\n\r\nhello bob");

        await aliceSb.SendPayloadAsync($"MSG 3 A {body.Length}", body);

        var msgLine = (await bobSb.ReadLineAsync()).Split(' ');

        Assert.AreEqual("MSG", msgLine[0]);
        Assert.AreEqual("alice", msgLine[1]);
        Assert.AreEqual(body.Length, int.Parse(msgLine[3]));

        var delivered = await bobSb.ReadBytesAsync(body.Length);

        CollectionAssert.AreEqual(body, delivered);

        // Alice gets an ACK for the A-flagged message.
        Assert.AreEqual("ACK 3", await aliceSb.ReadLineAsync());
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task PreChg_AuthenticatedUser_NotReportedOnlineByFind()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        PresenceRegistry.Register(new MsnPresenceProvider());

        using var alice = new MsnConn(server);

        // Log in but do NOT send CHG; the session is authenticated yet still Offline (FLN).
        await alice.LoginAsync("alice");

        Assert.IsNull(PresenceRegistry.Find("alice"), "An authenticated-but-pre-CHG session must not be reported online");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task DuplicateLogin_DoesNotGhostTheLiveSession()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        PresenceRegistry.Register(new MsnPresenceProvider());

        var first = new MsnConn(server);

        await first.LoginAsync("alice");
        await first.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await first.ReadLineAsync());

        // A second login for the same account supersedes the first.
        using var second = new MsnConn(server);

        await second.LoginAsync("alice");
        await second.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await second.ReadLineAsync());

        // The first (now-superseded) connection tears down; it must NOT evict the live second session.
        first.Dispose();

        Assert.IsNotNull(PresenceRegistry.Find("alice"), "The live duplicate-login session must remain registered after the stale one tears down");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Switchboard_OversizedMessageLength_IsRejected()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var aliceNs = new MsnConn(server);

        await aliceNs.LoginAsync("alice");
        await aliceNs.SendAsync("XFR 10 SB");

        var cookie = (await aliceNs.ReadLineAsync()).Split(' ')[5];

        using var aliceSb = new MsnConn(server);

        await aliceSb.SendAsync($"USR 1 alice {cookie}");
        Assert.IsTrue((await aliceSb.ReadLineAsync()).StartsWith("USR 1 OK"));

        // A wildly oversized declared length must be refused without allocating; the server closes the connection.
        await aliceSb.SendAsync("MSG 2 A 999999999");

        try
        {
            var line = await aliceSb.ReadLineAsync();

            Assert.IsNull(line, "Server should close the connection on an oversized MSG length");
        }
        catch (IOException)
        {
            // Windows surfaces the close as an IOException; also acceptable.
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AuthenticatedUser_AppearsInPresenceRegistry()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        PresenceRegistry.Register(new MsnPresenceProvider());

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 NLN");
        await alice.ReadLineAsync();

        var entry = PresenceRegistry.Find("alice");

        Assert.IsNotNull(entry, "MSN user should be visible via the shared presence registry");
        Assert.AreEqual("MSN", entry.Network);
        Assert.AreEqual(PresenceStatus.Online, entry.Status);
    }
}
