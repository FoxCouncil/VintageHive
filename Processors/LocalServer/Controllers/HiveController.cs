// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using HeyRed.Mime;
using HtmlAgilityPack;
using SmartReader;
using System.Diagnostics.CodeAnalysis;
using VintageHive.Proxy.Security;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("*.hive.com")]
internal class HiveController : Controller
{
    private const string DEFAULT_LOCATION_PRIVACY = "Your Location";

    public override async Task CallInitial(string rawPath)
    {
        await Task.Delay(0);

        if (Request.Host.ToLower() != "hive.com")
        {
            Response.SetRedirect("http://hive.com" + rawPath);

            return;
        }

        Response.Context.SetValue("menu", new[] {
            "Download",
            "Search",
            "Viewer",
            "News",
            "Weather",
            "Settings",
            "Help"
        });
    }

    [Route("/index.html")]
    public async Task Index()
    {
        var tempUnits = Mind.Db.ConfigLocalGet<string>(Request.ListenerSocket.RemoteIP, ConfigNames.TemperatureUnits);
        var distUnits = Mind.Db.ConfigLocalGet<string>(Request.ListenerSocket.RemoteIP, ConfigNames.DistanceUnits);

        Response.Context.SetValue("u", tempUnits[..1].ToLower());

        var geoipLocation = Mind.Db.ConfigGet<GeoIp>(ConfigNames.Location);

        var weatherData = await WeatherUtils.GetDataByGeoIp(geoipLocation, tempUnits, distUnits);

        Response.Context.SetValue("weather_privacy", DEFAULT_LOCATION_PRIVACY);

        Response.Context.SetValue("u", tempUnits[..1].ToLower());

        Response.Context.SetValue("weather", weatherData);

        Response.Context.SetValue("articles_local", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Local)).Take(5));

        Response.Context.SetValue("articles_us", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.US)).Take(5));

        Response.Context.SetValue("articles_world", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.World)).Take(5));

        Response.Context.SetValue("articles_tech", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Technology)).Take(5));

        Response.Context.SetValue("articles_science", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Science)).Take(5));

        Response.Context.SetValue("articles_business", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Business)).Take(5));

        Response.Context.SetValue("articles_ent", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Entertainment)).Take(5));

        Response.Context.SetValue("articles_sports", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Sports)).Take(5));        

        Response.Context.SetValue("articles_health", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Health)).Take(5));

        Response.Context.SetValue("directory_hotlinks", Mind.Db.LinksGetAll());

        Response.Context.SetValue("directory_protohttp", await ProtoWebUtils.GetAvailableHttpSites());

        Response.Context.SetValue("directory_protoftp", await ProtoWebUtils.GetAvailableFtpSites());
    }

    [Route("/download.html")]
    [SuppressMessage("Performance", "CA1854:Prefer the 'IDictionary.TryGetValue(TKey, out TValue)' method", Justification = "I'm lazy")]
    public async Task Download()
    {
        await Task.Delay(0);

        var repos = RepoUtils.Get();

        Response.Context.SetValue("repos", repos);

        if (Request.QueryParams.ContainsKey("repo") && repos.ContainsKey(Request.QueryParams["repo"]))
        {
            var path = Request.QueryParams.ContainsKey("path") ? Request.QueryParams["path"] : "/";

            // TODO: LOL Security ^.^;;
            if (!Path.EndsInDirectorySeparator(path) || !path.ConfirmValidPath() || path.Contains(".."))
            {
                path = "/";
            }

            Response.Context.SetValue("path", path);

            var reposhortname = Request.QueryParams["repo"];
            var repo = repos[reposhortname];

            Response.Context.SetValue("reponame", repo.Item1);
            Response.Context.SetValue("reposhortname", reposhortname);

            var vfsPath = VFS.GetFullPath(repo.Item2);

            var directoryInfo = new DirectoryInfo(repo.Item2 == VFS.DownloadsPath ? vfsPath : repo.Item2);

            var isRootPath = true;

            if (path.Length > 3)
            {
                var cadidates = directoryInfo.GetDirectories(path[1..^1]).FirstOrDefault();

                if (cadidates != null)
                {
                    directoryInfo = cadidates;

                    isRootPath = false;
                }
            }

            if (Request.QueryParams.ContainsKey("file"))
            {
                var file = Request.QueryParams["file"];

                var filePath = Path.Combine(directoryInfo.FullName, file);

                var fileInfo = new FileInfo(filePath);

                if (fileInfo.Exists)
                {
                    var mimetype = MimeTypesMap.GetMimeType(file);

                    var fileHandle = fileInfo.OpenRead();

                    Response.SetStreamForDownload(fileHandle, mimetype);

                    Response.Handled = true;
                }
            }

            Response.Context.SetValue("isroot", isRootPath);

            if (!isRootPath)
            {
                var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

                if (pathSegments.Count > 0)
                {
                    pathSegments.RemoveAt(pathSegments.Count - 1);
                }

                var output = '/' + string.Join('/', pathSegments);

                if (pathSegments.Count > 0)
                {
                    output += '/';
                }

                Response.Context.SetValue("parentpath", output);
            }

            var dirs = directoryInfo.EnumerateDirectories();
            var files = directoryInfo.EnumerateFiles();

            Response.Context.SetValue("dirs", dirs);
            Response.Context.SetValue("dirs_total", dirs.Count());
            Response.Context.SetValue("files", files);
            Response.Context.SetValue("files_total", files.Count());
        }
    }

    [Route("/search.html")]
    public async Task Search()
    {
        if (Request.QueryParams.ContainsKey("q"))
        {
            var keywords = Request.QueryParams["q"];

            Response.Context.SetValue("keywords", keywords);

            var results = await DDGUtils.Search(keywords);

            Response.Context.SetValue("results", results);
        }
    }

    [Route("/viewer.html")]
    public async Task Viewer()
    {
        if (Request.QueryParams.ContainsKey("url"))
        {
            var url = Request.QueryParams["url"];

            Response.Context.SetValue("url", url);

            Response.Context.SetValue("type", "document");

            var mimetype = MimeTypesMap.GetMimeType(url);

            if (mimetype.StartsWith("image"))
            {
                Response.Context.SetValue("type", "image");
                Response.Context.SetValue("image", $"http://api.hive.com/image/fetch?url={url}");
            }
            else
            {
                var result = await NewsUtils.GetReaderOutput(url);

                var articleDocument = new HtmlDocument();

                if (result == null)
                {
                    Response.Context.SetValue("result", "No worky! :(");

                    return;
                }

                Response.Context.SetValue("doctitle", result.Title);

                articleDocument.LoadHtml(result.Content);

                NormalizeAnchorLinks(articleDocument);

                NormalizeImages(articleDocument);

                Response.Context.SetValue("document", articleDocument.DocumentNode.OuterHtml);
            }
        }
    }

    private static void NormalizeAnchorLinks(HtmlDocument articleDocument)
    {
        var anchorNodes = articleDocument.DocumentNode.SelectNodes("//a");

        if (anchorNodes != null)
        {
            foreach (var node in anchorNodes)
            {
                var href = node.GetAttributeValue("href", "");

                if (!href.Any() || href[0] == '#')
                {
                    continue;
                }

                href = $"/viewer.html?url={WebUtility.UrlDecode(href)}";

                node.SetAttributeValue("href", href);

                node.SetAttributeValue("target", "");
            }
        }
    }

    private static void NormalizeImages(HtmlDocument articleDocument)
    {
        var imgNodes = articleDocument.DocumentNode.SelectNodes("//img");

        if (imgNodes != null)
        {
            foreach (var node in imgNodes)
            {
                var img = node.GetAttributeValue("src", "");

                if (string.IsNullOrEmpty(img))
                {
                    img = node.GetAttributeValue("data-src", "");
                }

                if (string.IsNullOrEmpty(img))
                {
                    img = node.GetAttributeValue("data-src-medium", "");
                }

                var imgUri = new Uri(img.StartsWith("//") ? $"https:{img}" : img);

                var imageLinkNode = HtmlNode.CreateNode($"<a href=\"/viewer.html?url={Uri.EscapeDataString(imgUri.ToString())}\"><img src=\"http://api.hive.com/image/fetch?url={Uri.EscapeDataString(imgUri.ToString())}\" border=\"0\"></a>");

                if (node.ParentNode.Name == "picture")
                {
                    var pictureEl = node.ParentNode;

                    pictureEl.ParentNode.InsertAfter(imageLinkNode, pictureEl);

                    pictureEl.Remove();
                }

                node.ParentNode.InsertAfter(imageLinkNode, node);

                node.Remove();
            }
        }
    }

    [Route("/news.html")]
    public async Task News()
    {
        Response.Context.SetValue("articles_local", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Local));

        Response.Context.SetValue("articles_us", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.US));

        Response.Context.SetValue("articles_world", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.World));

        Response.Context.SetValue("articles_tech", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Technology));

        Response.Context.SetValue("articles_science", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Science));

        Response.Context.SetValue("articles_business", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Business));

        Response.Context.SetValue("articles_ent", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Entertainment));

        Response.Context.SetValue("articles_sports", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Sports));

        Response.Context.SetValue("articles_health", await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Health));
    }

    [Route("/weather.html")]
    public async Task Weather()
    {
        var location = Request.QueryParams.ContainsKey("location") ? Request.QueryParams["location"] : null;

        location = location == DEFAULT_LOCATION_PRIVACY ? null : location;

        var tempUnits = Mind.Db.ConfigLocalGet<string>(Request.ListenerSocket.RemoteIP, ConfigNames.TemperatureUnits);
        var distUnits = Mind.Db.ConfigLocalGet<string>(Request.ListenerSocket.RemoteIP, ConfigNames.DistanceUnits);

        var geoipLocation = location != null ? WeatherUtils.FindLocation(location) : Mind.Db.ConfigGet<GeoIp>(ConfigNames.Location);

        var weatherData = await WeatherUtils.GetDataByGeoIp(geoipLocation, tempUnits, distUnits);

        Response.Context.SetValue("weather_privacy", DEFAULT_LOCATION_PRIVACY);

        Response.Context.SetValue("u", tempUnits[..1].ToLower());

        Response.Context.SetValue("weather", weatherData);

        Response.Context.SetValue("weather_location", location ?? DEFAULT_LOCATION_PRIVACY);

        Response.Context.SetValue("weather_fullname", location == null ? DEFAULT_LOCATION_PRIVACY : geoipLocation?.fullname ?? "N/A");
    }

    [Route("/settings.html")]
    public async Task Settings()
    {
        await Task.Delay(0);

        var isInternetArchiveEnabled = Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.InternetArchive);
        var internetArchiveYear = Mind.Db.ConfigLocalGet<int>(Request.ListenerSocket.RemoteIP, ConfigNames.InternetArchiveYear);

        Response.Context.SetValue("ia_years", InternetArchiveProcessor.ValidYears);
        Response.Context.SetValue("ia_toggle", isInternetArchiveEnabled);
        Response.Context.SetValue("ia_current", internetArchiveYear);

        var isProtoWebEnabled = Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.ProtoWeb);

        Response.Context.SetValue("proto_toggle", isProtoWebEnabled);

        var isDialnineEnabled = Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.Dialnine);

        Response.Context.SetValue("dialnine_toggle", isDialnineEnabled);
    }

    //[Controller("/settings/users.html")]
    //public async Task SettingsUser()
    //{
    //    await Task.Delay(0);

    //    var users = Mind.Db.UserList();

    //    Response.Context.SetValue("users", users);
    //}

    //[Controller("/api/user/exist")]
    //public async Task UserExists()
    //{
    //    await Task.Delay(0);

    //    string username;

    //    if (Request.QueryParams.ContainsKey("username"))
    //    {
    //        username = Request.QueryParams["username"];
    //    }
    //    else if (Request.FormData.ContainsKey("username"))
    //    {
    //        username = Request.FormData["username"];
    //    }
    //    else
    //    {
    //        return;
    //    }

    //    var result = Mind.Db.UserExistsByUsername(username);

    //    Response.SetBodyString(result.ToString().ToLower(), "text/plain");

    //    Response.Handled = true;
    //}

    //[Controller("/api/user/create")]
    //public async Task UserCreate()
    //{
    //    await Task.Delay(0);

    //    if (!Request.FormData.ContainsKey("username") || !Request.FormData.ContainsKey("password"))
    //    {
    //        return;
    //    }

    //    var username = Request.FormData["username"];

    //    var password = Request.FormData["password"];

    //    var result = Mind.Db.UserCreate(username, password);

    //    Response.SetBodyString(result.ToString().ToLower(), "text/plain").SetFound();
    //}

    [Route("/api/set")]
    public async Task SetUserSetting()
    {
        await Task.Delay(0);

        if (!Request.QueryParams.ContainsKey("u"))
        {
            return;
        }

        var oldUnit = Mind.Db.ConfigLocalGet<string>(Request.ListenerSocket.RemoteIP, ConfigNames.TemperatureUnits)[0].ToString().ToLower();
        var newUnit = Request.QueryParams["u"].ToLower();

        if (oldUnit == newUnit)
        {
            Response.SetFound();
        }

        if (oldUnit == "c" && newUnit == "f")
        {
            Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.TemperatureUnits, "fahrenheit");
        }
        else if (oldUnit == "f" && newUnit == "c")
        {
            Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.TemperatureUnits, "celsius");
        }
        else
        {
            return;
        }

        Response.SetFound();
    }

    [Route("/api/iatoggle")]
    public async Task InternetArchiveToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.InternetArchive, !Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.InternetArchive));

        Response.SetFound();
    }

    [Route("/api/iasetyear")]
    public async Task InternetArchiveSetYear()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("year"))
        {
            return;
        }

        if (int.TryParse(Request.FormData["year"], System.Globalization.NumberStyles.Integer, null, out var year))
        {
            var currentYear = Mind.Db.ConfigLocalGet<int>(Request.ListenerSocket.RemoteIP, ConfigNames.InternetArchiveYear);

            if (year != currentYear && InternetArchiveProcessor.ValidYears.Contains(year))
            {
                Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.InternetArchiveYear, year);
            }

            Response.SetFound();
        }
    }

    [Route("/api/protowebtoggle")]
    public async Task ProtoWebToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.ProtoWeb, !Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.ProtoWeb));

        Response.SetFound();
    }

    [Route("/api/dialninetoggle")]
    public async Task DialnineToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.Dialnine, !Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.Dialnine));

        Response.SetFound();
    }

    [Route("/api/download/dialnineca.crt")]
    public async Task DialnineCertDownload()
    {
        await Task.Delay(0);

        var cert = Mind.Db.CertGet(CertificateAuthority.Name); // Get's CA

        Response.SetBodyString(cert.Certificate, "application/x-x509-ca-cert");
    }
}
