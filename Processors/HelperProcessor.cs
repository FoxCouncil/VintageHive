// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Http;

namespace VintageHive.Processors;

internal static class HelperProcessor
{
    private const string GAMES_ANDKON = "andkon.com";
    private const string GAMES_DREAMPIPE = "dreampipe.net";
    private const string GAMES_HALO = "halo.bungie.org";

    private const string HOMEPAGE_ERIC_EXPERIMENT = "ericexperiment.com";

    private const string MUSIC_MIRSOFT = "mirsoft.info";

    private const string NEWS_68K = "68k.news";

    private const string SERP_FROG_FIND = "frogfind.com";
    private const string SERP_OLDAVISTA = "oldavista.com";

    private const string USENET_ETERNAL_SEPTEMBER = "eternal-september.org";

    private const string WEB_RAZORBACK95 = "razorback95.com";
    private const string WEB_RETROFOX = "retrofox.gay";

    private const string WEBTV_REDIALED = "webtv.zone";

    private const string WIN_UPDATE_RESTORED = "windowsupdaterestored.com";

    static readonly List<string> ExternalPassthroughDomains = new() {
        GAMES_ANDKON,
        GAMES_DREAMPIPE,
        GAMES_HALO,
        HOMEPAGE_ERIC_EXPERIMENT,
        MUSIC_MIRSOFT,
        NEWS_68K,
        SERP_FROG_FIND,
        SERP_OLDAVISTA,
        USENET_ETERNAL_SEPTEMBER,
        WEB_RAZORBACK95,
        WEB_RETROFOX,
        WEBTV_REDIALED,
        WIN_UPDATE_RESTORED,
    };

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        if (ExternalPassthroughDomains.Any(x => req.Host.ToLower().EndsWith(x)))
        {
            return await ProcessExternalPassthroughRequest(req, res);
        }

        return false;
    }

    private static async Task<bool> ProcessExternalPassthroughRequest(HttpRequest req, HttpResponse res)
    {
        // await res.PassthroughConnection();

        Log.WriteLine(Log.LEVEL_INFO, nameof(HelperProcessor), $"Helping; by sending to external -> {req.Uri}", req.ListenerSocket.TraceId.ToString());

        return true;
    }
}
