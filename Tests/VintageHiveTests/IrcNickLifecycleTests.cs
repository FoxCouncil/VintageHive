// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// IRC nick reservation lifecycle across every disconnect style. The historical leak: HandleQuit
// removed the Users entry itself, so ProcessDisconnection's guarded ReservedNicks release never
// ran and a graceful QUIT locked the nick until process restart (abrupt disconnects were fine).
// Cleanup is owned by ProcessDisconnection now; these tests pin the ghost-free invariant - after
// ANY disconnect style the nick is grantable again and WHOIS shows nothing. Each test uses its own
// IrcProxy instance (Users/ReservedNicks are per-instance), driven exactly like the Listener does:
// ProcessConnection, ProcessRequest per line, ProcessDisconnection when keep-alive drops.

using System.Net;
using System.Net.Sockets;
using VintageHive;
using VintageHive.Network;
using VintageHive.Proxy.Irc;

namespace Adversarial5.IrcNick;

[TestClass]
public class IrcNickLifecycleTests
{
    private const string TestPassword = "hunter2";

    private static IrcProxy NewProxy()
    {
        // Registration checks the user table (UserExistsByUsername / UserFetch).
        Mail.MailTestEnv.Ensure();

        return new IrcProxy(IPAddress.Loopback, 0);
    }

    private static async Task<ListenerSocket> Connect(IrcProxy proxy)
    {
        // Registration reads connection.RemoteIP, so the harness backs each session with a real
        // connected loopback socket pair (unlike the mail suites, whose handlers never touch it).
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);

        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        client.Connect(listener.LocalEndPoint!);

        var server = listener.Accept();

        listener.Dispose();

        var conn = new ListenerSocket { RawSocket = server };

        // Keep the peer alive for the connection's lifetime so Remote stays resolvable.
        conn.DataBag["_test_peer_socket"] = client;

        await proxy.ProcessConnection(conn);

        return conn;
    }

    private static Task<string> Send(IrcProxy proxy, ListenerSocket conn, string line)
    {
        return Mail.MailTestEnv.Cmd(proxy, conn, line);
    }

    // NICK + USER; returns the registration reply (001 burst or an error).
    private static async Task<string> Register(IrcProxy proxy, ListenerSocket conn, string nick)
    {
        await Send(proxy, conn, $"NICK {nick}");

        return await Send(proxy, conn, $"USER {nick} 0 * :Test User");
    }

    private static async Task<ListenerSocket> RegisterFresh(IrcProxy proxy, string nick)
    {
        var conn = await Connect(proxy);
        var reply = await Register(proxy, conn, nick);

        StringAssert.Contains(reply, " 001 ", $"registration failed for {nick}: {reply}");

        return conn;
    }

    [TestMethod]
    public async Task Quit_ThenReconnectSameNick_Welcome_Not433()
    {
        var proxy = NewProxy();
        var conn = await RegisterFresh(proxy, "qn1");

        var quit = await Send(proxy, conn, "QUIT :done for tonight");

        StringAssert.Contains(quit, "ERROR :Closing Link", quit);
        Assert.IsFalse(conn.IsKeepAlive);

        await proxy.ProcessDisconnection(conn);

        var reply = await Register(proxy, await Connect(proxy), "qn1");

        StringAssert.Contains(reply, " 001 ", reply);
        Assert.IsFalse(reply.Contains(" 433 "), $"clean QUIT leaked the nick: {reply}");
    }

    [TestMethod]
    public async Task KilledSocket_NoQuit_ReconnectSameNick_Welcome()
    {
        // Worked before the fix; pinned so cleanup ownership never regresses the abrupt path.
        var proxy = NewProxy();
        var conn = await RegisterFresh(proxy, "qn2");

        await proxy.ProcessDisconnection(conn);

        var reply = await Register(proxy, await Connect(proxy), "qn2");

        StringAssert.Contains(reply, " 001 ", reply);
    }

    [TestMethod]
    public async Task NickChange_ThenQuit_BothNamesFree()
    {
        var proxy = NewProxy();
        var conn = await RegisterFresh(proxy, "qn3a");

        await Send(proxy, conn, "NICK qn3b");
        await Send(proxy, conn, "QUIT :bye");
        await proxy.ProcessDisconnection(conn);

        StringAssert.Contains(await Register(proxy, await Connect(proxy), "qn3a"), " 001 ", "old nick not freed after change+quit");
        StringAssert.Contains(await Register(proxy, await Connect(proxy), "qn3b"), " 001 ", "new nick not freed after change+quit");
    }

    [TestMethod]
    public async Task NickChange_ThenKilledSocket_BothNamesFree()
    {
        var proxy = NewProxy();
        var conn = await RegisterFresh(proxy, "qn4a");

        await Send(proxy, conn, "NICK qn4b");
        await proxy.ProcessDisconnection(conn);

        StringAssert.Contains(await Register(proxy, await Connect(proxy), "qn4a"), " 001 ", "old nick not freed after change+kill");
        StringAssert.Contains(await Register(proxy, await Connect(proxy), "qn4b"), " 001 ", "new nick not freed after change+kill");
    }

    [TestMethod]
    public async Task FailedPass464_ThenDisconnect_RegisteredNameFree()
    {
        var proxy = NewProxy();
        var nick = "ircreg1";

        if (!Mind.Db.UserExistsByUsername(nick))
        {
            Assert.IsTrue(Mind.Db.UserCreate(nick, TestPassword));
        }

        try
        {
            var conn = await Connect(proxy);

            await Send(proxy, conn, "PASS wrongpw");
            await Send(proxy, conn, $"NICK {nick}");

            var reply = await Send(proxy, conn, $"USER {nick} 0 * :Test User");

            StringAssert.Contains(reply, " 464 ", reply);
            Assert.IsFalse(conn.IsKeepAlive);

            await proxy.ProcessDisconnection(conn);

            // Correct credentials must now claim the name cleanly.
            var conn2 = await Connect(proxy);

            await Send(proxy, conn2, $"PASS {TestPassword}");
            await Send(proxy, conn2, $"NICK {nick}");

            var welcome = await Send(proxy, conn2, $"USER {nick} 0 * :Test User");

            StringAssert.Contains(welcome, " 001 ", $"failed-PASS path leaked the reservation: {welcome}");
        }
        finally
        {
            Mind.Db.UserDelete(nick);
        }
    }

    [TestMethod]
    public async Task RapidQuitReconnectCycles_No433AtAnyPoint()
    {
        var proxy = NewProxy();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var conn = await Connect(proxy);
            var reply = await Register(proxy, conn, "qn5");

            StringAssert.Contains(reply, " 001 ", $"cycle {cycle}: {reply}");
            Assert.IsFalse(reply.Contains(" 433 "), $"cycle {cycle} hit 433: {reply}");

            await Send(proxy, conn, "QUIT :cycling");
            await proxy.ProcessDisconnection(conn);
        }
    }

    [TestMethod]
    public async Task GhostFreeInvariant_AfterQuit_WhoisEmptyAndNickGrantable()
    {
        var proxy = NewProxy();
        var watcher = await RegisterFresh(proxy, "qn6w");
        var target = await RegisterFresh(proxy, "qn6t");

        await Send(proxy, target, "QUIT :vanishing");
        await proxy.ProcessDisconnection(target);

        var whois = await Send(proxy, watcher, "WHOIS qn6t");

        StringAssert.Contains(whois, " 401 ", $"WHOIS shows a ghost after QUIT: {whois}");

        StringAssert.Contains(await Register(proxy, await Connect(proxy), "qn6t"), " 001 ", "nick not grantable after QUIT");
    }

    [TestMethod]
    public async Task Quit_BeforeRegistrationCompletes_ReservationFreed()
    {
        // NICK sent, USER never sent: the nick lives only in the DataBag reservation.
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        await Send(proxy, conn, "NICK qn7");
        await Send(proxy, conn, "QUIT :changed my mind");

        Assert.IsFalse(conn.IsKeepAlive);

        await proxy.ProcessDisconnection(conn);

        StringAssert.Contains(await Register(proxy, await Connect(proxy), "qn7"), " 001 ", "pre-registration QUIT leaked the reservation");
    }

    [TestMethod]
    public async Task DoubleQuit_NoCrash_NickFreedOnce()
    {
        var proxy = NewProxy();
        var conn = await RegisterFresh(proxy, "qn8");

        await Send(proxy, conn, "QUIT :first");

        // A second QUIT on the still-open handler path must not throw or corrupt state.
        await Send(proxy, conn, "QUIT :second");

        await proxy.ProcessDisconnection(conn);

        StringAssert.Contains(await Register(proxy, await Connect(proxy), "qn8"), " 001 ", "double QUIT wedged the nick");
    }

    [TestMethod]
    public async Task DisconnectRacingQuit_DoubleDisconnection_Idempotent()
    {
        var proxy = NewProxy();
        var conn = await RegisterFresh(proxy, "qn9");

        await Send(proxy, conn, "QUIT :racing");

        // The Listener fires ProcessDisconnection once, but a race can look like twice - both
        // passes must be harmless no-ops the second time through.
        await proxy.ProcessDisconnection(conn);
        await proxy.ProcessDisconnection(conn);

        StringAssert.Contains(await Register(proxy, await Connect(proxy), "qn9"), " 001 ", "double disconnection wedged the nick");
    }
}
