// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Utilities;

public static class HttpClientUtils
{
    public static async Task<T> GetHttpJson<T>(string url)
    {
        var client = GetHttpClient();

        var result = await client.GetStringAsync(url);

        return JsonSerializer.Deserialize<T>(result);
    }

    internal static async Task<string> GetHttpString(string url)
    {
        var client = GetHttpClient();

        var result = await client.GetStringAsync(url);

        return result;
    }

    public static HttpClient GetHttpClientWithSocketHandler(HttpRequest request = null, SocketsHttpHandler handler = null)
    {
        var httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);

        if (request != null)
        {
            httpClient.DefaultRequestHeaders.Clear();

            string userAgentValue = request.Headers.ContainsKey(HttpHeaderName.UserAgent) ? request.Headers[HttpHeaderName.UserAgent].ToString() : "VintageHive";

            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(HttpHeaderName.UserAgent, userAgentValue);

            httpClient.DefaultRequestVersion = Version.Parse(request.Version.Replace("HTTP/", string.Empty));
        }

        return httpClient;
    }

    public static HttpClient GetHttpClient(HttpRequest request = null, HttpClientHandler handler = null)
    {
        var httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);

        if (request != null)
        {
            httpClient.DefaultRequestHeaders.Clear();

            string userAgentValue = request.Headers.ContainsKey(HttpHeaderName.UserAgent) ? request.Headers[HttpHeaderName.UserAgent].ToString() : "VintageHive";

            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(HttpHeaderName.UserAgent, userAgentValue);

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
}
