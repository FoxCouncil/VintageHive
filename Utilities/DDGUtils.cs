// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;

namespace VintageHive.Utilities;

internal static class DDGUtils
{
    // The legacy links.duckduckgo.com/d.js JSON endpoint now answers 202 + a JS anti-bot challenge instead of JSON,
    // even with a valid VQD. Results are instead scraped from the no-JS HTML endpoint - a plain form POST that still
    // returns server-rendered result links and needs no VQD token.
    const string HtmlUrl = "https://html.duckduckgo.com/html/";

    const string RefererUrl = "https://html.duckduckgo.com/";

    // A standard desktop Firefox User-Agent - carries no identifying information.
    const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; rv:102.0) Gecko/20100101 Firefox/102.0";

    static readonly Regex ResultAnchorRegex = new("<a[^>]*class=\"result__a\"[^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.Singleline);

    static readonly Regex SnippetRegex = new("<a[^>]*class=\"result__snippet\"[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.Singleline);

    static readonly Regex UddgRegex = new("[?&]uddg=([^&]+)", RegexOptions.Compiled);

    static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    internal static async Task<List<SearchResult>> Search(string keywords, uint page = 1, string region = "wt-wt", SafeSearchLevel safesearch = SafeSearchLevel.Off)
    {
        var output = new List<SearchResult>();

        if (string.IsNullOrWhiteSpace(keywords))
        {
            return output;
        }

        var offset = (int)((page - 1) * 20);

        using var client = new HttpClient();

        var request = new HttpRequestMessage(HttpMethod.Post, HtmlUrl);

        request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.Add("User-Agent", BrowserUserAgent);
        request.Headers.Add("Referer", RefererUrl);

        var formList = new List<KeyValuePair<string, string>>
        {
            new("q", keywords),
            new("kl", region),
            new("kp", ((int)safesearch).ToString())
        };

        if (offset > 0)
        {
            formList.Add(new("s", offset.ToString()));
        }

        request.Content = new FormUrlEncodedContent(formList);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request);
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(DDGUtils), ex, "");

            return output;
        }

        if (!response.IsSuccessStatusCode)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(DDGUtils), $"HTML endpoint returned {(int)response.StatusCode} for '{keywords}'", "");

            return output;
        }

        var html = await response.Content.ReadAsStringAsync();

        return ParseHtmlResults(html);
    }

    // Exposed for testing: the parse is a pure function of the endpoint's HTML.
    internal static List<SearchResult> ParseHtmlResults(string html)
    {
        var output = new List<SearchResult>();

        if (string.IsNullOrEmpty(html))
        {
            return output;
        }

        var anchors = ResultAnchorRegex.Matches(html);
        var snippets = SnippetRegex.Matches(html);

        for (var i = 0; i < anchors.Count; i++)
        {
            var title = CleanText(anchors[i].Groups[2].Value);
            var targetUrl = DecodeResultUrl(anchors[i].Groups[1].Value);

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(targetUrl) || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
            {
                continue;
            }

            var abstractText = i < snippets.Count ? CleanText(snippets[i].Groups[1].Value) : string.Empty;

            output.Add(new SearchResult
            {
                Title = title,
                Abstract = abstractText,
                Uri = uri,
                UriDisplay = (uri.Host + uri.AbsolutePath).TrimEnd('/')
            });
        }

        return output;
    }

    // html.duckduckgo.com wraps every result URL in a //duckduckgo.com/l/?uddg=<encoded>&rut=... redirect; unwrap it.
    private static string DecodeResultUrl(string href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return null;
        }

        var uddg = UddgRegex.Match(href);

        if (uddg.Success)
        {
            return WebUtility.UrlDecode(uddg.Groups[1].Value);
        }

        if (href.StartsWith("//"))
        {
            return "https:" + href;
        }

        return href;
    }

    // Strip the <b>…</b> highlight tags DDG wraps around matched terms and decode HTML entities to plain text.
    private static string CleanText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        return WebUtility.HtmlDecode(HtmlTagRegex.Replace(html, string.Empty)).Trim();
    }

    public enum SafeSearchLevel
    {
        Off = -2,
        Moderate = -1,
        On = 1
    }
}
