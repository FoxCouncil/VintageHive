// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using HtmlAgilityPack;
using System;

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

        public static async Task<List<Tuple<string, string>>> GetSites()
        {
            var protowebSiteList = await Mind.Cache.Do<List<Tuple<string, string>>>("protowebsitelist", TimeSpan.FromHours(6), async () =>
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

                    return null;
                }

                var htmlDoc = new HtmlDocument();

                htmlDoc.LoadHtml(site);

                var links = htmlDoc.DocumentNode.SelectNodes("//a");

                var linkList = new List<Tuple<string, string>>();
                var httpLinksList = new List<string>();
                var ftpLinksList = new List<string>();

                var primedHttpDomains = new string[] { "counter.inode.com", "inode.com" };
                foreach(var httpDomain in primedHttpDomains)
                {
                    httpLinksList.Add(httpDomain);
                    linkList.Add(new Tuple<string, string>("http", httpDomain));
                }

                var primedFtpDomains = new string[] { "ftp.inode.com" };
                foreach (var ftpDomain in primedFtpDomains)
                {
                    ftpLinksList.Add(ftpDomain);
                    linkList.Add(new Tuple<string, string>("ftp", ftpDomain));
                }

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
                    var uriParsed = new Uri(linkVal);

                    if (uriParsed.Scheme == "http" && !httpLinksList.Contains(uriParsed.Host))
                    {
                        httpLinksList.Add(uriParsed.Host);

                        linkList.Add(new Tuple<string, string>("http", uriParsed.Host));
                    }

                    if (uriParsed.Scheme == "ftp" && !ftpLinksList.Contains(uriParsed.Host))
                    {
                        ftpLinksList.Add(uriParsed.Host);

                        linkList.Add(new Tuple<string, string>("ftp", uriParsed.Host));
                    }
                }

                Log.WriteLine(Log.LEVEL_INFO, nameof(ProtoWebUtils), $"Updated Sitelist HTTP:{httpLinksList.Count} FTP:{ftpLinksList.Count}", "");

                return linkList;
            });

            return protowebSiteList;
        }

        public static async Task<List<string>> GetAvailableHttpSites()
        {
            var linkList = await GetSites();
            
            if (linkList == null || !linkList.Any())
            {
                return null;
            }

            return linkList.Where(x => x.Item1 == "http").Select(x => x.Item2).Order().ToList();
        }

        public static async Task<List<string>> GetAvailableFtpSites()
        {
            var linkList = await GetSites();

            if (linkList == null || !linkList.Any())
            {
                return null;
            }

            return linkList.Where(x => x.Item1 == "ftp").Select(x => x.Item2).Order().ToList();
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
