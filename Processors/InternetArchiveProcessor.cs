// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using HtmlAgilityPack;
using System.Text.RegularExpressions;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors;

internal static class InternetArchiveProcessor
{
    public static readonly int[] ValidYears = Enumerable.Range(1997, 25).ToArray();

    // const string InternetArchiveAvailabilityApiUri = "https://archive.org/wayback/available?timestamp={1}&url={0}";

    const string WaybackCDXUrl = "http://web.archive.org/cdx/search/cdx?url={0}&from={1}&to={1}&fl=original,timestamp,mimetype,length";

    const string WorkerCDXUrl = "{0}/cdx/search/cdx?url={1}&from={2}&to={2}&fl=original,timestamp,mimetype,length";

    const string FullRewritePattern = "http://web.archive.org/web/";

    const string InternetArchivePublicUrl = "http://web.archive.org";

    const string InternetArchiveDataServer = "web.archive.org";

    const string IncludesPattern = "//archive.org/includes";

    const string StaticsPattern = "/_static/";

    const string AnalyticsPattern = "archive_analytics";

    const string WombatPattern = "_wm.wombat(";

    const string WombatJsPattern = "var _____WB$wombat$assign$function_____ = function(name)";

    const string CommentPattern = "<!-- End Wayback Rewrite JS Include -->";

    const string RewritePattern = "/web/";

    const string BlockCommentRegex = @"/\*(.|\n)*?\*/";

    const string SignatureRegex = @"\d{14}";

    const string ASCIIEncoding = "ISO-8859-1";

    const int InternetArchiveDataUrlLength = 41;

    static readonly TimeSpan CacheTtl = TimeSpan.FromDays(365);

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        var isInternetArchiveEnabled = Mind.Db.ConfigGet<bool>(ConfigNames.ServiceInternetArchive);

        if (!isInternetArchiveEnabled)
        {
            return false;
        }

        // TODO:
        // -- Pull in request details to determine content "relavence"
        // -- Detect stupid shit redirection services and fucking make it better
        // -- Allow the user to add their own redirection shitstuff <-- store in ConfigDB?

        // We don't search Internet Archive for single named domains...
        if (!req.Uri.Host.Contains('.'))
        {
            return false;
        }

        var urlStr = req.Uri.ToString();

        // This should cut out the IA static files, meant for modern browsers. ^.^;;;
        if (urlStr.Contains(StaticsPattern))
        {
            res.SetBodyString(string.Empty, "text/plain");

            return true;
        }

        var useWorker = Mind.Db.ConfigGet<bool>(ConfigNames.ServiceInternetArchiveWorker);
        var workerUrl = Mind.Db.ConfigGet<string>(ConfigNames.ServiceInternetArchiveWorkerUrl);

        if (useWorker && !string.IsNullOrWhiteSpace(workerUrl))
        {
            return await ProcessViaWorker(req, res, workerUrl.TrimEnd('/'));
        }

        return await ProcessDirect(req, res);
    }

    static async Task<bool> ProcessViaWorker(HttpRequest req, HttpResponse res, string workerUrl)
    {
        // check to see if getting data from archive or looking in the archive for a result
        var iaUrl = req.Uri.Host.Contains(InternetArchiveDataServer) ? req.Uri : await GetAvailability(req, res, workerUrl);

        if (iaUrl == null || iaUrl.ToString().Length <= InternetArchiveDataUrlLength)
        {
            return false;
        }

        res.Cache = false;

        // Rewrite the archive.org URL to go through the worker
        var workerFetchUrl = RewriteToWorker(iaUrl, workerUrl);

        // Use the worker URL as cache key so direct/worker caches don't collide
        var cachedResponse = Mind.Cache.GetWayback(workerFetchUrl);

        string contentType;

        byte[] contentData;

        if (cachedResponse == null)
        {
            var httpClient = HttpClientUtils.GetHttpClient(req);

            var workerResponse = await httpClient.GetAsync(workerFetchUrl);

            httpClient.Dispose();

            if (workerResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            contentType = workerResponse.Content.Headers.ContentType?.ToString() ?? "text/html";

            // Worker already scrubs HTML - just read the bytes directly
            contentData = await workerResponse.Content.ReadAsByteArrayAsync();

            if (contentData == null || contentData.Length == 0)
            {
                return false;
            }

            var cachedData = new ContentCachedData { ContentType = contentType, ContentDataBase64 = Convert.ToBase64String(contentData) };

            var jsonData = JsonSerializer.Serialize<ContentCachedData>(cachedData);

            Mind.Cache.SetWayback(workerFetchUrl, CacheTtl, jsonData);
        }
        else
        {
            var cachedData = JsonSerializer.Deserialize<ContentCachedData>(cachedResponse);

            contentType = cachedData.ContentType;
            contentData = Convert.FromBase64String(cachedData.ContentDataBase64);
        }

        if (contentData == null)
        {
            return false;
        }

        if (contentType.Contains("utf8"))
        {
            res.SetEncoding(Encoding.UTF8);
        }
        else
        {
            res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding));
        }

        res.SetBodyData(contentData, contentType);

        return true;
    }

    static async Task<bool> ProcessDirect(HttpRequest req, HttpResponse res)
    {
        // check to see if getting data from archive or looking in the archive for a result
        var iaUrl = req.Uri.Host.Contains(InternetArchiveDataServer) ? req.Uri : await GetAvailability(req, res);

        if (iaUrl == null || iaUrl.ToString().Length <= InternetArchiveDataUrlLength)
        {
            return false;
        }

        res.Cache = false;

        var cachedResponse = Mind.Cache.GetWayback(iaUrl);

        string contentType;

        byte[] contentData;

        if (cachedResponse == null)
        {
            var httpClient = HttpClientUtils.GetHttpClient(req);

            var iaResponse = await httpClient.GetAsync(iaUrl);

            httpClient.Dispose();

            if (iaResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            contentType = iaResponse.Content.Headers.ContentType?.ToString() ?? "text/html";

            if (contentType.StartsWith("text/html"))
            {
                var iaHtmlData = await iaResponse.Content.ReadAsStringAsync();

                if (!string.IsNullOrWhiteSpace(iaHtmlData))
                {
                    iaHtmlData = ScrubHtml(iaUrl, iaHtmlData);

                    if (iaHtmlData.Contains("https"))
                    {
                        // Debugger.Break();
                    }

                    contentData = Encoding.ASCII.GetBytes(iaHtmlData);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                contentData = await iaResponse.Content.ReadAsByteArrayAsync();
            }

            var cachedData = new ContentCachedData { ContentType = contentType, ContentDataBase64 = Convert.ToBase64String(contentData) };

            var jsonData = JsonSerializer.Serialize<ContentCachedData>(cachedData);

            Mind.Cache.SetWayback(iaUrl, CacheTtl, jsonData);
        }
        else
        {
            var cachedData = JsonSerializer.Deserialize<ContentCachedData>(cachedResponse);

            contentType = cachedData.ContentType;
            contentData = Convert.FromBase64String(cachedData.ContentDataBase64);
        }

        if (contentData == null)
        {
            return false;
        }

        if (contentType.Contains("utf8"))
        {
            res.SetEncoding(Encoding.UTF8);
        }
        else
        {
            res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding));
        }

        res.SetBodyData(contentData, contentType);

        return true;
    }

    internal static Uri RewriteToWorker(Uri iaUrl, string workerUrl)
    {
        // iaUrl is like: http://web.archive.org/web/19990125if_/http://www.example.com/
        // We need:        https://worker.example.com/web/19990125if_/http://www.example.com/
        var path = iaUrl.PathAndQuery;

        return new Uri(workerUrl + path);
    }

    // Maps a capture's MIME type to the Wayback Machine rewrite-suffix that requests the raw,
    // un-rewritten original: "if_" for HTML/other, "im_" for images, "oe_" for media/archives.
    internal static string GetArchiveTypeCode(string mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
        {
            return "if_";
        }

        if (mimeType.StartsWith("image"))
        {
            return "im_";
        }

        if (mimeType.StartsWith("audio") || mimeType.StartsWith("video"))
        {
            return "oe_";
        }

        if (mimeType.Contains("application/x-stuffit") || mimeType.Contains("application/zip"))
        {
            return "oe_";
        }

        return "if_";
    }

    static Task<Uri> GetAvailability(HttpRequest req, HttpResponse res, string workerUrl = null)
    {
        var internetArchiveYear = Mind.Db.ConfigLocalGet<int>(req.ListenerSocket.RemoteIP, ConfigNames.ServiceInternetArchiveYear);

        var incomingUrl = req.Uri.ToString();

        var incomingUrlEncoded = Uri.EscapeDataString(incomingUrl);

        string availabilityUri;

        if (!string.IsNullOrWhiteSpace(workerUrl))
        {
            availabilityUri = string.Format(WorkerCDXUrl, workerUrl, incomingUrlEncoded, internetArchiveYear);
        }
        else
        {
            availabilityUri = string.Format(WaybackCDXUrl, incomingUrlEncoded, internetArchiveYear);
        }

        return GetAvailabilityInternal(req, res, incomingUrl, internetArchiveYear, availabilityUri);
    }

    static async Task<Uri> GetAvailabilityInternal(HttpRequest req, HttpResponse res, string incomingUrl, int internetArchiveYear, string availabilityUri)
    {

        var result = Mind.Cache.GetWaybackAvailability(incomingUrl, internetArchiveYear);

        if (result == null)
        {
            using var httpClient = HttpClientUtils.GetHttpClient(req);

            var availabilityResponseRaw = await httpClient.GetStringAsync(availabilityUri);

            if (!string.IsNullOrWhiteSpace(availabilityResponseRaw))
            {
                var availabilityResponse = ProcessCDX(availabilityResponseRaw);

                if (availabilityResponse == null || !availabilityResponse.Any())
                {
                    res.ErrorMessage = $"No Internet Archive availability data found for this URL.</p><p><b>{availabilityUri}</b></p><p>[CacheMiss]";
                }
                else
                {
                    var largestCapture = availabilityResponse.OrderByDescending(x => x.Length).First();

                    var iaUrl = largestCapture.Url;
                    var timestamp = largestCapture.Timestamp;
                    var mimeType = largestCapture.MimeType;

                    var iaType = GetArchiveTypeCode(mimeType);

                    result = $"{FullRewritePattern}{timestamp}{iaType}/{iaUrl}";
                }

                Mind.Cache.SetWaybackAvailability(incomingUrl, internetArchiveYear, result, availabilityResponseRaw);
            }
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            if (string.IsNullOrWhiteSpace(res.ErrorMessage))
            {
                res.ErrorMessage = $"No Internet Archive availability data found for this URL.</p><p><b>{availabilityUri}</b></p><p>[CacheHit]";
            }

            return null;
        }

        return new Uri(result);
    }

    internal static List<WaybackCDXResult> ProcessCDX(string availabilityResponseRaw)
    {
        var response = new List<WaybackCDXResult>();

        var lines = availabilityResponseRaw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 4)
            {
                var result = new WaybackCDXResult
                {
                    Url = parts[0],
                    Timestamp = parts[1],
                    MimeType = parts[2],
                    Length = int.Parse(parts[3])
                };

                response.Add(result);
            }
        }

        return response;
    }

    static string ScrubHtml(Uri iaUrl, string html)
    {
        var doc = new HtmlDocument();

        doc.LoadHtml(html);

        var iaSignature = StripInternetArchiveTags(doc);

        AlterInternetArchiveBaseTag(doc);
        AlterInternetArchiveMetaTags(doc);
        AlterInternetArchiveImgBasedTags(doc);
        AlterInternetArchiveAnchorTags(doc);
        AlterInternetArchiveAreaTags(doc);
        AlterInternetArchiveScriptTags(doc);
        AlterInternetArchiveFrameTags(doc);
        AlterInternetArchiveEmbedTags(doc);

        var output = doc.DocumentNode.OuterHtml;

        return output;
    }

    static string StripInternetArchiveTags(HtmlDocument doc)
    {
        var signature = string.Empty;

        var matches = Regex.Match(doc.DocumentNode.InnerHtml, SignatureRegex);

        signature = $"{FullRewritePattern}{matches.Value}/";

        doc.DocumentNode.InnerHtml = doc.DocumentNode.InnerHtml.Replace(signature, string.Empty);

        StripInternetArchiveScriptTags(doc);
        StripInternetArchiveCSSTags(doc);

        var commentBegins = doc.DocumentNode.InnerHtml.IndexOf(CommentPattern);

        if (commentBegins != -1)
        {
            doc.DocumentNode.InnerHtml = doc.DocumentNode.InnerHtml.Remove(commentBegins, CommentPattern.Length);
        }

        return signature;
    }

    private static void AlterInternetArchiveMetaTags(HtmlDocument doc)
    {
        var metaTags = doc.DocumentNode.SelectNodes("//meta[@http-equiv]");

        if (metaTags == null)
        {
            return;
        }

        foreach (var metaTag in metaTags)
        {
            if (metaTag.Attributes["http-equiv"].Value.ToLowerInvariant() == "refresh")
            {
                var refreshContent = metaTag.Attributes["content"].Value;

                if (refreshContent.Contains("http:"))
                {
                    var rContentParsed = refreshContent.Split("=", 2);

                    var newUrl = rContentParsed[1][(rContentParsed[1].IndexOf("/http") + 1)..];

                    metaTag.Attributes["content"].Value = rContentParsed[0] + "=" + newUrl;
                }
            }
        }
    }

    private static void AlterInternetArchiveEmbedTags(HtmlDocument doc)
    {
        var embedTags = doc.DocumentNode.SelectNodes("//embed");

        if (embedTags == null)
        {
            return;
        }

        foreach (var embedTag in embedTags)
        {
            if (embedTag.Attributes["src"] != null && embedTag.Attributes["src"].Value.StartsWith(RewritePattern))
            {
                embedTag.Attributes["src"].Value = InternetArchivePublicUrl + embedTag.Attributes["src"].Value;
            }
        }
    }

    static void AlterInternetArchiveBaseTag(HtmlDocument doc)
    {
        var baseTags = doc.DocumentNode.SelectNodes("//base");

        if (baseTags == null)
        {
            return;
        }

        foreach (var baseTag in baseTags)
        {
            if (baseTag.Attributes["href"] != null && (baseTag.Attributes["href"].Value.StartsWith(RewritePattern) || baseTag.Attributes["href"].Value.StartsWith(FullRewritePattern)))
            {
                var linkPoint = baseTag.Attributes["href"].Value.IndexOf("/http://");

                baseTag.Attributes["href"].Value = baseTag.Attributes["href"].Value[(linkPoint + 1)..];
            }
        }
    }

    static void AlterInternetArchiveFrameTags(HtmlDocument doc)
    {
        var frameTags = doc.DocumentNode.SelectNodes("//frame");

        if (frameTags == null)
        {
            return;
        }

        foreach (var frameTag in frameTags)
        {
            if (frameTag.Attributes["src"] != null && frameTag.Attributes["src"].Value.StartsWith(RewritePattern))
            {
                frameTag.Attributes["src"].Value = InternetArchivePublicUrl + frameTag.Attributes["src"].Value;
            }
        }
    }

    static void AlterInternetArchiveScriptTags(HtmlDocument doc)
    {
        var scriptTags = doc.DocumentNode.SelectNodes("//script");

        if (scriptTags == null)
        {
            return;
        }

        foreach (var scriptTag in scriptTags)
        {
            if (scriptTag.Attributes["src"] != null && scriptTag.Attributes["src"].Value.StartsWith(RewritePattern))
            {
                scriptTag.Attributes["src"].Value = InternetArchivePublicUrl + scriptTag.Attributes["src"].Value;
            }
        }
    }

    static void AlterInternetArchiveAnchorTags(HtmlDocument doc)
    {
        var anchorTags = doc.DocumentNode.SelectNodes("//a");

        if (anchorTags == null)
        {
            return;
        }

        foreach (var anchorTag in anchorTags)
        {
            if (anchorTag.Attributes["href"] != null)
            {
                anchorTag.Attributes["href"].Value = anchorTag.Attributes["href"].Value.Replace("/https://", "/http://");

                var linkPoint = -1;

                if (anchorTag.Attributes["href"].Value.StartsWith(RewritePattern))
                {
                    linkPoint = anchorTag.Attributes["href"].Value.IndexOf("/", RewritePattern.Length);
                }
                else if (anchorTag.Attributes["href"].Value.StartsWith(FullRewritePattern))
                {
                    linkPoint = anchorTag.Attributes["href"].Value.IndexOf("/", FullRewritePattern.Length);
                }

                if (linkPoint == -1)
                {
                    continue;
                }

                anchorTag.Attributes["href"].Value = anchorTag.Attributes["href"].Value[(linkPoint + 1)..];
            }
        }
    }

    static void AlterInternetArchiveAreaTags(HtmlDocument doc)
    {
        var areaTags = doc.DocumentNode.SelectNodes("//area");

        if (areaTags == null)
        {
            return;
        }

        foreach (var areaTag in areaTags)
        {
            if (areaTag.Attributes["href"] != null)
            {
                var linkPoint = -1;

                if (areaTag.Attributes["href"].Value.StartsWith(RewritePattern))
                {
                    linkPoint = areaTag.Attributes["href"].Value.IndexOf("/", RewritePattern.Length);
                }
                else if (areaTag.Attributes["href"].Value.StartsWith(FullRewritePattern))
                {
                    linkPoint = areaTag.Attributes["href"].Value.IndexOf("/", FullRewritePattern.Length);
                }

                if (linkPoint == -1)
                {
                    // Debugger.Break();
                }

                areaTag.Attributes["href"].Value = areaTag.Attributes["href"].Value[(linkPoint + 1)..];
            }
        }
    }

    static void AlterInternetArchiveImgBasedTags(HtmlDocument doc)
    {
        /* Fixes background image linking */
        var backgroundAttrTags = doc.DocumentNode.SelectNodes("//*[@background]");

        if (backgroundAttrTags != null)
        {
            foreach (var backgroundAttrTag in backgroundAttrTags)
            {
                if (backgroundAttrTag.Attributes["background"] != null && backgroundAttrTag.Attributes["background"].Value.StartsWith(RewritePattern))
                {
                    backgroundAttrTag.Attributes["background"].Value = InternetArchivePublicUrl + backgroundAttrTag.Attributes["background"].Value;
                }
            }
        }

        var imgTags = doc.DocumentNode.SelectNodes("//img");

        if (imgTags != null)
        {
            foreach (var imgTag in imgTags)
            {
                if (imgTag.Attributes["src"] != null && imgTag.Attributes["src"].Value.StartsWith(RewritePattern))
                {
                    imgTag.Attributes["src"].Value = InternetArchivePublicUrl + imgTag.Attributes["src"].Value;
                }
            }
        }
    }

    static void StripInternetArchiveScriptTags(HtmlDocument doc)
    {
        var scriptTags = doc.DocumentNode.SelectNodes("//script");

        if (scriptTags == null)
        {
            return;
        }

        foreach (var scriptTag in scriptTags)
        {
            if (scriptTag.Attributes["src"] != null)
            {
                if (scriptTag.Attributes["src"].Value.Contains(IncludesPattern) || scriptTag.Attributes["src"].Value.Contains(StaticsPattern))
                {
                    scriptTag.Remove();
                }
            }
            else if (scriptTag.InnerHtml.Contains(AnalyticsPattern) || scriptTag.InnerHtml.Contains(WombatPattern))
            {
                scriptTag.Remove();
            }
            else
            {
                continue;
            }
        }
    }

    static void StripInternetArchiveCSSTags(HtmlDocument doc)
    {
        var styleLinkTags = doc.DocumentNode.SelectNodes("//head/link");

        if (styleLinkTags == null)
        {
            return;
        }

        foreach (var styleLinkTag in styleLinkTags)
        {
            if (styleLinkTag.Attributes["href"] != null)
            {
                if (styleLinkTag.Attributes["href"].Value.Contains(StaticsPattern))
                {
                    styleLinkTag.Remove();
                }
                else
                {
                    continue;
                }
            }
            else
            {
                continue;
            }
        }
    }
}
