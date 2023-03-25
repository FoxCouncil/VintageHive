using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;
using VintageHive.Data.Types;

namespace VintageHive.Utilities;

internal static class DDGUtils
{
    const string MainUrl = "https://duckduckgo.com";

    const string LinksUrl = "https://links.duckduckgo.com/d.js";

    static readonly Regex VqdRegex = new Regex("vqd=([0-9-]+)\\&", RegexOptions.Compiled);

    internal static async Task<List<SearchResult>> Search(string keywords, uint page = 1, string region = "wt-wt", SafeSearchLevel safesearch = SafeSearchLevel.Off)
    {
        var vqd = await GetVqd(keywords);

        var idx = ((page * 20) - 20);

        var getParams = $"?q={WebUtility.UrlEncode(keywords)}&l={region}&p={(int)safesearch}&s={idx}&df=&o=json&vqd={vqd}";

        var url = LinksUrl + getParams;

        using var client = new HttpClient();

        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get
        };

        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; rv:102.0) Gecko/20100101 Firefox/102.0");
        request.Headers.Add("Referer", $"{MainUrl}/");

        var response = await client.SendAsync(request);

        var rawouput = await response.Content.ReadAsStringAsync();

        var json = JsonConvert.DeserializeObject<JObject>(rawouput);

        var output = new List<SearchResult>();

        foreach (var result in json["results"])
        {
            if (result.Count() == 1)
            {
                continue;
            }

            var title = result["t"].ToString();

            if (title == "EOF")
            {
                continue;
            }

            output.Add(new SearchResult
            {
                Title = result["t"].ToString(),
                Abstract = result["a"].ToString(),
                Uri = new Uri(result["c"].ToString()),
                UriDisplay = result["d"].ToString()
            });
        }

        return output;
    }

    private static async Task<string> GetVqd(string keywords)
    {
        var vqd = Mind.Db.VqdGet(keywords);

        if (vqd != null)
        {
            Console.WriteLine(vqd+"-sql");

            return vqd;
        }

        using var client = new HttpClient();

        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(MainUrl),
            Method = HttpMethod.Post
        };

        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; rv:102.0) Gecko/20100101 Firefox/102.0");
        request.Headers.Add("Referer", $"{MainUrl}/");

        var formList = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("q", keywords)
        };

        request.Content = new FormUrlEncodedContent(formList);

        var response = await client.SendAsync(request);

        var result = await response.Content.ReadAsStringAsync();

        vqd = VqdRegex.Match(result).Groups[1].Value;

        Mind.Db.VqdSet(keywords, vqd);

        return vqd;
    }

    public enum SafeSearchLevel
    {
        Off = -2,
        Moderate = -1,
        On = 1
    }
}
