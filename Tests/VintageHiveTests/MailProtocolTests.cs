// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// Mail protocol conformance tests. SMTP/POP3/IMAP are all one-shot (a command line in via
// ProcessRequest, a reply out), so we drive the real handlers and assert the reply codes/format
// RFC 5321 / RFC 1939 / RFC 3501 prescribe. Reply text is left loose; the codes and structure are
// the contract.

using System.Net;
using System.Text;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Network;
using VintageHive.Proxy.Imap;
using VintageHive.Proxy.Pop3;
using VintageHive.Proxy.Smtp;

namespace Mail;

internal static class MailTestEnv
{
    private static readonly object Gate = new();
    private static bool _ready;

    // SMTP's constructor starts a postmaster thread that touches Mind.PostOfficeDb; auth on all three
    // touches Mind.Db. Both are file-backed and only read here, so empty initialized contexts suffice.
    // Their setters are private, so set once via reflection.
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
                Set(nameof(Mind.Db), new HiveDbContext());
            }

            if (Mind.PostOfficeDb == null)
            {
                Set(nameof(Mind.PostOfficeDb), new PostOfficeDbContext());
            }

            _ready = true;
        }
    }

    private static void Set(string prop, object value)
        => typeof(Mind).GetProperty(prop)!.GetSetMethod(nonPublic: true)!.Invoke(null, new[] { value });

    public static string Decode(byte[] b) => b == null ? "" : Encoding.ASCII.GetString(b);

    public static async Task<string> Cmd(Listener proxy, ListenerSocket conn, string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
        return Decode(await proxy.ProcessRequest(conn, bytes, bytes.Length));
    }
}

[TestClass]
public class SmtpConformanceTests
{
    private static SmtpProxy _proxy = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        MailTestEnv.Ensure();
        _proxy = new SmtpProxy(IPAddress.Loopback, 0);
    }

    [TestMethod]
    public async Task Greeting_Is220()
    {
        // RFC 5321 3.1: the server opens with a 220 greeting.
        var greeting = MailTestEnv.Decode(await _proxy.ProcessConnection(new ListenerSocket()));
        StringAssert.StartsWith(greeting, "220 ");
    }

    [TestMethod]
    public async Task Helo_Replies250()
    {
        // RFC 5321 4.1.1.1
        StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "HELO example.com"), "250 ");
    }

    [TestMethod]
    public async Task Ehlo_Replies250MultilineWithAuthLogin()
    {
        // RFC 5321 4.1.1.1 + RFC 4954: multi-line 250- reply advertising AUTH.
        var r = await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "EHLO example.com");
        StringAssert.StartsWith(r, "250-");
        StringAssert.Contains(r, "250 AUTH LOGIN");
    }

    [TestMethod]
    public async Task Noop_Replies250()
        => StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "NOOP"), "250 "); // RFC 5321 4.1.1.9

    [TestMethod]
    public async Task Quit_Replies221()
        => StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "QUIT"), "221 "); // RFC 5321 4.1.1.10

    [TestMethod]
    public async Task Vrfy_Replies252()
        => StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "VRFY fox"), "252 "); // RFC 5321 3.5.3

    [TestMethod]
    public async Task Data_Replies354()
        => StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "DATA"), "354 "); // RFC 5321 4.1.1.4

    [TestMethod]
    public async Task Help_Replies502NotImplemented()
        => StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "HELP"), "502 ");

    [TestMethod]
    public async Task UnimplementedVerb_Replies500()
        // EXPN is a real RFC 5321 verb this server doesn't implement -> falls through to 500.
        => StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "EXPN list"), "500 ");

    [TestMethod]
    public async Task MailBeforeAuth_Replies530()
        // RFC 4954: mail before AUTH -> 530 authentication required.
        => StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, new ListenerSocket(), "MAIL FROM:<fox@hive.com>"), "530 ");

    [TestMethod]
    public async Task Rset_AfterHelo_Replies250()
    {
        // RFC 5321 4.1.1.5
        var conn = new ListenerSocket();
        await MailTestEnv.Cmd(_proxy, conn, "HELO example.com");
        StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, conn, "RSET"), "250 ");
    }
}

[TestClass]
public class Pop3ConformanceTests
{
    private static Pop3Proxy _proxy = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        MailTestEnv.Ensure();
        _proxy = new Pop3Proxy(IPAddress.Loopback, 0);
    }

    private static async Task<(ListenerSocket conn, string greeting)> Connect()
    {
        var conn = new ListenerSocket();
        var greeting = MailTestEnv.Decode(await _proxy.ProcessConnection(conn));
        return (conn, greeting);
    }

    [TestMethod]
    public async Task Greeting_IsOk()
    {
        // RFC 1939 3: greeting is a +OK status indicator.
        var (_, greeting) = await Connect();
        StringAssert.StartsWith(greeting, "+OK");
    }

    [TestMethod]
    public async Task User_RepliesOk()
    {
        var (c, _) = await Connect();
        StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, c, "USER fox"), "+OK");
    }

    [TestMethod]
    public async Task StatBeforeAuth_RepliesErr()
    {
        // RFC 1939: STAT is a TRANSACTION-state command; unauthenticated -> -ERR.
        var (c, _) = await Connect();
        StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, c, "STAT"), "-ERR");
    }

    [TestMethod]
    public async Task Capa_RepliesOkAndListsCapabilities()
    {
        // RFC 2449
        var (c, _) = await Connect();
        var r = await MailTestEnv.Cmd(_proxy, c, "CAPA");
        StringAssert.StartsWith(r, "+OK");
        StringAssert.Contains(r, "USER");
        StringAssert.Contains(r, "UIDL");
        StringAssert.Contains(r, "TOP");
    }

    [TestMethod]
    public async Task Quit_RepliesOk()
    {
        var (c, _) = await Connect();
        StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, c, "QUIT"), "+OK");
    }

    [TestMethod]
    public async Task UnknownCommand_RepliesErr()
    {
        var (c, _) = await Connect();
        StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, c, "FLARGLE"), "-ERR");
    }

    [TestMethod]
    public async Task PassWithBadCredentials_RepliesErr()
    {
        var (c, _) = await Connect();
        await MailTestEnv.Cmd(_proxy, c, "USER nobody");
        StringAssert.StartsWith(await MailTestEnv.Cmd(_proxy, c, "PASS wrongpass"), "-ERR");
    }
}

[TestClass]
public class ImapConformanceTests
{
    private static ImapProxy _proxy = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        MailTestEnv.Ensure();
        _proxy = new ImapProxy(IPAddress.Loopback, 0);
    }

    private static async Task<ListenerSocket> Connect()
    {
        var conn = new ListenerSocket();
        await _proxy.ProcessConnection(conn);
        return conn;
    }

    [TestMethod]
    public async Task Greeting_IsUntaggedOk()
    {
        // RFC 3501 7.1.1: the greeting is an untagged OK.
        var greeting = MailTestEnv.Decode(await _proxy.ProcessConnection(new ListenerSocket()));
        StringAssert.StartsWith(greeting, "* OK");
    }

    [TestMethod]
    public async Task Capability_ListsImap4rev1AndTaggedOk()
    {
        // RFC 3501 6.1.1: untagged CAPABILITY line then a tagged OK.
        var r = await MailTestEnv.Cmd(_proxy, await Connect(), "a1 CAPABILITY");
        StringAssert.Contains(r, "* CAPABILITY IMAP4rev1");
        StringAssert.Contains(r, "a1 OK");
    }

    [TestMethod]
    public async Task Noop_RepliesTaggedOk()
        => StringAssert.Contains(await MailTestEnv.Cmd(_proxy, await Connect(), "a2 NOOP"), "a2 OK"); // RFC 3501 6.1.2

    [TestMethod]
    public async Task Logout_SendsByeThenTaggedOk()
    {
        // RFC 3501 6.1.3: untagged BYE, then tagged OK.
        var r = await MailTestEnv.Cmd(_proxy, await Connect(), "a3 LOGOUT");
        StringAssert.Contains(r, "* BYE");
        StringAssert.Contains(r, "a3 OK");
    }

    [TestMethod]
    public async Task UnknownCommand_RepliesTaggedBad()
        => StringAssert.Contains(await MailTestEnv.Cmd(_proxy, await Connect(), "a4 FLARGLE"), "a4 BAD");

    [TestMethod]
    public async Task InvalidFormat_RepliesUntaggedBad()
        // A line with no tag+command doesn't match the command grammar.
        => StringAssert.Contains(await MailTestEnv.Cmd(_proxy, await Connect(), "garbage"), "* BAD");

    [TestMethod]
    public async Task LoginWithBadCredentials_RepliesTaggedNo()
        // RFC 3501 6.2.3: failed LOGIN -> tagged NO.
        => StringAssert.Contains(await MailTestEnv.Cmd(_proxy, await Connect(), "a5 LOGIN nobody wrongpass"), "a5 NO");
}
