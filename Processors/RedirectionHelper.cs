using VintageHive.Proxy.Http;

namespace VintageHive.Processors;

public static class RedirectionHelper
{
    public static List<string> RedirectionDomains = new() { "rtd.yahoo.com" };
    
    internal static async Task<bool> ProcessRequest(HttpRequest req, HttpResponse res)
    {
        await Task.Delay(0);

        return false;
    }
}