using HtmlAgilityPack;
using System.Text.Json;

namespace VintageHive.Utilities
{
    public static class ProtoWebUtils
    {
        public const string MainProxyUri = "http://wayback.protoweb.org:7851/";
        
        private const string CacheKey = "PROTOWEB_SITE_LIST";
        
        private const string RequestUri = "http://www.inode.com/";

        public static async Task<List<string>> GetAvailableSites()
        {
            var rawList = Mind.Instance.CacheDb.Get<string>(CacheKey);
            
            if (rawList == null)
            {
                var proxyClient = Clients.GetProxiedHttpClient(null, MainProxyUri);

                proxyClient.Timeout = TimeSpan.FromSeconds(10);

                string site;
                
                try
                {
                    site = await proxyClient.GetStringAsync(RequestUri);
                }
                catch (Exception)
                {
                    Console.WriteLine("ProtoWeb is offline! Turning it off!");

                    Mind.Instance.ConfigDb.SettingSet(ConfigNames.ProtoWeb, false);

                    return null;
                }

                var htmlDoc = new HtmlDocument();

                htmlDoc.LoadHtml(site);

                var links = htmlDoc.DocumentNode.SelectNodes("//div/a");

                var linksList = new List<string>();

                linksList.Add("inode.com");

                foreach (var link in links)
                {
                    var uriParsed = new Uri(link.Attributes["href"].Value.Replace("www.", string.Empty));

                    if (!linksList.Contains(uriParsed.Host) && uriParsed.Scheme == "http")
                    {
                        linksList.Add(uriParsed.Host);
                    }
                }

                rawList = JsonSerializer.Serialize<List<string>>(linksList);

                // Mind.Instance.CacheDb.Set<string>(CacheKey, TimeSpan.FromDays(1), rawList);
            }

            return JsonSerializer.Deserialize<List<string>>(rawList);
        }
    }
}
