// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// ScrubHtml on worker output: the proven split, retargeted to the archive.foxcouncil.com host.
// Navigation flattens to the real origin; resources stay on the archive host at their exact timestamp.

using VintageHive.Processors;

namespace InternetArchiveScrub;

[TestClass]
public class ScrubHtmlSplitTests
{
    const string Nav = "https://archive.foxcouncil.com/web/19970404064352/http://www.apple.com/next.html";
    const string Img = "https://archive.foxcouncil.com/web/19970404064352im_/http://www.apple.com/logo.gif";

    static string Scrub(string body) => InternetArchiveProcessor.ScrubHtml(new Uri("http://www.apple.com/"), body);

    [TestMethod]
    public void Anchor_FlattensToRealOrigin()
    {
        var result = Scrub($"<html><body><a href=\"{Nav}\">n</a></body></html>");

        StringAssert.Contains(result, "http://www.apple.com/next.html");
        // the archive prefix must be gone from the link
        Assert.IsFalse(result.Contains(Nav), "anchor should not keep the archive.foxcouncil.com prefix");
    }

    [TestMethod]
    public void Image_StaysOnArchiveHost()
    {
        var result = Scrub($"<html><body><img src=\"{Img}\"></body></html>");

        // resource keeps the exact-timestamp archive URL so it loads the period-correct capture
        StringAssert.Contains(result, Img);
    }

    [TestMethod]
    public void MixedPage_LinksFlattenResourcesStay()
    {
        var result = Scrub($"<html><body><a href=\"{Nav}\">n</a><img src=\"{Img}\"></body></html>");

        Assert.IsFalse(result.Contains(Nav), "link should be flattened to the real origin");
        StringAssert.Contains(result, Img);
        StringAssert.Contains(result, "http://www.apple.com/next.html");
    }
}
