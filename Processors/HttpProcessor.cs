using LibFoxyProxy.Http;
using VintageHive.Processors.InternetArchive;
using static LibFoxyProxy.Http.HttpUtilities;

namespace VintageHive.Processors;

internal static class HttpProcessor
{
    const string RemoteControlUri = "control";

    const string IntranetHomeUri = "home";

    const string IntranetWeatherUri = "weather";

    const string IntranetNewsUri = "news";

    internal static HttpResponse ProcessRequest(HttpRequest req)
    {
        if (req.Uri == null)
        {
            return null;
        }

        if (Mind.Config.RemoteControl && req.Uri.HostContains(RemoteControlUri))
        {
            return new HttpResponse(req).SetBodyString("control arf", HttpContentType.Text.Plain);
        }

        if (Mind.Config.Intranet)
        {
            var request = HandleIntranetRequest(req);

            if (request != null)
            {
                return request;
            }
        }

        if (Mind.Config.InternetArchive)
        {
            var request = HandleInternetArchiveRequest(req);

            if (request != null)
            {
                return request;
            }
        }

        return null;
    }

    private static HttpResponse? HandleInternetArchiveRequest(HttpRequest req)
    {
        return new InternetArchiveRequest(req).Response;
    }

    private static HttpResponse? HandleIntranetRequest(HttpRequest req)
    {
        return req.Uri.Host switch
        {
            IntranetHomeUri => HandleIntranetHomeRequest(req),
            IntranetWeatherUri => HandleIntranetWeatherRequest(req),
            IntranetNewsUri => HandleIntranetNewsRequest(req),
            _ => null,
        };
    }

    private static HttpResponse? HandleIntranetHomeRequest(HttpRequest req)
    {
        return new HttpResponse(req).SetBodyString("home goes here", HttpContentType.Text.Plain);
    }

    private static HttpResponse? HandleIntranetWeatherRequest(HttpRequest req)
    {
        return new HttpResponse(req).SetBodyString("weather goes here", HttpContentType.Text.Plain);
    }

    private static HttpResponse? HandleIntranetNewsRequest(HttpRequest req)
    {
        return new HttpResponse(req).SetBodyString("news goes here", HttpContentType.Text.Plain);
    }
}