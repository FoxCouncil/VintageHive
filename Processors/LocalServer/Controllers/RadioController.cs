// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("radio.hive.com")]
internal class RadioController : Controller
{
    [Route("/index.html")]
    public async Task Index()
    {
        var list = await SCUtils.GetTop500();

        Response.Context.SetValue("stations", list.Stations);
    }

    [Route("/scplay.pls")]
    public Task ListenPlaylist()
    {
        var plsResponse = new StringBuilder();

        plsResponse.AppendLine("[playlist]");
        plsResponse.AppendLine($"File1=http://radio.hive.com/scplay.mp3?id={Request.QueryParams["id"]}");
        plsResponse.AppendLine("NumberOfEntries=1");

        Response.SetBodyString(plsResponse.ToString(), "audio/x-scpls");

        return Task.CompletedTask;
    }

    [Route("/scplay.mp3")]
    public async Task SCPlay()
    {
        var station = await SCUtils.GetStationById(Request.QueryParams["id"]);

        var httpClient = HttpClientUtils.GetHttpClient(null, new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3
        });

        if (Request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, Request.Headers[HttpHeaderName.UserAgent]);
        }

        if (Request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
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

        Response.SetBodyStream(clientStream, HttpContentType.Audio.Mpeg);
    }
}
