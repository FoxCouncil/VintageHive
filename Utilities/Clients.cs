using LibFoxyProxy.Http;

namespace VintageHive.Utilities;

public static class Clients
{
    public static HttpClient GetHttpClient(HttpRequest request)
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
