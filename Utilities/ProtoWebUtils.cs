// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using HtmlAgilityPack;

namespace VintageHive.Utilities
{
    public static class ProtoWebUtils
    {
        public const int MainProxyPort = 7851;

        public const string MainProxyHost = "wayback.protoweb.org";

        public const string MainProxyUri = "http://wayback.protoweb.org:7851/";

        const string FetchRequestHeaders = "GET {{URI}} HTTP/1.1\r\nUser-Agent: {{UA}}\r\nHost: {{HOST}}:21\r\nProxy-Connection: Keep-Alive\r\n\r\n";

        const string CacheKeyHttp = "HTTP";

        const string CacheKeyFtp = "FTP";

        const string RequestUri = "http://www.inode.com/cgi-bin/sites.cgi";

        static readonly object lockObj = new();

        public static async Task UpdateSiteLists()
        {
            var proxyClient = HttpClientUtils.GetProxiedHttpClient(null, MainProxyUri);

            proxyClient.Timeout = TimeSpan.FromSeconds(10);

            string site;
            
            try
            {
                site = await proxyClient.GetStringAsync(RequestUri);
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(ProtoWebUtils), ex, "");

                Log.WriteLine(Log.LEVEL_INFO, nameof(ProtoWebUtils), "ProtoWeb is offline! Turning it off!", "");

                Mind.Db.ConfigSet(ConfigNames.ProtoWeb, false);

                return;
            }

            var htmlDoc = new HtmlDocument();

            htmlDoc.LoadHtml(site);

            var links = htmlDoc.DocumentNode.SelectNodes("//a");

            var httpLinksList = new List<string> { "inode.com" };
            var ftpLinksList = new List<string> { "ftp.inode.com" };

            foreach (var link in links)
            {
                if (link.Attributes["href"] == null)
                { 
                    continue; 
                }

                var linkVal = link.Attributes["href"].Value;

                if (!linkVal.Contains('.') || !linkVal.Contains(':'))
                {
                    continue;
                }

                // var uriParsed = new Uri(link.Attributes["href"].Value.Replace("www.", string.Empty));
                var uriParsed = new Uri(linkVal);

                if (uriParsed.Scheme == "http" && !httpLinksList.Contains(uriParsed.Host))
                {
                    httpLinksList.Add(uriParsed.Host);
                }

                if (uriParsed.Scheme == "ftp" && !ftpLinksList.Contains(uriParsed.Host))
                {
                    ftpLinksList.Add(uriParsed.Host);
                }
            }

            // var rawHttpList = JsonSerializer.Serialize<List<string>>(httpLinksList);

            Mind.Cache.SetProtowebSiteList(CacheKeyHttp, httpLinksList);

            Mind.Cache.SetProtowebSiteList(CacheKeyFtp, ftpLinksList);

            Log.WriteLine(Log.LEVEL_INFO, nameof(ProtoWebUtils), $"Updated Sitelist HTTP:{httpLinksList.Count} FTP:{ftpLinksList.Count}", "");
        }

        public static async Task<List<string>> GetAvailableHttpSites()
        {
            var httpList = Mind.Cache.GetProtowebSiteList(CacheKeyHttp);
            
            if (httpList == null || !httpList.Any())
            {
                await UpdateSiteLists();

                httpList = Mind.Cache.GetProtowebSiteList(CacheKeyHttp);
            }

            return httpList;
        }

        public static async Task<List<string>> GetAvailableFtpSites()
        {
            var ftpList = Mind.Cache.GetProtowebSiteList(CacheKeyFtp);

            if (ftpList == null || !ftpList.Any())
            {
                await UpdateSiteLists();

                ftpList = Mind.Cache.GetProtowebSiteList(CacheKeyFtp);
            }

            return ftpList;
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
