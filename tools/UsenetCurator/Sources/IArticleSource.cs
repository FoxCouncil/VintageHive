// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace UsenetCurator.Sources;

internal class RawArticle
{
    public string MessageId { get; set; } = "";

    public string From { get; set; } = "";

    public string Subject { get; set; } = "";

    public string Date { get; set; } = "";

    public string Newsgroups { get; set; } = "";

    public string References { get; set; } = "";

    public string Body { get; set; } = "";

    public DateTimeOffset? ParsedDate { get; set; }
}

internal interface IArticleSource
{
    Task<List<RawArticle>> FetchArticlesAsync(GroupDefinition group, string cachePath, int readLimit, CancellationToken ct);
}
