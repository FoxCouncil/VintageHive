using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using LibFoxyProxy.Http;
using MonkeyCache.LiteDB;

namespace VintageHive.Utilities;

public static class Clients
{
    static readonly TimeSpan HttpTtl = TimeSpan.FromDays(10);

    internal static byte[] HttpGetDataFromUrl(string dataUri, bool flush = false, HttpRequest request = null)
    {
        using var httpClient = GetHttpClient(request);

        return GetCachedData(dataUri, HttpTtl, flush, httpClient);
    }

    public static string HttpGetStringFromUrl(string dataUri, bool flush = false, HttpRequest? request = null)
    {
        using var httpClient = GetHttpClient(request);

        return GetCachedString(dataUri, HttpTtl, flush, httpClient);
    }

    public static JsonNode? HttpGetJsonNodeFromUrl(string url, bool flush = false)
    {
        var rawJsonString = GetCachedString(url, HttpTtl, flush);

        if (rawJsonString == null)
        {
            return null;
        }

        return JsonNode.Parse(rawJsonString);
    }

    public static T? HttpGetJsonClassFromUrl<T>(string url, bool flush = false)
    {
        var rawJsonString = GetCachedString(url, HttpTtl, flush);

        if (rawJsonString == null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(rawJsonString);
    }

    static string GetCachedString(string url, TimeSpan ttl, bool flushCache = false, HttpClient? optHttpClient = null)
    {
        if (!flushCache && (Mind.Config.OfflineMode || !Barrel.Current.IsExpired(url)))
        {
            return Barrel.Current.Get<string>(url);
        }
        else if (flushCache)
        {
            Barrel.Current.Empty(new[] { url });
        }

        using var httpClient = optHttpClient ?? new HttpClient();

        try
        {
            var rawStringData = httpClient.GetStringAsync(url).Result;

            Barrel.Current.Add(url, rawStringData, ttl);

            return rawStringData;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    static byte[] GetCachedData(string url, TimeSpan ttl, bool flushCache = false, HttpClient? optHttpClient = null)
    {
        if (!flushCache && (Mind.Config.OfflineMode || !Barrel.Current.IsExpired(url)))
        {
            return Barrel.Current.Get<byte[]>(url);
        }
        else if (flushCache)
        {
            Barrel.Current.Empty(new[] { url });
        }

        using var httpClient = optHttpClient ?? new HttpClient();

        try
        {
            var rawStringData = httpClient.GetByteArrayAsync(url).Result;

            Barrel.Current.Add(url, rawStringData, ttl);

            return rawStringData;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static HttpClient GetHttpClient(HttpRequest request)
    {
        var httpClient = new HttpClient();

        if (request != null)
        {
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", request.Headers["User-Agent"].ToString());
            httpClient.DefaultRequestVersion = System.Net.HttpVersion.Version10;
        }

        return httpClient;
    }
}
