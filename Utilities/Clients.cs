using LibFoxyProxy.Http;
using SmartReader;
using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using VintageHive.Processors.Intranet;
using static LibFoxyProxy.Http.HttpUtilities;

namespace VintageHive.Utilities;

public static class Clients
{
    public static HttpClient GetHttpClient(HttpRequest request, HttpClientHandler handler = null)
    {
        var httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);

        if (request != null)
        {
            httpClient.DefaultRequestHeaders.Clear();

            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, request.Headers.ContainsKey(HttpHeaderName.UserAgent) ? request.Headers[HttpHeaderName.UserAgent].ToString() : "VintageHive");

            httpClient.DefaultRequestVersion = Version.Parse(request.Version.Replace("HTTP/", string.Empty));
        }

        return httpClient;
    }

    public static HttpClient GetProxiedHttpClient(HttpRequest request, string proxyUri)
    {
        var proxy = new WebProxy { Address = new Uri(proxyUri) };

        var client = GetHttpClient(request, new HttpClientHandler { Proxy = proxy });

        return client;
    }

    const string GoogleRssFeedUrl = "https://news.google.com/rss?gl={1}&hl={0}-{1}&ceid={1}:{0}";

    public static async Task<List<Headlines>> GetGoogleArticles(string language = "en", string market = "US")
    {
        var url = string.Format(GoogleRssFeedUrl, language, market);

        using var reader = XmlReader.Create(url);

        var feed = SyndicationFeed.Load(reader);

        var output = new List<Headlines>();

        foreach (var item in feed.Items)
        {
            output.Add(new Headlines()
            {
                Id = item.Links[0].Uri.Segments[5],
                Title = item.Title.Text,
                Published = item.PublishDate,
                Summary = item.Summary.Text.StripHtml()
            });
        }

        return output;
    }

    const string GoogleArticleUrl = "https://news.google.com/__i/rss/rd/articles/{0}";

    public static async Task<Article> GetGoogleNewsArticle(string articleId)
    {
        var url = string.Format(GoogleArticleUrl, articleId);

        try
        {
            var article = await Reader.ParseArticleAsync(url);

            return article;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
