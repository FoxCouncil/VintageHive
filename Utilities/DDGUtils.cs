// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;

namespace VintageHive.Utilities;

internal static class DDGUtils
{
    const string MainUrl = "https://duckduckgo.com";

    const string LinksUrl = "https://links.duckduckgo.com/d.js";

    static readonly Regex VqdRegex = new Regex("vqd=([0-9-]+)\\&", RegexOptions.Compiled);

    internal static async Task<List<SearchResult>> Search(string keywords, uint page = 1, string region = "wt-wt", SafeSearchLevel safesearch = SafeSearchLevel.Off)
    {
        var vqd = await GetVqd(keywords);

        var idx = (page * 20) - 20;

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

        var searchResults = JsonSerializer.Deserialize<DDGResults>(rawouput);

        var output = new List<SearchResult>();

        foreach (var result in searchResults.results)
        {
            if (string.IsNullOrEmpty(result.t))
            {
                continue;
            }

            var title = result.t;

            if (title == "EOF")
            {
                continue;
            }

            output.Add(new SearchResult
            {
                Title = result.t,
                Abstract = result.a,
                Uri = new Uri(result.c),
                UriDisplay = result.d
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

    public class DDGResults
    {
        public DeepAnswers deep_answers { get; set; }

        public List<Result> results { get; set; }
    }

    public class Result
    {
        public string a { get; set; }

        public object ae { get; set; }

        public string b { get; set; }

        public string c { get; set; }

        public string d { get; set; }

        public string da { get; set; }

        public int h { get; set; }

        public string i { get; set; }

        public object k { get; set; }

        public int m { get; set; }

        public int o { get; set; }

        public int p { get; set; }

        public string s { get; set; }

        public string t { get; set; }

        public string u { get; set; }

        public DateTime? e { get; set; }

        public string n { get; set; }
    }

    public class DeepAnswers
    {
        public List<List<object>>? spelling { get; set; }
    }
}
