// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Utilities;

namespace Utilities;

[TestClass]
public class DDGTests
{
    // A trimmed-down shape of two organic results from html.duckduckgo.com/html/, including the /l/?uddg= redirect
    // wrapper, <b> highlight tags, and HTML entities that the parser must unwrap/decode.
    private const string SampleHtml =
        "<div class=\"result results_links results_links_deep web-result\"><div class=\"links_main\">" +
        "<h2 class=\"result__title\"><a rel=\"nofollow\" class=\"result__a\" href=\"//duckduckgo.com/l/?uddg=https%3A%2F%2Fen.wikipedia.org%2Fwiki%2FMain_Page&amp;rut=aa\">Wikipedia, the free <b>encyclopedia</b></a></h2>" +
        "<a class=\"result__snippet\" href=\"z\">Wikipedia is a free <b>encyclopedia</b> &amp; more.</a>" +
        "<div class=\"result__url\">en.wikipedia.org</div></div></div>" +
        "<div class=\"result results_links results_links_deep web-result\"><div class=\"links_main\">" +
        "<h2 class=\"result__title\"><a rel=\"nofollow\" class=\"result__a\" href=\"//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2F&amp;rut=bb\">Example Domain</a></h2>" +
        "<a class=\"result__snippet\" href=\"z\">Illustrative example.</a>" +
        "<div class=\"result__url\">example.com</div></div></div>";

    [TestMethod]
    public void ParseHtmlResults_UnwrapsRedirect_StripsTags_DecodesEntities()
    {
        var results = DDGUtils.ParseHtmlResults(SampleHtml);

        Assert.AreEqual(2, results.Count);

        Assert.AreEqual("Wikipedia, the free encyclopedia", results[0].Title);
        Assert.AreEqual("https://en.wikipedia.org/wiki/Main_Page", results[0].Uri.ToString());
        Assert.AreEqual("Wikipedia is a free encyclopedia & more.", results[0].Abstract);

        Assert.AreEqual("Example Domain", results[1].Title);
        Assert.AreEqual("https://example.com/", results[1].Uri.ToString());
        Assert.AreEqual("Illustrative example.", results[1].Abstract);
    }

    [TestMethod]
    public void ParseHtmlResults_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.AreEqual(0, DDGUtils.ParseHtmlResults(null).Count);
        Assert.AreEqual(0, DDGUtils.ParseHtmlResults("").Count);
        Assert.AreEqual(0, DDGUtils.ParseHtmlResults("<html><body>no results here</body></html>").Count);
    }
}
