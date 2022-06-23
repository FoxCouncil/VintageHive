using HeyRed.Mime;
using HtmlAgilityPack;
using System.Text;
using Vereyon.Web;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;

namespace VintageHive.Processors;

internal static class IntranetProcessor
{
    private static readonly IReadOnlyList<int> InternetArchiveValidYears = Enumerable.Range(1996, DateTime.Now.Year - 1996 + 1).ToList();

    const string HtmlOkayGoBack = "<hr><a href=\"http://hive/settings\">&lt; Okay</a>";

    const string MainHostname = "hive";

    const string NewsPath = "/news";

    const string WeatherPath = "/weather";

    const string SettingsPath = "/settings";

    const string ASCIIEncoding = "ISO-8859-1";
    
    private const string FoundUri = $"http://{MainHostname}/settings";    

    public static async Task<bool> ProcessRequest(HttpRequest req, HttpResponse res)
    {
        var isIntranetEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.Intranet);

        if (req.Uri.Host.ToLowerInvariant() != MainHostname)
        {
            return false;
        }

        if (isIntranetEnabled && req.IsRelativeUri("/"))
        {
            return await ProcessHomeRequest(req, res);
        }
        else if (isIntranetEnabled && req.IsRelativeUri(NewsPath))
        {
            return await ProcessNewsRequest(req, res);
        }
        else if (isIntranetEnabled && req.IsRelativeUri(WeatherPath))
        {
            return await ProcessWeatherRequest(req, res);
        }
        else if (req.IsRelativeUri(SettingsPath))
        {
            return await ProcessSettingsRequest(req, res);
        }
        else
        {
            return await ProcessGeneralRequests(req, res);
        }
    }

    private static async Task<bool> ProcessHomeRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        var htmlDocument = new HtmlDocument();

        htmlDocument.LoadVirtual("home.html");

        htmlDocument.ReplaceTextById("location", Mind.Instance.ConfigDb.SettingGet<string>(ConfigNames.Location));

        htmlDocument.ReplaceTextById("timestamp", DateTime.Now.ToString("dddd, MMMM dd yyyy | hh:mm tt"));

        var location = Mind.Instance.ConfigDb.SettingGet<string>(ConfigNames.Location);

        var weatherData = await Clients.GetWeatherData(location);

        if (weatherData != null)
        {
            htmlDocument.ReplaceTextById("time", weatherData.CurrentConditions.Dayhour);

            htmlDocument.GetElementById("cc_icon").Attributes["src"].Value = weatherData.CurrentConditions.IconURL.CleanWeatherImageUrl();

            htmlDocument.ReplaceTextById("cc_temp1", weatherData.CurrentConditions.Temp.F + "°F");

            htmlDocument.ReplaceTextById("cc_temp2", weatherData.CurrentConditions.Temp.C + "°C");

            htmlDocument.ReplaceTextById("cc_comment", weatherData.CurrentConditions.Comment);
        }

        var edition = Mind.Instance.ConfigDb.SettingGet<string>(ConfigNames.Location).Split(", ").LastOrDefault("US").ToUpper();

        var articles = await Clients.GetGoogleArticles(edition);

        articles = articles.Take(5).ToList();

        var rawHtml = "<ul compact=\"true\" type=\"none\" style=\"list-style:none;margin:0 5px 0\">";

        foreach (var article in articles)
        {
            rawHtml += "<li><a href=\"/news?article=" + article.Id + "\"><font size=\"1\">" + article.Title + "</font></a></li><hr>";
        }

        rawHtml += "<li><a href=\"/news\"><font size=\"1\">More news...</font></a></li>";

        rawHtml += "</ul>";

        var contentEl = htmlDocument.DocumentNode.SelectSingleNode("//td[@id='news-content']");

        contentEl.InnerHtml = rawHtml;

        res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding)).SetBodyString(htmlDocument.DocumentNode.OuterHtml);

        return true;
    }

    private static async Task<bool> ProcessNewsRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        var edition = req.QueryParams.ContainsKey("edition") ? req.QueryParams["edition"].ToUpper() : Mind.Instance.ConfigDb.SettingGet<string>(ConfigNames.Location).Split(", ").LastOrDefault("US").ToUpper();

        var htmlDocument = new HtmlDocument();

        if (req.QueryParams.ContainsKey("article"))
        {
            var id = req.QueryParams["article"];

            var article = await Clients.GetGoogleNewsArticle(id);

            htmlDocument.LoadVirtual("article.html");

            htmlDocument.ReplaceTextById("edition-string", edition + " Edition");

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

        htmlDocument.LoadVirtual("news.html");

        htmlDocument.ReplaceTextById("edition-string", edition + " Edition");

        var articles = await Clients.GetGoogleArticles(edition);

        var rawHtml = "<ul>";

        foreach (var article in articles)
        {
            rawHtml += "<li><a href=\"/news?article=" + article.Id + "\">" + article.Title + "</a></li><br><hr>";
        }

        rawHtml += "</ul>";

        var contentEl = htmlDocument.DocumentNode.SelectSingleNode("//div[@id='content']");

        contentEl.InnerHtml = rawHtml;

        res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding)).SetBodyString(htmlDocument.DocumentNode.OuterHtml.ReplaceNewCharsWithOldChars());

        return true;
    }

    private static async Task<bool> ProcessWeatherRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        var htmlDocument = new HtmlDocument();

        var location = req.QueryParams.ContainsKey("location") ? req.QueryParams["location"] : Mind.Instance.ConfigDb.SettingGet<string>(ConfigNames.Location);

        var weatherData = await Clients.GetWeatherData(location);

        if (weatherData == null || weatherData.Region == null)
        {
            htmlDocument.LoadVirtual("weather404.html");

            htmlDocument.ReplaceTextById("region", location);
        }
        else
        {
            htmlDocument.LoadVirtual("weather.html");

            htmlDocument.GetElementById("location_field").SetAttributeValue("value", location);

            htmlDocument.ReplaceTextById("region", weatherData.Region);

            htmlDocument.ReplaceTextById("time", weatherData.CurrentConditions.Dayhour);

            htmlDocument.GetElementById("cc_icon").Attributes["src"].Value = weatherData.CurrentConditions.IconURL.CleanWeatherImageUrl();

            htmlDocument.ReplaceTextById("cc_temp1", weatherData.CurrentConditions.Temp.F + "°F");

            htmlDocument.ReplaceTextById("cc_temp2", weatherData.CurrentConditions.Temp.C + "°C");

            htmlDocument.ReplaceTextById("cc_comment", weatherData.CurrentConditions.Comment);

            var idx = 1;

            foreach (var nextWeather in weatherData.NextDays)
            {
                htmlDocument.ReplaceTextById($"nw_{idx}_day", nextWeather.Day.Substring(0, 3));
                htmlDocument.GetElementById($"nw_{idx}_icon").Attributes["src"].Value = nextWeather.IconURL.CleanWeatherImageUrl();
                htmlDocument.ReplaceTextById($"nw_{idx}_temp1", nextWeather.MinTemp.F + "°F");
                htmlDocument.ReplaceTextById($"nw_{idx}_temp2", nextWeather.MinTemp.C + "°C");
                htmlDocument.ReplaceTextById($"nw_{idx}_comment", nextWeather.Comment);

                idx++;
            }
        }

        res.SetEncoding(Encoding.GetEncoding(ASCIIEncoding)).SetBodyString(htmlDocument.DocumentNode.OuterHtml);

        return true;
    }

    private static async Task<bool> ProcessSettingsRequest(HttpRequest req, HttpResponse res)
    {
        await Task.Delay(0);
        
        res.Cache = false;

        var htmlDocument = new HtmlDocument();

        htmlDocument.LoadVirtual("settings.html");

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

        var internetArchiveYear = Mind.Instance.ConfigDb.SettingGet<int>(ConfigNames.InternetArchiveYear);

        foreach (var year in InternetArchiveValidYears)
        {
            var yearSelectEl = htmlDocument.GetElementById("internetArchiveYear");

            var yearOptionEl = htmlDocument.CreateElement("option");

            yearOptionEl.SetAttributeValue("value", year.ToString());

            yearOptionEl.InnerHtml = year.ToString();

            if (year == internetArchiveYear)
            {
                yearOptionEl.SetAttributeValue("selected", "selected");
            }

            yearSelectEl.AppendChild(yearOptionEl);
        }

        var location = Mind.Instance.ConfigDb.SettingGet<string>(ConfigNames.Location);

        htmlDocument.ReplaceTextById("geoiplocation", location);

        var address = Mind.Instance.ConfigDb.SettingGet<string>(ConfigNames.RemoteAddress);

        htmlDocument.ReplaceTextById("geoipaddress", address);

        var cacheStats = CacheUtils.GetCounters();

        htmlDocument.ReplaceTextById("totalProxyCache", cacheStats.Item1.ToString());

        htmlDocument.ReplaceTextById("totalAvailabilityCache", cacheStats.Item2.ToString());

        res.SetBodyString(htmlDocument.DocumentNode.OuterHtml);

        return true;
    }

    private static async Task<bool> ProcessGeneralRequests(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        if (req.Uri.Segments.Length == 3 && req.Uri.Segments[1] == "assets/")
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
        else if (req.Uri.Segments.Length == 4 && req.Uri.Segments[1] == "assets/" && req.Uri.Segments[2] == "weather/")
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
            return await ProcessActionRequest(req, res);
        }

        return false;
    }

    private static async Task<bool> ProcessActionRequest(HttpRequest req, HttpResponse res)
    {
        var actionName = req.Uri.Segments[2];

        switch (actionName)
        {
            case "clearcache":
            {
                Mind.Instance._cacheDb.Clear();

                res.SetBodyString("Cache is cleared!" + HtmlOkayGoBack).SetFound(FoundUri);

                return true;
            }

            case "resetgeoip":
            {
                await Mind.Instance.ResetGeoIP();

                res.SetBodyString("Reset GeoIP stored location! " + HtmlOkayGoBack).SetFound(FoundUri);

                return true;
            }

            case "toggleIntranet":
            {
                var isIntranetEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.Intranet);

                isIntranetEnabled = !isIntranetEnabled;

                Mind.Instance.ConfigDb.SettingSet(ConfigNames.Intranet, isIntranetEnabled);

                res.SetBodyString("Intranet is now " + isIntranetEnabled.ToOnOff() + HtmlOkayGoBack).SetFound(FoundUri);

                return true;
            }

            case "toggleInternetArchive":
            {
                var isInternetArchiveEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.InternetArchive);

                isInternetArchiveEnabled = !isInternetArchiveEnabled;

                Mind.Instance.ConfigDb.SettingSet(ConfigNames.InternetArchive, isInternetArchiveEnabled);

                res.SetBodyString("Internet Archive is now " + isInternetArchiveEnabled.ToOnOff() + HtmlOkayGoBack).SetFound(FoundUri);

                return true;
            }

            case "setInternetArchiveYear":
            {
                var year = req.Uri.Query.Split('=')[1];

                if (!int.TryParse(year, out int yearInt) || !InternetArchiveValidYears.Contains(yearInt))
                {
                    return false;
                }

                Mind.Instance.ConfigDb.SettingSet(ConfigNames.InternetArchiveYear, yearInt);

                res.SetBodyString("Internet Archive year is now set to " + yearInt + HtmlOkayGoBack).SetFound(FoundUri);

                return true;
            }

            case "toggleProtoWeb":
            {
                var isInternetArchiveEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.ProtoWeb);

                isInternetArchiveEnabled = !isInternetArchiveEnabled;

                Mind.Instance.ConfigDb.SettingSet(ConfigNames.ProtoWeb, isInternetArchiveEnabled);

                res.SetBodyString("ProtoWeb is now " + isInternetArchiveEnabled.ToOnOff() + HtmlOkayGoBack).SetFound(FoundUri);

                return true;
            }

            default:
            {
                res.SetBodyString("Unknown action: " + actionName + HtmlOkayGoBack);

                return true;
            }
        }
    }
}
