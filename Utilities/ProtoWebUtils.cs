using HtmlAgilityPack;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VintageHive.Proxy.Ftp;

namespace VintageHive.Utilities
{
    public static class ProtoWebUtils
    {
        public const int MainProxyPort = 7851;

        public const string MainProxyHost = "wayback.protoweb.org";

        public const string MainProxyUri = "http://wayback.protoweb.org:7851/";

        const string FetchRequestHeaders = "GET {{URI}} HTTP/1.1\r\nUser-Agent: {{UA}}\r\nHost: {{HOST}}:21\r\nProxy-Connection: Keep-Alive\r\n\r\n";

        const string CacheKeyHttp = "PROTOWEB_SITE_HTTP_LIST";

        const string CacheKeyFtp = "PROTOWEB_SITE_FTP_LIST";

        const string RequestUri = "http://www.inode.com/";

        public static async Task UpdateSiteLists()
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

                return;
            }

            var htmlDoc = new HtmlDocument();

            htmlDoc.LoadHtml(site);

            var httpLinks = htmlDoc.DocumentNode.SelectNodes("//div/a");

            var httpLinksList = new List<string>
            {
                "inode.com"
            };

            foreach (var link in httpLinks)
            {
                var uriParsed = new Uri(link.Attributes["href"].Value.Replace("www.", string.Empty));

                if (!httpLinksList.Contains(uriParsed.Host) && uriParsed.Scheme == "http")
                {
                    httpLinksList.Add(uriParsed.Host);
                }
            }

            var rawHttpList = JsonSerializer.Serialize<List<string>>(httpLinksList);

            Mind.Instance.CacheDb.Set<string>(CacheKeyHttp, TimeSpan.FromDays(7), rawHttpList);

            var ftpLinks = htmlDoc.DocumentNode.SelectNodes("//li/a");

            var ftpLinksList = new List<string>();

            foreach (var link in ftpLinks)
            {
                var uriParsed = new Uri(link.Attributes["href"].Value);

                if (!ftpLinksList.Contains(uriParsed.Host) && uriParsed.Scheme == "ftp")
                {
                    ftpLinksList.Add(uriParsed.Host);
                }
            }

            var rawFtpList = JsonSerializer.Serialize<List<string>>(ftpLinksList);

            Mind.Instance.CacheDb.Set<string>(CacheKeyFtp, TimeSpan.FromDays(7), rawFtpList);
        }

        public static async Task<List<string>> GetAvailableHttpSites()
        {
            var rawHttpList = Mind.Instance.CacheDb.Get<string>(CacheKeyHttp);
            
            if (rawHttpList == null)
            {
                await UpdateSiteLists();

                rawHttpList = Mind.Instance.CacheDb.Get<string>(CacheKeyHttp);
            }

            return JsonSerializer.Deserialize<List<string>>(rawHttpList);
        }

        public static async Task<List<string>> GetAvailableFtpSites()
        {
            var rawFtpList = Mind.Instance.CacheDb.Get<string>(CacheKeyFtp);

            if (rawFtpList == null)
            {
                await UpdateSiteLists();

                rawFtpList = Mind.Instance.CacheDb.Get<string>(CacheKeyFtp);
            }

            return JsonSerializer.Deserialize<List<string>>(rawFtpList);
        }

        internal static string CreateFtpContentRequest(string uri, string fetchRequestUserAgent, string host)
        {
            return FetchRequestHeaders.Replace("{{URI}}", uri).Replace("{{UA}}", fetchRequestUserAgent).Replace("{{HOST}}", host);
        }

        public static string GenerateRedirectResponse(string newUrl)
        {
            var redirectResponse = @"HTTP/1.0 200 OK
Date: {{DATE}}
Connection: close
Content-Type: text/html


<HTML>

<HEAD>
    <TITLE>ProtoWeb FTP Redirect</TITLE>
</HEAD>

<BODY>
    <H1>Redirect to {{NEW_URL}}</H1>
    <PRE><IMG SRC=""http://www.inode.com/icons/dir.gif"" ALT=""[[DIR]]""> <A HREF=""{{NEW_URL}}"">{{NEW_URL}}</A></PRE>
    <HR>
    <ADDRESS> VintageHive FTP Proxy at <A href=""http://hive/"">http://hive/</A> Port 1971</ADDRESS>
</BODY>

</HTML>";
            redirectResponse = redirectResponse.Replace("{{DATE}}", DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss G\\MT"));
            redirectResponse = redirectResponse.Replace("{{NEW_URL}}", newUrl);

            return redirectResponse;
        }
    }
}
