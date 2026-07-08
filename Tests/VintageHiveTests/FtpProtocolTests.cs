// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// FTP wire-protocol tests grounded in RFC 959 (with RFC 2389 for FEAT). The FTP path is a
// conversational proxy, so these cover the pure protocol pieces: reply codes, the reply/command
// wire format, control-line parsing, and TYPE handling.

using VintageHive.Proxy.Ftp;

namespace Ftp;

[TestClass]
public class FtpResponseCodeConformanceTests
{
    // RFC 959 4.2.2 assigns these reply codes. Pin them so a renumber is caught.
    [TestMethod] public void Code_150_FileStatusOkay() => Assert.AreEqual(150, (int)FtpResponseCode.FileStatusOkay);
    [TestMethod] public void Code_200_CommandOkay() => Assert.AreEqual(200, (int)FtpResponseCode.CommandSuccess);
    [TestMethod] public void Code_215_NameSystemType() => Assert.AreEqual(215, (int)FtpResponseCode.NameSystemType);
    [TestMethod] public void Code_220_ServiceReady() => Assert.AreEqual(220, (int)FtpResponseCode.ServiceReadyForNewUser);
    [TestMethod] public void Code_221_ClosingControl() => Assert.AreEqual(221, (int)FtpResponseCode.ServerClosingControlConnection);
    [TestMethod] public void Code_226_ClosingDataConnection() => Assert.AreEqual(226, (int)FtpResponseCode.DataConnectionClosing);
    [TestMethod] public void Code_227_PassiveMode() => Assert.AreEqual(227, (int)FtpResponseCode.EnteringPassiveMode);
    [TestMethod] public void Code_230_UserLoggedIn() => Assert.AreEqual(230, (int)FtpResponseCode.UserLoggedIn);
    [TestMethod] public void Code_250_FileActionOkay() => Assert.AreEqual(250, (int)FtpResponseCode.RequestedFileActionOkay);
    [TestMethod] public void Code_257_PathnameCreated() => Assert.AreEqual(257, (int)FtpResponseCode.PathnameCreated);
    [TestMethod] public void Code_331_NeedPassword() => Assert.AreEqual(331, (int)FtpResponseCode.UsernameOkay);
    [TestMethod] public void Code_350_PendingFurtherInfo() => Assert.AreEqual(350, (int)FtpResponseCode.RequestedFileActionPendingMore);
    [TestMethod] public void Code_501_SyntaxError() => Assert.AreEqual(501, (int)FtpResponseCode.SyntaxError);
    [TestMethod] public void Code_502_CommandNotImplemented() => Assert.AreEqual(502, (int)FtpResponseCode.CommandNotImplemented);
    [TestMethod] public void Code_503_BadSequence() => Assert.AreEqual(503, (int)FtpResponseCode.BadSequenceOfCommands);
    [TestMethod] public void Code_550_ActionNotTaken() => Assert.AreEqual(550, (int)FtpResponseCode.RequestedActionNotTaken);

    [TestMethod]
    public void Code_430_AuthFailure_IsAnExtension_NotRfc959()
    {
        // Documented deviation: RFC 959 uses 530 "Not logged in" for auth failure; VintageHive uses
        // 430 "Invalid username or password" (a later, widely-used code). Pin it so it doesn't drift.
        Assert.AreEqual(430, (int)FtpResponseCode.InvalidUsernameOrPassword);
    }
}

[TestClass]
public class FtpTransferTypeTests
{
    // RFC 959 3.1.1: TYPE A = ASCII, TYPE I = Image (binary).
    [TestMethod] public void Ascii() => Assert.AreEqual("ASCII", FtpTransferType.NameFromType("A"));
    [TestMethod] public void Image() => Assert.AreEqual("BINARY", FtpTransferType.NameFromType("I"));
    [TestMethod] public void AsciiConstant() => Assert.AreEqual("ASCII", FtpTransferType.NameFromType(FtpTransferType.ASCII));
    [TestMethod] public void Unknown() => Assert.AreEqual("N/A", FtpTransferType.NameFromType("X"));
}

[TestClass]
public class FtpCommandLineParseTests
{
    [TestMethod]
    public void Parse_CommandAndArgument()
    {
        var (command, args) = FtpRequest.ParseCommandLine("USER fox");
        Assert.AreEqual("USER", command);
        Assert.AreEqual("fox", args);
    }

    [TestMethod]
    public void Parse_CommandIsUpperCased()
    {
        // RFC 959 4.1: commands are case-insensitive.
        Assert.AreEqual("USER", FtpRequest.ParseCommandLine("user fox").Item1);
        Assert.AreEqual("RETR", FtpRequest.ParseCommandLine("retr file.txt").Item1);
    }

    [TestMethod]
    public void Parse_NoArgument()
    {
        var (command, args) = FtpRequest.ParseCommandLine("SYST");
        Assert.AreEqual("SYST", command);
        Assert.AreEqual("", args);
    }

    [TestMethod]
    public void Parse_ArgumentWithSpaces_Rejoined()
    {
        var (command, args) = FtpRequest.ParseCommandLine("RETR my long file.txt");
        Assert.AreEqual("RETR", command);
        Assert.AreEqual("my long file.txt", args);
    }

    [TestMethod]
    public void Parse_Pwd()
    {
        Assert.AreEqual("PWD", FtpRequest.ParseCommandLine("PWD").Item1);
    }
}

[TestClass]
public class FtpResponseFormatTests
{
    [TestMethod]
    public void Format_SingleLine_CodeSpaceTextCrLf()
    {
        // RFC 959 4.2: "<code><SP><text><CRLF>"
        Assert.AreEqual("220 Ready\r\n", FtpRequest.FormatResponse(FtpResponseCode.ServiceReadyForNewUser, "Ready"));
        Assert.AreEqual("550 Nope\r\n", FtpRequest.FormatResponse(FtpResponseCode.RequestedActionNotTaken, "Nope"));
    }

    [TestMethod]
    public void Format_FeatureList_MultiLine()
    {
        // RFC 2389: "<code>-Features", indented features, "<code> End"
        var result = FtpRequest.FormatFeatureList(FtpResponseCode.SystemStatus, new[] { "SIZE", "UTF8" });

        Assert.AreEqual("211-Features\r\n SIZE\r\n UTF8\r\n211 End\r\n", result);
    }
}
