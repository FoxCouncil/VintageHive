// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Web;
using static VintageHive.Proxy.Http.HttpUtilities;
using static VintageHive.Utilities.SCUtils;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("radio.hive.com")]
internal class RadioController : Controller
{
    public override async Task CallInitial(string rawPath)
    {
        await Task.Delay(0);

        Response.Context.SetValue("menu", new[] {
            "Browser",
            "Shoutcast",
            // "Podcasts",
        });
    }

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

    [Route("/browser.pls")]
    public Task BrowserListenPlaylistPls()
    {
        var plsResponse = new StringBuilder();

        plsResponse.AppendLine("[playlist]");
        plsResponse.AppendLine($"File1=http://radio.hive.com/browser.mp3?id={Request.QueryParams["id"]}");
        plsResponse.AppendLine("NumberOfEntries=1");

        Response.Headers.Add(HttpHeaderName.ContentDisposition, "attachment; filename=\"browser.pls\"");

        Response.SetBodyString(plsResponse.ToString(), "audio/x-scpls");

        return Task.CompletedTask;
    }

    [Route("/browser.asx")]
    public Task BrowserListenPlaylistAsx()
    {
        var asxResponse = new StringBuilder();

        asxResponse.AppendLine("<asx version=\"3.0\">");
        asxResponse.AppendLine($"<entry><ref href=\"http://radio.hive.com/browser.mp3?id={Request.QueryParams["id"]}\" /></entry>");
        asxResponse.AppendLine("</asx>");

        Response.Headers.Add(HttpHeaderName.ContentDisposition, "attachment; filename=\"browser.asx\"");

        Response.SetBodyString(asxResponse.ToString(), "video/x-ms-asf");

        return Task.CompletedTask;
    }

    [Route("/browser.mp3")]
    public async Task BrowserPlay()
    {
        var station = await Mind.RadioBrowser.StationGetAsync(Request.QueryParams["id"]);

        using var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
        });

        if (Request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, Request.Headers[HttpHeaderName.UserAgent]);
        }

        if (station.Codec.ToLower() == "mp3" && Request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        using var client = await httpClient.GetAsync(station.UrlResolved, HttpCompletionOption.ResponseHeadersRead);

        using var clientStream = await client.Content.ReadAsStreamAsync();

        if (station.Codec.ToLower() != "mp3")
        {
            using var process = CreateFfmpegProcess();

            Response.Headers.Add(HttpHeaderName.ContentType, HttpContentType.Audio.Mpeg);

            Response.Headers.Add("Icy-Name", station.Name + $" [Codec:{station.Codec}]");

            process.Start();

            try
            {
                await Request.ListenerSocket.Stream.WriteAsync(Response.GetResponseEncodedData());

                Task.WaitAny(clientStream.CopyToAsync(process.StandardInput.BaseStream), process.StandardOutput.BaseStream.CopyToAsync(Request.ListenerSocket.Stream));
            }
            catch (IOException)
            {
                // NOOP
            }
            
            process.Kill();

            Response.Handled = true;
        }
        else
        {
            foreach (var header in client.Headers)
            {
                if (header.Key.ToLower().StartsWith("icy"))
                {
                    Response.Headers.Add(header.Key, header.Value.First());
                }
            }

            Response.SetBodyStream(clientStream, HttpContentType.Audio.Mpeg);
            try
            {
                await Request.ListenerSocket.Stream.WriteAsync(Response.GetResponseEncodedData());

                await clientStream.CopyToAsync(Request.ListenerSocket.Stream);
            }
            catch (IOException)
            {
                // NOOP
            }
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

    [Route("/shoutcast.pls")]
    public Task ListenPlaylistPls()
    {
        var plsResponse = new StringBuilder();

        plsResponse.AppendLine("[playlist]");
        plsResponse.AppendLine($"File1=http://radio.hive.com/shoutcast.mp3?id={Request.QueryParams["id"]}");
        plsResponse.AppendLine("NumberOfEntries=1");

        Response.Headers.Add(HttpHeaderName.ContentDisposition, "attachment; filename=\"shoutcast.pls\"");

        Response.SetBodyString(plsResponse.ToString(), "audio/x-scpls");

        return Task.CompletedTask;
    }

    [Route("/shoutcast.asx")]
    public Task ListenPlaylistAsx()
    {
        var asxResponse = new StringBuilder();

        asxResponse.AppendLine("<asx version=\"3.0\">");
        asxResponse.AppendLine($"<entry><ref href=\"http://radio.hive.com/shoutcast.mp3?id={Request.QueryParams["id"]}\" /></entry>");
        asxResponse.AppendLine("</asx>");

        Response.Headers.Add(HttpHeaderName.ContentDisposition, "attachment; filename=\"shoutcast.asx\"");

        Response.SetBodyString(asxResponse.ToString(), "video/x-ms-asf");

        return Task.CompletedTask;
    }

    [Route("/shoutcast.mp3")]
    public async Task SCPlay()
    {
        var station = await GetStationById(Request.QueryParams["id"]);

        var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
        });

        if (Request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, Request.Headers[HttpHeaderName.UserAgent]);
        }

        var details = station.Item1;

        var stationCodec = GetFormatString(details.Mt);

        if (stationCodec == "MP3" && Request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        var client = await httpClient.GetAsync(station.Item2, HttpCompletionOption.ResponseHeadersRead);

        foreach (var header in client.Headers)
        {
            if (header.Key.ToLower().StartsWith("icy"))
            {
                Response.Headers.Add(header.Key, header.Value.First());
            }
        }

        var clientStream = await client.Content.ReadAsStreamAsync();

        if (stationCodec != "MP3")
        {
            using var process = CreateFfmpegProcess();

            Response.Headers.Add(HttpHeaderName.ContentType, HttpContentType.Audio.Mpeg);

            Response.Headers.Add("Icy-Name", details.Name + $" [Codec:{stationCodec}]");

            process.Start();

            try
            {
                await Request.ListenerSocket.Stream.WriteAsync(Response.GetResponseEncodedData());

                Task.WaitAny(clientStream.CopyToAsync(process.StandardInput.BaseStream), process.StandardOutput.BaseStream.CopyToAsync(Request.ListenerSocket.Stream));
            }
            catch (IOException)
            {
                // NOOP
            }

            process.Kill();

            Response.Handled = true;
        }
        else
        {
            Response.SetBodyStream(clientStream, HttpContentType.Audio.Mpeg);
        }
    }

    private static Process CreateFfmpegProcess()
    {
        var cmdPath = GetFfmpegExecutablePath();
        var argsff = "-i pipe:0 -c:a libmp3lame -f mp3 pipe:1";

        var process = new Process();

        process.StartInfo.FileName = cmdPath;
        process.StartInfo.Arguments = argsff;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;

        return process;
    }

    private static string GetFfmpegExecutablePath()
    {
        if (!Environment.Is64BitProcess)
        {
            throw new ApplicationException("Somehow, it's not x64? Everything VintageHive is 64bit. What?");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"libs\ffmpeg.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return @"libs\ffmpeg.osx.intel";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return @"libs\ffmpeg.amd64";
        }

        throw new Exception("Cannot determine operating system!");
    }
}
