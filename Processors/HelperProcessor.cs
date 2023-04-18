using VintageHive.Proxy.Http;
using VintageHive.Utilities;

namespace VintageHive.Processors;

internal static class HelperProcessor
{
    /* The authrootseq.txt file contains information about the current version of the trusted root certificate list.
     * When a Windows system needs to update its list of trusted root certificates,
     * it downloads the authrootseq.txt file to determine if there's a newer version available. */
    /* http://www.download.windowsupdate.com/msdownload/update/v3/static/trustedr/en/authrootseq.txt */
    private const string WIN_UPDATE_DOWNLOAD_WWW = "www.download.windowsupdate.com";

    static readonly List<string> CompatDomains = new() { WIN_UPDATE_DOWNLOAD_WWW };

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        //if (CompatDomains.Contains(req.Host))
        //{
        //    switch (req.Host)
        //    {
        //        case WIN_UPDATE_DOWNLOAD_WWW: return await ProcessWindowsUpdateCompat(req, res);
        //    }
        //}

        return false;
    }

    private static async Task<bool> ProcessWindowsUpdateCompat(HttpRequest req, HttpResponse res)
    {
        await res.SetExternal();

        Log.WriteLine(Log.LEVEL_INFO, nameof(HelperProcessor), $"Helping; by sending to external -> {req.Uri}", req.ListenerSocket.TraceId.ToString());

        return true;
    }
}
