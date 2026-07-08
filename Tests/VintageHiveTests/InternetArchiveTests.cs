// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Data.Types;
using VintageHive.Processors;

namespace InternetArchive;

[TestClass]
public class InternetArchiveRewriteToWorkerTests
{
    [TestMethod]
    public void RewriteToWorker_HtmlCapture_PrependsWorkerHost()
    {
        var iaUrl = new Uri("http://web.archive.org/web/19990125if_/http://www.example.com/");

        var result = InternetArchiveProcessor.RewriteToWorker(iaUrl, "https://worker.example.dev");

        Assert.AreEqual("https://worker.example.dev/web/19990125if_/http://www.example.com/", result.ToString());
    }

    [TestMethod]
    public void RewriteToWorker_PreservesQueryString()
    {
        var iaUrl = new Uri("http://web.archive.org/web/20011003if_/http://www.example.com/page?id=42&x=1");

        var result = InternetArchiveProcessor.RewriteToWorker(iaUrl, "https://w.dev");

        Assert.AreEqual("https://w.dev/web/20011003if_/http://www.example.com/page?id=42&x=1", result.ToString());
    }

    [TestMethod]
    public void RewriteToWorker_ImageCapture_KeepsTypeSuffix()
    {
        var iaUrl = new Uri("http://web.archive.org/web/19990125im_/http://www.example.com/logo.gif");

        var result = InternetArchiveProcessor.RewriteToWorker(iaUrl, "https://w.dev");

        Assert.AreEqual("https://w.dev/web/19990125im_/http://www.example.com/logo.gif", result.ToString());
    }
}

[TestClass]
public class InternetArchiveTypeCodeTests
{
    [TestMethod]
    public void GetArchiveTypeCode_Null_ReturnsIfUnderscore()
    {
        Assert.AreEqual("if_", InternetArchiveProcessor.GetArchiveTypeCode(null!));
    }

    [TestMethod]
    public void GetArchiveTypeCode_Empty_ReturnsIfUnderscore()
    {
        Assert.AreEqual("if_", InternetArchiveProcessor.GetArchiveTypeCode(""));
    }

    [TestMethod]
    public void GetArchiveTypeCode_Html_ReturnsIfUnderscore()
    {
        Assert.AreEqual("if_", InternetArchiveProcessor.GetArchiveTypeCode("text/html"));
    }

    [TestMethod]
    public void GetArchiveTypeCode_ImageGif_ReturnsImUnderscore()
    {
        Assert.AreEqual("im_", InternetArchiveProcessor.GetArchiveTypeCode("image/gif"));
    }

    [TestMethod]
    public void GetArchiveTypeCode_ImageJpeg_ReturnsImUnderscore()
    {
        Assert.AreEqual("im_", InternetArchiveProcessor.GetArchiveTypeCode("image/jpeg"));
    }

    [TestMethod]
    public void GetArchiveTypeCode_Audio_ReturnsOeUnderscore()
    {
        Assert.AreEqual("oe_", InternetArchiveProcessor.GetArchiveTypeCode("audio/mpeg"));
    }

    [TestMethod]
    public void GetArchiveTypeCode_Video_ReturnsOeUnderscore()
    {
        Assert.AreEqual("oe_", InternetArchiveProcessor.GetArchiveTypeCode("video/mp4"));
    }

    [TestMethod]
    public void GetArchiveTypeCode_Zip_ReturnsOeUnderscore()
    {
        Assert.AreEqual("oe_", InternetArchiveProcessor.GetArchiveTypeCode("application/zip"));
    }

    [TestMethod]
    public void GetArchiveTypeCode_Stuffit_ReturnsOeUnderscore()
    {
        Assert.AreEqual("oe_", InternetArchiveProcessor.GetArchiveTypeCode("application/x-stuffit"));
    }

    [TestMethod]
    public void GetArchiveTypeCode_UnknownBinary_ReturnsIfUnderscore()
    {
        Assert.AreEqual("if_", InternetArchiveProcessor.GetArchiveTypeCode("application/octet-stream"));
    }
}

[TestClass]
public class InternetArchiveProcessCDXTests
{
    [TestMethod]
    public void ProcessCDX_SingleRow_ParsesAllFields()
    {
        var raw = "http://www.example.com/ 19990125000000 text/html 1234";

        var result = InternetArchiveProcessor.ProcessCDX(raw);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("http://www.example.com/", result[0].Url);
        Assert.AreEqual("19990125000000", result[0].Timestamp);
        Assert.AreEqual("text/html", result[0].MimeType);
        Assert.AreEqual(1234, result[0].Length);
    }

    [TestMethod]
    public void ProcessCDX_MultipleRows_ParsesEach()
    {
        var raw = "http://a.com/ 19990101000000 text/html 10\nhttp://b.com/ 19990102000000 image/gif 20";

        var result = InternetArchiveProcessor.ProcessCDX(raw);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("http://a.com/", result[0].Url);
        Assert.AreEqual(20, result[1].Length);
        Assert.AreEqual("image/gif", result[1].MimeType);
    }

    [TestMethod]
    public void ProcessCDX_SkipsMalformedRows()
    {
        // Only the second line has the required four space-separated columns.
        var raw = "not enough columns\nhttp://ok.com/ 19990101000000 text/html 55";

        var result = InternetArchiveProcessor.ProcessCDX(raw);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("http://ok.com/", result[0].Url);
    }

    [TestMethod]
    public void ProcessCDX_EmptyInput_ReturnsEmptyList()
    {
        var result = InternetArchiveProcessor.ProcessCDX("");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ProcessCDX_BlankLines_Ignored()
    {
        var raw = "\n\nhttp://c.com/ 19990101000000 text/plain 7\n\n";

        var result = InternetArchiveProcessor.ProcessCDX(raw);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(7, result[0].Length);
    }
}
