using Fluid;
using HeyRed.Mime;
using HtmlAgilityPack;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;

namespace VintageHive.Processors.LocalServer.Controllers;

internal class HiveController : Controller
{
    public override async Task CallInitial(string rawPath)
    {
        await Task.Delay(0);

        Response.Context.SetValue("menu", new [] {
            "Download",
            "News",
            "Weather",
            "Services",
            "Settings",
            "Help"
        });
    }

    [Controller("/index.html")]
    public async Task Index()
    {
        Response.Context.SetValue("directory_hotlinks", new Dictionary<string, string>() {
            { "Cool Links", "http://www.web-search.com/cool.html" },
            { "GatewayToTheNet.com", "http://www.gatewaytothenet.com/" },
            { "Top10Links", "http://www.toptenlinks.com/" },
            { "House Of Links", "http://www.ozemail.com.au/~krisp/button.html" },
            { "The BIG EYE", "http://www.bigeye.com/" },
            { "STARTING PAGE", "http://www.startingpage.com/" },
            { "Hotsheet.com", "http://www.hotsheet.com/" },
            { "Nerd World Media", "http://www.nerdworld.com/" },
            { "Suite101.com", "http://www.suite101.com" },
            { "RefDesk.com", "http://www.refdesk.com/" },
            { "WWW Virtual Library", "http://vlib.org/" },
            { "Yahoo!", "http://www.yahoo.com" },
            { "Yahoo! Canada", "http://www.yahoo.ca" },
            { "DogPile Open Directory", "http://opendir.dogpile.com/" }
        });

        Response.Context.SetValue("directory_protohttp", await ProtoWebUtils.GetAvailableHttpSites());

        Response.Context.SetValue("directory_protoftp", await ProtoWebUtils.GetAvailableFtpSites());
    }

    [Controller("/download.html")]
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

            var directoryInfo = repo.Item2;

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

                    Response.SetBodyFileStream(fileHandle, mimetype);

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

    [Controller("/news.html")]
    public async Task News()
    {
        var articles = await Clients.GetGoogleArticles("US");

        Response.Context.SetValue("articles", articles);

        if (Request.QueryParams.ContainsKey("article"))
        {
            var id = Request.QueryParams["article"];

            var article = await Clients.GetGoogleNewsArticle(id);

            if (article == null)
            {
                return;
            }

            Response.Context.SetValue("article", article);

            //var sanitizer = HtmlSanitizer.SimpleHtml5Sanitizer();

            var cleanHtml = article.Content; // sanitizer.Sanitize(article.Content);

            if (!string.IsNullOrWhiteSpace(cleanHtml))
            {
                var articleDocument = new HtmlDocument();

                articleDocument.LoadHtml(cleanHtml);

                var nodes = articleDocument.DocumentNode.SelectNodes("//img");

                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var img = node.GetAttributeValue("src", "");

                        if (string.IsNullOrEmpty(img))
                        {
                            img = node.GetAttributeValue("data-src-medium", "");
                        }

                        var imgUri = new Uri(img.StartsWith("//") ? $"https:{img}" : img);

                        var imageLinkNode = HtmlNode.CreateNode($"<a href=\"/api/image/fetch?url={Uri.EscapeDataString(imgUri.ToString())}\" target=\"_blank\"><img src=\"/api/image/fetch?url={Uri.EscapeDataString(imgUri.ToString())}\" width=\"320\"></a>");

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

                Response.Context.SetValue("article_body", articleDocument.DocumentNode.OuterHtml);
            }
        }
    }

    [Controller("/weather.html")]
    public async Task Weather()
    {
        var geoipLocation = Mind.Instance.ConfigDb.SettingGet<string>(ConfigNames.Location);

        var location = Request.QueryParams.ContainsKey("location") ? Request.QueryParams["location"] : geoipLocation;

        if (string.IsNullOrEmpty(location))
        {
            location = geoipLocation;
        }

        var weatherData = await Clients.GetWeatherData(location);

        var stringLocation = location;

        if (stringLocation == geoipLocation)
        {
            stringLocation = "Your Location";
        }

        Response.Context.SetValue("weather", weatherData);

        Response.Context.SetValue("weather_location", stringLocation);
    }

    [Controller("/settings.html")]
    public async Task Settings()
    {
        await Task.Delay(0);

        var isInternetArchiveEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.InternetArchive);

        Response.Context.SetValue("ia_years", InternetArchiveProcessor.ValidYears);
        Response.Context.SetValue("ia_toggle", isInternetArchiveEnabled);
        Response.Context.SetValue("ia_current", Mind.Instance.ConfigDb.SettingGet<int>(ConfigNames.InternetArchiveYear));

        var isProtoWebEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.ProtoWeb);

        Response.Context.SetValue("proto_toggle", isProtoWebEnabled);

        var cacheCounters = CacheUtils.GetCounters();

        Response.Context.SetValue("cache_counters", cacheCounters);
    }

    [Controller("/settings/users.html")]
    public async Task SettingsUser()
    {
        await Task.Delay(0);

        var users = Mind.Instance.UserDb.List();

        Response.Context.SetValue("users", users);
    }

    [Controller("/api/user/exist")]
    public async Task UserExists()
    {
        await Task.Delay(0);

        string username;

        if (Request.QueryParams.ContainsKey("username"))
        {
            username = Request.QueryParams["username"];
        }
        else if (Request.FormData.ContainsKey("username"))
        {
            username = Request.FormData["username"];
        }
        else
        {
            return;
        }

        var result = Mind.Instance.UserDb.ExistsByUsername(username);

        Response.SetBodyString(result.ToString().ToLower(), "text/plain");

        Response.Handled = true;
    }

    [Controller("/api/user/create")]
    public async Task UserCreate()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("username") || !Request.FormData.ContainsKey("password"))
        {
            return;
        }

        var username = Request.FormData["username"];

        var password = Request.FormData["password"];

        var result = Mind.Instance.UserDb.Create(username, password);

        Response.SetBodyString(result.ToString().ToLower(), "text/plain").SetFound(Request.Headers["Referer"]);

        Response.Handled = true;
    }

    [Controller("/api/image/fetch")]
    public async Task ImageFetch()
    {
        if (!Request.QueryParams.ContainsKey("url"))
        {
            Response.SetNotFound();

            Response.Handled = true;

            return;
        }

        Image image;

        try
        {
            var fetchUri = new Uri(Request.QueryParams["url"]);

            byte[] _imageData;

            using var httpClient = Clients.GetHttpClient(Request);

            _imageData = await httpClient.GetByteArrayAsync(fetchUri);

            image = Image.Load(_imageData);
        }
        catch
        {
            Response.SetNotFound();

            Response.Handled = true;

            return;
        }        

        image.Mutate(x => x.Resize(800, 0));

        var memoryStream = new MemoryStream();

        await image.SaveAsJpegAsync(memoryStream);

        Response.SetBodyData(memoryStream.ToArray(), "image/jpeg");

        Response.Handled = true;
    }

    [Controller("/api/cache/clear")]
    public async Task CacheClear()
    {
        await Task.Delay(0);

        Mind.Instance._cacheDb.Clear();

        Response.SetFound(Request.Headers["Referer"]);

        Response.Handled = true;
    }

    [Controller("/api/ia/toggle")]
    public async Task InternetArchiveToggle()
    {
        if (!Request.FormData.ContainsKey("toggle"))
        {
            await Task.Delay(0);

            return;
        }
    
        Mind.Instance.ConfigDb.SettingSet(ConfigNames.InternetArchive, Request.FormData["toggle"].ToLower() != "disable");

        Response.SetFound(Request.Headers["Referer"]);

        Response.Handled = true;
    }

    [Controller("/api/ia/setyear")]
    public async Task InternetArchiveSetYear()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("year"))
        {
            return;
        }

        if (int.TryParse(Request.FormData["year"], System.Globalization.NumberStyles.Integer, null, out var year))
        {
            var currentYear = Mind.Instance.ConfigDb.SettingGet<int>(ConfigNames.InternetArchiveYear);

            if (year != currentYear && InternetArchiveProcessor.ValidYears.Contains(year))
            {
                Mind.Instance.ConfigDb.SettingSet(ConfigNames.InternetArchiveYear, year);
            }

            Response.SetFound(Request.Headers["Referer"]);

            Response.Handled = true;
        }
    }

    [Controller("/api/proto/toggle")]
    public async Task ProtoWebToggle()
    {
        if (!Request.FormData.ContainsKey("toggle"))
        {
            await Task.Delay(0);

            return;
        }

        Mind.Instance.ConfigDb.SettingSet(ConfigNames.ProtoWeb, Request.FormData["toggle"].ToLower() != "disable");

        Response.SetFound(Request.Headers["Referer"]);

        Response.Handled = true;
    }
}
