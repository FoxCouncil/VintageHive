// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using System.Web;
using VintageHive.Processors.LocalServer.Streaming;
using static VintageHive.Proxy.Http.HttpUtilities;
using static VintageHive.Utilities.SCUtils;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("radio.hive.com")]
internal class RadioController : Controller
{
    // ===================================================================
    // Play path routing — /play/{id}/{player}.{ext}
    // Serves playlist/metafiles that point to /stream/{player}?id={id}
    //
    // Supported players:
    //   winamp  → .pls  → /stream/winamp
    //   wmp     → .asx  → /stream/wmp
    //   real    → .ram  → pnm://{ip}:7070/stream/real
    //   (future: itunes → .m3u, etc.)
    // ===================================================================

    private static bool TryParsePlayPath(string rawPath, out string id, out string player, out string ext)
    {
        id = null; player = null; ext = null;

        if (!rawPath.StartsWith("/play/")) return false;

        var rest = rawPath["/play/".Length..];
        var slashIdx = rest.LastIndexOf('/');
        if (slashIdx < 1) return false;

        id = rest[..slashIdx];
        var filename = rest[(slashIdx + 1)..];

        var dotIdx = filename.LastIndexOf('.');
        if (dotIdx < 1) return false;

        player = filename[..dotIdx];
        ext = filename[(dotIdx + 1)..];

        return !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(player);
    }

    public override async Task CallInitial(string rawPath)
    {
        Response.Context.SetValue("menu", new[] {
            "Browser",
            "Shoutcast",
        });

        if (TryParsePlayPath(rawPath, out var id, out var player, out var ext))
        {
            var info = await RadioStationResolver.ResolveStation(id);

            switch (player)
            {
                case "winamp" when ext == "pls":
                {
                    var pls = new StringBuilder();
                    pls.AppendLine("[playlist]");
                    pls.AppendLine($"File1=http://radio.hive.com/stream/winamp?id={id}");
                    pls.AppendLine($"Title1={info.Name}");
                    pls.AppendLine("Length1=-1");
                    pls.AppendLine("NumberOfEntries=1");

                    SetPlaylistResponseHeaders();
                    Response.SetBodyString(pls.ToString(), "audio/x-scpls");
                    break;
                }
                case "real" when ext == "ram":
                {
                    var serverIp = Mind.Db.ConfigGet<string>(ConfigNames.IpAddress);
                    if (serverIp == "0.0.0.0")
                    {
                        serverIp = ((IPEndPoint)Request.ListenerSocket.RawSocket.LocalEndPoint).Address.MapToIPv4().ToString();
                    }

                    var ram = $"http://radio.hive.com/stream/real/{id}.ra\n";
                    SetPlaylistResponseHeaders();
                    Response.SetBodyString(ram, "audio/x-pn-realaudio");
                    break;
                }
                case "wmp" when ext == "asx":
                {
                    var esc = (string s) => System.Security.SecurityElement.Escape(s ?? "");

                    var serverIp = Mind.Db.ConfigGet<string>(ConfigNames.IpAddress);
                    if (serverIp == "0.0.0.0")
                    {
                        serverIp = ((IPEndPoint)Request.ListenerSocket.RawSocket.LocalEndPoint).Address.MapToIPv4().ToString();
                    }

                    // Minimal ASX — only station name as title (shown in WMP playlist pane).
                    // No author/copyright/abstract so WMP doesn't pre-populate Now Playing
                    // metadata fields — those come from the stream's $M and ASF header.
                    var asx = new StringBuilder();
                    asx.AppendLine("<asx version=\"3.0\">");
                    asx.AppendLine($"  <title>{esc(info.Name)}</title>");

                    // Banner: per-station generated image (194x32 area in WMP)
                    asx.AppendLine($"  <banner href=\"http://radio.hive.com/banner/{id}.gif\">");
                    asx.AppendLine($"    <moreinfo href=\"http://radio.hive.com/browser.html?id={id}\" />");
                    asx.AppendLine("  </banner>");

                    asx.AppendLine("  <entry clientskip=\"no\">");
                    // MMS first — protocol rollover scheme (RTSP → MMS/TCP → HTTP)
                    asx.AppendLine($"    <ref href=\"mms://{serverIp}/stream/wmp/{id}.asf\" />");
                    // HTTP fallback (direct MMSH)
                    asx.AppendLine($"    <ref href=\"http://radio.hive.com/stream/wmp/{id}.asf\" />");
                    asx.AppendLine("  </entry>");
                    asx.AppendLine("</asx>");

                    SetPlaylistResponseHeaders();
                    Response.SetBodyString(asx.ToString(), "application/x-ms-asf");
                    break;
                }
            }
        }

        // Banner image: /banner/{id}.gif — per-station generated 194x32 banner for WMP
        if (!Response.Handled && rawPath.StartsWith("/banner/") && rawPath.EndsWith(".gif"))
        {
            var bannerStationId = rawPath["/banner/".Length..^4];
            if (!string.IsNullOrEmpty(bannerStationId))
            {
                await ServeBannerImage(bannerStationId);
            }
        }

        // Favicon proxy: /favicon/{id}.jpg — proxies HTTPS favicons over HTTP for WMP9/XP
        if (!Response.Handled && rawPath.StartsWith("/favicon/") && rawPath.EndsWith(".jpg"))
        {
            var faviconStationId = rawPath["/favicon/".Length..^4];
            if (!string.IsNullOrEmpty(faviconStationId))
                await ServeFaviconProxy(faviconStationId);
        }

        // MMSH stream: /stream/wmp/{id}.asf — WMSP/MMSH protocol for WMP
        if (!Response.Handled && rawPath.StartsWith("/stream/wmp/") && rawPath.EndsWith(".asf"))
        {
            var stationId = rawPath["/stream/wmp/".Length..^4]; // strip ".asf"
            if (!string.IsNullOrEmpty(stationId))
            {
                // Route based on NSPlayer version
                Request.Headers.TryGetValue("User-Agent", out var ua);
                var nsMajor = 0;
                var nsMatch = System.Text.RegularExpressions.Regex.Match(ua ?? "", @"NSPlayer/(\d+)");
                if (nsMatch.Success) int.TryParse(nsMatch.Groups[1].Value, out nsMajor);

                if (nsMajor >= 9)
                    await RadioMmshStreaming.HandleWmp9Stream(Request, Response, stationId);
                else
                    await RadioMmshStreaming.HandleWmp6Stream(Request, Response, stationId);
            }
        }

        // SAMI captions: 404 — TEXT script commands handle Now Playing display instead
        if (!Response.Handled && rawPath.EndsWith(".smi"))
        {
            Response.SetNotFound();
        }

        // RealAudio stream: /stream/real/{id}.ra — raw RM container over HTTP
        if (!Response.Handled && rawPath.StartsWith("/stream/real/") && rawPath.EndsWith(".ra"))
        {
            var stationId = rawPath["/stream/real/".Length..^3]; // strip ".ra"
            if (!string.IsNullOrEmpty(stationId))
            {
                await RadioPnaStreaming.HandleRealStream(Request, Response, stationId);
            }
        }

        // Plain HTTP MP3 stream: /stream/wmp/{id}.mp3 — fallback for non-WMSP clients
        if (!Response.Handled && rawPath.StartsWith("/stream/wmp/") && rawPath.EndsWith(".mp3"))
        {
            var stationId = rawPath["/stream/wmp/".Length..^4]; // strip ".mp3"
            if (!string.IsNullOrEmpty(stationId))
            {
                await RadioMp3Streaming.HandleWmpMp3Stream(Request, Response, stationId);
            }
        }
    }

    // ===================================================================
    // Page routes
    // ===================================================================

    [Route("/index.html")]
    public async Task Index()
    {
        var top10countries = Mind.RadioBrowser.ListGetCountries(10);

        Response.Context.SetValue("top10countries", top10countries);

        var top10Tags = Mind.RadioBrowser.ListGetTags(10);

        Response.Context.SetValue("top10tags", top10Tags);

        var list = await Mind.RadioBrowser.StationsGetByClicksAsync(100);

        Response.Context.SetValue("stations", list);
    }

    [Route("/browser.html")]
    public async Task BrowserIndex()
    {
        Response.Context.SetValue("stats", await Mind.RadioBrowser.ServerStatsAsync());

        if (Request.QueryParams.ContainsKey("country"))
        {
            var countryCodeStripped = Request.QueryParams["country"][..2];

            var countryName = Mind.Geonames.GetCountryNameByIso(countryCodeStripped);

            if (string.IsNullOrWhiteSpace(countryName))
            {
                Response.Context.SetValue("error", $"This country code [{countryCodeStripped.ToUpper()}] is not a valid country, please check your input and try again.");
            }
            else
            {
                var stationsbycountry = await Mind.RadioBrowser.StationsByCountryCodePagedAsync(countryCodeStripped);

                Response.Context.SetValue("countrycode", countryCodeStripped);
                Response.Context.SetValue("countryname", countryName);
                Response.Context.SetValue("stationsbycountry", stationsbycountry);
            }
        }
        else if (Request.QueryParams.ContainsKey("tag"))
        {
            var tagName = Request.QueryParams["tag"];

            var stationsbytag = await Mind.RadioBrowser.StationsByTagPagedAsync(tagName);

            Response.Context.SetValue("tagname", tagName);
            Response.Context.SetValue("stationsbytag", stationsbytag);
        }
        else if (Request.QueryParams.ContainsKey("q"))
        {
            var searchTerm = Request.QueryParams["q"];

            var stationsbysearch = await Mind.RadioBrowser.StationsBySearchPagedAsync(searchTerm);

            Response.Context.SetValue("searchterm", searchTerm);
            Response.Context.SetValue("stationsbysearch", stationsbysearch);
        }
        else if (Request.QueryParams.ContainsKey("id"))
        {
            var station = await Mind.RadioBrowser.StationGetAsync(Request.QueryParams["id"]);

            Response.Context.SetValue("station", station);
        }
        else if (Request.QueryParams.ContainsKey("list") && Request.QueryParams["list"] == "countries")
        {
            var countryList = Mind.RadioBrowser.ListGetCountries();

            Response.Context.SetValue("countrylist", countryList);
        }
        else if (Request.QueryParams.ContainsKey("list") && Request.QueryParams["list"] == "tags")
        {
            var tagList = Mind.RadioBrowser.ListGetTags();

            Response.Context.SetValue("taglist", tagList.Take(2000));
        }
        else
        {
            var list = await Mind.RadioBrowser.StationsGetByClicksAsync();

            Response.Context.SetValue("stations", list);
        }
    }

    [Route("/shoutcast.html")]
    public async Task ShoutcastIndex()
    {
        List<ShoutcastStation> list;

        if (Request.QueryParams.ContainsKey("q"))
        {
            var query = HttpUtility.UrlEncode(Request.QueryParams["q"]);

            list = await StationSearch(query);

            Response.Context.SetValue("pagetitle", $"({Request.QueryParams["q"]}) Results");
        }
        else if (Request.QueryParams.ContainsKey("genre"))
        {
            var genreQuery = HttpUtility.UrlEncode(Request.QueryParams["genre"]);

            list = await StationSearchByGenre(genreQuery);

            Response.Context.SetValue("pagetitle", $"({Request.QueryParams["genre"]}) Genre");
        }
        else if (Request.QueryParams.ContainsKey("list") && Request.QueryParams["list"] == "genre")
        {
            var genreList = await GetGenres();

            Response.Context.SetValue("pagetitle", $"Genre List");
            Response.Context.SetValue("genres", genreList);

            return;
        }
        else
        {
            list = await GetTop500();

            Response.Context.SetValue("pagetitle", "Global Top500");
        }

        Response.Context.SetValue("stations", list);
    }

    // ===================================================================
    // Stream endpoints — thin wrappers into streaming classes
    // ===================================================================

    [Route("/stream/winamp")]
    public async Task StreamWinamp() =>
        await RadioMp3Streaming.HandleWinampStream(Request, Response);

    [Route("/browser.mp3")]
    public async Task BrowserPlay() =>
        await RadioMp3Streaming.HandleBrowserPlay(Request, Response);

    [Route("/shoutcast.mp3")]
    public async Task SCPlay() =>
        await RadioMp3Streaming.HandleShoutcastPlay(Request, Response);

    // ===================================================================
    // Helpers
    // ===================================================================

    private async Task ServeBannerImage(string stationId)
    {
        try
        {
            var bannerPath = $"data/banners/{stationId}.gif";

            if (VFS.FileExists(bannerPath))
            {
                var cached = await VFS.FileReadDataAsync(bannerPath);
                Response.SetBodyData(cached, "image/gif");
                return;
            }

            var info = await RadioStationResolver.ResolveStation(stationId);

            if (info == null)
            {
                Response.SetNotFound();
                return;
            }

            // Try to get favicon bytes (reuse same cache as ServeFaviconProxy)
            byte[] faviconBytes = null;

            try
            {
                var cacheKey = $"radio_favicon_{stationId}";
                var faviconCached = Mind.Cache.GetData(cacheKey);

                if (!string.IsNullOrEmpty(faviconCached))
                {
                    faviconBytes = Convert.FromBase64String(faviconCached);
                }
                else if (!string.IsNullOrEmpty(info.Favicon))
                {
                    using var client = HttpClientUtils.GetHttpClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    faviconBytes = await client.GetByteArrayAsync(info.Favicon);
                    Mind.Cache.SetData(cacheKey, TimeSpan.FromHours(24), Convert.ToBase64String(faviconBytes));
                }
            }
            catch
            {
                // Favicon fetch failed — generate without it
            }

            var gifBytes = BannerGenerator.Generate(info.Name, faviconBytes);

            // Ensure the banners directory exists
            if (!VFS.DirectoryExists("data/banners"))
            {
                VFS.DirectoryCreate("data/banners");
            }

            using (var fs = VFS.FileWrite(bannerPath))
            {
                fs.Write(gifBytes, 0, gifBytes.Length);
            }

            Response.SetBodyData(gifBytes, "image/gif");
        }
        catch
        {
            Response.SetNotFound();
        }
    }

    private async Task ServeFaviconProxy(string stationId)
    {
        try
        {
            var cacheKey = $"radio_favicon_{stationId}";
            var cached = Mind.Cache.GetData(cacheKey);
            byte[] imageBytes;

            if (!string.IsNullOrEmpty(cached))
            {
                imageBytes = Convert.FromBase64String(cached);
            }
            else
            {
                var info = await RadioStationResolver.ResolveStation(stationId);
                if (string.IsNullOrEmpty(info.Favicon))
                {
                    Response.SetNotFound();
                    return;
                }

                using var client = HttpClientUtils.GetHttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                imageBytes = await client.GetByteArrayAsync(info.Favicon);

                Mind.Cache.SetData(cacheKey, TimeSpan.FromHours(24), Convert.ToBase64String(imageBytes));
            }

            // Determine MIME type from first bytes
            var contentType = "image/jpeg";
            if (imageBytes.Length >= 4)
            {
                if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50) contentType = "image/png";
                else if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49) contentType = "image/gif";
            }

            Response.SetBodyData(imageBytes, contentType);
        }
        catch
        {
            Response.SetNotFound();
        }
    }

    private void SetPlaylistResponseHeaders()
    {
        Response.Headers.Add("Pragma", "public");
        Response.Headers.Add("Cache-Control", "must-revalidate, post-check=0, pre-check=0");
        Response.Headers.Add("Expires", "0");
    }
}
