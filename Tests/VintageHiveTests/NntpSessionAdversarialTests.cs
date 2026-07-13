// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Text;
using VintageHive.Network;
using VintageHive.Proxy.Usenet;

namespace Adversarial5.Nntp;

// Deterministic, socket-free harness for the NNTP session grammar. ProcessConnection/ProcessRequest
// format response bytes via SendResponse (no NetworkStream is touched), so we can drive the command
// parser and out-of-sequence rejections without a live socket and without reaching Mind.Db / the
// article-and-group data source (any command that would retrieve articles/groups is deliberately not driven).
internal static class NntpHarness
{
    internal static async Task<(NntpProxy Proxy, ListenerSocket Conn, string Greeting)> Connect()
    {
        var proxy = new NntpProxy(IPAddress.Loopback, 0);
        var conn = new ListenerSocket();

        var task = proxy.ProcessConnection(conn);
        var done = await Task.WhenAny(task, Task.Delay(2000));

        Assert.AreSame(task, done, "ProcessConnection timed out (possible hang)");

        var bytes = await task;

        Assert.IsNotNull(bytes, "ProcessConnection must return greeting bytes");

        return (proxy, conn, Encoding.ASCII.GetString(bytes));
    }

    // Returns the raw response bytes (may be null when the server emits nothing for the read).
    internal static async Task<byte[]> SendRaw(NntpProxy proxy, ListenerSocket conn, string command)
    {
        var bytes = Encoding.ASCII.GetBytes(command);

        var task = proxy.ProcessRequest(conn, bytes, bytes.Length);
        var done = await Task.WhenAny(task, Task.Delay(2000));

        Assert.AreSame(task, done, "ProcessRequest timed out (possible hang)");

        return await task;
    }

    // Convenience wrapper for the common case where a response is expected; decodes to ASCII.
    internal static async Task<string> Send(NntpProxy proxy, ListenerSocket conn, string command)
    {
        var bytes = await SendRaw(proxy, conn, command);

        Assert.IsNotNull(bytes, $"Expected a response for '{command.Replace("\r\n", "\\r\\n")}' but got null");

        return Encoding.ASCII.GetString(bytes);
    }
}

[TestClass]
public class NntpGreetingAndModeTests
{
    [TestMethod]
    public async Task Greeting_IsServerReadyNoPosting()
    {
        var (_, _, greeting) = await NntpHarness.Connect();

        Assert.IsTrue(greeting.StartsWith("201 "), greeting);
        Assert.IsTrue(greeting.Contains("NNTP Service Ready"), greeting);
        Assert.IsTrue(greeting.Contains("posting not allowed"), greeting);
        Assert.IsTrue(greeting.EndsWith("\r\n"), "Greeting must terminate with CRLF");
    }

    [TestMethod]
    public async Task Greeting_InitializesKeepAlive()
    {
        var (_, conn, _) = await NntpHarness.Connect();

        Assert.IsTrue(conn.IsKeepAlive, "Connection should be keep-alive after greeting");
    }

    [TestMethod]
    public async Task ModeReader_ReturnsReadyNoPosting()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "MODE READER\r\n");

        Assert.IsTrue(resp.StartsWith("201 "), resp);
        Assert.IsTrue(resp.Contains("NNTP Service Ready"), resp);
    }

    [TestMethod]
    public async Task ModeReader_LowerCase_ReturnsReadyNoPosting()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "mode reader\r\n");

        Assert.IsTrue(resp.StartsWith("201 "), resp);
    }

    [TestMethod]
    public async Task ModeReader_MixedCaseArgument_ReturnsReadyNoPosting()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "MODE ReAdEr\r\n");

        Assert.IsTrue(resp.StartsWith("201 "), resp);
    }

    [TestMethod]
    public async Task ModeStream_UnknownVariant_Rejected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "MODE STREAM\r\n");

        Assert.IsTrue(resp.StartsWith("500 "), resp);
        Assert.IsTrue(resp.Contains("Unknown MODE variant"), resp);
    }

    [TestMethod]
    public async Task Mode_NoArgument_Rejected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "MODE\r\n");

        Assert.IsTrue(resp.StartsWith("500 "), resp);
    }
}

[TestClass]
public class NntpUnknownAndMalformedTests
{
    [TestMethod]
    public async Task UnknownCommand_ReturnsCommandNotRecognized()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "FROBNICATE now\r\n");

        Assert.IsTrue(resp.StartsWith("500 "), resp);
        Assert.IsTrue(resp.Contains("Command not recognized"), resp);
    }

    [TestMethod]
    public async Task UnknownCommand_DoesNotEchoInput_NoInjectionSurface()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // The bogus token must never be echoed back into the status line (would be a response-splitting vector).
        var resp = await NntpHarness.Send(proxy, conn, "ZZTOP payload\r\n");

        Assert.IsFalse(resp.Contains("ZZTOP"), resp);
        Assert.IsFalse(resp.Contains("payload"), resp);
    }

    [TestMethod]
    public async Task LeadingSpaceLine_ParsesToEmptyCommand_Rejected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // A leading space makes Split(" ", 2)[0] empty, so the whole thing is an unrecognized command.
        var resp = await NntpHarness.Send(proxy, conn, " MODE READER\r\n");

        Assert.IsTrue(resp.StartsWith("500 "), resp);
    }

    [TestMethod]
    public async Task EmptyLine_ProducesNoResponse()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.SendRaw(proxy, conn, "\r\n");

        Assert.IsNull(resp, "A bare CRLF is whitespace-only and must be skipped silently");
    }

    [TestMethod]
    public async Task WhitespaceOnlyLine_ProducesNoResponse()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.SendRaw(proxy, conn, "   \t  \r\n");

        Assert.IsNull(resp, "A whitespace-only line must be skipped silently");
    }

    [TestMethod]
    public async Task BareLf_NoCarriageReturn_IsAccepted()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // Line splitting is on '\n' with a trailing '\r' trim, so a bare LF terminates a command.
        var resp = await NntpHarness.Send(proxy, conn, "HELP\n");

        Assert.IsTrue(resp.StartsWith("100 "), resp);
    }

    [TestMethod]
    public async Task TrailingSpacesAroundCommand_AreTrimmed()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "MODE READER   \r\n");

        Assert.IsTrue(resp.StartsWith("201 "), resp);
    }
}

[TestClass]
public class NntpOutOfSequenceRejectionTests
{
    // Every command here is issued on a fresh connection with no group selected. These are the DB-free
    // rejection paths: the handler short-circuits with 412/501/etc BEFORE any article/group retrieval.

    [TestMethod]
    public async Task Group_NoArgument_SyntaxError()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "GROUP\r\n");

        Assert.IsTrue(resp.StartsWith("501 "), resp);
        Assert.IsTrue(resp.Contains("No group specified"), resp);
    }

    [TestMethod]
    public async Task Group_WhitespaceOnlyArgument_SyntaxError()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // "GROUP    " -> argument trims to empty, so it is rejected before GetGroupAsync (no DB touch).
        var resp = await NntpHarness.Send(proxy, conn, "GROUP     \r\n");

        Assert.IsTrue(resp.StartsWith("501 "), resp);
        Assert.IsTrue(resp.Contains("No group specified"), resp);
    }

    [TestMethod]
    public async Task ListGroup_NoArgumentNoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "LISTGROUP\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
        Assert.IsTrue(resp.Contains("No group selected"), resp);
    }

    [TestMethod]
    public async Task Article_NoArgumentNoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "ARTICLE\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
        Assert.IsTrue(resp.Contains("No group selected"), resp);
    }

    [TestMethod]
    public async Task Article_NumericNoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // A syntactically valid article number still requires a selected group; rejection is DB-free.
        var resp = await NntpHarness.Send(proxy, conn, "ARTICLE 42\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
    }

    [TestMethod]
    public async Task Article_NegativeNumberNoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // int.TryParse("-1") succeeds, so the no-group check fires (412) rather than the syntax path.
        var resp = await NntpHarness.Send(proxy, conn, "ARTICLE -1\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
    }

    [TestMethod]
    public async Task Article_NonNumericGarbage_SyntaxError()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // Not empty, not a <message-id>, not an int -> the invalid-argument syntax path (no retrieval).
        var resp = await NntpHarness.Send(proxy, conn, "ARTICLE notanumber\r\n");

        Assert.IsTrue(resp.StartsWith("501 "), resp);
        Assert.IsTrue(resp.Contains("Invalid argument"), resp);
    }

    [TestMethod]
    public async Task Article_HugeOverflowNumber_SyntaxError()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // Overflows Int32 so int.TryParse fails and it is treated as an invalid argument (no OOM, no crash).
        var resp = await NntpHarness.Send(proxy, conn, "ARTICLE 99999999999999999999\r\n");

        Assert.IsTrue(resp.StartsWith("501 "), resp);
        Assert.IsTrue(resp.Contains("Invalid argument"), resp);
    }

    [TestMethod]
    public async Task Head_NoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "HEAD\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
    }

    [TestMethod]
    public async Task Body_NoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "BODY\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
    }

    [TestMethod]
    public async Task Stat_NonNumericGarbage_SyntaxError()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "STAT bogus!!\r\n");

        Assert.IsTrue(resp.StartsWith("501 "), resp);
    }

    [TestMethod]
    public async Task Xover_NoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "XOVER\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
        Assert.IsTrue(resp.Contains("No group selected"), resp);
    }

    [TestMethod]
    public async Task Xover_MalformedRangeNoGroup_RejectedBeforeRangeParse()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // Even a nonsense range is rejected at the group gate before any range parsing / retrieval.
        foreach (var arg in new[] { "1-", "-5", "1-2-3", "abc", "9999999999-1" })
        {
            var resp = await NntpHarness.Send(proxy, conn, $"XOVER {arg}\r\n");

            Assert.IsTrue(resp.StartsWith("412 "), $"arg='{arg}' -> {resp}");
        }
    }

    [TestMethod]
    public async Task Over_Alias_NoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "OVER 1-100\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
    }

    [TestMethod]
    public async Task Next_NoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "NEXT\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
    }

    [TestMethod]
    public async Task Last_NoGroup_NoGroupSelected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "LAST\r\n");

        Assert.IsTrue(resp.StartsWith("412 "), resp);
    }
}

[TestClass]
public class NntpSimpleCommandTests
{
    [TestMethod]
    public async Task Post_AlwaysRejected()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "POST\r\n");

        Assert.IsTrue(resp.StartsWith("440 "), resp);
        Assert.IsTrue(resp.Contains("Posting not allowed"), resp);
    }

    [TestMethod]
    public async Task Help_ReturnsMultilineTextTerminatedByDot()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "HELP\r\n");

        Assert.IsTrue(resp.StartsWith("100 "), resp);
        Assert.IsTrue(resp.Contains("ARTICLE"), resp);
        Assert.IsTrue(resp.EndsWith(".\r\n"), "Multiline body must end with a lone dot line");
    }

    [TestMethod]
    public async Task Capabilities_ReturnsVersionList()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "CAPABILITIES\r\n");

        Assert.IsTrue(resp.StartsWith("100 "), resp);
        Assert.IsTrue(resp.Contains("VERSION 2"), resp);
        Assert.IsTrue(resp.Contains("READER"), resp);
        Assert.IsTrue(resp.EndsWith(".\r\n"), resp);
    }

    [TestMethod]
    public async Task NewGroups_ReturnsEmptyMultiline()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "NEWGROUPS 990101 000000 GMT\r\n");

        Assert.IsTrue(resp.StartsWith("231 "), resp);
        Assert.IsTrue(resp.EndsWith(".\r\n"), resp);
    }

    [TestMethod]
    public async Task NewNews_ReturnsEmptyMultiline()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "NEWNEWS * 990101 000000 GMT\r\n");

        Assert.IsTrue(resp.StartsWith("230 "), resp);
        Assert.IsTrue(resp.EndsWith(".\r\n"), resp);
    }

    [TestMethod]
    public async Task Quit_ReturnsGoodbyeAndClearsKeepAlive()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "QUIT\r\n");

        Assert.IsTrue(resp.StartsWith("205 "), resp);
        Assert.IsTrue(resp.Contains("Goodbye"), resp);
        Assert.IsFalse(conn.IsKeepAlive, "QUIT must clear keep-alive");
    }

    [TestMethod]
    public async Task Quit_LowerCase_ReturnsGoodbye()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "quit\r\n");

        Assert.IsTrue(resp.StartsWith("205 "), resp);
    }

    [TestMethod]
    public async Task AuthInfo_ReturnsAcceptedWithoutTouchingCredentials()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // Documents current behavior: AUTHINFO is a no-op that always accepts. There is no DB-gated
        // resource behind it (POST is refused for everyone), so this is not an exploitable bypass here.
        var resp = await NntpHarness.Send(proxy, conn, "AUTHINFO USER anybody\r\n");

        Assert.IsTrue(resp.StartsWith("281 "), resp);
        Assert.IsTrue(resp.Contains("Authentication accepted"), resp);
    }
}

[TestClass]
public class NntpPipeliningAndBufferingTests
{
    [TestMethod]
    public async Task Pipelined_TwoCommandsInOneRead_ProduceTwoResponses()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "MODE READER\r\nHELP\r\n");

        var first201 = resp.IndexOf("201 ", StringComparison.Ordinal);
        var then100 = resp.IndexOf("100 ", StringComparison.Ordinal);

        Assert.IsTrue(first201 >= 0, resp);
        Assert.IsTrue(then100 > first201, "HELP response must follow the MODE response in order");
    }

    [TestMethod]
    public async Task Pipelined_ThreeRejections_AllEmitted()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "ARTICLE\r\nNEXT\r\nLAST\r\n");

        // Three 412 status lines back to back.
        var count = resp.Split(new[] { "412 " }, StringSplitOptions.None).Length - 1;

        Assert.AreEqual(3, count, resp);
    }

    [TestMethod]
    public async Task Pipelined_QuitInMiddle_DropsRemainderAfterQuit()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // After QUIT clears keep-alive the loop returns early, so the trailing HELP is not processed.
        var resp = await NntpHarness.Send(proxy, conn, "HELP\r\nQUIT\r\nHELP\r\n");

        Assert.IsTrue(resp.Contains("205 "), resp);

        var helpCount = resp.Split(new[] { "100 " }, StringSplitOptions.None).Length - 1;

        Assert.AreEqual(1, helpCount, "Only the HELP before QUIT should be answered");
        Assert.IsFalse(conn.IsKeepAlive, "QUIT must clear keep-alive");
    }

    [TestMethod]
    public async Task SplitCommand_AcrossTwoReads_IsBufferedAndCompleted()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        // First read carries no CRLF: nothing should be emitted, the partial is buffered.
        var partial = await NntpHarness.SendRaw(proxy, conn, "MO");

        Assert.IsNull(partial, "A partial command with no CRLF must not emit a response");

        var resp = await NntpHarness.Send(proxy, conn, "DE READER\r\n");

        Assert.IsTrue(resp.StartsWith("201 "), resp);
    }

    [TestMethod]
    public async Task SplitCommand_LineSpanningManyTinyReads_Completes()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        foreach (var chunk in new[] { "HE", "L", "P" })
        {
            var r = await NntpHarness.SendRaw(proxy, conn, chunk);

            Assert.IsNull(r, $"chunk '{chunk}' should buffer, not respond");
        }

        var final = await NntpHarness.Send(proxy, conn, "\r\n");

        Assert.IsTrue(final.StartsWith("100 "), final);
    }

    [TestMethod]
    public async Task LowerCaseCommand_IsNormalizedAndHandled()
    {
        var (proxy, conn, _) = await NntpHarness.Connect();

        var resp = await NntpHarness.Send(proxy, conn, "capabilities\r\n");

        Assert.IsTrue(resp.StartsWith("100 "), resp);
        Assert.IsTrue(resp.Contains("VERSION 2"), resp);
    }
}

[TestClass]
public class NntpParseCommandEdgeTests
{
    [TestMethod]
    public void ParseCommand_EmptyString_ReturnsEmptyCommandAndArgument()
    {
        var (command, argument) = NntpProxy.ParseCommand("");

        Assert.AreEqual("", command);
        Assert.AreEqual("", argument);
    }

    [TestMethod]
    public void ParseCommand_WhitespaceOnly_ReturnsEmptyCommand()
    {
        var (command, argument) = NntpProxy.ParseCommand("    ");

        Assert.AreEqual("", command);
        Assert.AreEqual("", argument);
    }

    [TestMethod]
    public void ParseCommand_LeadingSpace_YieldsEmptyCommand()
    {
        // Split(" ", 2) on a leading-space line makes the command token empty and shoves everything into arg.
        var (command, argument) = NntpProxy.ParseCommand(" MODE READER");

        Assert.AreEqual("", command);
        Assert.AreEqual("MODE READER", argument);
    }

    [TestMethod]
    public void ParseCommand_UpcasesCommandButPreservesArgumentCase()
    {
        var (command, argument) = NntpProxy.ParseCommand("group Comp.Lang.C");

        Assert.AreEqual("GROUP", command);
        Assert.AreEqual("Comp.Lang.C", argument);
    }

    [TestMethod]
    public void ParseCommand_CollapsesLeadingArgumentSpaces_ButKeepsInnerSpaces()
    {
        var (command, argument) = NntpProxy.ParseCommand("XOVER    1-10   ");

        Assert.AreEqual("XOVER", command);
        Assert.AreEqual("1-10", argument);
    }

    [TestMethod]
    public void ParseCommand_OnlySplitsOnFirstSpace()
    {
        var (command, argument) = NntpProxy.ParseCommand("AUTHINFO USER some user name");

        Assert.AreEqual("AUTHINFO", command);
        Assert.AreEqual("USER some user name", argument);
    }

    [TestMethod]
    public void ParseCommand_TabIsNotASeparator_StaysInCommandToken()
    {
        // Commands are split on ' ' only; a tab-delimited command is not recognized as a bare verb.
        var (command, argument) = NntpProxy.ParseCommand("MODE\tREADER");

        Assert.AreEqual("MODE\tREADER", command);
        Assert.AreEqual("", argument);
    }

    [TestMethod]
    public void ParseCommand_ByteSpanOverload_MatchesStringOverload()
    {
        var data = Encoding.ASCII.GetBytes("ARTICLE 42 trailing\r\n");

        // The span overload runs ToASCII on data[..read]; \r\n survive into the argument and are trimmed.
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("ARTICLE", command);
        Assert.AreEqual("42 trailing", argument);
    }

    [TestMethod]
    public void ParseCommand_ByteSpanOverload_RespectsReadLength()
    {
        var data = Encoding.ASCII.GetBytes("QUITEXTRA");

        // Only the first 4 bytes are meaningful; read length must bound the parse.
        var (command, argument) = NntpProxy.ParseCommand(data, 4);

        Assert.AreEqual("QUIT", command);
        Assert.AreEqual("", argument);
    }
}