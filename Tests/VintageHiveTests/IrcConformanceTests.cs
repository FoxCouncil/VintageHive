// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// RFC-conformance tests: drive the real IrcProxy command handlers end-to-end and assert the server
// replies with the sequences/numerics RFC 1459 / RFC 2812 prescribe. The goal is a "good spec
// citizen" - respectful of 1990s IRC and, where VintageHive intentionally deviates, the deviation is
// asserted explicitly (and cited) so a regression is caught either way.

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Network;
using VintageHive.Proxy.Irc;

namespace Irc;

// A real loopback socket wired into a ListenerSocket, so the handlers see a genuine RemoteEndPoint
// and writable stream (matching how the DNS/H.245/RTP tests exercise real sockets).
internal sealed class IrcTestClient : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Socket _client;
    private readonly Socket _server;

    public ListenerSocket Connection { get; }

    public IrcTestClient()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        var acceptTask = _listener.AcceptSocketAsync();

        _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _client.Connect(IPAddress.Loopback, port);

        _server = acceptTask.GetAwaiter().GetResult();
        _listener.Stop();

        Connection = new ListenerSocket
        {
            RawSocket = _server,
            Stream = new NetworkStream(_server),
            IsKeepAlive = true,
        };
    }

    public void Dispose()
    {
        try { Connection.Stream?.Dispose(); } catch { }
        try { _server.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
    }
}

internal static class IrcTestEnv
{
    private static readonly object Gate = new();
    private static bool _ready;

    // Mind.Db is a file-backed SQLite; registration only reads it (UserExistsByUsername), so an empty
    // initialized context is enough. Its setter is private, so we set it once via reflection.
    public static void EnsureDb()
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

            _ready = true;
        }
    }

    public static async Task<string> Send(IrcProxy proxy, IrcTestClient c, string lines)
    {
        var bytes = Encoding.UTF8.GetBytes(lines + "\r\n");
        var resp = await proxy.ProcessRequest(c.Connection, bytes, bytes.Length);
        return resp == null ? "" : Encoding.UTF8.GetString(resp);
    }

    public static Task<string> Register(IrcProxy proxy, IrcTestClient c, string nick = "fox")
        => Send(proxy, c, $"NICK {nick}\r\nUSER {nick} 0 * :Real Name");
}

[TestClass]
public class IrcRegistrationConformanceTests
{
    [ClassInitialize]
    public static void Init(TestContext _) => IrcTestEnv.EnsureDb();

    private static IrcProxy NewProxy() => new(IPAddress.Loopback, 0);

    [TestMethod]
    public async Task Registration_SendsWelcomeNumerics001To004InOrder()
    {
        // RFC 2812 5.1: on successful registration the server sends replies 001-004.
        using var c = new IrcTestClient();
        var reply = await IrcTestEnv.Register(NewProxy(), c, "fox");

        var i1 = reply.IndexOf(" 001 ");
        var i2 = reply.IndexOf(" 002 ");
        var i3 = reply.IndexOf(" 003 ");
        var i4 = reply.IndexOf(" 004 ");

        Assert.IsTrue(i1 >= 0 && i2 > i1 && i3 > i2 && i4 > i3, $"001-004 not present and in order:\n{reply}");
    }

    [TestMethod]
    public async Task Registration_WelcomeTextMatchesRfc()
    {
        // RFC 2812 5.1 RPL_WELCOME: "Welcome to the Internet Relay Network <nick>!<user>@<host>"
        using var c = new IrcTestClient();
        var reply = await IrcTestEnv.Register(NewProxy(), c, "fox");

        StringAssert.Contains(reply, "001 fox :Welcome to the Internet Relay Network fox!fox@");
    }

    [TestMethod]
    public async Task Registration_IsFollowedByMotd()
    {
        // RFC 2812 5.1: MOTD is bracketed by RPL_MOTDSTART (375) and RPL_ENDOFMOTD (376).
        using var c = new IrcTestClient();
        var reply = await IrcTestEnv.Register(NewProxy(), c, "fox");

        StringAssert.Contains(reply, " 375 ");
        StringAssert.Contains(reply, " 376 ");
    }

    [TestMethod]
    public async Task CommandBeforeRegistration_RepliesNotRegistered()
    {
        // RFC 2812 3.2.1: commands before registration get ERR_NOTREGISTERED (451).
        using var c = new IrcTestClient();
        var reply = await IrcTestEnv.Send(NewProxy(), c, "JOIN #hive");

        StringAssert.Contains(reply, " 451 ");
    }

    [TestMethod]
    public async Task Nick_MissingParam_RepliesNoNicknameGiven()
    {
        // RFC 2812 3.1.2: ERR_NONICKNAMEGIVEN (431).
        using var c = new IrcTestClient();
        var reply = await IrcTestEnv.Send(NewProxy(), c, "NICK");

        StringAssert.Contains(reply, " 431 ");
    }

    [TestMethod]
    public async Task Nick_Invalid_RepliesErroneousNickname()
    {
        // RFC 2812 2.3.1: a nick may not start with a digit -> ERR_ERRONEUSNICKNAME (432).
        using var c = new IrcTestClient();
        var reply = await IrcTestEnv.Send(NewProxy(), c, "NICK 1badnick");

        StringAssert.Contains(reply, " 432 ");
    }

    [TestMethod]
    public async Task Reregister_RepliesAlreadyRegistered()
    {
        // RFC 2812 3.1.3: a second USER after registration -> ERR_ALREADYREGISTRED (462).
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");

        var reply = await IrcTestEnv.Send(proxy, c, "USER x 0 * :y");

        StringAssert.Contains(reply, " 462 ");
    }

    [TestMethod]
    public async Task Nick_RfcNineCharLimit_IsAccepted_AndLongerIsToo_DocumentedDeviation()
    {
        // RFC 1459 1.2 / RFC 2812 2.3.1 cap nicknames at nine (9) characters. VintageHive intentionally
        // allows longer nicks (up to 30) like most modern ircds. Pin both so a regression in either
        // direction is caught: the 9-char RFC nick must work, and the era-extension must still hold.
        var proxy = NewProxy();

        using (var c1 = new IrcTestClient())
        {
            var reply = await IrcTestEnv.Register(proxy, c1, "ninecharx"); // exactly 9
            StringAssert.Contains(reply, " 001 ninecharx ");
        }

        using (var c2 = new IrcTestClient())
        {
            var reply = await IrcTestEnv.Register(proxy, c2, "fifteencharacter"); // > RFC 9
            StringAssert.Contains(reply, " 001 fifteencharacter ");
        }
    }
}

[TestClass]
public class IrcChannelConformanceTests
{
    [ClassInitialize]
    public static void Init(TestContext _) => IrcTestEnv.EnsureDb();

    private static IrcProxy NewProxy() => new(IPAddress.Loopback, 0);

    [TestMethod]
    public async Task Join_NewChannel_SendsJoinTopicNamesEndSequence()
    {
        // RFC 2812 3.2.1: a successful JOIN echoes the JOIN, then RPL_TOPIC/RPL_NOTOPIC (331/332),
        // then RPL_NAMREPLY (353) and RPL_ENDOFNAMES (366).
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");

        var reply = await IrcTestEnv.Send(proxy, c, "JOIN #test");

        var join = reply.IndexOf("JOIN");
        var notopic = reply.IndexOf(" 331 ");     // brand-new channel has no topic
        var names = reply.IndexOf(" 353 ");
        var end = reply.IndexOf(" 366 ");

        Assert.IsTrue(join >= 0, $"no JOIN echo:\n{reply}");
        Assert.IsTrue(notopic > join, $"no RPL_NOTOPIC after JOIN:\n{reply}");
        Assert.IsTrue(names > notopic && end > names, $"NAMES/ENDOFNAMES out of order:\n{reply}");
    }

    [TestMethod]
    public async Task Join_FirstUserOfNewChannel_IsOperatorInNames()
    {
        // The creator of a fresh channel is opped, so RPL_NAMREPLY lists them as @fox.
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");

        var reply = await IrcTestEnv.Send(proxy, c, "JOIN #test");

        StringAssert.Contains(reply, "@fox");
    }

    [TestMethod]
    public async Task Join_NoParams_RepliesNeedMoreParams()
    {
        // RFC 2812 3.2.1: ERR_NEEDMOREPARAMS (461).
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");

        var reply = await IrcTestEnv.Send(proxy, c, "JOIN");

        StringAssert.Contains(reply, " 461 ");
    }

    [TestMethod]
    public async Task Privmsg_UnknownNick_RepliesNoSuchNick()
    {
        // RFC 2812 3.3.1: ERR_NOSUCHNICK (401).
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");

        var reply = await IrcTestEnv.Send(proxy, c, "PRIVMSG ghost :hello");

        StringAssert.Contains(reply, " 401 ");
    }

    [TestMethod]
    public async Task Privmsg_UnknownChannel_RepliesNoSuchChannel()
    {
        // RFC 2812 3.3.1: ERR_NOSUCHCHANNEL (403).
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");

        var reply = await IrcTestEnv.Send(proxy, c, "PRIVMSG #ghost :hello");

        StringAssert.Contains(reply, " 403 ");
    }

    [TestMethod]
    public async Task Part_ChannelNotJoined_RepliesNoSuchChannel()
    {
        // PART on a channel the server doesn't know -> ERR_NOSUCHCHANNEL (403).
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");

        var reply = await IrcTestEnv.Send(proxy, c, "PART #nowhere :bye");

        StringAssert.Contains(reply, " 403 ");
    }

    [TestMethod]
    public async Task UnknownCommand_RepliesUnknownCommand()
    {
        // RFC 2812 3.: ERR_UNKNOWNCOMMAND (421).
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");

        var reply = await IrcTestEnv.Send(proxy, c, "FLARGLE nonsense");

        StringAssert.Contains(reply, " 421 ");
    }

    [TestMethod]
    public async Task Topic_SetThenQuery_RoundTrips()
    {
        // RFC 2812 3.2.4: setting a topic broadcasts TOPIC; querying returns RPL_TOPIC (332).
        using var c = new IrcTestClient();
        var proxy = NewProxy();
        await IrcTestEnv.Register(proxy, c, "fox");
        await IrcTestEnv.Send(proxy, c, "JOIN #test");

        var setReply = await IrcTestEnv.Send(proxy, c, "TOPIC #test :retro computing");
        StringAssert.Contains(setReply, "TOPIC #test :retro computing");

        var queryReply = await IrcTestEnv.Send(proxy, c, "TOPIC #test");
        StringAssert.Contains(queryReply, " 332 fox #test :retro computing");
    }
}
