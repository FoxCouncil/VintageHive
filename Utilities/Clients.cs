using SmartReader;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using VintageHive.Data;
using VintageHive.Processors.Intranet;
using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Utilities;

public static class Clients
{
    public static async Task<T> GetHttpJson<T>(string url)
    {
        var client = GetHttpClient();

        var result = await client.GetStringAsync(url);

        return JsonSerializer.Deserialize<T>(result);
    }

    public static HttpClient GetHttpClient(HttpRequest request = null, HttpClientHandler handler = null)
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

    public static async Task<List<Headlines>> GetGoogleArticles(string market = "US", string language = "en")
    {
        await Task.Delay(0);
        
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

    const string GoogleArticleUrl = "https://news.google.com/__i/rss/rd/articles/";

    public static async Task<Article> GetGoogleNewsArticle(string articleId)
    {
        var url = string.Concat(GoogleArticleUrl, articleId);

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

    const string WeatherDataApiUrl = "https://weatherdbi.herokuapp.com/data/weather/";

    internal static async Task<WeatherData> GetWeatherData(string location)
    {
        var url = string.Concat(WeatherDataApiUrl, location);

        var cacheKey = $"WEA-{url}";

        var rawData = Mind.Instance.CacheDb.Get<string>(cacheKey);

        if (rawData == null)
        {
            var client = GetHttpClient();

            try
            {
                var result = await client.GetStringAsync(url);

                if (result.Contains("\"status\":\"fail\""))
                {
                    Mind.Instance.CacheDb.Set(cacheKey, TimeSpan.FromDays(1000), result);

                    return null;
                }

                Mind.Instance.CacheDb.Set(cacheKey, TimeSpan.FromMinutes(15), result);

                rawData = result;
            }
            catch(HttpRequestException)
            {
                return null;
            }
        }

        return JsonSerializer.Deserialize<WeatherData>(rawData);
    }

    const string GeoIPApiUri = "http://ip-api.com/json/";

    internal static async Task<GeoIp> GetGeoIpData()
    {
        return await GetHttpJson<GeoIp>(GeoIPApiUri);
    }
}
