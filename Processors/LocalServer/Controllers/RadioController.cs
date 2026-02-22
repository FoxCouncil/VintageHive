// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

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
    // Ad banner helpers — pick random ad image from embedded resources
    // ===================================================================

    private static readonly string AdResourcePrefix = "controllers.ads.hive.com.img.";

    private static string GetRandomAdImageUrl()
    {
        var adKeys = Resources.Statics.Keys
            .Where(k => k.StartsWith(AdResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (adKeys.Count == 0) return null;

        var key = adKeys[Random.Shared.Next(adKeys.Count)];
        var filename = key[AdResourcePrefix.Length..];
        return $"http://ads.hive.com/img/{filename}";
    }

    // ===================================================================
    // Play path routing — /play/{id}/{player}.{ext}
    // Serves playlist/metafiles that point to /stream/{player}?id={id}
    //
    // Supported players:
    //   winamp  → .pls  → /stream/winamp
    //   wmp     → .asx  → /stream/wmp
    //   (future: itunes → .m3u, real → .ram, etc.)
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
                case "wmp" when ext == "asx":
                {
                    var esc = (string s) => System.Security.SecurityElement.Escape(s ?? "");

                    var asx = new StringBuilder();
                    asx.AppendLine("<asx version=\"3.0\">");
                    asx.AppendLine($"  <title>{esc(info.Name)}</title>");
                    asx.AppendLine($"  <author>VintageHive/{Mind.ApplicationVersion}</author>");

                    if (!string.IsNullOrEmpty(info.Tags))
                        asx.AppendLine($"  <abstract>{esc(info.Tags)}</abstract>");

                    // Banner: random ad image (194x32 area in WMP 6.4)
                    var adUrl = GetRandomAdImageUrl();
                    if (adUrl != null)
                    {
                        asx.AppendLine($"  <banner href=\"{esc(adUrl)}\">");
                        asx.AppendLine($"    <abstract>{esc(info.Name)}</abstract>");
                        asx.AppendLine($"    <moreinfo href=\"http://radio.hive.com/browser.html?id={id}\" />");
                        asx.AppendLine("  </banner>");
                    }

                    asx.AppendLine("  <entry clientskip=\"no\">");
                    asx.AppendLine($"    <title>{esc(info.CurrentTrack ?? info.Name)}</title>");
                    asx.AppendLine($"    <author>VintageHive/{Mind.ApplicationVersion}</author>");

                    if (!string.IsNullOrEmpty(info.Country))
                        asx.AppendLine($"    <copyright>{esc(info.Country)}</copyright>");

                    // .asf extension triggers NSPlayer/WMSP pipeline for MMSH streaming
                    asx.AppendLine($"    <ref href=\"http://radio.hive.com/stream/wmp/{id}.asf\" />");
                    asx.AppendLine("  </entry>");
                    asx.AppendLine("</asx>");

                    SetPlaylistResponseHeaders();
                    Response.SetBodyString(asx.ToString(), "application/x-ms-asf");
                    break;
                }
            }
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

    private void SetPlaylistResponseHeaders()
    {
        Response.Headers.Add("Pragma", "public");
        Response.Headers.Add("Cache-Control", "must-revalidate, post-check=0, pre-check=0");
        Response.Headers.Add("Expires", "0");
    }
}
