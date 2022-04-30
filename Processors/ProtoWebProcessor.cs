using LibFoxyProxy.Http;
using System.Diagnostics;
using VintageHive.Utilities;

namespace VintageHive.Processors;

internal static class ProtoWebProcessor
{
    public static List<string> AvailableSites;

    public static async Task<bool> ProcessRequest(HttpRequest req, HttpResponse res)
    {
        var protoWebEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.ProtoWeb);

        if (!protoWebEnabled)
        {
            return false;
        }
        
        if (AvailableSites == null)
        {
            AvailableSites = await ProtoWebUtils.GetAvailableSites();
            
            if (AvailableSites == null)
            {                
                return false;
            }
        }

        if (AvailableSites.Any(x => req.Uri.Host.EndsWith(x)))
        {
            var proxyClient = Clients.GetProxiedHttpClient(req, ProtoWebUtils.MainProxyUri);

            try
            {
                var proxyRes = await proxyClient.GetAsync(req.Uri);

                var contentType = proxyRes.Content.Headers.ContentType?.ToString() ?? "text/html";

                res.SetBodyData(await proxyClient.GetByteArrayAsync(req.Uri), contentType);

                res.CacheTtl = TimeSpan.FromDays(30);

                Console.WriteLine($"[{"ProtoWeb", 15} Request] ({req.Uri}) [{contentType}]");

                return true;
            }
            catch (HttpRequestException ex) { }
            catch (Exception ext)
            {
                Debugger.Break();
            }

            return false;
        }

        return false;
    }
}
