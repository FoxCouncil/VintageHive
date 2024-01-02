// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Http;

namespace VintageHive.Processors;

internal static class HelperProcessor
{
    private const string EXT_DOCS_BLOOBERRY = "blooberry.com";

    private const string EXT_GAMES_ANDKON = "andkon.com";
    private const string EXT_GAMES_DREAMPIPE = "dreampipe.net";
    private const string EXT_GAMES_HALO = "halo.bungie.org";

    private const string EXT_HOMEPAGE_ERIC_EXPERIMENT = "ericexperiment.com";

    private const string EXT_MUSIC_MIRSOFT = "mirsoft.info";

    private const string EXT_NEWS_68K = "68k.news";

    private const string EXT_SERP_FROG_FIND = "frogfind.com";
    private const string EXT_SERP_OLDAVISTA = "oldavista.com";

    private const string EXT_USENET_ETERNAL_SEPTEMBER = "eternal-september.org";

    private const string EXT_WEB_RAZORBACK95 = "razorback95.com";
    private const string EXT_WEB_RETROFOX = "retrofox.gay";

    private const string EXT_WEBTV_REDIALED = "webtv.zone";

    private const string EXT_WIN_UPDATE_RESTORED = "windowsupdaterestored.com";

    static readonly List<string> ExternalPassthroughDomains = new() {
        EXT_DOCS_BLOOBERRY,
        EXT_GAMES_ANDKON,
        EXT_GAMES_DREAMPIPE,
        EXT_GAMES_HALO,
        EXT_HOMEPAGE_ERIC_EXPERIMENT,
        EXT_MUSIC_MIRSOFT,
        EXT_NEWS_68K,
        EXT_SERP_FROG_FIND,
        EXT_SERP_OLDAVISTA,
        EXT_USENET_ETERNAL_SEPTEMBER,
        EXT_WEB_RAZORBACK95,
        EXT_WEB_RETROFOX,
        EXT_WEBTV_REDIALED,
        EXT_WIN_UPDATE_RESTORED,
    };

    private const string SERVICE_REAL_CHANNELS = "channels.real.com";

    private static readonly List<string> ServiceSupportDomains = new() {
        SERVICE_REAL_CHANNELS,
    };

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        if (ExternalPassthroughDomains.Any(x => req.Host.ToLower().EndsWith(x)))
        {
            return await ProcessExternalPassthroughRequest(req, res);
        }

        if (ServiceSupportDomains.Any(x => req.Host.ToLower().EndsWith(x)))
        {
            return ProcessServiceSupportRequest(req, res);
        }

        return false;
    }

    private static async Task<bool> ProcessExternalPassthroughRequest(HttpRequest req, HttpResponse res)
    {
        await res.SetExternal();

        res.Cache = false;

        Log.WriteLine(Log.LEVEL_INFO, nameof(HelperProcessor), $"Helping; by sending to external -> {req.Uri}", req.ListenerSocket.TraceId.ToString());

        return true;
    }

    private static bool ProcessServiceSupportRequest(HttpRequest req, HttpResponse res)
    {
        var domain = req.Host.ToLower();

        res.Cache = true;

        switch (domain)
        {
            case SERVICE_REAL_CHANNELS:
            {
                res.SetBodyString("<rn-channels></rn-channels>", "text/xml");
            }
            break;
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(HelperProcessor), $"Helping; by servicing a request from -> {req.Uri}", req.ListenerSocket.TraceId.ToString());

        return true;
    }
}
