// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Usenet;

namespace Adversarial3.Nntp;

// Adversarial coverage for NntpProxy tokenizing/formatting logic.
//
// Targets (all internal static, reachable directly via InternalsVisibleTo):
//   - (string, string) ParseCommand(string line)
//   - (string, string) ParseCommand(ReadOnlySpan<byte> data, int read)
//   - string DotStuff(string body)
//
// The article-number range parser ("1-100", "1-", "-5", "1-2-3") lives INLINE inside the async
// handler HandleXover (it calls _dataSource.GetGroupAsync / GetArticlesAsync against the DB), so per
// the harness rules it is NOT invoked here. Its behavior is only reasoned about, not exercised.
[TestClass]
public class NntpParseCommandStringTests
{
    [TestMethod]
    public void Empty_ReturnsEmptyCommandAndEmptyArgument()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("");

        Assert.AreEqual("", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void Null_ThrowsNullReferenceException()
    {
        // line.Split on a null string dereferences null. Real callers never pass null (the span
        // overload feeds ToASCII which always returns a string), so this is documented, not a defect.
        Assert.ThrowsExactly<NullReferenceException>(() => NntpProxy.ParseCommand(null!));
    }

    [TestMethod]
    public void SingleSpace_ReturnsEmptyCommandAndEmptyArgument()
    {
        var (cmd, arg) = NntpProxy.ParseCommand(" ");

        Assert.AreEqual("", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void WhitespaceOnly_ReturnsEmptyCommandAndEmptyArgument()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("   ");

        Assert.AreEqual("", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void CommandOnly_UppercasesCommandAndEmptyArgument()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE");

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void LowerCaseCommand_IsUppercased()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("article");

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void MixedCaseCommand_IsUppercased()
    {
        var (cmd, _) = NntpProxy.ParseCommand("ArTiClE 42");

        Assert.AreEqual("ARTICLE", cmd);
    }

    [TestMethod]
    public void CommandAndArgument_SplitOnFirstSpace()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("GROUP comp.lang.c");

        Assert.AreEqual("GROUP", cmd);
        Assert.AreEqual("comp.lang.c", arg);
    }

    [TestMethod]
    public void Argument_IsNotUppercased()
    {
        // Only the command is upper-cased; the argument keeps its original casing (message-ids,
        // group names, etc. are case-sensitive downstream).
        var (cmd, arg) = NntpProxy.ParseCommand("MODE reader");

        Assert.AreEqual("MODE", cmd);
        Assert.AreEqual("reader", arg);
    }

    [TestMethod]
    public void MultipleSpacesBetweenCommandAndArg_ArgumentIsTrimmed()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("GROUP  comp.lang.c");

        Assert.AreEqual("GROUP", cmd);
        Assert.AreEqual("comp.lang.c", arg);
    }

    [TestMethod]
    public void ManySpacesBetweenCommandAndArg_ArgumentIsTrimmed()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("GROUP          alt.test");

        Assert.AreEqual("GROUP", cmd);
        Assert.AreEqual("alt.test", arg);
    }

    [TestMethod]
    public void LeadingSpaces_CommandBecomesEmptyAndRestBecomesArgument()
    {
        // Split(" ", 2) with a leading space yields ["", " ARTICLE 5"]; the command is lost and the
        // rest becomes the argument. The switch then falls through to "command not recognized", so it
        // degrades gracefully rather than crashing. Documented quirk, not flagged as a defect.
        var (cmd, arg) = NntpProxy.ParseCommand("  ARTICLE 5");

        Assert.AreEqual("", cmd);
        Assert.AreEqual("ARTICLE 5", arg);
    }

    [TestMethod]
    public void TrailingSpace_ArgumentIsEmpty()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE ");

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void TrailingSpaces_ArgumentIsEmpty()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE     ");

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void TrailingCrLf_IsTrimmedFromArgument()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE 1\r\n");

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual("1", arg);
    }

    [TestMethod]
    public void TrailingCrLf_OnCommandOnly_IsTrimmed()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("QUIT\r\n");

        Assert.AreEqual("QUIT", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void TabIsNotADelimiter_StaysInsideCommandToken()
    {
        // Only the ASCII space is a delimiter. A tab-separated command is treated as one token, so
        // the tab (and everything after it) becomes part of the command word and will not match.
        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE\t123");

        Assert.AreEqual("ARTICLE\t123", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void EmbeddedCrLfWithoutSpace_StaysInsideCommandToken()
    {
        // A CRLF in the middle of a single token is not stripped by Trim (it only trims the ends), so
        // it survives inside the command word. Real callers split on '\n' upstream, so ParseCommand
        // never actually sees an embedded newline from the wire.
        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE\r\n5");

        Assert.AreEqual("ARTICLE\r\n5", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void EmbeddedControlCharInArgument_IsPreserved()
    {
        // A control char (U+0001) is not whitespace, so Trim does not remove it and it survives in arg.
        // Build it programmatically so the source stays pure ASCII with no literal control byte.
        var ctrl = ((char)1).ToString();

        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE " + ctrl + "x");

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual(ctrl + "x", arg);
    }

    [TestMethod]
    public void UnicodeGroupNameArgument_IsPreserved()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("GROUP café.münchen");

        Assert.AreEqual("GROUP", cmd);
        Assert.AreEqual("café.münchen", arg);
    }

    [TestMethod]
    public void UnicodeCommandToken_IsUppercasedInvariant()
    {
        // ToUpperInvariant is culture-independent, so U+00E9 (e-acute) folds to U+00C9 here.
        var (cmd, arg) = NntpProxy.ParseCommand("café");

        Assert.AreEqual("CAFÉ", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void DottedLowercaseI_UppercasesInvariantNotTurkish()
    {
        // Guards against a locale-dependent 'i' -> dotless-I bug: invariant upper of "list" is "LIST".
        var (cmd, _) = NntpProxy.ParseCommand("list");

        Assert.AreEqual("LIST", cmd);
    }

    [TestMethod]
    public void HugeArticleNumberArgument_IsPreservedAsStringNotParsed()
    {
        // ParseCommand does no numeric parsing, so an out-of-range number cannot overflow here; it is
        // just carried through verbatim.
        var big = new string('9', 40);

        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE " + big);

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual(big, arg);
    }

    [TestMethod]
    public void NegativeNumberArgument_IsPreservedAsString()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE -5");

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual("-5", arg);
    }

    [TestMethod]
    public void RangeArgument_IsPreservedVerbatim()
    {
        // The range splitting on '-' happens later inside HandleXover; ParseCommand keeps it intact.
        var (cmd, arg) = NntpProxy.ParseCommand("XOVER 1-100");

        Assert.AreEqual("XOVER", cmd);
        Assert.AreEqual("1-100", arg);
    }

    [TestMethod]
    public void MalformedRangeArgument_IsPreservedVerbatim()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("XOVER 1-2-3");

        Assert.AreEqual("XOVER", cmd);
        Assert.AreEqual("1-2-3", arg);
    }

    [TestMethod]
    public void MessageIdArgument_IsPreservedIncludingAngleBrackets()
    {
        var (cmd, arg) = NntpProxy.ParseCommand("ARTICLE <abc.123@news.example.com>");

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual("<abc.123@news.example.com>", arg);
    }

    [TestMethod]
    public void InternalSpacesInArgument_ArePreserved()
    {
        // Only the first space delimits; everything after it (including inner spaces) is one argument.
        var (cmd, arg) = NntpProxy.ParseCommand("XPAT Subject 1-10 *foo bar*");

        Assert.AreEqual("XPAT", cmd);
        Assert.AreEqual("Subject 1-10 *foo bar*", arg);
    }

    [TestMethod]
    public void VeryLongArgument_IsPreservedInFull()
    {
        var payload = new string('a', 200000);

        var (cmd, arg) = NntpProxy.ParseCommand("GROUP " + payload);

        Assert.AreEqual("GROUP", cmd);
        Assert.AreEqual(200000, arg.Length);
        Assert.AreEqual(payload, arg);
    }

    [TestMethod]
    public void VeryLongCommandWordNoSpace_IsUppercasedAndReturned()
    {
        var word = new string('x', 100000);

        var (cmd, arg) = NntpProxy.ParseCommand(word);

        Assert.AreEqual(word.ToUpperInvariant(), cmd);
        Assert.AreEqual("", arg);
    }
}

[TestClass]
public class NntpParseCommandSpanTests
{
    [TestMethod]
    public void Span_NormalCommand_ParsesLikeString()
    {
        var bytes = Encoding.ASCII.GetBytes("ARTICLE 5");

        var (cmd, arg) = NntpProxy.ParseCommand(bytes, bytes.Length);

        Assert.AreEqual("ARTICLE", cmd);
        Assert.AreEqual("5", arg);
    }

    [TestMethod]
    public void Span_ZeroRead_ReturnsEmptyCommandAndArgument()
    {
        var bytes = Encoding.ASCII.GetBytes("ARTICLE 5");

        var (cmd, arg) = NntpProxy.ParseCommand(bytes, 0);

        Assert.AreEqual("", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void Span_ReadShorterThanBuffer_TruncatesToReadLength()
    {
        // read is the honored length; trailing buffer bytes past 'read' are ignored.
        var bytes = Encoding.ASCII.GetBytes("GROUP alt.testXXXXXX");

        var (cmd, arg) = NntpProxy.ParseCommand(bytes, "GROUP alt.test".Length);

        Assert.AreEqual("GROUP", cmd);
        Assert.AreEqual("alt.test", arg);
    }

    [TestMethod]
    public void Span_ReadBeyondBufferLength_ThrowsArgumentOutOfRange()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            var bytes = Encoding.ASCII.GetBytes("LIST");

            NntpProxy.ParseCommand(bytes, 999);
        });
    }

    [TestMethod]
    public void Span_NegativeRead_ThrowsArgumentOutOfRange()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            var bytes = Encoding.ASCII.GetBytes("LIST");

            NntpProxy.ParseCommand(bytes, -1);
        });
    }

    [TestMethod]
    public void Span_HighByteInArgument_IsReplacedWithQuestionMark()
    {
        // Encoding.ASCII maps every byte > 0x7F to '?'. A single high byte becomes one '?'.
        var bytes = new byte[] { 0x47, 0x52, 0x4F, 0x55, 0x50, 0x20, 0xE9 }; // "GROUP " + 0xE9

        var (cmd, arg) = NntpProxy.ParseCommand(bytes, bytes.Length);

        Assert.AreEqual("GROUP", cmd);
        Assert.AreEqual("?", arg);
    }

    [TestMethod]
    public void Span_MultibyteUtf8InArgument_EachByteBecomesQuestionMark()
    {
        // UTF-8 'e-acute' is two bytes (0xC3 0xA9); ASCII decoding yields two '?' characters.
        var bytes = new byte[] { 0x47, 0x52, 0x4F, 0x55, 0x50, 0x20, 0xC3, 0xA9 }; // "GROUP " + UTF8 e-acute

        var (cmd, arg) = NntpProxy.ParseCommand(bytes, bytes.Length);

        Assert.AreEqual("GROUP", cmd);
        Assert.AreEqual("??", arg);
    }

    [TestMethod]
    public void Span_EmbeddedNullByteInCommand_SurvivesAsControlChar()
    {
        // 0x00 is a valid ASCII code point and is not whitespace, so it survives inside the command
        // token (there is no space, so the whole thing is the command word).
        var bytes = new byte[] { 0x41, 0x00, 0x42 }; // "A\0B"

        var (cmd, arg) = NntpProxy.ParseCommand(bytes, bytes.Length);

        Assert.AreEqual("A\0B", cmd);
        Assert.AreEqual("", arg);
    }

    [TestMethod]
    public void Span_MatchesStringOverloadForSameBytes()
    {
        var text = "XOVER 10-20";
        var bytes = Encoding.ASCII.GetBytes(text);

        var fromSpan = NntpProxy.ParseCommand(bytes, bytes.Length);
        var fromString = NntpProxy.ParseCommand(text);

        Assert.AreEqual(fromString.Command, fromSpan.Command);
        Assert.AreEqual(fromString.Argument, fromSpan.Argument);
    }

    [TestMethod]
    public void Span_EmptyBuffer_ReturnsEmpty()
    {
        var bytes = Array.Empty<byte>();

        var (cmd, arg) = NntpProxy.ParseCommand(bytes, 0);

        Assert.AreEqual("", cmd);
        Assert.AreEqual("", arg);
    }
}

[TestClass]
public class NntpDotStuffTests
{
    [TestMethod]
    public void Null_ReturnsEmptyString()
    {
        Assert.AreEqual("", NntpProxy.DotStuff(null!));
    }

    [TestMethod]
    public void Empty_ReturnsEmptyString()
    {
        Assert.AreEqual("", NntpProxy.DotStuff(""));
    }

    [TestMethod]
    public void PlainSingleLine_IsUnchanged()
    {
        Assert.AreEqual("hello world", NntpProxy.DotStuff("hello world"));
    }

    [TestMethod]
    public void LeadingDot_IsDoubled()
    {
        Assert.AreEqual("..only", NntpProxy.DotStuff(".only"));
    }

    [TestMethod]
    public void SingleDotBody_IsDoubled()
    {
        Assert.AreEqual("..", NntpProxy.DotStuff("."));
    }

    [TestMethod]
    public void DotAfterCrLf_IsDoubled()
    {
        Assert.AreEqual("line1\r\n..line2", NntpProxy.DotStuff("line1\r\n.line2"));
    }

    [TestMethod]
    public void MultipleDotLines_AllDoubled()
    {
        Assert.AreEqual("a\r\n..b\r\n..c", NntpProxy.DotStuff("a\r\n.b\r\n.c"));
    }

    [TestMethod]
    public void LeadingDotAndInnerDotLine_BothDoubled()
    {
        Assert.AreEqual("..\r\n..b", NntpProxy.DotStuff(".\r\n.b"));
    }

    [TestMethod]
    public void NonLeadingDotWithinLine_IsNotDoubled()
    {
        // Only a dot at the very start of a line is stuffed; a dot mid-line is left alone.
        Assert.AreEqual("a.b.c", NntpProxy.DotStuff("a.b.c"));
    }
}
