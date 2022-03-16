using HeyRed.Mime;
using HtmlAgilityPack;
using LibFoxyProxy.Http;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using VintageHive.Processors.InternetArchive;
using VintageHive.Utilities;

namespace VintageHive.Processors;

internal static class InternetArchiveProcessor
{
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

    const int InternetArchiveDataUrlLength = 41;

    public static async Task<bool> ProcessRequest(HttpRequest req, HttpResponse res)
    {
        // TODO:
        // -- Pull in request details to determine content "relavence"
        // -- Detect stupid shit redirection services and fucking make it better
        // -- Allow the user to add their own redirection shitstuff <-- store in ConfigDB?

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

        var mimeType = MimeTypesMap.GetMimeType(iaUrl.ToString());

        var dataType = iaUrl.ToString()[InternetArchiveDataUrlLength..];

        if (dataType == null)
        {
            return false;
        }

        dataType = dataType[..dataType.IndexOf('/')].ToLower();

        if (dataType == "")
        {
            Debugger.Break();
        }

        var httpClient = Clients.GetHttpClient(req);

        try
        {
            switch (dataType)
            {
                case "if_":
                case "fw_":
                case "":
                {
                    var iaHtmlData = await httpClient.GetStringAsync(iaUrl);

                    // Todo read incoming encoding
                    res.SetEncoding(Encoding.GetEncoding("ISO-8859-1"));

                    if (!string.IsNullOrWhiteSpace(iaHtmlData))
                    {
                        iaHtmlData = ScrubHtml(iaUrl, iaHtmlData);

                        res.SetBodyString(iaHtmlData.Trim(), mimeType ?? "text/html");

                        return true;
                    }
                }
                break;

                case "oe_":
                {
                    var iaOctetData = await httpClient.GetByteArrayAsync(iaUrl);

                    if (iaOctetData != null && iaOctetData.Length > 10)
                    {
                        var objectMimeType = "application/octet-stream";

                        if (mimeType.StartsWith("audio"))
                        {
                            objectMimeType = mimeType;
                        }

                        res.SetEncoding(Encoding.GetEncoding("ISO-8859-1")).SetBodyData(iaOctetData, objectMimeType);

                        return true;
                    }
                }
                break;

                case "im_":
                {
                    var iaImgData = await httpClient.GetByteArrayAsync(iaUrl);

                    if (iaImgData != null && iaImgData.Length > 10)
                    {
                        res.SetEncoding(Encoding.GetEncoding("ISO-8859-1")).SetBodyData(iaImgData, MimeTypesMap.GetMimeType(iaUrl.ToString()));

                        return true;
                    }
                }
                break;

                case "js_":
                {
                    var iaJsData = await httpClient.GetStringAsync(iaUrl);

                    if (iaJsData.StartsWith(WombatJsPattern))
                    {
                        res.SetBodyString(string.Empty, "text/javascript");

                        return true;
                    }

                    res.SetEncoding(Encoding.GetEncoding("ISO-8859-1")).SetBodyString(iaJsData, "text/javascript");

                    return true;
                }

                case "cs_":
                {
                    var iaCssData = await httpClient.GetStringAsync(iaUrl);

                    iaCssData = Regex.Replace(iaCssData, BlockCommentRegex, string.Empty);

                    res.SetEncoding(Encoding.GetEncoding("ISO-8859-1")).SetBodyString(iaCssData, "text/css");

                    return true;
                }
            }
        }
        catch (HttpRequestException) { }

        return false;
    }

    static async Task<Uri> GetAvailability(HttpRequest req)
    {
        var incomingUrl = req.Uri.ToString();

        var key = $"AREQ-{incomingUrl}";

        var result = Mind.Instance.CacheDb.Get<string>(key);

        if (result == null)
        {
            var internetArchiveYear = Mind.Instance.ConfigDb.SettingGet<int>("internetarchiveyear");

            var availabilityDate = $"{internetArchiveYear}{DateTime.UtcNow:MMdd}";

            var incomingUrlEncoded = HttpUtility.UrlEncode(incomingUrl);

            var availabilityUri = string.Format(InternetArchiveAvailabilityApiUri, incomingUrlEncoded, availabilityDate);

            var httpClient = Clients.GetHttpClient(req);

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

        StripInternetArchiveTags(doc);

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

    static void StripInternetArchiveTags(HtmlDocument doc)
    {
        StripInternetArchiveScriptTags(doc);
        StripInternetArchiveCSSTags(doc);

        var commentBegins = doc.DocumentNode.InnerHtml.IndexOf(CommentPattern);

        if (commentBegins != -1)
        {
            doc.DocumentNode.InnerHtml = doc.DocumentNode.InnerHtml.Remove(commentBegins, CommentPattern.Length);
        }
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

            }
        }

        Console.WriteLine(metaTags.Count);
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

                baseTag.Attributes["href"].Value = baseTag.Attributes["href"].Value.Substring(linkPoint + 1);
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
            if (anchorTag.Attributes["href"] != null && (anchorTag.Attributes["href"].Value.StartsWith(RewritePattern) || anchorTag.Attributes["href"].Value.StartsWith(FullRewritePattern)))
            {
                anchorTag.Attributes["href"].Value = anchorTag.Attributes["href"].Value.Replace("/https://", "/http://");

                var linkPoint = anchorTag.Attributes["href"].Value.IndexOf("/http://");

                anchorTag.Attributes["href"].Value = anchorTag.Attributes["href"].Value.Substring(linkPoint + 1);
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
            if (areaTag.Attributes["href"] != null && (areaTag.Attributes["href"].Value.StartsWith(RewritePattern) || areaTag.Attributes["href"].Value.StartsWith(FullRewritePattern)))
            {
                var linkPoint = areaTag.Attributes["href"].Value.IndexOf("/http://");

                areaTag.Attributes["href"].Value = areaTag.Attributes["href"].Value.Substring(linkPoint + 1);
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
