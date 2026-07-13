// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Adversarial SMTP command-grammar and sequencing tests. Everything here stays in the DB-free zone:
// greeting, HELO/EHLO shape, MAIL/RCPT rejection BEFORE authentication (530), DATA before a
// transaction (503), unknown/known-unimplemented verbs, line buffering, pipelining, CRLF/LF edges,
// oversized lines, and the AUTH LOGIN handshake up to (never through) the DB user lookup. No test
// completes AUTH, delivers DATA, or otherwise reaches Mind.Db / Mind.PostOfficeDb.

using System.Net;
using System.Text;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Smtp;

namespace Adversarial5.Smtp;

internal static class SmtpAdv
{
    public static SmtpProxy NewProxy()
    {
        return new SmtpProxy(IPAddress.Loopback, 0);
    }

    public static string Decode(byte[] bytes)
    {
        return bytes == null ? string.Empty : Encoding.ASCII.GetString(bytes);
    }

    // First three characters of an SMTP reply are the status code (multiline replies repeat it).
    public static string Code(string response)
    {
        return response.Length >= 3 ? response.Substring(0, 3) : response;
    }

    // Every await that could block is bounded by a 2s guard so a hang fails the test instead of the run.
    public static async Task<string> Greet(SmtpProxy proxy, ListenerSocket conn)
    {
        var task = proxy.ProcessConnection(conn);

        var done = await Task.WhenAny(task, Task.Delay(2000));

        Assert.IsTrue(done == task, "ProcessConnection timed out (possible hang).");

        return Decode(await task);
    }

    public static async Task<string> Send(SmtpProxy proxy, ListenerSocket conn, string raw)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);

        var task = proxy.ProcessRequest(conn, bytes, bytes.Length);

        var done = await Task.WhenAny(task, Task.Delay(2000));

        Assert.IsTrue(done == task, $"ProcessRequest timed out for input: {raw}");

        return Decode(await task);
    }

    // Connect + advance past the greeting so IsKeepAlive is true (required for pipelining/multi-line paths).
    public static async Task<ListenerSocket> Connect(SmtpProxy proxy)
    {
        var conn = new ListenerSocket();

        await Greet(proxy, conn);

        return conn;
    }
}

[TestClass]
public class SmtpGreetingAndHelloTests
{
    [TestMethod]
    public async Task Greeting_Is220_WithSmtpDomain()
    {
        var proxy = SmtpAdv.NewProxy();

        var greeting = await SmtpAdv.Greet(proxy, new ListenerSocket());

        Assert.AreEqual("220", SmtpAdv.Code(greeting), greeting);
        StringAssert.Contains(greeting, "smtp.hive.com", greeting);
        StringAssert.EndsWith(greeting, "\r\n", greeting);
    }

    [TestMethod]
    public async Task Helo_WithArgument_Replies250()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "HELO example.com\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
        StringAssert.Contains(resp, "example.com", resp);
    }

    [TestMethod]
    public async Task Helo_WithoutArgument_StillReplies250()
    {
        // Adversarial: HELO with no domain. ReadRequest hands the switch an empty Message; the server
        // still answers 250 (it does not enforce the RFC 5321 mandatory domain argument).
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "HELO\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task Ehlo_WithArgument_Replies250MultilineAuthLogin()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "EHLO example.com\r\n");

        StringAssert.StartsWith(resp, "250-", resp);
        StringAssert.Contains(resp, "250 AUTH LOGIN\r\n", resp);
    }

    [TestMethod]
    public async Task Ehlo_WithoutArgument_StillReplies250Multiline()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "EHLO\r\n");

        StringAssert.StartsWith(resp, "250-", resp);
        StringAssert.Contains(resp, "250 AUTH LOGIN\r\n", resp);
    }

    [TestMethod]
    public async Task LowercaseHelo_ParsedCaseInsensitively_Replies250()
    {
        // Verb parsing is case-insensitive (Enum.TryParse ignoreCase=true), so lowercase verbs are accepted.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "helo example.com\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task LowercaseEhlo_ParsedCaseInsensitively_Replies250()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "ehlo box\r\n");

        StringAssert.StartsWith(resp, "250-", resp);
    }

    [TestMethod]
    public async Task MixedCaseVerb_Replies250()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "HeLo box\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task Noop_Replies250()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "NOOP\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task Quit_Replies221AndClearsKeepAlive()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "QUIT\r\n");

        Assert.AreEqual("221", SmtpAdv.Code(resp), resp);
        Assert.IsFalse(conn.IsKeepAlive, "QUIT must drop keep-alive.");
    }
}

[TestClass]
public class SmtpSequencingRejectionTests
{
    [TestMethod]
    public async Task MailFrom_BeforeAnything_Rejected530()
    {
        // MAIL is gated on the Authenticated flag before the address is even parsed, so this is DB-free.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "MAIL FROM:<fox@hive.com>\r\n");

        Assert.AreEqual("530", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task RcptTo_BeforeAnything_Rejected530()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "RCPT TO:<fox@hive.com>\r\n");

        Assert.AreEqual("530", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task MailFrom_AfterHeloButBeforeAuth_Rejected530()
    {
        // HELO does not authenticate. MAIL is still refused with 530.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        await SmtpAdv.Send(proxy, conn, "HELO example.com\r\n");

        var resp = await SmtpAdv.Send(proxy, conn, "MAIL FROM:<fox@hive.com>\r\n");

        Assert.AreEqual("530", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task RcptTo_AfterHeloButBeforeAuth_Rejected530()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        await SmtpAdv.Send(proxy, conn, "HELO example.com\r\n");

        var resp = await SmtpAdv.Send(proxy, conn, "RCPT TO:<fox@hive.com>\r\n");

        Assert.AreEqual("530", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task LowercaseMailFrom_BeforeAuth_StillRejected530()
    {
        // Even the lowercase verb is recognized and rejected pre-auth (no auth bypass via casing).
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "mail from:<fox@hive.com>\r\n");

        Assert.AreEqual("530", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task MailFrom_WithMalformedAddress_BeforeAuth_RejectedNotParsed()
    {
        // The auth gate fires BEFORE EmailAddress.ParseFromSmtp, so a malformed address unauthenticated
        // yields 530 (not a parser exception). Confirms the parser is unreachable pre-auth.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "MAIL FROM:garbage-no-brackets\r\n");

        Assert.AreEqual("530", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task Data_BeforeTransaction_Rejected503()
    {
        // DATA without MAIL FROM + RCPT TO is a bad command sequence; must not begin body input.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "DATA\r\n");

        Assert.AreEqual("503", SmtpAdv.Code(resp), resp);
        Assert.IsFalse(conn.DataBag.ContainsKey("requesting_data"), "DATA must not switch to body mode without a transaction.");
    }

    [TestMethod]
    public async Task Vrfy_Replies252()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "VRFY fox\r\n");

        Assert.AreEqual("252", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task Help_Replies502()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "HELP\r\n");

        Assert.AreEqual("502", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task Expn_KnownVerbNoHandler_Replies500()
    {
        // EXPN is a real SmtpCommands enum value with no switch case -> falls to default -> 500.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "EXPN list\r\n");

        Assert.AreEqual("500", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task Starttls_KnownVerbNoHandler_Replies500()
    {
        // STARTTLS is enum-known but unhandled -> default -> 500 (server advertises no TLS).
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "STARTTLS\r\n");

        Assert.AreEqual("500", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task Rset_OnFreshConnection_Replies250NoCrash()
    {
        // RSET with a near-empty bag must not throw despite its "preserve the first bag entry" logic.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "RSET\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
    }
}

[TestClass]
public class SmtpParsingEdgeTests
{
    [TestMethod]
    public async Task UnknownVerb_Replies500()
    {
        // An unrecognized but non-empty command now gets the RFC 5321 4.2.1 500 reply instead of silence,
        // so a spec-conformant client no longer blocks waiting for a response.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "FLARGLE now\r\n");

        Assert.AreEqual("500", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task UnknownVerb_DoesNotKillSession_NextCommandStillWorks()
    {
        // The silent drop at least does not corrupt session state: a following valid command answers.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        await SmtpAdv.Send(proxy, conn, "ZORP\r\n");

        var resp = await SmtpAdv.Send(proxy, conn, "NOOP\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task EmptyLine_ProducesNoReply()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "\r\n");

        Assert.AreEqual(string.Empty, resp, "Empty line unexpectedly produced a reply.");
    }

    [TestMethod]
    public async Task WhitespaceOnlyLine_ProducesNoReply()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "   \r\n");

        Assert.AreEqual(string.Empty, resp, "Whitespace-only line unexpectedly produced a reply.");
    }

    [TestMethod]
    public async Task PipelinedCommands_AllAnsweredInOrder()
    {
        // Three commands in one ProcessRequest call. The line buffer must answer every one, not just the first.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "NOOP\r\nNOOP\r\nQUIT\r\n");

        var noopCount = resp.Split(new[] { "250 " }, StringSplitOptions.None).Length - 1;

        Assert.AreEqual(2, noopCount, resp);
        StringAssert.Contains(resp, "221 ", resp);
    }

    [TestMethod]
    public async Task SplitCommandAcrossTwoCalls_ReassembledFromLineBuffer()
    {
        // "NO" then "OP\r\n": the first call buffers with no reply, the second completes the line.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var first = await SmtpAdv.Send(proxy, conn, "NO");

        Assert.AreEqual(string.Empty, first, "Partial command must not produce a reply yet.");

        var second = await SmtpAdv.Send(proxy, conn, "OP\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(second), second);
    }

    [TestMethod]
    public async Task BareLfWithoutCr_Accepted()
    {
        // The splitter keys on '\n' and trims a trailing '\r', so a bare-LF line is a valid command.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "NOOP\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task TwoBareLfCommandsInOneCall_BothAnswered()
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "NOOP\nNOOP\n");

        var count = resp.Split(new[] { "250 " }, StringSplitOptions.None).Length - 1;

        Assert.AreEqual(2, count, resp);
    }

    [TestMethod]
    public async Task OversizedCompleteLine_HandledNoCrash()
    {
        // A very long but properly terminated HELO line is processed whole (the 8KB cap only bounds an
        // UNTERMINATED remainder). ~20KB is well short of any allocation concern.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var giant = new string('A', 20000);

        var resp = await SmtpAdv.Send(proxy, conn, $"HELO {giant}\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
    }

    [TestMethod]
    public async Task OversizedUnterminatedLine_DroppedThenRecovers()
    {
        // An unterminated line longer than the 8KB line-buffer cap is discarded (no reply), and the
        // buffer resets cleanly so the next real command still works. No hang, no OOM.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var giant = new string('A', 20000);

        var dropped = await SmtpAdv.Send(proxy, conn, giant);

        Assert.AreEqual(string.Empty, dropped, "Oversized unterminated input should buffer/drop with no reply.");

        var recover = await SmtpAdv.Send(proxy, conn, "NOOP\r\n");

        Assert.AreEqual("250", SmtpAdv.Code(recover), recover);
    }
}

[TestClass]
public class SmtpAuthHandshakeTests
{
    [TestMethod]
    public async Task AuthLogin_ReturnsBase64UsernameChallenge_NoDb()
    {
        // AUTH sets the "requesting_username" flag and returns a 334 challenge. This is fully DB-free;
        // the DB user lookup only happens after BOTH username and password lines are supplied.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var resp = await SmtpAdv.Send(proxy, conn, "AUTH LOGIN\r\n");

        Assert.AreEqual("334", SmtpAdv.Code(resp), resp);
        Assert.IsTrue(conn.DataBag.ContainsKey("requesting_username"), "AUTH must arm the username-request state.");
        Assert.IsFalse(conn.DataBag.ContainsKey("auth"), "AUTH LOGIN alone must not authenticate.");
    }

    [TestMethod]
    public async Task AuthCancelToken_NonBase64_AbortedGracefully()
    {
        // RFC 4954: a bare "*" (and any non-base64 line) now aborts the AUTH exchange with a 500 reply
        // and clears the auth state, instead of an unhandled FormatException tearing down the session.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        var challenge = await SmtpAdv.Send(proxy, conn, "AUTH LOGIN\r\n");

        Assert.AreEqual("334", SmtpAdv.Code(challenge), challenge);

        var resp = await SmtpAdv.Send(proxy, conn, "*\r\n");

        Assert.AreEqual("500", SmtpAdv.Code(resp), resp);
        Assert.IsFalse(conn.DataBag.ContainsKey("requesting_username"), "AUTH abort must clear the username-request state.");
    }

    [TestMethod]
    public async Task AuthLogin_ThenValidBase64Username_ReturnsPasswordChallenge_NoDb()
    {
        // Supplying a valid-base64 username advances to the password challenge (334) WITHOUT any DB
        // access. The DB is only touched on the subsequent password line, which we never send.
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        await SmtpAdv.Send(proxy, conn, "AUTH LOGIN\r\n");

        var user = Convert.ToBase64String(Encoding.ASCII.GetBytes("nobody"));

        var resp = await SmtpAdv.Send(proxy, conn, user + "\r\n");

        Assert.AreEqual("334", SmtpAdv.Code(resp), resp);
        Assert.IsTrue(conn.DataBag.ContainsKey("requesting_password"), "Username step must arm the password-request state.");
        Assert.IsFalse(conn.DataBag.ContainsKey("auth"), "Username step must not authenticate.");
    }
}

[TestClass]
public class SmtpEmailAddressParserAdversarialTests
{
    // These hit the hardened parser directly. The authenticated MAIL/RCPT path calls ParseFromSmtp and
    // echoes/stores the result, but that path is auth-gated (DB), so the parser is probed in isolation.

    [TestMethod]
    public void ParseFromSmtp_NoAngleBrackets_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("MAIL FROM:user@example.com"));
    }

    [TestMethod]
    public void ParseFromSmtp_EmptyAngles_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("<>"));
    }

    [TestMethod]
    public void ParseFromSmtp_MultipleAtSigns_Rejected()
    {
        // The hardened domain group excludes '@', so a second '@' no longer parses; a plainly invalid
        // address is now rejected rather than leniently accepted.
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("<a@b@c.com>"));
    }

    [TestMethod]
    public void ParseFromSmtp_CrlfInsideAddress_Rejected()
    {
        // The hardened domain group excludes whitespace, so a CRLF embedded in the address breaks the
        // match and the response-splitting / stored-address-corruption vector is closed at the parser.
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("<user@evil\r\nINJECTED>"));
    }
}
