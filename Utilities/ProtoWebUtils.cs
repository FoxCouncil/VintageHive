using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Utilities
{
    public static class ProtoWebUtils
    {
        public const string MainProxyUri = "http://wayback2.protoweb.org:7851/";

        public static async Task<List<string>> GetAvailableSites()
        {
            var proxyClient = Clients.GetProxiedHttpClient(null, MainProxyUri);

            try
            {
                proxyClient.Timeout = TimeSpan.FromSeconds(1);
                
                var site = await proxyClient.GetStringAsync("http://www.inode.com/");

                var htmlDoc = new HtmlDocument();

                htmlDoc.LoadHtml(site);

                var links = htmlDoc.DocumentNode.SelectNodes("//li/a");

                var linksList = new List<string>();

                foreach (var link in links)
                {
                    var uriParsed = new Uri(link.Attributes["href"].Value.Replace("www.", string.Empty));

                    if (!linksList.Contains(uriParsed.Host) && uriParsed.Scheme == "http")
                    {
                        linksList.Add(uriParsed.Host);
                    }
                }

                return linksList;
            }
            catch (Exception)
            {
                Console.WriteLine("ProtoWeb is offline! Turning it off!");

                Mind.Instance.ConfigDb.SettingSet(ConfigNames.ProtoWeb, false);

                return null;
            }
        }
    }
}
