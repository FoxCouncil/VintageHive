// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.IO.Compression;
using System.Text.Json.Serialization;
using VintageHive.Utilities;

namespace VintageHive.Proxy.Usenet;

internal class UsenetDataSource
{
    private const string CacheKeyPrefix = "usenet:";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private List<UsenetGroup> _bundledGroups;

    private Dictionary<string, List<UsenetArticle>> _bundledArticles;

    public UsenetDataSource()
    {
        LoadBundledData();
    }

    public Task<List<UsenetGroup>> GetGroupsAsync()
    {
        return Task.FromResult(new List<UsenetGroup>(_bundledGroups));
    }

    public async Task<UsenetGroup> GetGroupAsync(string groupName)
    {
        var groups = await GetGroupsAsync();

        return groups.Find(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<UsenetArticle>> GetArticlesAsync(string groupName, int first, int last)
    {
        var allArticles = GetBundledArticles(groupName);

        if (allArticles.Count == 0)
        {
            var cacheKey = $"{CacheKeyPrefix}articles:{groupName}";
            var cached = Mind.Cache.GetData(cacheKey);

            if (cached != null)
            {
                allArticles = JsonSerializer.Deserialize<List<UsenetArticle>>(cached);
            }
            else
            {
                allArticles = await FetchFromArchivesAsync(groupName);

                if (allArticles.Count > 0)
                {
                    Mind.Cache.SetData(cacheKey, CacheTtl, JsonSerializer.Serialize(allArticles));
                }
            }
        }

        return allArticles
            .Where(a => a.Number >= first && a.Number <= last)
            .OrderBy(a => a.Number)
            .ToList();
    }

    public async Task<UsenetArticle> GetArticleAsync(string groupName, int articleNumber)
    {
        var articles = await GetArticlesAsync(groupName, articleNumber, articleNumber);

        return articles.FirstOrDefault();
    }

    public async Task<UsenetArticle> GetArticleByIdAsync(string messageId)
    {
        if (_bundledArticles == null)
        {
            return await Task.FromResult<UsenetArticle>(null);
        }

        foreach (var group in _bundledArticles)
        {
            var article = group.Value.Find(a => a.MessageId == messageId);

            if (article != null)
            {
                return await Task.FromResult(article);
            }
        }

        return await Task.FromResult<UsenetArticle>(null);
    }

    private List<UsenetArticle> GetBundledArticles(string groupName)
    {
        if (_bundledArticles != null && _bundledArticles.TryGetValue(groupName, out var articles))
        {
            return new List<UsenetArticle>(articles);
        }

        return new List<UsenetArticle>();
    }

    private async Task<List<UsenetArticle>> FetchFromArchivesAsync(string groupName)
    {
        var articles = await FetchFromInternetArchiveAsync(groupName);

        if (articles.Count > 0)
        {
            return articles;
        }

        articles = await FetchFromUsenetArchivesAsync(groupName);

        if (articles.Count > 0)
        {
            return articles;
        }

        return new List<UsenetArticle>();
    }

    private async Task<List<UsenetArticle>> FetchFromInternetArchiveAsync(string groupName)
    {
        try
        {
            var year = Mind.Db.ConfigGet<int>(ConfigNames.ServiceInternetArchiveYear);

            var searchUrl = $"https://archive.org/advancedsearch.php?q=subject%3A%22{Uri.EscapeDataString(groupName)}%22+AND+mediatype%3Atexts&fl[]=identifier,title,date&sort[]=date+desc&rows=20&output=json";

            var json = await HttpClientUtils.GetHttpString(searchUrl);

            if (string.IsNullOrEmpty(json))
            {
                return new List<UsenetArticle>();
            }

            using var doc = JsonDocument.Parse(json);

            var response = doc.RootElement.GetProperty("response");

            var numFound = response.GetProperty("numFound").GetInt32();

            if (numFound == 0)
            {
                return new List<UsenetArticle>();
            }

            var articles = new List<UsenetArticle>();
            var number = 1;

            foreach (var item in response.GetProperty("docs").EnumerateArray())
            {
                var identifier = item.GetProperty("identifier").GetString();
                var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : identifier;
                var date = item.TryGetProperty("date", out var dateProp) ? dateProp.GetString() : $"01 Jan {year} 00:00:00 -0000";

                articles.Add(new UsenetArticle
                {
                    Number = number++,
                    MessageId = $"<{identifier}@archive.org>",
                    From = "archive@archive.org (Internet Archive)",
                    Subject = title,
                    Date = date,
                    Newsgroups = groupName,
                    Body = $"This article was retrieved from the Internet Archive.\r\nOriginal identifier: {identifier}\r\n\r\nVisit https://archive.org/details/{identifier} for the full content.",
                    Bytes = 0,
                    Lines = 4
                });
            }

            foreach (var article in articles)
            {
                article.Bytes = Encoding.ASCII.GetByteCount(article.Body);
                article.Lines = article.Body.Split('\n').Length;
            }

            return articles;
        }
        catch
        {
            return new List<UsenetArticle>();
        }
    }

    private async Task<List<UsenetArticle>> FetchFromUsenetArchivesAsync(string groupName)
    {
        try
        {
            var url = $"https://www.usenetarchives.com/groups/{groupName}/";

            var html = await HttpClientUtils.GetHttpString(url);

            if (string.IsNullOrEmpty(html) || html.Contains("404") || html.Contains("Not Found"))
            {
                return new List<UsenetArticle>();
            }

            return new List<UsenetArticle>();
        }
        catch
        {
            return new List<UsenetArticle>();
        }
    }

    private void LoadBundledData()
    {
        _bundledGroups = new List<UsenetGroup>();
        _bundledArticles = new Dictionary<string, List<UsenetArticle>>(StringComparer.OrdinalIgnoreCase);

        // Try external data first (from UsenetCurator output), fall back to embedded resources
        var groupDefs = LoadGroupDefs();

        if (groupDefs == null || groupDefs.Count == 0)
        {
            return;
        }

        var articlesMap = LoadArticlesMap();

        foreach (var def in groupDefs)
        {
            var articleList = new List<UsenetArticle>();

            if (articlesMap != null && articlesMap.TryGetValue(def.Name, out var articles))
            {
                articleList = articles;

                foreach (var article in articleList)
                {
                    article.Newsgroups = def.Name;
                    article.Bytes = Encoding.ASCII.GetByteCount(article.Body ?? "");
                    article.Lines = (article.Body ?? "").Split('\n').Length;
                }
            }

            _bundledArticles[def.Name] = articleList;

            _bundledGroups.Add(new UsenetGroup
            {
                Name = def.Name,
                FirstArticle = articleList.Count > 0 ? articleList.Min(a => a.Number) : 0,
                LastArticle = articleList.Count > 0 ? articleList.Max(a => a.Number) : 0,
                ArticleCount = articleList.Count,
                PostingStatus = "n"
            });
        }
    }

    private static readonly string ExternalDataPath = Path.Combine("data", "usenet");

    private List<BundledGroupDef> LoadGroupDefs()
    {
        // Try external data directory first
        var externalGroupsFile = Path.Combine(ExternalDataPath, "groups.json");

        if (File.Exists(externalGroupsFile))
        {
            var json = File.ReadAllText(externalGroupsFile, Encoding.UTF8);

            return JsonSerializer.Deserialize<List<BundledGroupDef>>(json);
        }

        // Fall back to embedded resource
        var embeddedJson = Resources.GetStaticsResourceString("usenet.groups.json");

        if (embeddedJson != null)
        {
            return JsonSerializer.Deserialize<List<BundledGroupDef>>(embeddedJson.RemoveBOM());
        }

        return null;
    }

    private Dictionary<string, List<UsenetArticle>> LoadArticlesMap()
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Try external GZip file first
        var externalGzFile = Path.Combine(ExternalDataPath, "articles.json.gz");

        if (File.Exists(externalGzFile))
        {
            return DecompressArticles(File.ReadAllBytes(externalGzFile), jsonOptions);
        }

        // Try external plain JSON file
        var externalJsonFile = Path.Combine(ExternalDataPath, "articles.json");

        if (File.Exists(externalJsonFile))
        {
            var json = File.ReadAllText(externalJsonFile, Encoding.UTF8);

            return JsonSerializer.Deserialize<Dictionary<string, List<UsenetArticle>>>(json, jsonOptions);
        }

        // Fall back to embedded GZip resource
        var gzData = Resources.GetStaticsResourceData("usenet.articles.json.gz");

        if (gzData != null)
        {
            return DecompressArticles(gzData, jsonOptions);
        }

        // Fall back to embedded plain JSON resource
        var embeddedJson = Resources.GetStaticsResourceString("usenet.articles.json");

        if (embeddedJson != null)
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<UsenetArticle>>>(embeddedJson.RemoveBOM(), jsonOptions);
        }

        return null;
    }

    private static Dictionary<string, List<UsenetArticle>> DecompressArticles(byte[] gzData, JsonSerializerOptions options)
    {
        using var compressedStream = new MemoryStream(gzData);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();

        gzipStream.CopyTo(resultStream);

        var json = Encoding.UTF8.GetString(resultStream.ToArray());

        return JsonSerializer.Deserialize<Dictionary<string, List<UsenetArticle>>>(json, options);
    }

    private class BundledGroupDef
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }
}
