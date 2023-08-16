// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Http;

namespace VintageHive.Processors;

internal static class DialNineProcessor
{
    static readonly IReadOnlyList<string> BlockedDomains = new List<string> {
        "ssl.google-analytics.com",
        "www.google-analytics.com"
    };

    static readonly IReadOnlyList<string> StrippedDomains = new List<string>
    {
        "oocities.com"
    };

    public static async Task<bool> ProcessHttpsRequest(HttpRequest req, HttpResponse res)
    {
        if (!Mind.Db.ConfigLocalGet<bool>(req.ListenerSocket.RemoteIP, ConfigNames.ServiceDialnine))
        {
            return false;
        }

        if (BlockedDomains.Contains(req.Host.ToLower()))
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(DialNineProcessor), $"Blocking Domain -> {req.Host}", req.ListenerSocket.TraceId.ToString());
            
            return false;
        }

        try
        {
            await res.SetExternal();

            Log.WriteLine(Log.LEVEL_INFO, nameof(DialNineProcessor), $"Forwarding to external -> {req.Uri}", req.ListenerSocket.TraceId.ToString());

        }
        catch (Exception e)
        {
            // Custom Error Messages
            Log.WriteLine(Log.LEVEL_ERROR, nameof(DialNineProcessor), e.Message, req.ListenerSocket.TraceId.ToString());

            res.ErrorMessage = e.Message + "<br><br>" + e.InnerException?.Message ?? "";

            res.SetNotFound();
        }

        return true;
    }
}
