// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Irc;
using static VintageHive.Proxy.Irc.IrcServerReplyType;

namespace Irc;

[TestClass]
public class IrcParserTests
{
    [TestMethod]
    public void Parse_SimpleCommandWithParam()
    {
        var cmd = IrcProxy.ParseIrcCommand("NICK fox");

        Assert.AreEqual("NICK", cmd.Command);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("fox", cmd.Params[0]);
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void Parse_CommandIsUppercased()
    {
        Assert.AreEqual("NICK", IrcProxy.ParseIrcCommand("nick fox").Command);
        Assert.AreEqual("PRIVMSG", IrcProxy.ParseIrcCommand("privmsg #x :y").Command);
    }

    [TestMethod]
    public void Parse_CommandOnly()
    {
        var cmd = IrcProxy.ParseIrcCommand("LIST");

        Assert.AreEqual("LIST", cmd.Command);
        Assert.AreEqual(0, cmd.Params.Count);
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void Parse_MultipleParams()
    {
        var cmd = IrcProxy.ParseIrcCommand("MODE #chan +o fox");

        Assert.AreEqual("MODE", cmd.Command);
        CollectionAssert.AreEqual(new[] { "#chan", "+o", "fox" }, cmd.Params);
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void Parse_TrailingWithSpaces()
    {
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG #hive :hello world how are you");

        Assert.AreEqual("PRIVMSG", cmd.Command);
        CollectionAssert.AreEqual(new[] { "#hive" }, cmd.Params);
        Assert.AreEqual("hello world how are you", cmd.Trailing);
    }

    [TestMethod]
    public void Parse_TrailingKeepsColons()
    {
        var cmd = IrcProxy.ParseIrcCommand("PRIVMSG #hive :see http://example.com now");

        Assert.AreEqual("see http://example.com now", cmd.Trailing);
    }

    [TestMethod]
    public void Parse_TrailingOnly_NoParams()
    {
        var cmd = IrcProxy.ParseIrcCommand("QUIT :Goodbye everyone");

        Assert.AreEqual("QUIT", cmd.Command);
        Assert.AreEqual(0, cmd.Params.Count);
        Assert.AreEqual("Goodbye everyone", cmd.Trailing);
    }

    [TestMethod]
    public void Parse_EmptyTrailing()
    {
        var cmd = IrcProxy.ParseIrcCommand("PART #hive :");

        CollectionAssert.AreEqual(new[] { "#hive" }, cmd.Params);
        Assert.AreEqual("", cmd.Trailing);
    }

    [TestMethod]
    public void Parse_PrefixIsIgnored()
    {
        var cmd = IrcProxy.ParseIrcCommand(":nick!user@host PRIVMSG #chan :hi there");

        Assert.AreEqual("PRIVMSG", cmd.Command);
        CollectionAssert.AreEqual(new[] { "#chan" }, cmd.Params);
        Assert.AreEqual("hi there", cmd.Trailing);
    }

    [TestMethod]
    public void Parse_PingWithToken()
    {
        var cmd = IrcProxy.ParseIrcCommand("PING :LAG1234567");

        Assert.AreEqual("PING", cmd.Command);
        Assert.AreEqual("LAG1234567", cmd.Trailing);
    }

    [TestMethod]
    public void Parse_EmptyInput_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand(""));
    }

    [TestMethod]
    public void Parse_WhitespaceInput_ReturnsNull()
    {
        Assert.IsNull(IrcProxy.ParseIrcCommand("     "));
    }

    [TestMethod]
    public void Parse_JoinWithKey()
    {
        var cmd = IrcProxy.ParseIrcCommand("JOIN #secret sekritpass");

        Assert.AreEqual("JOIN", cmd.Command);
        CollectionAssert.AreEqual(new[] { "#secret", "sekritpass" }, cmd.Params);
    }
}

[TestClass]
public class IrcReplyFormatTests
{
    // nick/parameters/trailing are null-tolerant in SendIrcReply (the main project builds under Nullable=annotations,
    // so it passes null freely); mirror that here and suppress on the forward so the enable-mode test project stays clean.
    private static string Reply(string host, IrcServerReplyType type, string? nick, string[]? parameters, string? trailing)
        => Encoding.UTF8.GetString(IrcProxy.SendIrcReply(host, type, nick!, parameters!, trailing!));

    [TestMethod]
    public void Format_NumericUnder100_ZeroPadded()
    {
        // RPL_WELCOME = 1 -> "001"
        Assert.AreEqual(":irc.hive.com 001 fox :Welcome!\r\n", Reply("irc.hive.com", RPL_WELCOME, "fox", null, "Welcome!"));
    }

    [TestMethod]
    public void Format_NumericInRange_PlainNumber()
    {
        // ERR_NOSUCHNICK = 401
        Assert.AreEqual(":irc.hive.com 401 fox bar :No such nick/channel\r\n", Reply("irc.hive.com", ERR_NOSUCHNICK, "fox", new[] { "bar" }, "No such nick/channel"));
    }

    [TestMethod]
    public void Format_StringCommand_StripsStrPrefix()
    {
        // STR_PRIVMSG (901) -> "PRIVMSG"
        Assert.AreEqual(":nick!u@h PRIVMSG #chan :hi\r\n", Reply("nick!u@h", STR_PRIVMSG, "#chan", null, "hi"));
    }

    [TestMethod]
    public void Format_JoinCommand_NullNickAndParams()
    {
        // STR_JOIN (902) -> "JOIN"; nick + params null, target is the trailing
        Assert.AreEqual(":nick!u@h JOIN :#chan\r\n", Reply("nick!u@h", STR_JOIN, null, null, "#chan"));
    }

    [TestMethod]
    public void Format_ParamsAndTrailing()
    {
        // RPL_NAMREPLY = 353
        Assert.AreEqual(":irc.hive.com 353 fox = #chan :@fox +bar baz\r\n", Reply("irc.hive.com", RPL_NAMREPLY, "fox", new[] { "=", "#chan" }, "@fox +bar baz"));
    }

    [TestMethod]
    public void Format_NoTrailing_OmitsColon()
    {
        // RPL_CHANNELMODEIS = 324, trailing null -> no " :" section
        Assert.AreEqual(":h 324 fox #chan +nt\r\n", Reply("h", RPL_CHANNELMODEIS, "fox", new[] { "#chan", "+nt" }, null));
    }

    [TestMethod]
    public void Format_EmptyTrailing_KeepsColon()
    {
        // Empty (non-null) trailing still emits the " :" marker
        Assert.AreEqual(":h 332 fox #chan :\r\n", Reply("h", RPL_TOPIC, "fox", new[] { "#chan" }, ""));
    }

    [TestMethod]
    public void Format_AlwaysEndsWithCrLf()
    {
        Assert.IsTrue(Reply("h", RPL_MOTD, "fox", null, "line").EndsWith("\r\n"));
    }
}

[TestClass]
public class IrcNickValidationTests
{
    [TestMethod]
    public void Valid_SimpleNick() => Assert.IsTrue(IrcProxy.IsValidNick("fox"));

    [TestMethod]
    public void Valid_WithDigitsAfterFirst() => Assert.IsTrue(IrcProxy.IsValidNick("Fox123"));

    [TestMethod]
    public void Valid_DashAfterFirst() => Assert.IsTrue(IrcProxy.IsValidNick("fox-bar"));

    [TestMethod]
    public void Valid_SpecialFirstChars() => Assert.IsTrue(IrcProxy.IsValidNick("[nick]"));

    [TestMethod]
    public void Valid_AllSpecialChars() => Assert.IsTrue(IrcProxy.IsValidNick("{|}`_^"));

    [TestMethod]
    public void Valid_ExactlyMaxLength() => Assert.IsTrue(IrcProxy.IsValidNick(new string('a', 30)));

    [TestMethod]
    public void Invalid_Empty() => Assert.IsFalse(IrcProxy.IsValidNick(""));

    [TestMethod]
    public void Invalid_Null() => Assert.IsFalse(IrcProxy.IsValidNick(null!));

    [TestMethod]
    public void Invalid_TooLong() => Assert.IsFalse(IrcProxy.IsValidNick(new string('a', 31)));

    [TestMethod]
    public void Invalid_StartsWithDigit() => Assert.IsFalse(IrcProxy.IsValidNick("1fox"));

    [TestMethod]
    public void Invalid_StartsWithDash() => Assert.IsFalse(IrcProxy.IsValidNick("-fox"));

    [TestMethod]
    public void Invalid_ContainsSpace() => Assert.IsFalse(IrcProxy.IsValidNick("fox bar"));

    [TestMethod]
    public void Invalid_ContainsAt() => Assert.IsFalse(IrcProxy.IsValidNick("fox@bar"));
}

[TestClass]
public class IrcChannelLogicTests
{
    private static IrcUser User(string nick, string user = "u", string host = "host")
        => new(null!) { Nick = nick, Username = user, Hostname = host };

    [TestMethod]
    public void Operator_IsCaseInsensitive()
    {
        var chan = new IrcChannel("#test");
        chan.Operators.Add("Fox");

        Assert.IsTrue(chan.IsOperator("fox"));
        Assert.IsTrue(chan.IsOperator("FOX"));
        Assert.IsFalse(chan.IsOperator("bar"));
    }

    [TestMethod]
    public void GetNamesPrefix_OpVoicePlain()
    {
        var chan = new IrcChannel("#test");
        chan.Operators.Add("op");
        chan.Voiced.Add("voice");

        Assert.AreEqual("@", chan.GetNamesPrefix("op"));
        Assert.AreEqual("+", chan.GetNamesPrefix("voice"));
        Assert.AreEqual("", chan.GetNamesPrefix("nobody"));
    }

    [TestMethod]
    public void GetNamesPrefix_OperatorWinsOverVoice()
    {
        var chan = new IrcChannel("#test");
        chan.Operators.Add("boss");
        chan.Voiced.Add("boss");

        Assert.AreEqual("@", chan.GetNamesPrefix("boss"));
    }

    [TestMethod]
    public void IsBanned_HostWildcard()
    {
        var chan = new IrcChannel("#test");
        chan.BanMasks.Add("*!*@evil.com");

        Assert.IsTrue(chan.IsBanned("nick!user@evil.com"));
        Assert.IsFalse(chan.IsBanned("nick!user@good.com"));
    }

    [TestMethod]
    public void IsBanned_NickWildcard()
    {
        var chan = new IrcChannel("#test");
        chan.BanMasks.Add("baduser!*@*");

        Assert.IsTrue(chan.IsBanned("baduser!x@y.com"));
        Assert.IsFalse(chan.IsBanned("gooduser!x@y.com"));
    }

    [TestMethod]
    public void IsBanned_SingleCharWildcard()
    {
        var chan = new IrcChannel("#test");
        chan.BanMasks.Add("fo?!*@*");

        Assert.IsTrue(chan.IsBanned("fox!user@host"));
        Assert.IsFalse(chan.IsBanned("foxx!user@host"));
    }

    [TestMethod]
    public void IsBanned_CaseInsensitive()
    {
        var chan = new IrcChannel("#test");
        chan.BanMasks.Add("*!*@EVIL.COM");

        Assert.IsTrue(chan.IsBanned("nick!user@evil.com"));
    }

    [TestMethod]
    public void IsBanned_NoMasks()
    {
        Assert.IsFalse(new IrcChannel("#test").IsBanned("anyone!any@where"));
    }

    [TestMethod]
    public void CanSendMessage_MemberCanSend()
    {
        var chan = new IrcChannel("#test");
        var u = User("fox");
        chan.Members[u.Nick] = u;

        Assert.IsTrue(chan.CanSendMessage(u));
    }

    [TestMethod]
    public void CanSendMessage_NonMemberBlockedByNoExternal()
    {
        // Fresh channels default to +n (no external messages)
        var chan = new IrcChannel("#test");

        Assert.IsFalse(chan.CanSendMessage(User("outsider")));
    }

    [TestMethod]
    public void CanSendMessage_BannedCannotSend()
    {
        var chan = new IrcChannel("#test");
        var u = User("fox", "u", "evil.com");
        chan.Members[u.Nick] = u;
        chan.BanMasks.Add("*!*@evil.com");

        Assert.IsFalse(chan.CanSendMessage(u));
    }

    [TestMethod]
    public void CanSendMessage_OperatorBypassesBanAndMode()
    {
        var chan = new IrcChannel("#test");
        var u = User("fox", "u", "evil.com");
        chan.Operators.Add(u.Nick);           // op, but not a member
        chan.BanMasks.Add("*!*@evil.com");    // and banned

        Assert.IsTrue(chan.CanSendMessage(u));
    }
}
