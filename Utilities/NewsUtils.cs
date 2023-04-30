// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SmartReader;
using System.ServiceModel.Syndication;
using System.Xml;

namespace VintageHive.Utilities;

public static class NewsUtils
{
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
                Id = item.Links[0].Uri.Segments.Last(),
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
