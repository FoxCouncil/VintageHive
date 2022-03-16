using HeyRed.Mime;
using HtmlAgilityPack;
using LibFoxyProxy.Http;
using System.Diagnostics;
using VintageHive.Utilities;
using static LibFoxyProxy.Http.HttpUtilities;

namespace VintageHive.Processors;

internal static class IntranetProcessor
{
    const string RemoteControlUri = "control";

    const string IntranetHomeUri = "home";

    const string IntranetWeatherUri = "weather";

    const string IntranetNewsUri = "news";

    public static async Task<bool> ProcessRequest(HttpRequest req, HttpResponse res)
    {
        var isRemoteControlEnabled = Mind.Instance.ConfigDb.SettingGet<bool>("remotecontrol");

        if (isRemoteControlEnabled && req.Uri.Host.Equals(RemoteControlUri))
        {
            return await HandleControlRequest(req, res);
        }

        var isIntranetEnabled = Mind.Instance.ConfigDb.SettingGet<bool>("intranet");

        if (isIntranetEnabled)
        {
            return await HandleIntranetRequest(req, res);
        }

        return false;
    }

    private static async Task<bool> HandleControlRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        if (req.Uri.Segments.Length == 1 || req.Uri.Segments[1] == "index.html")
        {
            var htmlDocument = new HtmlDocument();

            htmlDocument.LoadVirtual("control/index.html");

            res.SetBodyString(htmlDocument.DocumentNode.OuterHtml);

            return true;
        }
        else if (req.Uri.Segments.Length == 3 && req.Uri.Segments[1] == "assets/")
        {
            var resData = Resources.GetStaticsResourceData($"control{string.Concat(req.Uri.Segments)}");

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
            switch (req.Uri.Segments[2])
            {
                case "clearcache":
                {
                    res.SetBodyString("Cache is cleared!");

                    return true;
                }

                default:
                {
                    res.SetBodyString("Unknown action.");

                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<bool> HandleIntranetRequest(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        return req.Uri.Host.ToLowerInvariant() switch
        {
            IntranetHomeUri => await HandleIntranetHomeRequest(req, res),
            IntranetWeatherUri => await HandleIntranetWeatherRequest(req, res),
            IntranetNewsUri => await HandleIntranetNewsRequest(req, res),
            _ => false,
        };
    }

    private static async Task<bool> HandleIntranetHomeRequest(HttpRequest req, HttpResponse res)
    {
        res.SetBodyString("home goes here", HttpContentType.Text.Plain);

        return true;
    }

    private static async Task<bool> HandleIntranetWeatherRequest(HttpRequest req, HttpResponse res)
    {
        res.SetBodyString("weather goes here", HttpContentType.Text.Plain);

        return true;
    }

    private static async Task<bool> HandleIntranetNewsRequest(HttpRequest req, HttpResponse res)
    {
        if (req.Uri.Segments.Length == 1 || req.Uri.Segments[1] == "index.html")
        {
            var htmlDocument = new HtmlDocument();

            htmlDocument.LoadVirtual("news/index.html");

            res.SetBodyString(htmlDocument.DocumentNode.OuterHtml);

            return true;
        }
        else if (req.Uri.Segments.Length == 3 && req.Uri.Segments[1] == "assets/")
        {
            var resData = Resources.GetStaticsResourceData($"news{string.Concat(req.Uri.Segments)}");

            if (resData != null)
            {
                var mimeType = MimeTypesMap.GetMimeType(req.Uri.Segments.Last());

                res.SetBodyData(resData, mimeType);

                return true;
            }

            return false;
        }
        else if (req.Uri.Segments.Length == 3 && req.Uri.Segments[1] == "article/")
        {
            switch (req.Uri.Segments[2])
            {
                case "clearcache":
                {
                    res.SetBodyString("Cache is cleared!");

                    return true;
                }

                default:
                {
                    res.SetBodyString("Unknown action.");

                    return true;
                }
            }
        }

        return true;
    }
}
