﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

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

        string userName = Response.HasSession("user") ? Convert.ToString(Session.user) : "";

        Response.Context.SetValue("user", userName);
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

        Response.Context.SetValue("articles_us", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.US)).Take(2));

        Response.Context.SetValue("articles_world", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.World)).Take(2));

        Response.Context.SetValue("articles_tech", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Technology)).Take(2));

        Response.Context.SetValue("articles_science", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Science)).Take(2));

        Response.Context.SetValue("articles_business", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Business)).Take(2));

        Response.Context.SetValue("articles_ent", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Entertainment)).Take(2));

        Response.Context.SetValue("articles_sports", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Sports)).Take(2));

        Response.Context.SetValue("articles_health", (await NewsUtils.GetGoogleTopicArticles(GoogleNewsTopic.Health)).Take(2));

        Response.Context.SetValue("directory_hotlinks", Mind.Db.LinksGetAll());

        Response.Context.SetValue("directory_protohttp", await ProtoWebUtils.GetAvailableHttpSites());

        Response.Context.SetValue("directory_protoftp", await ProtoWebUtils.GetAvailableFtpSites());
    }

    [Route("/login.html")]
    public async Task Login()
    {
        await Task.Delay(0);

        if (Request.Type == "POST")
        {
            var username = Request.FormData["username"] ?? string.Empty;
            var password = Request.FormData["password"] ?? string.Empty;

            if (username == string.Empty || password == string.Empty || username.Length < 3 || password.Length < 3 || username.Length > 8 || password.Length > 8)
            {
                NotAuthorized("Invalid input, try again.");

                return;
            }

            if (!Mind.Db.UserExistsByUsername(username))
            {
                NotAuthorized("Invalid credentials, try again.");

                return;
            }

            var user = Mind.Db.UserFetch(username, password);

            Session.user = user.Username;

            Response.SetFound("/me.html");
        }
    }

    [Route("/signup.html")]
    public async Task Signup()
    {
        await Task.Delay(0);

        if (Request.Type == "POST")
        {
            var username = Request.FormData["username"] ?? string.Empty;
            var password = Request.FormData["password"] ?? string.Empty;

            if (username == string.Empty || password == string.Empty || username.Length < 3 || password.Length < 3 || username.Length > 8 || password.Length > 8)
            {
                NotAuthorized("Invalid input, try again.");

                return;
            }

            if (Mind.Db.UserExistsByUsername(username))
            {
                NotAuthorized("User exists, try again.");

                return;
            }

            if (!Mind.Db.UserCreate(username, password))
            {
                NotAuthorized("Oopsy poopsy, try again.");

                return;
            }

            var user = Mind.Db.UserFetch(username);

            Session.error = $"User ({user.Username}) has been created, please login";

            Response.SetFound("/login.html");
        }
    }

    [Route("/logout.html")]
    public async Task Logout()
    {
        await Task.Delay(0);

        if (Response.HasSession("user"))
        {
            Response.RemoveSession("user");

            Session.error = "Successfully logged out, goodbye.";
        }
        else
        {
            Session.error = "No user logged in...";
        }

        Response.SetFound("/login.html");
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
                var bareId = url.Replace("https://news.google.com/__i/rss/rd/articles/", string.Empty);

                var result = await NewsUtils.GetGoogleNewsArticle(bareId);

                var articleDocument = new HtmlDocument();

                if (result == null)
                {
                    Response.Context.SetValue("result", "No worky! :(");

                    return;
                }

                Response.Context.SetValue("doctitle", result.Title);

                articleDocument.LoadHtml(result.Content);

                HtmlUtils.NormalizeAnchorLinks(articleDocument);

                HtmlUtils.NormalizeImages(articleDocument);

                Response.Context.SetValue("document", articleDocument.DocumentNode.OuterHtml);
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

        var isInternetArchiveEnabled = Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceInternetArchive);
        var internetArchiveYear = Mind.Db.ConfigLocalGet<int>(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceInternetArchiveYear);

        Response.Context.SetValue("ia_years", InternetArchiveProcessor.ValidYears);
        Response.Context.SetValue("ia_toggle", isInternetArchiveEnabled);
        Response.Context.SetValue("ia_current", internetArchiveYear);

        var isProtoWebEnabled = Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceProtoWeb);

        Response.Context.SetValue("proto_toggle", isProtoWebEnabled);

        var isDialnineEnabled = Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceDialnine);

        Response.Context.SetValue("dialnine_toggle", isDialnineEnabled);
    }

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

        Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceInternetArchive, !Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceInternetArchive));

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
            var currentYear = Mind.Db.ConfigLocalGet<int>(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceInternetArchiveYear);

            if (year != currentYear && InternetArchiveProcessor.ValidYears.Contains(year))
            {
                Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceInternetArchiveYear, year);
            }

            Response.SetFound();
        }
    }

    [Route("/api/protowebtoggle")]
    public async Task ProtoWebToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceProtoWeb, !Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceProtoWeb));

        Response.SetFound();
    }

    [Route("/api/dialninetoggle")]
    public async Task DialnineToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigLocalSet(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceDialnine, !Mind.Db.ConfigLocalGet<bool>(Request.ListenerSocket.RemoteIP, ConfigNames.ServiceDialnine));

        Response.SetFound();
    }

    [Route("/api/download/dialnineca.crt")]
    public async Task DialnineCertDownload()
    {
        await Task.Delay(0);

        var cert = Mind.Db.CertGet(CertificateAuthority.Name); // Get's CA

        Response.SetBodyString(cert.Certificate, "application/x-x509-ca-cert");
    }

    private void NotAuthorized(string error = "Not Authorized")
    {
        Session.error = error;

        Response.SetFound();
    }
}
