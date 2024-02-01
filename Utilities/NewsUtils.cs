// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SmartReader;
using System.ServiceModel.Syndication;
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

    const string GoogleArticleUrl = "https://news.google.com/__i/rss/rd/articles/";

    public static async Task<Article> GetGoogleNewsArticle(string articleId)
    {
        var url = string.Concat(GoogleArticleUrl, articleId);

        return await GetReaderOutput(url);
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
}
