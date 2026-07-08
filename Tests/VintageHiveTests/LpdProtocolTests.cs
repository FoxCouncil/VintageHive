// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// LPD (RFC 1179) control-file parsing. The daemon/subcommand conversation is socket-driven, but the
// control-file format (RFC 1179 section 7 - one line per field, first char is the code) is pure.
// Format detection is covered separately in PrintFormatDetectorTests.

using System.Text;
using VintageHive.Proxy.Printer;

namespace Printer;

[TestClass]
public class LpdControlFileTests
{
    private static (string user, string job, string host, string src) Parse(string control)
    {
        string user = null!, job = null!, host = null!, src = null!;
        LpdProxy.ParseControlFile(Encoding.ASCII.GetBytes(control), ref user, ref job, ref host, ref src);
        return (user, job, host, src);
    }

    [TestMethod]
    public void Parse_AllFields()
    {
        // RFC 1179 7: H=host, P=user, J=job name, N=source file name.
        var (user, job, host, src) = Parse("Hprintserver\nPfox\nJMy Document\nNreport.ps\n");

        Assert.AreEqual("fox", user);
        Assert.AreEqual("My Document", job);
        Assert.AreEqual("printserver", host);
        Assert.AreEqual("report.ps", src);
    }

    [TestMethod]
    public void Parse_OnlyUser_LeavesOthersNull()
    {
        var (user, job, host, src) = Parse("Pfox\n");

        Assert.AreEqual("fox", user);
        Assert.IsNull(job);
        Assert.IsNull(host);
        Assert.IsNull(src);
    }

    [TestMethod]
    public void Parse_StripsTrailingCr()
    {
        // Real LPR clients send CRLF line endings.
        var (user, _, host, _) = Parse("Hmyhost\r\nPfox\r\n");

        Assert.AreEqual("myhost", host);
        Assert.AreEqual("fox", user);
    }

    [TestMethod]
    public void Parse_UnhandledCodes_AreIgnored()
    {
        // C (class), T (title), f (formatted-file) are consumed but not tracked.
        var (user, job, host, src) = Parse("Cclass\nTtitle\nfdfA001\n");

        Assert.IsNull(user);
        Assert.IsNull(job);
        Assert.IsNull(host);
        Assert.IsNull(src);
    }

    [TestMethod]
    public void Parse_RealisticLprJob()
    {
        // What an lpr client typically sends: H/P/J/N plus f/U data-file directives that we ignore.
        var control = "Hws01\nProot\nJresume.txt\nfdfA001ws01\nUdfA001ws01\nNresume.txt\n";

        var (user, job, host, src) = Parse(control);

        Assert.AreEqual("ws01", host);
        Assert.AreEqual("root", user);
        Assert.AreEqual("resume.txt", job);
        Assert.AreEqual("resume.txt", src);
    }

    [TestMethod]
    public void Parse_CodeWithNoValue_IsSkipped()
    {
        // A bare code (line length < 2) has no value and is skipped.
        var (user, _, host, _) = Parse("H\nPfox\n");

        Assert.IsNull(host);
        Assert.AreEqual("fox", user);
    }

    [TestMethod]
    public void Parse_Empty_LeavesEverythingNull()
    {
        var (user, job, host, src) = Parse("");

        Assert.IsNull(user);
        Assert.IsNull(job);
        Assert.IsNull(host);
        Assert.IsNull(src);
    }
}
