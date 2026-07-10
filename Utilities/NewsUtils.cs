// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartReader;
using System.ServiceModel.Syndication;
using System.Web;
using System.Xml;

namespace VintageHive.Utilities;

public static class NewsUtils
{
    private static readonly Dictionary<GoogleNewsTopic, string> GoogleNewsTopicIds = new()
    {
        {GoogleNewsTopic.World, "CAAqJggKIiBDQkFTRWdvSUwyMHZNRGx1YlY4U0FtVnVHZ0pWVXlnQVAB"},
        {GoogleNewsTopic.US, "CAAqIggKIhxDQkFTRHdvSkwyMHZNRGxqTjNjd0VnSmxiaWdBUAE"},
        {GoogleNewsTopic.Local, "CAAqHAgKIhZDQklTQ2pvSWJHOWpZV3hmZGpJb0FBUAE"},
        {GoogleNewsTopic.Business, "CAAqJggKIiBDQkFTRWdvSUwyMHZNRGx6TVdZU0FtVnVHZ0pWVXlnQVAB"},
        {GoogleNewsTopic.Technology, "CAAqJggKIiBDQkFTRWdvSUwyMHZNRGRqTVhZU0FtVnVHZ0pWVXlnQVAB"},
        {GoogleNewsTopic.Entertainment, "CAAqJggKIiBDQkFTRWdvSUwyMHZNREpxYW5RU0FtVnVHZ0pWVXlnQVAB"},
        {GoogleNewsTopic.Sports, "CAAqJggKIiBDQkFTRWdvSUwyMHZNRFp1ZEdvU0FtVnVHZ0pWVXlnQVAB"},
        {GoogleNewsTopic.Science, "CAAqJggKIiBDQkFTRWdvSUwyMHZNRFp0Y1RjU0FtVnVHZ0pWVXlnQVAB"},
        {GoogleNewsTopic.Health, "CAAqIQgKIhtDQkFTRGdvSUwyMHZNR3QwTlRFU0FtVnVLQUFQAQ"}
    };

    const string GoogleRssFeedUrl = "https://news.google.com/rss";

    const string GoogleRssTopicFeedUrl = "/topics/{0}";

    const string GoogleGetParams = "?gl={1}&hl={0}-{1}&gl={1}&ceid={1}:{0}";

    public static async Task<List<Headlines>> GetGoogleForYouArticles(string market = "US", string language = "en")
    {
        var url = $"{GoogleRssFeedUrl}{string.Format(GoogleGetParams, language, market)}";

        var output = await Mind.Cache.Do<List<Headlines>>($"googlenews_{url}", TimeSpan.FromHours(1), () =>
        {
            using var reader = XmlReader.Create(url);

            var feed = SyndicationFeed.Load(reader);

            var output = new List<Headlines>();

            foreach (var item in feed.Items)
            {
                output.Add(new Headlines()
                {
                    Id = item.Links[0].Uri.Segments.Last(),
                    Title = item.Title.Text,
                    Published = item.PublishDate,
                    Summary = item.Summary.Text.StripHtml()
                });
            }

            return Task.FromResult(output);
        });

        return output;
    }

    public static async Task<List<Headlines>> GetGoogleTopicArticles(GoogleNewsTopic topic = GoogleNewsTopic.World, string market = "US", string language = "en")
    {
        var url = $"{GoogleRssFeedUrl}{string.Format(GoogleRssTopicFeedUrl, GoogleNewsTopicIds[topic])}{string.Format(GoogleGetParams, language, market)}";

        var output = await Mind.Cache.Do<List<Headlines>>($"googlenews_{url}", TimeSpan.FromHours(1), () =>
        {

            var output = new List<Headlines>();

            try
            {
                using var reader = XmlReader.Create(url);

                var feed = SyndicationFeed.Load(reader);

                foreach (var item in feed.Items)
                {
                    output.Add(new Headlines()
                    {
                        Id = item.Links[0].Uri.Segments.Last(),
                        Title = item.Title.Text,
                        Published = item.PublishDate,
                        Summary = item.Summary.Text.StripHtml()
                    });
                }
            }
            catch (HttpRequestException httpRequestException)
            {
                Log.WriteLine(Log.LEVEL_ERROR, nameof(NewsUtils), $"Failed to get news from: {url}", Guid.Empty.ToString());
                Log.WriteException(nameof(NewsUtils), httpRequestException, Guid.Empty.ToString());
            }

            return Task.FromResult(output);
        });

        return output;
    }

    const string GoogleArticleUrl = "https://news.google.com/rss/articles/";

    // One shared client with a browser User-Agent (was a per-call `new HttpClient()` with no UA, leaking sockets)
    private static readonly HttpClient GoogleNewsClient = CreateGoogleNewsClient();

    private static HttpClient CreateGoogleNewsClient()
    {
        var client = new HttpClient();

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        return client;
    }

    public static async Task<Article> GetGoogleNewsArticle(string articleId)
    {
        try
        {
            var url = string.Concat(GoogleArticleUrl, articleId);
            var gnArtId = new Uri(url).AbsolutePath.Split('/').Last();

            var decodingParams = await GetDecodingParams(gnArtId);

            if (decodingParams == null)
            {
                return null;
            }

            var decodedUrls = await DecodeUrls(new List<Dictionary<string, string>> { decodingParams });

            var decodedUrl = decodedUrls.FirstOrDefault();

            return decodedUrl == null ? null : await GetReaderOutput(decodedUrl);
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(NewsUtils), $"Google News article decode failed for {articleId}: {ex.Message}", "");

            return null;
        }
    }

    public static async Task<Article> GetReaderOutput(string url)
    {
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

    static async Task<Dictionary<string, string>> GetDecodingParams(string gn_art_id)
    {
        var response = await GoogleNewsClient.GetAsync($"https://news.google.com/articles/{gn_art_id}");

        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();

        doc.LoadHtml(responseText);

        var div = doc.DocumentNode.SelectSingleNode("//c-wiz/div");

        if (div == null)
        {
            return null; // markup changed / not the expected shape - fail soft instead of NREing the article view
        }

        return new Dictionary<string, string>
        {
            { "signature", div.GetAttributeValue("data-n-a-sg", "") },
            { "timestamp", div.GetAttributeValue("data-n-a-ts", "") },
            { "gn_art_id", gn_art_id }
        };
    }

    static async Task<List<string>> DecodeUrls(List<Dictionary<string, string>> articles)
    {
        var articles_reqs = articles.Select(art => new List<string>
        {
            "Fbv4je",
            $"[\"garturlreq\",[[\"X\",\"X\",[\"X\",\"X\"],null,null,1,1,\"US:en\",null,1,null,null,null,null,null,0,1],\"X\",\"X\",1,[1,1,1],1,1,null,0,0,null,0],\"{art["gn_art_id"]}\",{art["timestamp"]},\"{art["signature"]}\"]"
        }).ToList();

        var jsonPayload = JsonConvert.SerializeObject(new List<object> { articles_reqs });

        var encodedPayload = $"f.req={HttpUtility.UrlEncode(jsonPayload)}";

        var content = new StringContent(encodedPayload, Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await GoogleNewsClient.PostAsync("https://news.google.com/_/DotsSplashUi/data/batchexecute", content);

        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();

        var parts = responseText.Split(new[] { "\n\n" }, StringSplitOptions.None);
        var jsonResponseText = parts.Length > 1 ? parts[1] : null;

        if (jsonResponseText == null)
        {
            throw new Exception("Unexpected response format");
        }

        var jsonResponse = JsonConvert.DeserializeObject<JArray>(jsonResponseText);
        var results = new List<string>();

        foreach (var res in jsonResponse.Take(jsonResponse.Count - 2))
        {
            var resArray = res as JArray;

            if (resArray == null || resArray.Count < 3)
            {
                continue;
            }

            var resItem = resArray[2];
            var decodedRes = JsonConvert.DeserializeObject<JArray>(resItem.ToString());

            results.Add(decodedRes[1].ToString());
        }
        return results;
    }
}
