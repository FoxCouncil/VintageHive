// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Irc;

namespace Protocols;

[TestClass]
public class FtpCommandTests
{
    [TestMethod]
    public void FtpCommand_AuthenticationUsername()
    {
        Assert.AreEqual("USER", FtpCommand.AuthenticationUsername);
    }

    [TestMethod]
    public void FtpCommand_SystemType()
    {
        Assert.AreEqual("SYST", FtpCommand.SystemType);
    }

    [TestMethod]
    public void FtpCommand_FeatureList()
    {
        Assert.AreEqual("FEAT", FtpCommand.FeatureList);
    }

    [TestMethod]
    public void FtpCommand_PrintWorkingDirectory()
    {
        Assert.AreEqual("PWD", FtpCommand.PrintWorkingDirectory);
    }

    [TestMethod]
    public void FtpCommand_TransferMode()
    {
        Assert.AreEqual("TYPE", FtpCommand.TransferMode);
    }

    [TestMethod]
    public void FtpCommand_PassiveMode()
    {
        Assert.AreEqual("PASV", FtpCommand.PassiveMode);
    }

    [TestMethod]
    public void FtpCommand_ListInfo()
    {
        Assert.AreEqual("LIST", FtpCommand.ListInfo);
    }

    [TestMethod]
    public void FtpCommand_Quit()
    {
        Assert.AreEqual("QUIT", FtpCommand.Quit);
    }

    [TestMethod]
    public void FtpCommand_ChangeWorkingDirectory()
    {
        Assert.AreEqual("CWD", FtpCommand.ChangeWorkingDirectory);
    }

    [TestMethod]
    public void FtpCommand_RetrieveFile()
    {
        Assert.AreEqual("RETR", FtpCommand.RetrieveFile);
    }

    [TestMethod]
    public void FtpCommand_StoreFile()
    {
        Assert.AreEqual("STOR", FtpCommand.StoreFile);
    }

    [TestMethod]
    public void FtpCommand_DeleteFile()
    {
        Assert.AreEqual("DELE", FtpCommand.DeleteFile);
    }

    [TestMethod]
    public void FtpCommand_RenameFrom()
    {
        Assert.AreEqual("RNFR", FtpCommand.RenameFrom);
    }

    [TestMethod]
    public void FtpCommand_RenameTo()
    {
        Assert.AreEqual("RNTO", FtpCommand.RenameTo);
    }

    [TestMethod]
    public void FtpCommand_MakeDirectory()
    {
        Assert.AreEqual("MKD", FtpCommand.MakeDirectory);
    }

    [TestMethod]
    public void FtpCommand_DeleteDirectory()
    {
        Assert.AreEqual("RMD", FtpCommand.DeleteDirectory);
    }

    [TestMethod]
    public void FtpCommand_AbortActiveFileTransfer()
    {
        Assert.AreEqual("ABOR", FtpCommand.AbortActiveFileTransfer);
    }

    [TestMethod]
    public void FtpCommand_RestartTransfer()
    {
        Assert.AreEqual("REST", FtpCommand.RestartTransfer);
    }

    [TestMethod]
    public void FtpCommand_Site()
    {
        Assert.AreEqual("SITE", FtpCommand.Site);
    }

    [TestMethod]
    public void FtpCommand_Open()
    {
        Assert.AreEqual("OPEN", FtpCommand.Open);
    }

    [TestMethod]
    public void FtpCommand_Bark()
    {
        Assert.AreEqual("ARF", FtpCommand.Bark);
    }
}

[TestClass]
public class IrcCommandTests
{
    #region Constants

    [TestMethod]
    public void IrcCommand_AllConstants()
    {
        Assert.AreEqual("USER", IrcCommand.USER);
        Assert.AreEqual("NICK", IrcCommand.NICK);
        Assert.AreEqual("QUIT", IrcCommand.QUIT);
        Assert.AreEqual("PRIVMSG", IrcCommand.PRIVMSG);
        Assert.AreEqual("MOTD", IrcCommand.MOTD);
        Assert.AreEqual("JOIN", IrcCommand.JOIN);
        Assert.AreEqual("PART", IrcCommand.PART);
        Assert.AreEqual("TOPIC", IrcCommand.TOPIC);
        Assert.AreEqual("LIST", IrcCommand.LIST);
        Assert.AreEqual("NAMES", IrcCommand.NAMES);
        Assert.AreEqual("WHO", IrcCommand.WHO);
        Assert.AreEqual("WHOIS", IrcCommand.WHOIS);
        Assert.AreEqual("KICK", IrcCommand.KICK);
        Assert.AreEqual("MODE", IrcCommand.MODE);
        Assert.AreEqual("NOTICE", IrcCommand.NOTICE);
        Assert.AreEqual("AWAY", IrcCommand.AWAY);
        Assert.AreEqual("PING", IrcCommand.PING);
        Assert.AreEqual("PONG", IrcCommand.PONG);
        Assert.AreEqual("PASS", IrcCommand.PASS);
        Assert.AreEqual("ISON", IrcCommand.ISON);
        Assert.AreEqual("USERHOST", IrcCommand.USERHOST);
        Assert.AreEqual("INVITE", IrcCommand.INVITE);
    }

    #endregion

    #region Instance Properties

    [TestMethod]
    public void IrcCommand_InstanceProperties_Default()
    {
        var cmd = new IrcCommand();

        Assert.IsNull(cmd.Command);
        Assert.IsNotNull(cmd.Params);
        Assert.AreEqual(0, cmd.Params.Count);
        Assert.IsNull(cmd.Trailing);
    }

    [TestMethod]
    public void IrcCommand_InstanceProperties_SetAndGet()
    {
        var cmd = new IrcCommand
        {
            Command = "PRIVMSG",
            Trailing = "Hello, World!"
        };
        cmd.Params.Add("#channel");

        Assert.AreEqual("PRIVMSG", cmd.Command);
        Assert.AreEqual(1, cmd.Params.Count);
        Assert.AreEqual("#channel", cmd.Params[0]);
        Assert.AreEqual("Hello, World!", cmd.Trailing);
    }

    [TestMethod]
    public void IrcCommand_MultipleParams()
    {
        var cmd = new IrcCommand
        {
            Command = "MODE"
        };
        cmd.Params.Add("#channel");
        cmd.Params.Add("+o");
        cmd.Params.Add("SomeUser");

        Assert.AreEqual(3, cmd.Params.Count);
        Assert.AreEqual("#channel", cmd.Params[0]);
        Assert.AreEqual("+o", cmd.Params[1]);
        Assert.AreEqual("SomeUser", cmd.Params[2]);
    }

    #endregion
}

[TestClass]
public class IrcUserTests
{
    [TestMethod]
    public void Fullname_FormatsCorrectly()
    {
        var user = new IrcUser(null!)
        {
            Nick = "Fox",
            Username = "fox",
            Hostname = "hive.com"
        };

        Assert.AreEqual("Fox!fox@hive.com", user.Fullname);
    }

    [TestMethod]
    public void DefaultProperties()
    {
        var user = new IrcUser(null!);

        Assert.IsNotNull(user.Channels);
        Assert.AreEqual(0, user.Channels.Count);
        Assert.IsNotNull(user.Modes);
        Assert.AreEqual(0, user.Modes.Count);
        Assert.IsFalse(user.IsAuthenticated);
        Assert.IsFalse(user.IsAway);
        Assert.AreEqual(string.Empty, user.AwayMessage);
        Assert.AreEqual(0, user.MessageCount);
        Assert.IsFalse(user.IsOperator);
    }

    [TestMethod]
    public void Channels_CaseInsensitive()
    {
        var user = new IrcUser(null!);

        user.Channels.Add("#Channel");

        Assert.IsTrue(user.Channels.Contains("#channel"));
        Assert.IsTrue(user.Channels.Contains("#CHANNEL"));
    }

    [TestMethod]
    public void Modes_TrackCorrectly()
    {
        var user = new IrcUser(null!);

        user.Modes.Add('o');
        user.Modes.Add('i');

        Assert.IsTrue(user.Modes.Contains('o'));
        Assert.IsTrue(user.Modes.Contains('i'));
        Assert.AreEqual(2, user.Modes.Count);
    }
}
