// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Usenet;

internal class UsenetGroup
{
    public string Name { get; set; }

    public int FirstArticle { get; set; }

    public int LastArticle { get; set; }

    public int ArticleCount { get; set; }

    public string PostingStatus { get; set; } = "n";
}
