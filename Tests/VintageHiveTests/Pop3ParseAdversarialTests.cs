// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Reflection;
using VintageHive.Proxy.Pop3;

namespace Adversarial3.Pop3;

// Adversarial coverage for Pop3Proxy.ParseCommandLine(string line):
//   private static (string Command, string Message) ParseCommandLine(string line)
//   {
//       var rawData = line.Split(" ", 2);
//       var cmd = rawData[0].Trim().ToUpperInvariant();
//       var msg = rawData.Length == 2 ? rawData[1].Trim() : string.Empty;
//       return (cmd, msg);
//   }
//
// The method is PRIVATE static, so every test drives it through reflection. It is pure string
// logic: no sockets, no ListenerSocket, no Mind.Db. We assert the EXACT (Command, Message) tuple
// that the method actually returns for each hostile input. Control and non-ASCII characters are
// built with (char) casts so the source stays pure ASCII and encoding-independent.
[TestClass]
public class Pop3ParseCommandLineAdversarialTests
{
    private static MethodInfo GetParseMethod()
    {
        var method = typeof(Pop3Proxy).GetMethod("ParseCommandLine", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method, "ParseCommandLine(string) was not found via reflection; signature may have changed.");

        return method;
    }

    private static (string Command, string Message) Parse(string line)
    {
        var result = GetParseMethod().Invoke(null, new object[] { line });

        Assert.IsNotNull(result, "ParseCommandLine returned null instead of a ValueTuple.");

        return ((string Command, string Message))result;
    }

    private static string Ch(int codepoint)
    {
        return ((char)codepoint).ToString();
    }

    #region Empty / whitespace-only inputs

    [TestMethod]
    public void EmptyString_ReturnsEmptyCommandAndMessage()
    {
        var (command, message) = Parse("");

        Assert.AreEqual("", command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void SingleSpace_SplitsIntoTwoEmptyParts()
    {
        // " ".Split(" ", 2) => ["", ""]; both parts trim to empty.
        var (command, message) = Parse(" ");

        Assert.AreEqual("", command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void OnlySpaces_ReturnsEmptyCommandAndMessage()
    {
        // Three spaces: split on the first space, remainder "  " trims to "".
        var (command, message) = Parse("   ");

        Assert.AreEqual("", command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void SingleTabOnly_TrimmedToEmptyCommand()
    {
        // No space present, so single element; Trim() removes the tab entirely.
        var (command, message) = Parse("\t");

        Assert.AreEqual("", command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void CrLfOnly_TrimmedToEmptyCommand()
    {
        // No space; "\r\n".Trim() => "" for the command, no message.
        var (command, message) = Parse("\r\n");

        Assert.AreEqual("", command);
        Assert.AreEqual("", message);
    }

    #endregion

    #region Command-only (no message)

    [TestMethod]
    public void CommandOnly_NoMessage_ReturnsEmptyMessage()
    {
        var (command, message) = Parse("STAT");

        Assert.AreEqual("STAT", command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void CommandOnly_QuitUppercased_Unchanged()
    {
        var (command, message) = Parse("QUIT");

        Assert.AreEqual("QUIT", command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void CommandWithTrailingSpaceOnly_MessageEmpty()
    {
        // "NOOP   ".Split(" ", 2) => ["NOOP", "  "]; remainder trims to "".
        var (command, message) = Parse("NOOP   ");

        Assert.AreEqual("NOOP", command);
        Assert.AreEqual("", message);
    }

    #endregion

    #region Case handling of the command

    [TestMethod]
    public void LowercaseCommand_UppercasedInvariant()
    {
        var (command, message) = Parse("user alice");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("alice", message);
    }

    [TestMethod]
    public void MixedCaseCommand_UppercasedButMessageCasePreserved()
    {
        // Command is uppercased; message is NOT touched by ToUpperInvariant.
        var (command, message) = Parse("UsEr PaSsWoRd");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("PaSsWoRd", message);
    }

    [TestMethod]
    public void MessageCaseNeverUppercased()
    {
        var (command, message) = Parse("PASS SeCrEt");

        Assert.AreEqual("PASS", command);
        Assert.AreEqual("SeCrEt", message);
    }

    #endregion

    #region Message preservation (spaces and payload)

    [TestMethod]
    public void SimpleTwoTokenLine_SplitsOnFirstSpace()
    {
        var (command, message) = Parse("USER bob");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("bob", message);
    }

    [TestMethod]
    public void MessageWithSpaces_PreservedAsSingleMessage()
    {
        // Only the FIRST space is a delimiter; the rest of the line stays as one Message.
        var (command, message) = Parse("USER foo bar baz");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("foo bar baz", message);
    }

    [TestMethod]
    public void MessageWithInternalDoubleSpace_InternalSpacesPreserved()
    {
        // "USER a  b" => ["USER", "a  b"]; internal spacing untouched by Trim().
        var (command, message) = Parse("USER a  b");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("a  b", message);
    }

    [TestMethod]
    public void TwoSpacesBetweenCommandAndMessage_LeadingSpaceOfMessageTrimmed()
    {
        // "USER  foo" => ["USER", " foo"]; the leading space of the remainder is trimmed away.
        var (command, message) = Parse("USER  foo");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("foo", message);
    }

    [TestMethod]
    public void TopStyleTwoNumericArgs_KeptAsOneMessage()
    {
        // ParseCommandLine only splits once; "1 10" stays whole (the TOP handler re-splits later).
        var (command, message) = Parse("TOP 1 10");

        Assert.AreEqual("TOP", command);
        Assert.AreEqual("1 10", message);
    }

    [TestMethod]
    public void NumericMessage_Preserved()
    {
        var (command, message) = Parse("RETR 1");

        Assert.AreEqual("RETR", command);
        Assert.AreEqual("1", message);
    }

    [TestMethod]
    public void TrailingWhitespaceInMessage_TrimmedAway()
    {
        // Trailing spaces in the argument are silently dropped by Trim().
        var (command, message) = Parse("PASS secret   ");

        Assert.AreEqual("PASS", command);
        Assert.AreEqual("secret", message);
    }

    #endregion

    #region Leading whitespace: space vs tab asymmetry

    [TestMethod]
    public void LeadingSpace_MakesCommandEmptyAndPushesRestIntoMessage()
    {
        // A leading space means rawData[0] == "" so the real keyword lands in the Message.
        var (command, message) = Parse(" USER foo");

        Assert.AreEqual("", command);
        Assert.AreEqual("USER foo", message);
    }

    [TestMethod]
    public void LeadingTab_TrimmedFromCommand_KeywordSurvives()
    {
        // A leading tab is NOT a split delimiter, so Trim() strips it and the keyword survives.
        var (command, message) = Parse("\tUSER foo");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("foo", message);
    }

    #endregion

    #region Tabs inside the token

    [TestMethod]
    public void TabSeparatedCommandAndArg_NotSplit_TreatedAsOneCommand()
    {
        // Split is on " " only; a tab does not separate. The whole thing becomes the (uppercased) command.
        var (command, message) = Parse("USER\tfoo");

        Assert.AreEqual("USER\tFOO", command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void TabDelimitedTopArgs_NotSplit_DigitsUnchangedByUppercase()
    {
        var (command, message) = Parse("TOP\t1\t10");

        Assert.AreEqual("TOP\t1\t10", command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void VerticalTabAndFormFeedAroundMessage_TrimmedAsWhitespace()
    {
        // \v and \f are whitespace to Trim(), so they are stripped from the message edges.
        var (command, message) = Parse("USER \vfoo\f");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("foo", message);
    }

    #endregion

    #region Embedded CRLF and control characters

    [TestMethod]
    public void TrailingCrLfInMessage_Trimmed()
    {
        var (command, message) = Parse("USER foo\r\n");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("foo", message);
    }

    [TestMethod]
    public void EmbeddedCrLfInMiddleOfMessage_Preserved()
    {
        // In the live path each line is pre-split on '\n', but ParseCommandLine in isolation keeps
        // an embedded CRLF because it sits between non-whitespace characters (not trimmed).
        var (command, message) = Parse("USER foo\r\nDELE 1");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("foo\r\nDELE 1", message);
    }

    [TestMethod]
    public void TrailingCrLfAfterPassword_Trimmed()
    {
        var (command, message) = Parse("PASS secret\r\n");

        Assert.AreEqual("PASS", command);
        Assert.AreEqual("secret", message);
    }

    [TestMethod]
    public void NullCharInMessage_NotWhitespace_Preserved()
    {
        // U+0000 is not whitespace, so Trim() leaves it in place.
        var nul = Ch(0x00);

        var (command, message) = Parse("USER a" + nul + "b");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("a" + nul + "b", message);
    }

    [TestMethod]
    public void ControlCharInMessage_Soh_Preserved()
    {
        // U+0001 (SOH) is a control char but not whitespace; it survives untrimmed inside the message.
        var soh = Ch(0x01);

        var (command, message) = Parse("USER a" + soh + "b");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("a" + soh + "b", message);
    }

    [TestMethod]
    public void ControlCharAsLoneToken_UppercaseNoOpAndPreserved()
    {
        // A lone SOH is not whitespace, so Trim() leaves it and it becomes the command verbatim.
        var soh = Ch(0x01);

        var (command, message) = Parse(soh);

        Assert.AreEqual(soh, command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void BellControlCharInMessage_Preserved()
    {
        // U+0007 (BEL) is a control char, not whitespace; preserved inside the message.
        var bel = Ch(0x07);

        var (command, message) = Parse("USER a" + bel + "b");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("a" + bel + "b", message);
    }

    #endregion

    #region Unicode

    [TestMethod]
    public void UnicodeMessage_Preserved()
    {
        // Snowman (U+2603) + accented e (U+00E9) as the argument: preserved verbatim, not uppercased.
        var payload = Ch(0x2603) + "caf" + Ch(0x00E9);

        var (command, message) = Parse("USER " + payload);

        Assert.AreEqual("USER", command);
        Assert.AreEqual(payload, message);
    }

    [TestMethod]
    public void UnicodeCommand_UppercasedInvariant()
    {
        // "cafe" + accented e (U+00E9) uppercases to "CAF" + accented E (U+00C9) under invariant culture.
        var input = "caf" + Ch(0x00E9) + " x";

        var (command, message) = Parse(input);

        Assert.AreEqual("CAF" + Ch(0x00C9), command);
        Assert.AreEqual("x", message);
    }

    [TestMethod]
    public void NoBreakSpaceAroundMessage_TrimmedAsWhitespace()
    {
        // U+00A0 (no-break space) is whitespace to char.IsWhiteSpace, so Trim() strips it at the edges.
        var nbsp = Ch(0x00A0);

        var (command, message) = Parse("USER " + nbsp + "bob" + nbsp);

        Assert.AreEqual("USER", command);
        Assert.AreEqual("bob", message);
    }

    #endregion

    #region Length boundaries and duplicate delimiters

    [TestMethod]
    public void VeryLongArgument_PreservedInFull()
    {
        var payload = new string('a', 20000);

        var (command, message) = Parse("USER " + payload);

        Assert.AreEqual("USER", command);
        Assert.AreEqual(payload, message);
        Assert.AreEqual(20000, message.Length);
    }

    [TestMethod]
    public void VeryLongSingleTokenNoSpace_BecomesCommand()
    {
        // No space anywhere: the whole (uppercased) blob is the command, message empty.
        var payload = new string('a', 5000);

        var (command, message) = Parse(payload);

        Assert.AreEqual(new string('A', 5000), command);
        Assert.AreEqual("", message);
    }

    [TestMethod]
    public void ManyRepeatedSpaces_OnlyFirstIsDelimiter()
    {
        // "USER" then ten spaces then "x": remainder is nine spaces + x, which trims to "x".
        var (command, message) = Parse("USER          x");

        Assert.AreEqual("USER", command);
        Assert.AreEqual("x", message);
    }

    #endregion

    #region Null input (reflection wraps the thrown exception)

    [TestMethod]
    public void NullLine_ThrowsNullReferenceViaTargetInvocation()
    {
        var method = GetParseMethod();

        // line.Split(...) dereferences null; reflection wraps the NRE in TargetInvocationException.
        var ex = Assert.ThrowsExactly<TargetInvocationException>(() => method.Invoke(null, new object[] { null! }));

        Assert.IsInstanceOfType(ex.InnerException, typeof(NullReferenceException));
    }

    #endregion
}
