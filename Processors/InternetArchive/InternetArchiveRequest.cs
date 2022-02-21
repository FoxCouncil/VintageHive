using HeyRed.Mime;
using HtmlAgilityPack;
using LibFoxyProxy.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using VintageHive.Utilities;

namespace VintageHive.Processors.InternetArchive
{
    internal class InternetArchiveRequest
    {
        const int InternetArchiveDataUrlLength = 41;

        const string InternetArchiveAvailabilityApiUri = "https://archive.org/wayback/available?url={0}&timestamp={1}";

        const string InternetArchiveDataServer = "web.archive.org";

        const string IncludesPattern = "//archive.org/includes";

        const string StaticsPattern = "/_static/";

        const string AnalyticsPattern = "archive_analytics";

        const string WombatPattern = "_wm.wombat(";

        const string CommentPattern = "<!-- End Wayback Rewrite JS Include -->";

        const string RewritePattern = "/web/";

        const string FullRewritePattern = "http://web.archive.org/web/";

        const string InternetArchivePublicUrl = "http://web.archive.org";

        readonly HttpRequest request;

        string availabilityDate;

        string availabilityUri;

        string dataUri;

        InternetArchiveResponse availabilityResponse;

        public HttpResponse Response { get; private set; }

        public InternetArchiveRequest(HttpRequest req)
        {
            if (req == null || req.Uri == null)
            {
                return;
            }

            request = req;

            if (!request.Uri.Host.Contains(InternetArchiveDataServer))
            {
                ProcessCachedAvailabilityRequest();
            }
            else
            {
                dataUri = request.Uri.ToString();
            }

            if (dataUri != null)
            {
                ProcessCachedDataRequest();
            }
        }

        private void ProcessCachedDataRequest()
        {
            var dataType = dataUri[InternetArchiveDataUrlLength..];

            dataType = dataType[..dataType.IndexOf('/')].ToLower();

            switch (dataType)
            {
                case "if_":
                case "fw_":
                {
                    var iaHtmlData = Clients.HttpGetStringFromUrl(dataUri, request: request);

                    Response = new HttpResponse(request).SetEncoding(Encoding.GetEncoding("ISO-8859-1"));

                    if (!string.IsNullOrWhiteSpace(iaHtmlData))
                    {
                        iaHtmlData = ScrubHtml(iaHtmlData);

                        Response.SetBodyString(iaHtmlData);
                    }
                }
                break;

                case "im_":
                {
                    var iaImgData = Clients.HttpGetDataFromUrl(dataUri, request: request);

                    if (iaImgData != null && iaImgData.Length > 10)
                    {
                        Response = new HttpResponse(request).SetEncoding(Encoding.GetEncoding("ISO-8859-1")).SetBodyData(iaImgData, MimeTypesMap.GetMimeType(dataUri));
                    }
                }
                break;
            }
        }

        private void ProcessCachedAvailabilityRequest()
        {
            availabilityDate = $"{Mind.Config.InternetArchiveYear}{DateTime.UtcNow:MMdd}";

            var incomingUrl = request.Uri.ToString();

            var incomingUrlEncoded = HttpUtility.UrlEncode(incomingUrl);

            availabilityUri = string.Format(InternetArchiveAvailabilityApiUri, incomingUrlEncoded, availabilityDate);

            availabilityResponse = Clients.HttpGetJsonClassFromUrl<InternetArchiveResponse>(availabilityUri);

            if (availabilityResponse == null || availabilityResponse.archived_snapshots.closest == null)
            {
                return;
            }

            dataUri = availabilityResponse.archived_snapshots.closest.url.Insert(41, "if_"); // TODO: Terrible, fix it later...;            
        }

        public string ScrubHtml(string html)
        {
            var doc = new HtmlDocument();

            doc.LoadHtml(html);

            StripInternetArchiveTags(doc);

            var output = doc.DocumentNode.OuterHtml;

            return output;
        }

        private void StripInternetArchiveTags(HtmlDocument doc)
        {
            StripInternetArchiveScriptTags(doc);
            StripInternetArchiveCSSTags(doc);

            var commentBegins = doc.DocumentNode.InnerHtml.IndexOf(CommentPattern);

            if (commentBegins != -1)
            {
                doc.DocumentNode.InnerHtml = doc.DocumentNode.InnerHtml.Remove(commentBegins, CommentPattern.Length);
            }

            AlterInternetArchiveBaseTag(doc);
            AlterInternetArchiveImgTags(doc);
            AlterInternetArchiveAnchorTags(doc);
            AlterInternetArchiveAreaTags(doc);
            AlterInternetArchiveScriptTags(doc);
        }

        private void AlterInternetArchiveBaseTag(HtmlDocument doc)
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

        private void AlterInternetArchiveScriptTags(HtmlDocument doc)
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

        private void AlterInternetArchiveAnchorTags(HtmlDocument doc)
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
                    var linkPoint = anchorTag.Attributes["href"].Value.IndexOf("/http://");

                    anchorTag.Attributes["href"].Value = anchorTag.Attributes["href"].Value.Substring(linkPoint + 1);
                }
            }
        }

        private void AlterInternetArchiveAreaTags(HtmlDocument doc)
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

        private void AlterInternetArchiveImgTags(HtmlDocument doc)
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

        private void StripInternetArchiveScriptTags(HtmlDocument doc)
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

        private void StripInternetArchiveCSSTags(HtmlDocument doc)
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
}
