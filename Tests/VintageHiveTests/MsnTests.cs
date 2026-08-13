// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Finger;
using VintageHive.Proxy.Msn;
using VintageHive.Proxy.Presence;
using VintageHive.Proxy.Yahoo;

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

    // Runs VER/CVR/USR-MD5 login and returns the USR OK line, so callers can assert which display
    // name sign-on carried. Versions are offered highest-first, so the first one is what the server
    // must choose.
    public async Task<string> LoginAsync(string account, string versions = "MSNP7 MSNP6")
    {
        await SendAsync($"VER 1 {versions} CVR0");

        var ver = await ReadLineAsync();

        Assert.IsTrue(ver.StartsWith($"VER 1 {versions.Split(' ')[0]}"), $"Unexpected VER reply: {ver}");

        await SendAsync($"CVR 2 0x0409 win 5.0 i386 MSNMSGR 5.0.0544 MSMSGS {account}");

        var cvr = await ReadLineAsync();

        Assert.IsTrue(cvr.StartsWith("CVR 2 5.0.0544 5.0.0544 5.0.0544 "), $"CVR must echo the client's own version, got: {cvr}");

        await SendAsync($"USR 3 MD5 I {account}");

        var challengeLine = await ReadLineAsync();
        var challenge = challengeLine.Split(' ')[4];
        var hash = MsnServer.Md5Response(challenge, MsnTestEnv.Password);

        await SendAsync($"USR 4 MD5 S {hash}");

        var ok = await ReadLineAsync();

        Assert.IsTrue(ok.StartsWith("USR 4 OK"), $"Login failed: {ok}");

        return ok;
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

    [TestMethod]
    [Timeout(15000)]
    public async Task Syn_SendsAllFourContactLists()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("SYN 5");

        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("SYN 5"), "SYN echo");
        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("GTC 5"), "GTC");
        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("BLP 5"), "BLP");

        // The harness negotiates MSNP7, so the implicit contact group precedes the lists.
        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("LSG 5"), "LSG");

        // Each list arrives as LST lines terminated by its item#/total pair (a single 0 0 line when
        // empty); the client will not consider contact sync complete until all four lists have arrived.
        foreach (var list in new[] { "FL", "AL", "BL", "RL" })
        {
            var first = (await alice.ReadLineAsync()).Split(' ');

            Assert.AreEqual("LST", first[0], $"Expected an LST line for {list}");
            Assert.AreEqual(list, first[2], $"Lists must arrive in FL/AL/BL/RL order; got {first[2]} while expecting {list}");

            var total = int.Parse(first[5]);
            var members = new List<string>();

            if (total > 0)
            {
                members.Add(first[6]);
            }

            for (var i = 1; i < total; i++)
            {
                var line = (await alice.ReadLineAsync()).Split(' ');

                Assert.AreEqual(list, line[2]);

                members.Add(line[6]);
            }

            if (list == "BL")
            {
                Assert.AreEqual(0, total, "BL must be empty");
            }
            else
            {
                CollectionAssert.Contains(members, "bob", $"{list} must contain the other hive accounts");
            }
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Syn_Msnp6_KeepsThePre7Shape()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice", "MSNP6");
        await alice.SendAsync("SYN 5");

        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("SYN 5"), "SYN echo");
        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("GTC 5"), "GTC");
        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("BLP 5"), "BLP");

        // Pre-7 dialects have no group concept: no LSG lines anywhere, and every LST entry stays
        // byte-identical to the historical shape - seven fields after the verb, no trailing group-id.
        foreach (var list in new[] { "FL", "AL", "BL", "RL" })
        {
            var first = (await alice.ReadLineAsync()).Split(' ');

            Assert.AreEqual("LST", first[0], $"MSNP6 must go straight from BLP to the lists; got {first[0]} while expecting the {list} list");
            Assert.AreEqual(list, first[2]);

            var total = int.Parse(first[5]);

            Assert.AreEqual(total == 0 ? 6 : 8, first.Length, $"MSNP6 {list} lines must not carry a group-id field");

            for (var i = 1; i < total; i++)
            {
                var line = (await alice.ReadLineAsync()).Split(' ');

                Assert.AreEqual(list, line[2]);
                Assert.AreEqual(8, line.Length, $"MSNP6 {list} lines must not carry a group-id field");
            }
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Syn_Msnp7_SendsGroupsAndFlGroupIds()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("SYN 5");

        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("SYN 5"), "SYN echo");
        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("GTC 5"), "GTC");
        Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("BLP 5"), "BLP");

        // The implicit "Other Contacts" group (id 0) always exists and is counted in the group total.
        Assert.AreEqual("LSG 5 1 1 1 0 Other%20Contacts 0", await alice.ReadLineAsync());

        // FL entries carry the trailing group-id field ("0" = the implicit group); AL/BL/RL never do.
        foreach (var list in new[] { "FL", "AL", "BL", "RL" })
        {
            var first = (await alice.ReadLineAsync()).Split(' ');

            Assert.AreEqual("LST", first[0], $"Expected an LST line for {list}");
            Assert.AreEqual(list, first[2]);

            var total = int.Parse(first[5]);
            var expectedLength = list == "FL" ? 9 : 8;

            Assert.AreEqual(total == 0 ? 6 : expectedLength, first.Length, $"{list} line has the wrong field count for MSNP7");

            if (total > 0 && list == "FL")
            {
                Assert.AreEqual("0", first[8], "FL entries must end with the implicit group id");
            }

            for (var i = 1; i < total; i++)
            {
                var line = (await alice.ReadLineAsync()).Split(' ');

                Assert.AreEqual(list, line[2]);
                Assert.AreEqual(expectedLength, line.Length, $"{list} line has the wrong field count for MSNP7");

                if (list == "FL")
                {
                    Assert.AreEqual("0", line[8], "FL entries must end with the implicit group id");
                }
            }
        }

        // The corrected SYN is what unblocks sign-on: the client's next command is CHG, which must echo.
        await alice.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await alice.ReadLineAsync());
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Syn_AutoRosterOff_SendsAllFourListsEmptyAndStillAcceptsAddRem()
    {
        Mind.Db!.ConfigSet(ConfigNames.MsnAutoRoster, false);

        try
        {
            var server = new MsnServer(IPAddress.Loopback, 0);

            using var alice = new MsnConn(server);

            await alice.LoginAsync("alice");
            await alice.SendAsync("SYN 5");

            Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("SYN 5"), "SYN echo");
            Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("GTC 5"), "GTC");
            Assert.IsTrue((await alice.ReadLineAsync()).StartsWith("BLP 5"), "BLP");

            // The MSNP7 group block still precedes the lists regardless of contents; a 4.5 client stalls without it.
            Assert.AreEqual("LSG 5 1 1 1 0 Other%20Contacts 0", await alice.ReadLineAsync(), "The implicit group must survive the roster being off");

            // All four lists arrive empty but well-formed: an empty list is a valid answer, a missing one hangs.
            Assert.AreEqual("LST 5 FL 1 0 0", await alice.ReadLineAsync(), "FL must be empty with the roster off");
            Assert.AreEqual("LST 5 AL 1 0 0", await alice.ReadLineAsync(), "AL must be empty too - AL means pre-authorized");
            Assert.AreEqual("LST 5 BL 1 0 0", await alice.ReadLineAsync());
            Assert.AreEqual("LST 5 RL 1 0 0", await alice.ReadLineAsync());

            // Members build their own lists, so ADD/REM must still be acknowledged (echo + next list serial).
            await alice.SendAsync("ADD 6 FL bob bob 0");
            Assert.AreEqual("ADD 6 FL 2 bob bob 0", await alice.ReadLineAsync(), "ADD must be acked so the client commits the contact");

            await alice.SendAsync("REM 7 FL bob");
            Assert.AreEqual("REM 7 FL 3 bob", await alice.ReadLineAsync(), "REM must be acked so the client commits the removal");
        }
        finally
        {
            Mind.Db!.ConfigSet(ConfigNames.MsnAutoRoster, true);
        }
    }

    // The three roster keys are independent by contract: turning MSN's off must leave the Yahoo roster and
    // the finger directory at today's behavior (and vice versa - there is deliberately no global switch).
    [TestMethod]
    public void AutoRosterKeys_AreIndependent()
    {
        Mind.Db!.ConfigSet(ConfigNames.MsnAutoRoster, false);

        try
        {
            CollectionAssert.Contains(YahooSessionRegistry.OtherUsernames("alice"), "bob", "MsnAutoRoster must not gate the Yahoo roster");

            // Either table form proves the finger directory branch still runs; the gated form is banner-only.
            var finger = FingerServer.BuildUserList();

            Assert.IsTrue(finger.Contains("No users currently online.") || finger.Contains("On Since"), $"MsnAutoRoster must not gate the finger directory, got: {finger}");
        }
        finally
        {
            Mind.Db!.ConfigSet(ConfigNames.MsnAutoRoster, true);
        }
    }

    // DbContextBase names its sqlite file after the runtime type, so this subclass opens a fresh store:
    // the one-account hive the empty-forward-list case needs (the shared context always holds alice+bob).
    private sealed class LoneUserDbContext : HiveDbContext { }

    [TestMethod]
    [Timeout(15000)]
    public async Task Syn_Msnp7_EmptyForwardList_StillSendsTheGroup()
    {
        var shared = Mind.Db;
        var setter = typeof(Mind).GetProperty(nameof(Mind.Db))!.GetSetMethod(nonPublic: true)!;

        try
        {
            var lone = new LoneUserDbContext();

            if (!lone.UserExistsByUsername("solo"))
            {
                Assert.IsTrue(lone.UserCreate("solo", MsnTestEnv.Password), "could not create the lone test user");
            }

            setter.Invoke(null, new object[] { lone });

            var server = new MsnServer(IPAddress.Loopback, 0);

            using var solo = new MsnConn(server);

            await solo.LoginAsync("solo");
            await solo.SendAsync("SYN 5");

            Assert.IsTrue((await solo.ReadLineAsync()).StartsWith("SYN 5"), "SYN echo");
            Assert.IsTrue((await solo.ReadLineAsync()).StartsWith("GTC 5"), "GTC");
            Assert.IsTrue((await solo.ReadLineAsync()).StartsWith("BLP 5"), "BLP");

            Assert.AreEqual("LSG 5 1 1 1 0 Other%20Contacts 0", await solo.ReadLineAsync(), "The implicit group must be present even with no contacts");
            Assert.AreEqual("LST 5 FL 1 0 0", await solo.ReadLineAsync(), "An empty FL keeps the 0 0 form with no group field");
            Assert.AreEqual("LST 5 AL 1 0 0", await solo.ReadLineAsync());
            Assert.AreEqual("LST 5 BL 1 0 0", await solo.ReadLineAsync());
            Assert.AreEqual("LST 5 RL 1 0 0", await solo.ReadLineAsync());
        }
        finally
        {
            setter.Invoke(null, new object[] { shared });
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Cal_HiddenCallee_Gets217NotRinging()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var aliceNs = new MsnConn(server);
        using var bobNs = new MsnConn(server);

        await aliceNs.LoginAsync("alice");
        await aliceNs.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await aliceNs.ReadLineAsync());

        await bobNs.LoginAsync("bob");
        await bobNs.SendAsync("CHG 6 HDN");
        Assert.AreEqual("CHG 6 HDN", await bobNs.ReadLineAsync());

        // Going hidden broadcasts FLN, so alice believes bob is offline.
        Assert.AreEqual("FLN bob", await aliceNs.ReadLineAsync());

        await aliceNs.SendAsync("XFR 10 SB");

        var cookie = (await aliceNs.ReadLineAsync()).Split(' ')[5];

        using var aliceSb = new MsnConn(server);

        await aliceSb.SendAsync($"USR 1 alice {cookie}");
        Assert.IsTrue((await aliceSb.ReadLineAsync()).StartsWith("USR 1 OK"));

        // Calling the hidden bob must confirm nothing: 217 (not online), no RINGING, no RNG to bob.
        await aliceSb.SendAsync("CAL 2 bob");

        var reply = await aliceSb.ReadLineAsync();

        Assert.IsTrue(reply.StartsWith("217 2"), $"Calling a hidden user must report not-online, got: {reply}");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Switchboard_CookieRedeemedByWrongAccount_IsRejected()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var aliceNs = new MsnConn(server);

        await aliceNs.LoginAsync("alice");
        await aliceNs.SendAsync("XFR 10 SB");

        var cookie = (await aliceNs.ReadLineAsync()).Split(' ')[5];

        // The ticket was minted for alice; a different self-asserted account must not be able to redeem it.
        using var mallory = new MsnConn(server);

        await mallory.SendAsync($"USR 1 bob {cookie}");

        var reply = await mallory.ReadLineAsync();

        Assert.IsTrue(reply.StartsWith("911"), $"A cookie must only be redeemable by the account it was minted for, got: {reply}");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ChgIdl_TracksIdleTimeForPresence()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        PresenceRegistry.Register(new MsnPresenceProvider());

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 IDL");
        Assert.AreEqual("CHG 5 IDL", await alice.ReadLineAsync());

        var session = MsnServer.NsSessions["alice"];

        Assert.AreNotEqual(DateTimeOffset.MinValue, session.IdleSince, "CHG IDL must stamp the idle start time");
        Assert.AreEqual(PresenceStatus.Idle, PresenceRegistry.Find("alice").Status);

        // Returning online clears the idle clock.
        await alice.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await alice.ReadLineAsync());

        Assert.AreEqual(DateTimeOffset.MinValue, session.IdleSince);
        Assert.AreEqual(0u, PresenceRegistry.Find("alice").IdleSeconds);
    }

    // MSN Messenger refuses to submit a bare sign-in name (it demands the .NET Passport name@domain
    // form client-side), so name@<hosted domain> must authenticate against the local part's stored
    // password while the wire identity stays the address the client presented.
    [TestMethod]
    [Timeout(15000)]
    public async Task Login_PassportAddressOnHostedDomain_AuthenticatesAsTheLocalPart()
    {
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

        var server = new MsnServer(IPAddress.Loopback, 0);
        var account = $"alice@{MailDomains.Primary}";

        using var conn = new MsnConn(server);

        await conn.SendAsync("VER 1 MSNP7 CVR0");
        await conn.ReadLineAsync();

        await conn.SendAsync($"USR 3 MD5 I {account}");

        var challenge = (await conn.ReadLineAsync()).Split(' ')[4];

        await conn.SendAsync($"USR 4 MD5 S {MsnServer.Md5Response(challenge, MsnTestEnv.Password)}");

        var ok = await conn.ReadLineAsync();

        // The OK (and everything downstream) carries the presented address, not the bare local part.
        StringAssert.StartsWith(ok, $"USR 4 OK {account} ", $"A Passport address on a hosted domain must sign in with the local part's password, got: {ok}");

        // The session store keys on the resolved username, so cross-protocol lookups and the bare form agree.
        Assert.IsTrue(MsnServer.NsSessions.TryGetValue("alice", out var session), "The session must be keyed on the resolved username");
        Assert.AreEqual(account, session.Account, "The wire identity must stay the presented address");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Login_PassportAddressOnUnhostedDomain_Is911NotStripped()
    {
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

        var server = new MsnServer(IPAddress.Loopback, 0);

        using var conn = new MsnConn(server);

        await conn.SendAsync("VER 1 MSNP7 CVR0");
        await conn.ReadLineAsync();

        await conn.SendAsync("USR 3 MD5 I alice@evil.example");

        var challenge = (await conn.ReadLineAsync()).Split(' ')[4];

        // The CORRECT password: only the foreign domain may cause the refusal, never a stripped-domain success.
        await conn.SendAsync($"USR 4 MD5 S {MsnServer.Md5Response(challenge, MsnTestEnv.Password)}");

        var reply = await conn.ReadLineAsync();

        StringAssert.StartsWith(reply, "911", $"An unhosted domain must be refused, not silently stripped to its local part, got: {reply}");
        Assert.IsFalse(MsnServer.NsSessions.ContainsKey("alice"), "The refused sign-on must not register a session");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task DuplicateLogin_BareThenPassportForm_SupersedesNotGhosts()
    {
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

        var server = new MsnServer(IPAddress.Loopback, 0);
        var account = $"alice@{MailDomains.Primary}";

        var first = new MsnConn(server);

        try
        {
            await first.LoginAsync("alice");

            using var second = new MsnConn(server);

            await second.LoginAsync(account);

            // One member, one entry: the Passport form must land on the SAME key and supersede, not sit
            // alongside as a ghost that still receives presence.
            Assert.IsFalse(MsnServer.NsSessions.Keys.Any(k => k.Contains('@')), "The Passport form must not occupy a second NsSessions entry");
            Assert.IsTrue(MsnServer.NsSessions.TryGetValue("alice", out var live));
            Assert.AreEqual(account, live.Account, "The second (Passport-form) login must have superseded the bare-name session");

            // The superseded connection was closed by the server.
            try
            {
                Assert.IsNull(await first.ReadLineAsync(), "The superseded bare-name connection must be closed");
            }
            catch (IOException)
            {
                // Windows may surface the close as an IOException; also acceptable.
            }
        }
        finally
        {
            first.Dispose();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Presence_CarriesThePresentedPassportAddress()
    {
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

        var server = new MsnServer(IPAddress.Loopback, 0);
        var account = $"alice@{MailDomains.Primary}";

        using var alice = new MsnConn(server);

        await alice.LoginAsync(account);
        await alice.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await alice.ReadLineAsync());

        using var bob = new MsnConn(server);

        await bob.LoginAsync("bob");
        await bob.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await bob.ReadLineAsync());

        // Bob's initial presence dump and alice's arrival broadcast both name the Passport address.
        StringAssert.StartsWith(await bob.ReadLineAsync(), $"ILN 6 NLN {account}", "ILN must carry the address the client presented");
        StringAssert.StartsWith(await alice.ReadLineAsync(), "NLN NLN bob", "Alice still receives bob's presence under his bare name");
    }

    // The CVR reply's third version field is a MINIMUM the client obeys literally: 4.5.0121 used to be
    // told "A newer version is available" by the old hardcoded 4.7.2009 floor and sign-on died right there,
    // after auth/SYN/CHG were all already working. The server cannot host an upgrade, so the minimum must
    // never exceed whatever version the client itself states.
    [TestMethod]
    [Timeout(15000)]
    public async Task Cvr_EchoesTheClientsVersion_SoOlderClientsAreNotWalled()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var conn = new MsnConn(server);

        await conn.SendAsync("VER 1 MSNP7 CVR0");
        await conn.ReadLineAsync();

        await conn.SendAsync("CVR 2 0x0409 win 4.10 i386 MSMSGS 4.5.0121 MSMSGS alice");

        var parts = (await conn.ReadLineAsync()).Split(' ');

        Assert.AreEqual("CVR", parts[0]);
        Assert.AreEqual("2", parts[1]);
        Assert.AreEqual("4.5.0121", parts[2], "The recommended version must echo the client's own");
        Assert.AreEqual("4.5.0121", parts[3]);
        Assert.AreEqual("4.5.0121", parts[4], "The minimum must never exceed the version the client stated");
        Assert.AreEqual($"http://{HiveDomains.Intranet}/download.html", parts[5], "The download URL must point at the hive, not an off-network microsoft.com name");
        Assert.AreEqual($"http://{HiveDomains.Intranet}/", parts[6]);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Cvr_MalformedOrShort_NeverEmitsAMinimumAboveAnyClient()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var conn = new MsnConn(server);

        await conn.SendAsync("VER 1 MSNP7 CVR0");
        await conn.ReadLineAsync();

        // No fields at all after the trid.
        await conn.SendAsync("CVR 2");

        var shortReply = (await conn.ReadLineAsync()).Split(' ');

        Assert.AreEqual("CVR", shortReply[0]);
        Assert.AreEqual("2", shortReply[1]);
        Assert.AreEqual("1.0.0000", shortReply[4], "An unparseable request must fall back to a floor minimum, never a wall");

        // A garbage token in the version slot must not be echoed back.
        await conn.SendAsync("CVR 3 0x0409 win 5.0 i386 MSNMSGR bogus%ver MSMSGS alice");

        var garbageReply = (await conn.ReadLineAsync()).Split(' ');

        Assert.AreEqual("CVR", garbageReply[0]);
        Assert.AreEqual("3", garbageReply[1]);
        Assert.AreEqual("1.0.0000", garbageReply[4], "A garbage version token must fall back to the floor minimum");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task HiddenUser_IsHiddenFromThePresenceRegistry()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        PresenceRegistry.Register(new MsnPresenceProvider());

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 HDN");
        Assert.AreEqual("CHG 5 HDN", await alice.ReadLineAsync());

        Assert.IsNull(PresenceRegistry.Find("alice"), "Hidden users must not be exposed via the presence registry");
        Assert.IsFalse(PresenceRegistry.Online().Any(e => e.Network == "MSN" && e.Username == "alice"), "Hidden users must not appear in the online list");
    }
}

[TestClass]
public class MsnDisplayNameTests
{
    [TestInitialize]
    public void Setup()
    {
        MsnTestEnv.Ensure();
        MsnServer.NsSessions.Clear();

        // The profile store is on-disk sqlite that outlives the process, so a name left behind by a prior
        // run (or a prior test) must not leak into the no-stored-name regression cases.
        Mind.Db!.UserSetDisplayName("alice", null);
        Mind.Db!.UserSetDisplayName("bob", null);
    }

    // The compatibility contract: with no stored name, every surface emits exactly what it emitted before
    // the profile table existed - the sign-in address.
    [TestMethod]
    [Timeout(20000)]
    public async Task NoStoredName_UsrIlnAndNln_AllCarryTheSignInAddress()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        var ok = await alice.LoginAsync("alice");

        Assert.AreEqual("USR 4 OK alice alice 1 0", ok, "With no stored name the USR OK must carry the sign-in address as the display name");

        await alice.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await alice.ReadLineAsync());

        using var bob = new MsnConn(server);

        await bob.LoginAsync("bob");
        await bob.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await bob.ReadLineAsync());

        Assert.AreEqual("ILN 6 NLN alice alice", await bob.ReadLineAsync(), "With no stored name the ILN display name must be the sign-in address");
        Assert.AreEqual("NLN NLN bob bob", await alice.ReadLineAsync(), "With no stored name the NLN display name must be the sign-in address");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Rea_PersistsAndAFreshSignOnCarriesTheStoredName()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using (var alice = new MsnConn(server))
        {
            await alice.LoginAsync("alice");
            await alice.SendAsync("REA 5 alice The%20Fox");

            // SYN always answers version 1, so the first account-state edit acks serial 2.
            Assert.AreEqual("REA 5 2 alice The%20Fox", await alice.ReadLineAsync(), "REA must be acknowledged with the serial-bearing form");
        }

        Assert.AreEqual("The Fox", Mind.Db!.UserGetDisplayName("alice"), "The DECODED name must be what persists");

        using var again = new MsnConn(server);

        var ok = await again.LoginAsync("alice");

        Assert.AreEqual("USR 4 OK alice The%20Fox 1 0", ok, "A fresh sign-on must carry the stored name without another REA");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Rea_ReannouncesSoConnectedContactsSeeTheNewName()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);
        using var bob = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await alice.ReadLineAsync());

        await bob.LoginAsync("bob");
        await bob.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await bob.ReadLineAsync());
        Assert.AreEqual("ILN 6 NLN alice alice", await bob.ReadLineAsync());
        Assert.AreEqual("NLN NLN bob bob", await alice.ReadLineAsync());

        await alice.SendAsync("REA 7 alice The%20Fox");
        Assert.AreEqual("REA 7 2 alice The%20Fox", await alice.ReadLineAsync());

        Assert.AreEqual("NLN NLN alice The%20Fox", await bob.ReadLineAsync(), "The rename must re-announce presence so already-connected contacts see the new name");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Rea_NamingAnotherAccount_IsRefusedAndNotStored()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("REA 5 bob Sneaky");

        Assert.AreEqual("201 5", await alice.ReadLineAsync(), "One member must not be able to rename another");
        Assert.IsNull(Mind.Db!.UserGetDisplayName("bob"), "The refused rename must not persist anything");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Rea_MalformedNames_AreRefusedNotStoredAndNotEchoed()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");

        // Over-long (decoded length past the period cap).
        await alice.SendAsync($"REA 5 alice {new string('a', MsnServer.MaxDisplayNameLength + 1)}");
        Assert.AreEqual("209 5", await alice.ReadLineAsync(), "An over-long name must be refused, and the refusal must not echo it");

        // A control character survives URL-decoding.
        await alice.SendAsync("REA 6 alice bad%01name");
        Assert.AreEqual("209 6", await alice.ReadLineAsync(), "A control character in the decoded name must be refused");

        // CR/LF would break MSNP's line framing if it ever reached an outbound line unencoded.
        await alice.SendAsync("REA 7 alice evil%0D%0Aname");
        Assert.AreEqual("209 7", await alice.ReadLineAsync(), "A CRLF-bearing name must be refused");

        // A raw space means the client never encoded the name; the truncated first token must not persist.
        await alice.SendAsync("REA 8 alice raw spacename");
        Assert.AreEqual("209 8", await alice.ReadLineAsync(), "An unencoded name must be refused, not truncated at the first space");

        Assert.IsNull(Mind.Db!.UserGetDisplayName("alice"), "No refused name may be stored");

        // And none of them touched the live session either: presence still carries the address.
        await alice.SendAsync("CHG 9 NLN");
        Assert.AreEqual("CHG 9 NLN", await alice.ReadLineAsync());

        Assert.AreEqual("alice", MsnServer.NsSessions["alice"].DisplayName, "A refused rename must not change the session's display name");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Rea_EmptyOrWhitespaceName_ClearsBackToTheAddress()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("REA 5 alice The%20Fox");
        Assert.AreEqual("REA 5 2 alice The%20Fox", await alice.ReadLineAsync());

        // Whitespace-only decodes to nothing a contact could see; it must clear, not store a blank.
        await alice.SendAsync("REA 6 alice %20");
        Assert.AreEqual("REA 6 3 alice alice", await alice.ReadLineAsync(), "Clearing must fall back to the sign-in address, never a blank name");

        Assert.IsNull(Mind.Db!.UserGetDisplayName("alice"), "Clearing must remove the stored name entirely");
        Assert.AreEqual("alice", MsnServer.NsSessions["alice"].DisplayName);
    }
}

[TestClass]
public class MsnPersistListsTests
{
    [TestInitialize]
    public void Setup()
    {
        MsnTestEnv.Ensure();
        MsnServer.NsSessions.Clear();

        // The list store is on-disk sqlite that outlives the process: rows and serials left by a prior
        // run (or a prior test) must not leak in, so every owner starts never-edited. Display names too -
        // a "The Fox" left behind by the REA tests would change every exact-match line below.
        foreach (var owner in new[] { "alice", "bob" })
        {
            Mind.Db!.MsnListReset(owner);
            Mind.Db!.UserSetDisplayName(owner, null);
        }

        Mind.Db!.ConfigSet(ConfigNames.MsnPersistLists, true);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Mind.Db!.ConfigSet(ConfigNames.MsnPersistLists, false);
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task AddFl_PersistsAndSurvivesReconnect()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using (var alice = new MsnConn(server))
        {
            await alice.LoginAsync("alice");
            await alice.SendAsync("ADD 5 FL bob bob 0");

            Assert.AreEqual("ADD 5 FL 2 bob bob 0", await alice.ReadLineAsync(), "The first edit must ack the bumped stored serial");
        }

        // A fresh connection: the list must come back from storage, under the serial that survived.
        using var again = new MsnConn(server);

        await again.LoginAsync("alice");
        await again.SendAsync("SYN 6");

        Assert.AreEqual("SYN 6 2", await again.ReadLineAsync(), "SYN must serve the stored serial, not a fresh 1");
        Assert.AreEqual("GTC 6 2 A", await again.ReadLineAsync());
        Assert.AreEqual("BLP 6 2 AL", await again.ReadLineAsync());
        Assert.AreEqual("LSG 6 2 1 1 0 Other%20Contacts 0", await again.ReadLineAsync());
        Assert.AreEqual("LST 6 FL 2 1 1 bob bob 0", await again.ReadLineAsync(), "The FL must hold exactly the stored contact");
        Assert.AreEqual("LST 6 AL 2 0 0", await again.ReadLineAsync(), "Nothing was added to the AL, so it must come back empty - no server-built mirror");
        Assert.AreEqual("LST 6 BL 2 0 0", await again.ReadLineAsync());
        Assert.AreEqual("LST 6 RL 2 0 0", await again.ReadLineAsync());
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task AddFl_LiveTarget_AdderGetsIlnAndTargetGetsRlAdd()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);
        using var bob = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await alice.ReadLineAsync());

        await bob.LoginAsync("bob");
        await bob.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await bob.ReadLineAsync());

        // Both lists are empty, so neither the ILN dump nor the NLN broadcast delivered anything yet;
        // the very next line each side reads is the one the ADD produces.
        await alice.SendAsync("ADD 7 FL bob bob 0");

        Assert.AreEqual("ADD 7 FL 2 bob bob 0", await alice.ReadLineAsync(), "The ADD ack keeps its serial-bearing shape");
        Assert.AreEqual("ILN 7 NLN bob bob", await alice.ReadLineAsync(), "A live contact must light up at ADD, not at their next CHG");

        Assert.AreEqual("ADD 0 RL 2 alice alice", await bob.ReadLineAsync(), "The target must hear about the reverse-list add - the authorization prompt's trigger");
        Assert.IsTrue(Mind.Db!.MsnListGet("bob", "RL").Any(e => e.Contact == "alice"), "The reverse-list entry must persist");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task MutualAdd_PresenceFlowsBothWays()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);
        using var bob = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await alice.ReadLineAsync());

        await bob.LoginAsync("bob");
        await bob.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await bob.ReadLineAsync());

        await alice.SendAsync("ADD 7 FL bob bob 0");
        Assert.AreEqual("ADD 7 FL 2 bob bob 0", await alice.ReadLineAsync());
        Assert.AreEqual("ILN 7 NLN bob bob", await alice.ReadLineAsync());
        Assert.AreEqual("ADD 0 RL 2 alice alice", await bob.ReadLineAsync());

        await bob.SendAsync("ADD 8 FL alice alice 0");
        Assert.AreEqual("ADD 8 FL 3 alice alice 0", await bob.ReadLineAsync(), "Bob's serial already moved when his RL grew, so his own edit acks 3");
        Assert.AreEqual("ILN 8 NLN alice alice", await bob.ReadLineAsync());
        Assert.AreEqual("ADD 0 RL 3 bob bob", await alice.ReadLineAsync());

        // With both FLs in place, presence flows in both directions.
        await alice.SendAsync("CHG 9 AWY");
        Assert.AreEqual("CHG 9 AWY", await alice.ReadLineAsync());
        Assert.AreEqual("ILN 9 NLN bob bob", await alice.ReadLineAsync(), "The CHG presence dump is FL-scoped and bob is now on it");
        Assert.AreEqual("NLN AWY alice alice", await bob.ReadLineAsync(), "Bob's FL names alice, so her status change must reach him");

        await bob.SendAsync("CHG 10 BSY");
        Assert.AreEqual("CHG 10 BSY", await bob.ReadLineAsync());
        Assert.AreEqual("ILN 10 AWY alice alice", await bob.ReadLineAsync());
        Assert.AreEqual("NLN BSY bob bob", await alice.ReadLineAsync(), "Alice's FL names bob, so his status change must reach her");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task RemFl_DropsPresenceButKeepsReverseList()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);
        using var bob = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await alice.ReadLineAsync());

        await bob.LoginAsync("bob");
        await bob.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await bob.ReadLineAsync());

        await alice.SendAsync("ADD 7 FL bob bob 0");
        Assert.AreEqual("ADD 7 FL 2 bob bob 0", await alice.ReadLineAsync());
        Assert.AreEqual("ILN 7 NLN bob bob", await alice.ReadLineAsync());
        Assert.AreEqual("ADD 0 RL 2 alice alice", await bob.ReadLineAsync());

        await alice.SendAsync("REM 8 FL bob");
        Assert.AreEqual("REM 8 FL 3 bob", await alice.ReadLineAsync());

        // Bob's next status change must no longer reach alice. Bob's QNG sequences his handler past the
        // CHG (broadcast included), so if an NLN had been sent it would already sit ahead of alice's QNG.
        await bob.SendAsync("CHG 9 AWY");
        Assert.AreEqual("CHG 9 AWY", await bob.ReadLineAsync());
        await bob.SendAsync("PNG");
        Assert.AreEqual("QNG 50", await bob.ReadLineAsync());

        await alice.SendAsync("PNG");
        Assert.AreEqual("QNG 50", await alice.ReadLineAsync(), "After REM FL the contact's presence must no longer be delivered");

        Assert.IsTrue(Mind.Db!.MsnListGet("bob", "RL").Any(e => e.Contact == "alice"), "REM FL must leave the target's RL entry intact (real MSN semantics)");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task AddUnknownName_AcksIdenticallyAndStaysDark()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");

        // The unknown name and the real-but-offline member must be observationally identical: same ack
        // shape, and in both cases the next reply after a PNG is the QNG - no ILN, no error, nothing.
        await alice.SendAsync("ADD 5 FL ghost ghost 0");
        Assert.AreEqual("ADD 5 FL 2 ghost ghost 0", await alice.ReadLineAsync(), "An unknown name must ack in exactly the shape a real one does");
        await alice.SendAsync("PNG");
        Assert.AreEqual("QNG 50", await alice.ReadLineAsync(), "Nothing may follow the ack that distinguishes the unknown name");

        await alice.SendAsync("ADD 6 FL bob bob 0");
        Assert.AreEqual("ADD 6 FL 3 bob bob 0", await alice.ReadLineAsync());
        await alice.SendAsync("PNG");
        Assert.AreEqual("QNG 50", await alice.ReadLineAsync(), "An offline real member looks exactly like the unknown one");

        var forward = Mind.Db!.MsnListGet("alice", "FL");

        Assert.IsTrue(forward.Any(e => e.Contact == "ghost"), "The unknown name is stored like any other entry");
        Assert.IsTrue(forward.Any(e => e.Contact == "bob"));
        Assert.AreEqual(0, Mind.Db!.MsnListGet("ghost", "RL").Count, "A name resolving to no account has no reverse side");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Bl_SuppressesPresenceFromTheBlockedMember()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);
        using var bob = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("CHG 5 NLN");
        Assert.AreEqual("CHG 5 NLN", await alice.ReadLineAsync());

        await bob.LoginAsync("bob");
        await bob.SendAsync("CHG 6 NLN");
        Assert.AreEqual("CHG 6 NLN", await bob.ReadLineAsync());

        await alice.SendAsync("ADD 7 FL bob bob 0");
        Assert.AreEqual("ADD 7 FL 2 bob bob 0", await alice.ReadLineAsync());
        Assert.AreEqual("ILN 7 NLN bob bob", await alice.ReadLineAsync());
        Assert.AreEqual("ADD 0 RL 2 alice alice", await bob.ReadLineAsync());

        // Bob blocks alice: even though her FL names him, his presence must stop reaching her.
        await bob.SendAsync("ADD 8 BL alice alice");
        Assert.AreEqual("ADD 8 BL 3 alice alice", await bob.ReadLineAsync());

        await bob.SendAsync("CHG 9 AWY");
        Assert.AreEqual("CHG 9 AWY", await bob.ReadLineAsync());
        await bob.SendAsync("PNG");
        Assert.AreEqual("QNG 50", await bob.ReadLineAsync());

        await alice.SendAsync("PNG");
        Assert.AreEqual("QNG 50", await alice.ReadLineAsync(), "A member's BL must veto their presence reaching the blocked member");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task GtcBlp_PersistAndServeBackInSyn()
    {
        var server = new MsnServer(IPAddress.Loopback, 0);

        using (var alice = new MsnConn(server))
        {
            await alice.LoginAsync("alice");
            await alice.SendAsync("GTC 5 N");
            Assert.AreEqual("GTC 5 2 N", await alice.ReadLineAsync(), "GTC must ack with the bumped serial");

            await alice.SendAsync("BLP 6 BL");
            Assert.AreEqual("BLP 6 3 BL", await alice.ReadLineAsync());
        }

        using var again = new MsnConn(server);

        await again.LoginAsync("alice");
        await again.SendAsync("SYN 7");

        Assert.AreEqual("SYN 7 3", await again.ReadLineAsync());
        Assert.AreEqual("GTC 7 3 N", await again.ReadLineAsync(), "SYN must serve the stored GTC back");
        Assert.AreEqual("BLP 7 3 BL", await again.ReadLineAsync(), "SYN must serve the stored BLP back");
        Assert.AreEqual("LSG 7 3 1 1 0 Other%20Contacts 0", await again.ReadLineAsync());
        Assert.AreEqual("LST 7 FL 3 0 0", await again.ReadLineAsync());
        Assert.AreEqual("LST 7 AL 3 0 0", await again.ReadLineAsync());
        Assert.AreEqual("LST 7 BL 3 0 0", await again.ReadLineAsync());
        Assert.AreEqual("LST 7 RL 3 0 0", await again.ReadLineAsync());
    }

    // The compatibility contract: with the flag off, stored rows must be invisible everywhere - SYN serves
    // the autoroster shape, ADD/REM go back to ack-only, and nothing new lands in storage.
    [TestMethod]
    [Timeout(20000)]
    public async Task FlagOff_EverythingRevertsToTodaysBehavior()
    {
        Mind.Db!.ConfigSet(ConfigNames.MsnPersistLists, false);

        // Rows left over from some flag-on life must not surface.
        Mind.Db!.MsnListUpsert("alice", "FL", "ghost", "ghost", 0);
        Mind.Db!.MsnListBumpSerial("alice");

        var server = new MsnServer(IPAddress.Loopback, 0);

        using var alice = new MsnConn(server);

        await alice.LoginAsync("alice");
        await alice.SendAsync("SYN 5");

        Assert.AreEqual("SYN 5 1", await alice.ReadLineAsync(), "Flag off must serve the version-1 shape, ignoring the stored serial");
        Assert.AreEqual("GTC 5 1 A", await alice.ReadLineAsync());
        Assert.AreEqual("BLP 5 1 AL", await alice.ReadLineAsync());
        Assert.AreEqual("LSG 5 1 1 1 0 Other%20Contacts 0", await alice.ReadLineAsync());

        // The FL is the server-built roster again: bob present, the stored ghost absent.
        var first = (await alice.ReadLineAsync()).Split(' ');

        Assert.AreEqual("LST", first[0]);
        Assert.AreEqual("FL", first[2]);

        var total = int.Parse(first[5]);
        var members = new List<string> { first[6] };

        for (var i = 1; i < total; i++)
        {
            members.Add((await alice.ReadLineAsync()).Split(' ')[6]);
        }

        CollectionAssert.Contains(members, "bob", "Flag off must serve the autoroster FL");
        CollectionAssert.DoesNotContain(members, "ghost", "Stored rows must be invisible with the flag off");

        // Drain the remaining three lists so the next read really is the ADD ack.
        foreach (var list in new[] { "AL", "BL", "RL" })
        {
            var head = (await alice.ReadLineAsync()).Split(' ');

            Assert.AreEqual(list, head[2]);

            for (var i = 1; i < int.Parse(head[5]); i++)
            {
                await alice.ReadLineAsync();
            }
        }

        // ADD reverts to ack-only on the session counter: nothing persisted, no reverse side.
        await alice.SendAsync("ADD 6 FL bob bob 0");
        Assert.AreEqual("ADD 6 FL 2 bob bob 0", await alice.ReadLineAsync(), "Flag off must ack from the session serial, exactly as today");

        Assert.AreEqual(1, Mind.Db!.MsnListGet("alice", "FL").Count, "The ack-only ADD must not have stored anything (only the seeded row remains)");
        Assert.AreEqual(0, Mind.Db!.MsnListGet("bob", "RL").Count, "The ack-only ADD must not have grown anyone's reverse list");
    }
}
