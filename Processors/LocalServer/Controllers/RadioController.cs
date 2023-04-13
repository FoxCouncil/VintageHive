using AngleSharp.Io;
using Fluid;
using Fluid.Values;
using System.Text;
using VintageHive.Utilities;

namespace VintageHive.Processors.LocalServer.Controllers;

internal class RadioController : Controller
{
    [Controller("/index.html")]
    public async Task Index()
    {
        var list = await SCUtils.GetTop500();

        Response.Context.SetValue("stations", list.Stations);
    }

    [Controller("/listen.pls")]
    public async Task ListenPlaylist()
    {
        var station = await SCUtils.GetStationById(Request.QueryParams["id"]);

        var plsResponse = new StringBuilder();

        plsResponse.AppendLine("[playlist]");

        plsResponse.AppendLine($"File1=http://radio.com/scplay.mp3?id={Request.QueryParams["id"]}");
        plsResponse.AppendLine($"Title1={station.Item1}");

        plsResponse.AppendLine("NumberOfEntries=1");

        Response.SetBodyString(plsResponse.ToString(), "audio/x-scpls");
    }

    [Controller("/scplay.mp3")]
    public async Task SCPlay()
    {
        var station = await SCUtils.GetStationById(Request.QueryParams["id"]);

        var httpClient = HttpClientUtils.GetHttpClient(null, new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3
        });

        var clientStream = await httpClient.GetStreamAsync(station.Item2);

        Response.DownloadStream = clientStream;

        Response.Handled = true;
    }
}
