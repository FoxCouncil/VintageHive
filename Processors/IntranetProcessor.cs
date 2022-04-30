using HeyRed.Mime;
using HtmlAgilityPack;
using LibFoxyProxy.Http;
using System.Text;
using Vereyon.Web;
using VintageHive.Utilities;
using static LibFoxyProxy.Http.HttpUtilities;

namespace VintageHive.Processors;

internal static class IntranetProcessor
{
    const string HtmlOkayGoBack = "<hr><a href=\"http://hive/\">&lt; Okay</a>";

    const string MainHostname = "hive";

    const string HomeHostname = "home";

    const string NewsHostname = "news";

    const string WeatherHostname = "weather";
    
    const string ASCIIEncoding = "ISO-8859-1";

    public static async Task<bool> ProcessRequest(HttpRequest req, HttpResponse res)
    {
        var isIntranetEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.Intranet);

        if (isIntranetEnabled)
        {
            return req.Uri.Host.ToLowerInvariant() switch
            {
                MainHostname => await ProcessHiveRequest(req, res),
                HomeHostname => await ProcessHomeRequest(req, res),
                WeatherHostname => await ProcessWeatherRequest(req, res),
                NewsHostname => await ProcessNewsRequest(req, res),
                _ => false,
            };
        }        

        return false;
    }

    private static async Task<bool> ProcessHiveRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        if (req.IsRelativeUri("/") || req.IsRelativeUri("/index.html"))
        {
            var htmlDocument = new HtmlDocument();

            htmlDocument.LoadVirtual("index.html");

            HandleControlIndexPage(htmlDocument);

            res.SetBodyString(htmlDocument.DocumentNode.OuterHtml);

            return true;
        }
        else if (req.Uri.Segments.Length == 3 && req.Uri.Segments[1] == "assets/")
        {
            var resData = Resources.GetStaticsResourceData($"{string.Concat(req.Uri.Segments)[1..]}");

            if (resData != null)
            {
                var mimeType = MimeTypesMap.GetMimeType(req.Uri.Segments.Last());

                res.SetBodyData(resData, mimeType);

                return true;
            }

            return false;
        }
        else if (req.Uri.Segments.Length == 3 && req.Uri.Segments[1] == "action/")
        {
            return await ProcessHiveActionRequest(req, res);
        }

        return false;
    }

    private static async Task<bool> ProcessHiveActionRequest(HttpRequest req, HttpResponse res)
    {
        switch (req.Uri.Segments[2])
        {
            case "clearcache":
            {
                Mind.Instance._cacheDb.Clear();

                res.SetBodyString("Cache is cleared!" + HtmlOkayGoBack)
                    .SetFound($"http://{MainHostname}/");

                return true;
            }

            case "toggleIntranet":
            {
                var isIntranetEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.Intranet);

                isIntranetEnabled = !isIntranetEnabled;

                Mind.Instance.ConfigDb.SettingSet(ConfigNames.Intranet, isIntranetEnabled);

                res.SetBodyString("Intranet is now " + isIntranetEnabled.ToOnOff() + HtmlOkayGoBack)
                    .SetFound($"http://{MainHostname}/");

                return true;
            }

            case "toggleInternetArchive":
            {
                var isInternetArchiveEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.InternetArchive);

                isInternetArchiveEnabled = !isInternetArchiveEnabled;

                Mind.Instance.ConfigDb.SettingSet(ConfigNames.InternetArchive, isInternetArchiveEnabled);

                res.SetBodyString("Internet Archive is now " + isInternetArchiveEnabled.ToOnOff() + HtmlOkayGoBack)
                    .SetFound($"http://{MainHostname}/");

                return true;
            }

            case "toggleProtoWeb":
            {
                var isInternetArchiveEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.ProtoWeb);

                isInternetArchiveEnabled = !isInternetArchiveEnabled;

                Mind.Instance.ConfigDb.SettingSet(ConfigNames.ProtoWeb, isInternetArchiveEnabled);

                res.SetBodyString("Internet Archive is now " + isInternetArchiveEnabled.ToOnOff() + HtmlOkayGoBack)
                    .SetFound($"http://{MainHostname}/");

                return true;
            }

            default:
            {
                res.SetBodyString("Unknown action." + HtmlOkayGoBack);

                return true;
            }
        }
    }

    private static void HandleControlIndexPage(HtmlDocument htmlDocument)
    {
        var intranetStatus = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.Intranet);

        htmlDocument.ReplaceTextById("toggleIntranetLabel", intranetStatus.ToOnOff());

        htmlDocument.GetElementById("toggleIntranetLabel").Attributes["color"].Value = intranetStatus ? "green" : "red";

        htmlDocument.ReplaceTextById("toggleIntranetButton", "Turn " + (!intranetStatus).ToOnOff());

        var protoWebStatus = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.ProtoWeb);

        htmlDocument.ReplaceTextById("toggleProtoWebLabel", protoWebStatus.ToOnOff());

        htmlDocument.GetElementById("toggleProtoWebLabel").Attributes["color"].Value = protoWebStatus ? "green" : "red";

        htmlDocument.ReplaceTextById("toggleProtoWebButton", "Turn " + (!protoWebStatus).ToOnOff());

        var internetArchiveStatus = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.InternetArchive);

        htmlDocument.ReplaceTextById("toggleInternetArchiveLabel", internetArchiveStatus.ToOnOff());

        htmlDocument.GetElementById("toggleInternetArchiveLabel").Attributes["color"].Value = internetArchiveStatus ? "green" : "red";

        htmlDocument.ReplaceTextById("toggleInternetArchiveButton", "Turn " + (!internetArchiveStatus).ToOnOff());

        var cacheStats = CacheUtils.GetCounters();

        htmlDocument.ReplaceTextById("totalProxyCache", cacheStats.Item1.ToString());

        htmlDocument.ReplaceTextById("totalAvailabilityCache", cacheStats.Item2.ToString());
    }

    private static async Task<bool> ProcessHomeRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        if (req.Uri.Segments.Length == 1 || req.Uri.Segments[1] == "index.html")
        {
            var htmlDocument = new HtmlDocument();

            htmlDocument.LoadVirtual("home/index.html");

            res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding)).SetBodyString(htmlDocument.DocumentNode.OuterHtml);

            return true;
        }

        return false;
    }

    private static async Task<bool> ProcessWeatherRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        if (req.Uri.Segments.Length == 1 || req.Uri.Segments[1] == "index.html")
        {
            var htmlDocument = new HtmlDocument();

            htmlDocument.LoadVirtual("weather/index.html");

            res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding)).SetBodyString(htmlDocument.DocumentNode.OuterHtml);

            return true;
        }

        return false;
    }

    private static async Task<bool> ProcessNewsRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        if (req.IsRelativeUri("/") || req.IsRelativeUri("/index.html"))
        {
            var htmlDocument = new HtmlDocument();

            htmlDocument.LoadVirtual("news/index.html");

            var articles = await Clients.GetGoogleArticles();

            var rawHtml = "<ul>";

            foreach (var article in articles)
            {
                rawHtml += "<li><a href=\"/article/" + article.Id + "\">" + article.Title + "</a></li><br><hr>";
            }

            rawHtml += "</ul>";

            var contentEl = htmlDocument.DocumentNode.SelectSingleNode("//div[@id='content']");

            contentEl.InnerHtml = rawHtml;

            res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding)).SetBodyString(htmlDocument.DocumentNode.OuterHtml.ReplaceNewCharsWithOldChars());

            return true;
        }
        else if (req.Uri.Segments.Length == 3 && req.Uri.Segments[1] == "article/")
        {
            var id = req.Uri.Segments[2];

            var article = await Clients.GetGoogleNewsArticle(id);
            
            var htmlDocument = new HtmlDocument();

            htmlDocument.LoadVirtual("news/article.html");

            if (article == null)
            {
                htmlDocument.ReplaceTextById("content", "Unable to parse article!");
            }
            else
            {
                htmlDocument.DocumentNode.SelectSingleNode("//h1[@id='title']").InnerHtml = article.Title;

                htmlDocument.DocumentNode.SelectSingleNode("//h5[@id='date']").InnerHtml = "Published: " + article.PublicationDate.ToString();

                var sanitizer = HtmlSanitizer.SimpleHtml5Sanitizer();

                var cleanHtml = sanitizer.Sanitize(article.Content);

                if (!string.IsNullOrWhiteSpace(cleanHtml))
                {
                    var articleDocument = new HtmlDocument();

                    articleDocument.LoadHtml(cleanHtml);

                    var nodes = articleDocument.DocumentNode.SelectNodes("//img");

                    if (nodes != null)
                    {
                        foreach (var node in nodes)
                        {
                            node.Remove();
                        }
                    }

                    cleanHtml = articleDocument.DocumentNode.OuterHtml;
                }
                else
                {
                    cleanHtml = article.Content.SanitizeHtml();
                }

                htmlDocument.ReplaceTextById("content", cleanHtml);
            }

            res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding)).SetBodyString(htmlDocument.DocumentNode.OuterHtml.ReplaceNewCharsWithOldChars());

            return true;
        }

        return true;
    }
}
