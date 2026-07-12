// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Globalization;
using System.Threading;
using VintageHive.Proxy.Irc;

namespace Adversarial3.Irc;

// Adversarial coverage for IrcProxy.ParseIrcCommand(string) - the regex-based IRC message-line parser.
// Pattern under test:
//   ^(?::(?<prefix>[^\s]+)\s+)?(?<command>[A-Za-z0-9]+)(?:\s+(?<params>[^\s:]+))*(?:\s+:(?<trailing>.*))?$
// Every assertion below reflects ACTUAL observed behavior, captured by running the parser first.
// The struct (IrcCommand) exposes only Command / Params / Trailing - the parsed prefix is discarded.

[TestClass]
public class IrcParseNullEmptyWhitespaceTests
{
    [TestMethod]
    public void Null_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand(null));
    }

    [TestMethod]
    public void Empty_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand(""));
    }

    [TestMethod]
    public void Spaces_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand("     "));
    }

    [TestMethod]
    public void SingleTab_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand("\t"));
    }

    [TestMethod]
    public void CrLfOnly_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand("\r\n"));
    }

    [TestMethod]
    public void MixedWhitespaceOnly_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand(" \t \r \n "));
    }

    [TestMethod]
    public void LeadingSpaceBeforeCommand_ReturnsNull()
    {
        // No leading-whitespace tolerance: the '^' anchor plus alnum command means a leading space kills the match.
        Assert.IsNull(IrcProxy.ParseIrcCommand(" PING"));
    }

    [TestMethod]
    public void TrailingSpaceAfterCommand_IsTolerated()
    {
        // The trailing \s* in the pattern now absorbs a lone trailing space instead of dropping the command.
        var cmd = IrcProxy.ParseIrcCommand("PING ");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PING", cmd.Command);
        Assert.AreEqual(0, cmd.Params.Count);
    }
}

[TestClass]
public class IrcParsePrefixTests
{
    [TestMethod]
    public void OnlyColon_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand(":"));
    }

    [TestMethod]
    public void OnlyPrefix_NoCommand_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand(":prefix"));
    }

    [TestMethod]
    public void ColonSpaceCommand_ReturnsNull()
    {
        // ':' then space: the prefix group needs a non-space token before its whitespace, so this fails entirely.
        Assert.IsNull(IrcProxy.ParseIrcCommand(": PING"));
    }

    [TestMethod]
    public void PrefixIsDiscarded_CommandParamsTrailingRemain()
    {
        var cmd = IrcProxy.ParseIrcCommand(":nick!user@host PRIVMSG #chan :hi there");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PRIVMSG", cmd.Command);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("#chan", cmd.Params[0]);
        Assert.AreEqual("hi there", cmd.Trailing);
        // IrcCommand has no Prefix property - the ":nick!user@host" source is parsed then dropped on the floor.
    }

    [TestMethod]
    public void PrefixOnlyThenCommand_NoParams()
    {
        var cmd = IrcProxy.ParseIrcCommand(":irc.example.net PING");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PING", cmd.Command);
        Assert.AreEqual(0, cmd.Params.Count);
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void PrefixWithMultipleSpacesBeforeCommand_Parses()
    {
        var cmd = IrcProxy.ParseIrcCommand(":srv    NICK newbie");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("NICK", cmd.Command);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("newbie", cmd.Params[0]);
    }
}

[TestClass]
public class IrcParseCommandTokenTests
{
    [TestMethod]
    public void LowercaseCommand_IsUppercased()
    {
        Assert.AreEqual("NICK", IrcProxy.ParseIrcCommand("nick fox").Command);
    }

    [TestMethod]
    public void MixedCaseCommand_IsUppercased()
    {
        Assert.AreEqual("PRIVMSG", IrcProxy.ParseIrcCommand("PrIvMsG #x :y").Command);
    }

    [TestMethod]
    public void BareCommand_NoParamsNoTrailing()
    {
        var cmd = IrcProxy.ParseIrcCommand("LIST");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("LIST", cmd.Command);
        Assert.AreEqual(0, cmd.Params.Count);
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void NumericCommand_IsAccepted()
    {
        // The command class is [A-Za-z0-9]+, so a purely numeric "command" from a client is happily accepted.
        var cmd = IrcProxy.ParseIrcCommand("001 foo");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("001", cmd.Command);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("foo", cmd.Params[0]);
    }

    [TestMethod]
    public void CommandWithDigits_Parses()
    {
        Assert.AreEqual("ABC123", IrcProxy.ParseIrcCommand("abc123 x").Command);
    }

    [TestMethod]
    public void NonAsciiInCommand_ReturnsNull()
    {
        // A non-ASCII byte glued to the command breaks the [A-Za-z0-9]+ class and rejects the line.
        Assert.IsNull(IrcProxy.ParseIrcCommand("PRIVMSGÜ x"));
    }

    [TestMethod]
    public void CommandWithLeadingDigitStillValid()
    {
        var cmd = IrcProxy.ParseIrcCommand("4XYZ a b");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("4XYZ", cmd.Command);
        Assert.AreEqual(2, cmd.Params.Count);
    }
}

[TestClass]
public class IrcParseParamsTests
{
    [TestMethod]
    public void ManyParams_AllCaptured()
    {
        var cmd = IrcProxy.ParseIrcCommand("MODE #chan +ovlk a b c d");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("MODE", cmd.Command);
        Assert.AreEqual(6, cmd.Params.Count);
        CollectionAssert.AreEqual(new[] { "#chan", "+ovlk", "a", "b", "c", "d" }, cmd.Params.ToArray());
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void MultipleSpacesBetweenParams_Collapse()
    {
        var cmd = IrcProxy.ParseIrcCommand("PING    foo");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("foo", cmd.Params[0]);
    }

    [TestMethod]
    public void TabSeparatedParams_TabActsAsWhitespace()
    {
        // \s in .NET matches TAB, so tabs work as parameter separators just like spaces.
        var cmd = IrcProxy.ParseIrcCommand("PING\tfoo\tbar");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PING", cmd.Command);
        Assert.AreEqual(2, cmd.Params.Count);
        CollectionAssert.AreEqual(new[] { "foo", "bar" }, cmd.Params.ToArray());
    }

    [TestMethod]
    public void VerticalTabAndFormFeed_ActAsWhitespace()
    {
        Assert.AreEqual("foo", IrcProxy.ParseIrcCommand("PING\vfoo").Params[0]);
        Assert.AreEqual("foo", IrcProxy.ParseIrcCommand("PING\ffoo").Params[0]);
    }

    [TestMethod]
    public void NonBreakingSpace_ActsAsWhitespaceSeparator()
    {
        // U+00A0 is a Unicode Zs space, so .NET \s treats it as a separator (unlike a strict ASCII IRC parser).
        var cmd = IrcProxy.ParseIrcCommand("PING foo");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("foo", cmd.Params[0]);
    }

    [TestMethod]
    public void NoColonMultiWord_EachWordBecomesSeparateParam()
    {
        // Without a trailing ':', a multi-word message is shredded into individual params - the words are NOT joined.
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG #c hello world");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(3, cmd.Params.Count);
        CollectionAssert.AreEqual(new[] { "#c", "hello", "world" }, cmd.Params.ToArray());
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void NonAsciiParam_Preserved()
    {
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG #café :héllo");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("#café", cmd.Params[0]);
        Assert.AreEqual("héllo", cmd.Trailing);
    }

    [TestMethod]
    public void ExtremelyLongParamList_AllCaptured()
    {
        const int count = 1000;
        var line = "PING" + string.Concat(Enumerable.Repeat(" x", count));

        var cmd = IrcProxy.ParseIrcCommand(line);

        Assert.IsNotNull(cmd);
        Assert.AreEqual(count, cmd.Params.Count);
        Assert.IsTrue(cmd.Params.All(p => p == "x"));
    }

    [TestMethod]
    public void SingleVeryLongParam_Preserved()
    {
        var big = new string('a', 10000);
        var cmd = IrcProxy.ParseIrcCommand("PING " + big);

        Assert.IsNotNull(cmd);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual(big, cmd.Params[0]);
    }

    [TestMethod]
    public void ParamStartingWithColon_BecomesTrailingNotParam()
    {
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG :hello");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(0, cmd.Params.Count);
        Assert.AreEqual("hello", cmd.Trailing);
    }
}

[TestClass]
public class IrcParseTrailingTests
{
    [TestMethod]
    public void MissingTrailing_IsNull()
    {
        var cmd = IrcProxy.ParseIrcCommand("JOIN #chan key");

        Assert.IsNotNull(cmd);
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void EmptyTrailing_IsEmptyStringNotNull()
    {
        // A bare ':' with nothing after yields Trailing == "" (present, empty) - distinct from a missing trailing (null).
        var cmd = IrcProxy.ParseIrcCommand("PING :");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(0, cmd.Params.Count);
        Assert.AreEqual("", cmd.Trailing);
    }

    [TestMethod]
    public void TrailingOfOnlySpaces_PreservedVerbatim()
    {
        var cmd = IrcProxy.ParseIrcCommand("PING :   ");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("   ", cmd.Trailing);
    }

    [TestMethod]
    public void TrailingWithEmbeddedSpaces_Preserved()
    {
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG #c :a b  c");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("#c", cmd.Params[0]);
        Assert.AreEqual("a b  c", cmd.Trailing);
    }

    [TestMethod]
    public void TrailingWithEmbeddedColons_Preserved()
    {
        // Only the FIRST ' :' delimits; every later colon (even a leading one) stays inside the trailing text.
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG #c ::a:b");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("#c", cmd.Params[0]);
        Assert.AreEqual(":a:b", cmd.Trailing);
    }

    [TestMethod]
    public void TrailingWithEmbeddedTab_Preserved()
    {
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG #c :a\tb");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("a\tb", cmd.Trailing);
    }

    [TestMethod]
    public void QuitTrailing_Preserved()
    {
        var cmd = IrcProxy.ParseIrcCommand("QUIT :Goodbye everyone");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("QUIT", cmd.Command);
        Assert.AreEqual("Goodbye everyone", cmd.Trailing);
    }

    [TestMethod]
    public void ExtremelyLongTrailing_Preserved()
    {
        var big = new string('b', 50000);
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG #c :" + big);

        Assert.IsNotNull(cmd);
        Assert.AreEqual(big, cmd.Trailing);
    }
}

[TestClass]
public class IrcParseControlAndNewlineTests
{
    [TestMethod]
    public void LoneLfAtEnd_IsToleratedByDollarAnchor()
    {
        // '$' in .NET matches before a single trailing '\n', so "PING\n" parses as a bare PING.
        var cmd = IrcProxy.ParseIrcCommand("PING\n");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PING", cmd.Command);
        Assert.AreEqual(0, cmd.Params.Count);
    }

    [TestMethod]
    public void CrLfAtEnd_IsTolerated()
    {
        // The trailing \s* now absorbs a trailing CRLF as well, so "PING\r\n" parses as a bare PING,
        // consistent with the lone-LF case (production still pre-splits, so this is just defense in depth).
        var cmd = IrcProxy.ParseIrcCommand("PING\r\n");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PING", cmd.Command);
        Assert.AreEqual(0, cmd.Params.Count);
    }

    [TestMethod]
    public void EmbeddedCrLf_SecondLineBecomesAParam()
    {
        // With no line pre-splitting, an injected CRLF is just treated as whitespace and the tail becomes a param.
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG\r\nNICK");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PRIVMSG", cmd.Command);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("NICK", cmd.Params[0]);
    }

    [TestMethod]
    public void EmbeddedBareLf_TailBecomesAParam()
    {
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG\nfoo");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PRIVMSG", cmd.Command);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("foo", cmd.Params[0]);
    }

    [TestMethod]
    public void EmbeddedBareCr_ActsAsSeparator()
    {
        var cmd = IrcProxy.ParseIrcCommand("PING\rfoo");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("PING", cmd.Command);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("foo", cmd.Params[0]);
    }

    [TestMethod]
    public void NullByteInParam_IsRetained()
    {
        // A NUL is not whitespace and not a colon, so it lives inside a param unmolested.
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG a\0b");

        Assert.IsNotNull(cmd);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("a\0b", cmd.Params[0]);
    }

    [TestMethod]
    public void AdversarialInputs_NeverThrow()
    {
        // The parser must degrade to null (or a value), never throw, across a spread of hostile inputs.
        var inputs = new[]
        {
            null,
            "",
            "   ",
            ":",
            "::::",
            ":::: :::: ::::",
            " ",
            "\r\n\r\n",
            "\0\0\0",
            "PING ",
            "PING\r\n",
            ":prefix",
            "MODE #c +b a:b",
            "",
            new string(':', 500),
            new string(' ', 500) + "PING",
        };

        foreach (var input in inputs)
        {
            // Should not throw for any of these.
            var _ = IrcProxy.ParseIrcCommand(input);
        }
    }
}

[TestClass]
public class IrcParseColonAndFoldingTests
{
    // These once pinned down genuine defects (colon-in-middle-param dropping the line, culture-sensitive
    // folding). The parser is now fixed, so they assert the corrected behavior.

    [TestMethod]
    public void MiddleParamWithNonLeadingColon_IsKeptInParam()
    {
        // RFC 2812 allows ':' inside a middle param after its first char; the whole token is now one param.
        var cmd = IrcProxy.ParseIrcCommand("MODE #chan +b abc:def");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("MODE", cmd.Command);
        CollectionAssert.AreEqual(new[] { "#chan", "+b", "abc:def" }, cmd.Params);
    }

    [TestMethod]
    public void Ipv6BanMask_IsKeptInParam()
    {
        // The IPv6 ban mask is now preserved as a single middle param instead of being swallowed.
        var cmd = IrcProxy.ParseIrcCommand("MODE #c +b *!*@2001:db8::1");

        Assert.IsNotNull(cmd);
        CollectionAssert.AreEqual(new[] { "#c", "+b", "*!*@2001:db8::1" }, cmd.Params);
    }

    [TestMethod]
    public void HostmaskWithPortLikeColon_IsKeptInParam()
    {
        // A "server:6667" token is now a valid middle param, not a line-killer.
        var cmd = IrcProxy.ParseIrcCommand("CONNECT irc.example.net:6667 3");

        Assert.IsNotNull(cmd);
        Assert.AreEqual("CONNECT", cmd.Command);
        CollectionAssert.AreEqual(new[] { "irc.example.net:6667", "3" }, cmd.Params);
    }

    [TestMethod]
    public void CommandFolding_TurkishLocale_StillFoldsToAsciiNick()
    {
        // ToUpperInvariant means even under tr-TR (where ToUpper folds 'i' to U+0130) the command still
        // folds to plain ASCII "NICK" and matches the command constant HandleCommand switches on.
        var prev = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");

            var cmd = IrcProxy.ParseIrcCommand("nick fox");

            Assert.IsNotNull(cmd);
            Assert.AreEqual("NICK", cmd.Command);
            Assert.AreEqual(IrcCommand.NICK, cmd.Command);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [TestMethod]
    public void CommandFolding_InvariantCulture_IsAscii()
    {
        // Control: under invariant culture the same command folds to plain ASCII "NICK".
        var prev = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.AreEqual("NICK", IrcProxy.ParseIrcCommand("nick fox").Command);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }
}
