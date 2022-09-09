using HeyRed.Mime;
using HtmlAgilityPack;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VintageHive.Processors.InternetArchive;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;

namespace VintageHive.Processors;

internal static class InternetArchiveProcessor
{
    public static readonly int[] ValidYears = Enumerable.Range(1997, 25).ToArray();

    const string InternetArchiveAvailabilityApiUri = "https://archive.org/wayback/available?url={0}&timestamp={1}";

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

    public static async Task<bool> ProcessRequest(HttpRequest req, HttpResponse res)
    {
        var isInternetArchiveEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.InternetArchive);

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

        // check to see if getting data from archive or looking in the archive for a result
        var iaUrl = req.Uri.Host.Contains(InternetArchiveDataServer) ? req.Uri : await GetAvailability(req);

        if (iaUrl == null || iaUrl.ToString().Length <= InternetArchiveDataUrlLength)
        {
            return false;
        }
        
        res.Cache = false;

        var internetArchiveYear = Mind.Instance.ConfigDb.SettingGet<int>(ConfigNames.InternetArchiveYear);

        var cacheKey = $"PC-{internetArchiveYear}-{req.Uri}";

        var cachedResponse = Mind.Instance.CacheDb.Get<string>(cacheKey);

        var contentType = string.Empty;

        byte[] contentData = null;

        var isCached = false;

        if (cachedResponse == null)
        {
            var httpClient = Clients.GetHttpClient(req);

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

            if (req.Uri.Host.StartsWith(InternetArchiveDataServer))
            {
                res.Cache = true;
                res.CacheTtl = CacheTtl;
            }
            else
            {
                var cachedData = new InternetArchiveCachedData { ContentType = contentType, ContentDataBase64 = Convert.ToBase64String(contentData) };

                var jsonData = JsonSerializer.Serialize<InternetArchiveCachedData>(cachedData);

                Mind.Instance.CacheDb.Set<string>(cacheKey, CacheTtl, jsonData);
            }
        }
        else
        {
            isCached = true;
            
            var cachedData = JsonSerializer.Deserialize<InternetArchiveCachedData>(cachedResponse);

            contentType = cachedData.ContentType;
            contentData = Convert.FromBase64String(cachedData.ContentDataBase64);
        }

        if (contentData == null)
        {
            return false;
        }
        
        Display.WriteLog($"[{"InternetArchive",15} Request] |{(isCached ? "HIT" : "MISS"), 4}| \\{internetArchiveYear}\\ ({req.Uri}) [{contentType}]");

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

    static async Task<Uri> GetAvailability(HttpRequest req)
    {
        var incomingUrl = req.Uri.ToString();

        var internetArchiveYear = Mind.Instance.ConfigDb.SettingGet<int>(ConfigNames.InternetArchiveYear);

        var key = $"AREQ-{internetArchiveYear}-{incomingUrl}";

        var result = Mind.Instance.CacheDb.Get<string>(key);

        if (result == null)
        {
            // var availabilityDate = $"{internetArchiveYear}{DateTime.UtcNow:ddMM}";

            var availabilityDate = $"{internetArchiveYear}";

            var incomingUrlEncoded = Uri.EscapeDataString(incomingUrl);

            var availabilityUri = string.Format(InternetArchiveAvailabilityApiUri, incomingUrlEncoded, availabilityDate);

            using var httpClient = Clients.GetHttpClient(req);

            var availabilityResponseRaw = await httpClient.GetStringAsync(availabilityUri);

            if (!string.IsNullOrWhiteSpace(availabilityResponseRaw))
            {
                var availabilityResponse = JsonSerializer.Deserialize<InternetArchiveResponse>(availabilityResponseRaw);

                if (availabilityResponse == null || availabilityResponse.archived_snapshots.closest == null)
                {
                    result = string.Empty;
                }
                else
                {
                    // Format, format, format!!!!!!
                    var iaUrl = availabilityResponse.archived_snapshots.closest.url;
                    var timestamp = availabilityResponse.archived_snapshots.closest.timestamp;
                    var indexOfDate = iaUrl.IndexOf(timestamp) + timestamp.Length;
                    var mimeType = MimeTypesMap.GetMimeType(iaUrl);

                    Debug.Assert(indexOfDate == InternetArchiveDataUrlLength);

                    var iaType = "if_";

                    if (mimeType.StartsWith("image"))
                    {
                        iaType = "im_";
                    }
                    else if (mimeType.StartsWith("audio") || mimeType.StartsWith("video"))
                    {
                        iaType = "oe_";
                    }
                    else if (mimeType.Contains("application/x-stuffit") || mimeType.Contains("application/zip"))
                    {
                        iaType = "oe_";
                    }

                    result = availabilityResponse.archived_snapshots.closest.url.Insert(InternetArchiveDataUrlLength, iaType);
                }

                Mind.Instance.CacheDb.Set<string>(key, TimeSpan.FromDays(1000), result);
            }
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        return new Uri(result);
    }

    static string ScrubHtml(Uri iaUrl, string html)
    {
        var doc = new HtmlDocument();

        doc.LoadHtml(html);

        var iaSignature = StripInternetArchiveTags(doc);

        AlterInternetArchiveBaseTag(doc);
        AlterInternetArchiveMetaTags(doc);
        AlterInternetArchiveImgTags(doc);
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

                    var newUrl = rContentParsed[1][(rContentParsed[1].IndexOf("/http")+1)..];

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

                anchorTag.Attributes["href"].Value = anchorTag.Attributes["href"].Value[(linkPoint+1)..];
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

                areaTag.Attributes["href"].Value = areaTag.Attributes["href"].Value[(linkPoint+1)..];
            }
        }
    }

    static void AlterInternetArchiveImgTags(HtmlDocument doc)
    {
        var imgTags = doc.DocumentNode.SelectNodes("//img");

        if (imgTags == null)
        {
            return;
        }

        foreach (var imgTag in imgTags)
        {
            if (imgTag.Attributes["src"] != null && imgTag.Attributes["src"].Value.StartsWith(RewritePattern))
            {
                imgTag.Attributes["src"].Value = InternetArchivePublicUrl + imgTag.Attributes["src"].Value;
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
