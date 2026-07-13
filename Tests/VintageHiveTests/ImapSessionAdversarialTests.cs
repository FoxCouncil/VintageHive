// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Adversarial grammar tests for the IMAP tagged-command parser (Proxy/Imap/ImapProxy.cs +
// ImapSession.cs). These drive the real ProcessConnection/ProcessRequest surface WITHOUT a socket:
// SendResponse just formats bytes, so no NetworkStream is required for command-grammar paths.
//
// Hard boundary: every exercised path stays DB-free. The DB (Mind.Db / Mind.PostOfficeDb) is only
// reached AFTER argument validation on LOGIN (needs both a username and a password) and only from the
// authenticated/selected states. All tests here either (a) run in the default NotAuthenticated state,
// where SELECT/FETCH/etc are rejected before any DB call, or (b) hit LOGIN's missing-argument
// rejection, which returns BAD before Mind.Db.UserFetch. A two-argument LOGIN would reach the DB, so
// it is deliberately never sent.

using System.Net;
using System.Text;
using VintageHive.Network;
using VintageHive.Proxy.Imap;

namespace Adversarial5.Imap;

[TestClass]
public class ImapSessionAdversarialTests
{
    private const int TimeoutMs = 2000;

    // Fresh proxy + connection, greeting consumed so the per-connection DataBag (ImapSession) is live.
    private static async Task<(ImapProxy proxy, ListenerSocket conn, string greeting)> NewSessionAsync()
    {
        var proxy = new ImapProxy(IPAddress.Loopback, 0);
        var conn = new ListenerSocket();

        var greetingBytes = await GuardAsync(proxy.ProcessConnection(conn));

        Assert.IsNotNull(greetingBytes, "ProcessConnection returned no greeting bytes");

        return (proxy, conn, Encoding.ASCII.GetString(greetingBytes));
    }

    // Every await that could conceivably block is raced against a 2s delay as a hang tripwire.
    private static async Task<byte[]> GuardAsync(Task<byte[]> task)
    {
        var delay = Task.Delay(TimeoutMs);
        var completed = await Task.WhenAny(task, delay);

        if (completed != task)
        {
            Assert.Fail("IMAP handler did not complete within timeout (possible hang)");
        }

        return await task;
    }

    // Send a raw command line and return the decoded response (or null when the handler emits nothing).
    private static async Task<string?> SendAsync(ImapProxy proxy, ListenerSocket conn, string raw)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);

        var resp = await GuardAsync(proxy.ProcessRequest(conn, bytes, bytes.Length));

        return resp == null ? null : Encoding.ASCII.GetString(resp);
    }

    #region Greeting

    [TestMethod]
    public async Task Greeting_IsUntaggedOk_AndAdvertisesImap4Rev1()
    {
        var (_, _, greeting) = await NewSessionAsync();

        Assert.IsTrue(greeting.StartsWith("* OK"), greeting);
        Assert.IsTrue(greeting.Contains("IMAP4rev1 server ready"), greeting);
        Assert.IsTrue(greeting.EndsWith("\r\n"), "greeting must be CRLF terminated");
    }

    #endregion

    #region CAPABILITY / NOOP (any-state, DB-free)

    [TestMethod]
    public async Task Capability_ReturnsUntaggedListAndTaggedOk()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "A1 CAPABILITY\r\n");

        Assert.IsNotNull(text);
        Assert.IsTrue(text!.Contains("* CAPABILITY IMAP4rev1 AUTH=LOGIN"), text);
        Assert.IsTrue(text.Contains("A1 OK CAPABILITY completed"), text);
    }

    [TestMethod]
    public async Task Noop_BeforeAuth_ReturnsTaggedOk_NoDb()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // NotAuthenticated state means HandleNoop never touches Mind.PostOfficeDb (that branch is
        // Selected-only), so this is a safe DB-free path.
        var text = await SendAsync(proxy, conn, "A2 NOOP\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A2 OK NOOP completed\r\n", text);
    }

    #endregion

    #region Missing tag / missing command (unparseable single token)

    [TestMethod]
    public async Task MissingTag_SingleTokenCommand_UntaggedBad()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // "CAPABILITY" with no tag is a single token: the two-token regex cannot match, so the server
        // replies with an untagged BAD (it has no tag to echo).
        var text = await SendAsync(proxy, conn, "CAPABILITY\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("* BAD Invalid command format\r\n", text);
    }

    [TestMethod]
    public async Task LeadingWhitespace_NoTag_UntaggedBad()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // Leading space defeats the ^(\S+) anchor: no tag can be extracted.
        var text = await SendAsync(proxy, conn, " CAPABILITY\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("* BAD Invalid command format\r\n", text);
    }

    [TestMethod]
    public async Task TagButNoCommand_UntaggedBad()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // A tag with no following command word ("A1 " with a trailing space) also fails the two-token
        // grammar. NOTE the response is untagged BAD even though the tag "A1" is textually present; a
        // strict client waiting on tag A1 gets no tagged completion. Documented, not asserted as a bug.
        var text = await SendAsync(proxy, conn, "A1 \r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("* BAD Invalid command format\r\n", text);
    }

    #endregion

    #region Unknown command

    [TestMethod]
    public async Task UnknownCommand_TaggedBad_EchoesTag()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "A3 FLIBBERTIGIBBET\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A3 BAD Unknown command\r\n", text);
    }

    #endregion

    #region LOGIN argument-parsing rejections (all BEFORE the DB call)

    [TestMethod]
    public async Task Login_NoArgs_TaggedBad_MissingCreds_NoDb()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // args is empty -> username empty -> BAD before Mind.Db.UserFetch is ever called.
        var text = await SendAsync(proxy, conn, "A4 LOGIN\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A4 BAD Missing username or password\r\n", text);
    }

    [TestMethod]
    public async Task Login_UsernameOnly_TaggedBad_MissingCreds_NoDb()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // A single argument leaves the password empty, so the handler rejects with BAD before any DB
        // lookup. A two-argument LOGIN would reach Mind.Db, so it is intentionally never sent.
        var text = await SendAsync(proxy, conn, "A5 LOGIN onlyuser\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A5 BAD Missing username or password\r\n", text);
    }

    [TestMethod]
    public async Task Login_UnterminatedQuote_TaggedBad_MissingCreds_NoDb()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // Malformed quoting: ParseTwoArgs returns the whole fragment as username and empty password,
        // so the missing-password guard fires before the DB. No crash on the dangling quote.
        var text = await SendAsync(proxy, conn, "A6 LOGIN \"user\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A6 BAD Missing username or password\r\n", text);
    }

    [TestMethod]
    public async Task Login_EmptyQuotedArgs_TaggedBad_MissingCreds_NoDb()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // "" "" parses to empty username/password -> BAD, never touching the DB.
        var text = await SendAsync(proxy, conn, "A7 LOGIN \"\" \"\"\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A7 BAD Missing username or password\r\n", text);
    }

    #endregion

    #region Malformed / oversized literal lengths are inert (no synchronizing-literal handling)

    [TestMethod]
    public async Task Literal_NonNumericLength_NotProcessed_UnknownCommandBad()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // The server implements no synchronizing literals, so "{abc}" is just an argument token. FOO is
        // an unknown command, so we get a tagged BAD and, crucially, no continuation and no hang.
        var text = await SendAsync(proxy, conn, "A8 FOO {abc}\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A8 BAD Unknown command\r\n", text);
    }

    [TestMethod]
    public async Task Literal_HugeLength_NotAllocated_LoginRejectedBeforeDb()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // A {999999} literal length must NOT trigger a buffer allocation. Here it lands as the LOGIN
        // username with no password, so the handler rejects with BAD before any DB call and without
        // allocating anything sized by the literal. Guarded against hang by the 2s tripwire.
        var text = await SendAsync(proxy, conn, "A9 LOGIN {999999}\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A9 BAD Missing username or password\r\n", text);
    }

    #endregion

    #region Out-of-sequence commands rejected before login (no DB reached)

    [TestMethod]
    public async Task Select_BeforeAuth_TaggedNo_NotAuthenticated()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // HandleSelect's state guard fires first, returning NO before GetMailboxByName (the DB call).
        var text = await SendAsync(proxy, conn, "A10 SELECT INBOX\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A10 NO Not authenticated\r\n", text);
    }

    [TestMethod]
    public async Task Examine_BeforeAuth_TaggedNo_NotAuthenticated()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "A11 EXAMINE INBOX\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A11 NO Not authenticated\r\n", text);
    }

    [TestMethod]
    public async Task Fetch_BeforeSelect_TaggedNo_NoMailboxSelected()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // FETCH requires Selected state; the guard returns NO before RefreshMessages (the DB call).
        var text = await SendAsync(proxy, conn, "A12 FETCH 1 BODY[]\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A12 NO No mailbox selected\r\n", text);
    }

    [TestMethod]
    public async Task Fetch_WithOversizedLiteralInBody_BeforeSelect_TaggedNo()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // Even with a huge literal in the FETCH body item, the state guard rejects first: the literal
        // is never parsed or allocated.
        var text = await SendAsync(proxy, conn, "A13 FETCH 1 BODY[]{999999}\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A13 NO No mailbox selected\r\n", text);
    }

    [TestMethod]
    public async Task Uid_BeforeSelect_TaggedNo_NoMailboxSelected()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "A14 UID FETCH 1 FLAGS\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A14 NO No mailbox selected\r\n", text);
    }

    [TestMethod]
    public async Task Store_BeforeSelect_TaggedNo_NoMailboxSelected()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "A15 STORE 1 +FLAGS (\\Seen)\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("A15 NO No mailbox selected\r\n", text);
    }

    #endregion

    #region Empty / whitespace lines produce no response

    [TestMethod]
    public async Task BareCrLf_ProducesNoResponse()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // Blank lines are skipped (IsNullOrWhiteSpace), so ProcessRequest returns null.
        var text = await SendAsync(proxy, conn, "\r\n");

        Assert.IsNull(text);
    }

    [TestMethod]
    public async Task WhitespaceOnlyLine_ProducesNoResponse()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "     \r\n");

        Assert.IsNull(text);
    }

    [TestMethod]
    public async Task BlankLineThenValidCommand_StillAnswersValidCommand()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // A blank line must not corrupt the line buffer for the command that follows it.
        Assert.IsNull(await SendAsync(proxy, conn, "\r\n"));

        var text = await SendAsync(proxy, conn, "B1 CAPABILITY\r\n");

        Assert.IsNotNull(text);
        Assert.IsTrue(text!.Contains("B1 OK CAPABILITY completed"), text);
    }

    #endregion

    #region Pipelining and TCP reassembly

    [TestMethod]
    public async Task Pipelined_TwoCommandsInOneBuffer_BothAnswered()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "P1 CAPABILITY\r\nP2 NOOP\r\n");

        Assert.IsNotNull(text);
        Assert.IsTrue(text!.Contains("P1 OK CAPABILITY completed"), text);
        Assert.IsTrue(text.Contains("P2 OK NOOP completed"), text);

        // Ordering: the CAPABILITY completion must precede the NOOP completion.
        Assert.IsTrue(text.IndexOf("P1 OK", StringComparison.Ordinal) < text.IndexOf("P2 OK NOOP", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task Pipelined_ThreeMixedCommands_AllTagsEchoedInOrder()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "Q1 NOOP\r\nQ2 BOGUS\r\nQ3 CAPABILITY\r\n");

        Assert.IsNotNull(text);
        Assert.IsTrue(text!.Contains("Q1 OK NOOP completed"), text);
        Assert.IsTrue(text.Contains("Q2 BAD Unknown command"), text);
        Assert.IsTrue(text.Contains("Q3 OK CAPABILITY completed"), text);
    }

    [TestMethod]
    public async Task SplitAcrossReads_CommandReassembledAndAnswered()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // First read has no newline -> buffered, no response yet.
        var first = await SendAsync(proxy, conn, "S1 CAPAB");

        Assert.IsNull(first);

        // Second read completes the line -> the reassembled command is answered.
        var second = await SendAsync(proxy, conn, "ILITY\r\n");

        Assert.IsNotNull(second);
        Assert.IsTrue(second!.Contains("S1 OK CAPABILITY completed"), second);
    }

    [TestMethod]
    public async Task LoneLf_WithoutCr_IsAcceptedAsLineTerminator()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // The parser splits on '\n' and only trims a trailing '\r', so a bare LF still terminates.
        var text = await SendAsync(proxy, conn, "L1 CAPABILITY\n");

        Assert.IsNotNull(text);
        Assert.IsTrue(text!.Contains("L1 OK CAPABILITY completed"), text);
    }

    #endregion

    #region Case sensitivity and tag echo fidelity

    [TestMethod]
    public async Task LowercaseCommand_IsRecognized_TagCasePreserved()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // Command matching is case-insensitive (ToUpperInvariant); the tag is echoed verbatim.
        var text = await SendAsync(proxy, conn, "cap1 capability\r\n");

        Assert.IsNotNull(text);
        Assert.IsTrue(text!.Contains("cap1 OK CAPABILITY completed"), text);
    }

    [TestMethod]
    public async Task MixedCaseCommand_IsRecognized()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "m1 NoOp\r\n");

        Assert.IsNotNull(text);
        Assert.AreEqual("m1 OK NOOP completed\r\n", text);
    }

    [TestMethod]
    public async Task ExtremelyLongTag_IsEchoedWithoutCrash()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        // A ~5000 char tag stays well under the 16 KB line cap (with its CRLF the line is one complete
        // unit), so it is parsed and echoed in full without truncation or crash.
        var longTag = new string('T', 5000);

        var text = await SendAsync(proxy, conn, $"{longTag} CAPABILITY\r\n");

        Assert.IsNotNull(text);
        Assert.IsTrue(text!.Contains($"{longTag} OK CAPABILITY completed"), "long tag not echoed intact");
    }

    #endregion

    #region LOGOUT (any-state, DB-free) and post-logout state

    [TestMethod]
    public async Task Logout_ReturnsByeAndTaggedOk_ClosesKeepAlive()
    {
        var (proxy, conn, _) = await NewSessionAsync();

        var text = await SendAsync(proxy, conn, "Z1 LOGOUT\r\n");

        Assert.IsNotNull(text);
        Assert.IsTrue(text!.Contains("* BYE"), text);
        Assert.IsTrue(text.Contains("Z1 OK LOGOUT completed"), text);
        Assert.IsFalse(conn.IsKeepAlive, "LOGOUT must clear keep-alive");
    }

    #endregion
}