// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Text;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Pop3;

namespace Adversarial5.Pop3;

// Adversarial coverage of the POP3 command grammar, auth gating, and CRLF line buffering:
// greeting, USER (resolves an optional @domain against the hosted list), the auth-required
// REJECTIONS for STAT/LIST/RETR/DELE/UIDL/TOP/RSET/NOOP issued before authentication, CAPA, QUIT,
// unknown/malformed commands, blank lines, pipelining, split reads, and the 8KB line cap.
// PASS is NEVER issued (it calls Mind.Db.UserFetch), so no authenticated state is ever reached; the
// only DB touched is the config read behind MailDomains, served by MailTestEnv's file-backed context.
[TestClass]
public class Pop3SessionAdversarialTests
{
    private const string AuthKey = "auth";
    private const string UsernameKey = "username";
    private const string LineBufferKey = "_pop3_linebuf";

    private static Pop3Proxy NewProxy()
    {
        // Constructor only stores address/port (base Listener with secure=false does no socket/SSL work).
        // Banner identity and USER's hosted-domain check read MailDomains (config) at runtime.
        Mail.MailTestEnv.Ensure();

        return new Pop3Proxy(IPAddress.Loopback, 0);
    }

    // Safety net: no DB-free path blocks, but wrap every await so a regression that hangs fails fast
    // instead of pinning the whole test run.
    private static async Task<byte[]?> Guard(Task<byte[]> op)
    {
        var done = await Task.WhenAny(op, Task.Delay(2000));

        Assert.AreSame(op, done, "POP3 operation timed out (possible hang)");

        return await op;
    }

    private static async Task<ListenerSocket> Connect(Pop3Proxy proxy)
    {
        var conn = new ListenerSocket();

        var greeting = await Guard(proxy.ProcessConnection(conn));

        Assert.IsNotNull(greeting, "ProcessConnection must return a greeting");

        return conn;
    }

    private static async Task<byte[]?> Request(Pop3Proxy proxy, ListenerSocket conn, string raw)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);

        return await Guard(proxy.ProcessRequest(conn, bytes, bytes.Length));
    }

    private static string Text(byte[]? resp)
    {
        Assert.IsNotNull(resp, "expected a response but the handler returned null");

        return Encoding.ASCII.GetString(resp!);
    }

    #region Greeting

    [TestMethod]
    public async Task Greeting_ReturnsPop3ServerReady()
    {
        var proxy = NewProxy();
        var conn = new ListenerSocket();

        var greeting = Text(await Guard(proxy.ProcessConnection(conn)));

        Assert.AreEqual($"+OK pop3.{MailDomains.Primary} POP3 server ready\r\n", greeting);
    }

    [TestMethod]
    public async Task Greeting_InitializesUnauthenticatedState()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual(false, conn.DataBag[AuthKey]);
        Assert.AreEqual(string.Empty, conn.DataBag[UsernameKey]);
    }

    #endregion

    #region USER (DB-free: stores the name only)

    [TestMethod]
    public async Task User_StoresName_ReturnsAcceptedOK()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var text = Text(await Request(proxy, conn, "USER fox\r\n"));

        Assert.AreEqual("+OK User name accepted, password please\r\n", text);
    }

    [TestMethod]
    public async Task User_UpdatesStoredUsername()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        await Request(proxy, conn, "USER fox\r\n");

        Assert.AreEqual("fox", conn.DataBag[UsernameKey].ToString());
        // Storing the name must NOT authenticate the session.
        Assert.AreEqual(false, conn.DataBag[AuthKey]);
    }

    [TestMethod]
    public async Task User_NoArgument_Rejected_StoresNothing()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var text = Text(await Request(proxy, conn, "USER\r\n"));

        // USER requires a resolvable mailbox now that logins pass through the hosted-domain seam;
        // the old lenient path (+OK, stage an empty name that PASS then fails) is gone.
        StringAssert.StartsWith(text, "-ERR", text);
        Assert.AreEqual(string.Empty, conn.DataBag[UsernameKey].ToString());
    }

    [TestMethod]
    public async Task User_ExtraSpacesBetweenArgs_TrimsToName()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var text = Text(await Request(proxy, conn, "USER      fox\r\n"));

        Assert.AreEqual("+OK User name accepted, password please\r\n", text);
        // ParseCommandLine splits on the first space then Trim()s the remainder, collapsing the extra spaces.
        Assert.AreEqual("fox", conn.DataBag[UsernameKey].ToString());
    }

    [TestMethod]
    public async Task Lowercase_User_Accepted()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var text = Text(await Request(proxy, conn, "user fox\r\n"));

        Assert.AreEqual("+OK User name accepted, password please\r\n", text);
        Assert.AreEqual("fox", conn.DataBag[UsernameKey].ToString());
    }

    #endregion

    #region Auth-required commands rejected before authentication

    [TestMethod]
    public async Task Stat_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "STAT\r\n")));
    }

    [TestMethod]
    public async Task List_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "LIST\r\n")));
    }

    [TestMethod]
    public async Task Retr_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // Argument present but auth guard fires first, so the message index is never dereferenced.
        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "RETR 1\r\n")));
    }

    [TestMethod]
    public async Task Dele_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "DELE 1\r\n")));
    }

    [TestMethod]
    public async Task Uidl_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "UIDL\r\n")));
    }

    [TestMethod]
    public async Task Top_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "TOP 1 5\r\n")));
    }

    [TestMethod]
    public async Task Rset_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "RSET\r\n")));
    }

    [TestMethod]
    public async Task Noop_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "NOOP\r\n")));
    }

    [TestMethod]
    public async Task Lowercase_Stat_BeforeAuth_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // Command tokens are upper-cased before the auth switch, so lowercase does not slip past the guard.
        Assert.AreEqual("-ERR Not authenticated\r\n", Text(await Request(proxy, conn, "stat\r\n")));
    }

    #endregion

    #region Unknown / malformed commands

    [TestMethod]
    public async Task UnknownCommand_Rejected()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        Assert.AreEqual("-ERR Unknown command\r\n", Text(await Request(proxy, conn, "FROBNICATE now\r\n")));
    }

    [TestMethod]
    public async Task LeadingWhitespace_BreaksCommandParsing()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // A leading space makes the first split token empty, so the command is unrecognised.
        // This documents observed behaviour: leading whitespace is not tolerated (no auth impact since
        // it degrades to an unknown-command rejection).
        var text = Text(await Request(proxy, conn, "  USER fox\r\n"));

        Assert.AreEqual("-ERR Unknown command\r\n", text);
    }

    [TestMethod]
    public async Task OversizedCompleteLine_HandledAsUnknown_NoCrash()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // A single complete (newline-terminated) line far larger than a real command must not crash;
        // it parses to one giant unknown token. 12KB is trivial to allocate; no overflow is attempted here.
        var raw = new string('A', 12000) + "\r\n";

        var text = Text(await Request(proxy, conn, raw));

        Assert.IsTrue(text.StartsWith("-ERR"), text);
        Assert.IsTrue(text.Contains("Unknown command"), text);
    }

    #endregion

    #region CAPA / QUIT

    [TestMethod]
    public async Task Capa_ReturnsDotTerminatedCapabilityList()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var text = Text(await Request(proxy, conn, "CAPA\r\n"));

        Assert.IsTrue(text.StartsWith("+OK Capability list follows\r\n"), text);
        Assert.IsTrue(text.Contains("USER\r\n"), text);
        Assert.IsTrue(text.Contains("UIDL\r\n"), text);
        Assert.IsTrue(text.Contains("TOP\r\n"), text);
        Assert.IsTrue(text.Contains("RESP-CODES\r\n"), text);
        Assert.IsTrue(text.EndsWith(".\r\n"), text);
    }

    [TestMethod]
    public async Task Quit_ReturnsSignOff_AndClosesConnection()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // QUIT before auth skips the mail-delete branch (Authenticated=false), so Mind.Db is never touched.
        var text = Text(await Request(proxy, conn, "QUIT\r\n"));

        Assert.IsTrue(text.StartsWith("+OK"), text);
        Assert.IsTrue(text.Contains("POP3 proxy signing off"), text);
        Assert.IsFalse(conn.IsKeepAlive, "QUIT must drop keep-alive so the listener closes the connection");
    }

    #endregion

    #region Blank lines

    [TestMethod]
    public async Task EmptyLine_ProducesNoResponse()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var resp = await Request(proxy, conn, "\r\n");

        Assert.IsNull(resp, "a bare CRLF is whitespace-only and must be skipped with no reply");
    }

    [TestMethod]
    public async Task WhitespaceOnlyLine_ProducesNoResponse()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var resp = await Request(proxy, conn, "    \t  \r\n");

        Assert.IsNull(resp, "a whitespace-only line must be skipped with no reply");
    }

    [TestMethod]
    public async Task MultipleBlankLinesThenCommand_OnlyCommandAnswered()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var text = Text(await Request(proxy, conn, "\r\n\r\nCAPA\r\n"));

        // Exactly one response (CAPA); the blank lines contribute nothing.
        Assert.IsTrue(text.StartsWith("+OK Capability list follows\r\n"), text);
        Assert.IsFalse(text.Contains("+OK", StringComparison.Ordinal) && text.IndexOf("+OK", StringComparison.Ordinal) != text.LastIndexOf("+OK", StringComparison.Ordinal), "only one +OK expected");
    }

    #endregion

    #region Pipelining and split reads (the CRLF line buffer)

    [TestMethod]
    public async Task Pipelined_UserThenCapa_BothAnswered()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // Two commands in one packet: the buffer loop must answer BOTH, in order.
        var text = Text(await Request(proxy, conn, "USER fox\r\nCAPA\r\n"));

        Assert.IsTrue(text.StartsWith("+OK User name accepted, password please\r\n"), text);
        Assert.IsTrue(text.Contains("Capability list follows"), text);

        var userIdx = text.IndexOf("User name accepted", StringComparison.Ordinal);
        var capaIdx = text.IndexOf("Capability list follows", StringComparison.Ordinal);

        Assert.IsTrue(userIdx >= 0 && capaIdx > userIdx, "USER reply must precede CAPA reply");
    }

    [TestMethod]
    public async Task Pipelined_AfterQuit_TrailingCommandsDropped()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // Once QUIT flips keep-alive off, the loop stops; the trailing USER must not be processed.
        var text = Text(await Request(proxy, conn, "CAPA\r\nQUIT\r\nUSER fox\r\n"));

        Assert.IsTrue(text.Contains("Capability list follows"), text);
        Assert.IsTrue(text.Contains("POP3 proxy signing off"), text);
        Assert.IsFalse(text.Contains("User name accepted"), "commands after QUIT must be discarded");
        Assert.IsFalse(conn.IsKeepAlive);
    }

    [TestMethod]
    public async Task CommandSplitAcrossReads_BufferedThenAnswered()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // First packet has no newline: nothing to answer yet, buffer retains the partial line.
        var first = await Request(proxy, conn, "US");

        Assert.IsNull(first, "a partial line (no CRLF) must produce no response");
        Assert.AreEqual("US", (string?)conn.DataBag[LineBufferKey]);

        // Second packet completes the line: the reassembled USER command is answered.
        var text = Text(await Request(proxy, conn, "ER fox\r\n"));

        Assert.AreEqual("+OK User name accepted, password please\r\n", text);
        Assert.AreEqual("fox", conn.DataBag[UsernameKey].ToString());
    }

    [TestMethod]
    public async Task PartialLineUnderCap_RetainedInBuffer()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        var resp = await Request(proxy, conn, "CAP");

        Assert.IsNull(resp);
        Assert.AreEqual("CAP", (string?)conn.DataBag[LineBufferKey]);
    }

    [TestMethod]
    public async Task OversizedPartialLine_BufferSilentlyDropped()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // A partial line (no CRLF) larger than the 8KB cap is discarded to bound memory. 9000 bytes is
        // just over 8192 and tiny to allocate; this exercises the cap, it does not attempt an overflow.
        var raw = new string('A', 9000);

        var resp = await Request(proxy, conn, raw);

        Assert.IsNull(resp, "an incomplete oversized line yields no response");
        Assert.AreEqual(string.Empty, (string?)conn.DataBag[LineBufferKey], "over-cap partial buffer must be reset to empty");
    }

    #endregion

    #region Line-termination edge cases

    [TestMethod]
    public async Task LfOnlyLineTermination_Accepted()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // The buffer splits on '\n' and TrimEnd's '\r', so a bare-LF line still parses.
        var text = Text(await Request(proxy, conn, "USER fox\n"));

        Assert.AreEqual("+OK User name accepted, password please\r\n", text);
    }

    [TestMethod]
    public async Task TrailingCarriageReturns_Trimmed_CommandStillParses()
    {
        var proxy = NewProxy();
        var conn = await Connect(proxy);

        // Extra trailing CRs are trimmed by TrimEnd('\r'); the command remains CAPA.
        var text = Text(await Request(proxy, conn, "CAPA\r\r\n"));

        Assert.IsTrue(text.StartsWith("+OK Capability list follows\r\n"), text);
    }

    #endregion
}