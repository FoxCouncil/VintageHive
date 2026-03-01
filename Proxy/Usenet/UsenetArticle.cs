// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Usenet;

internal class UsenetArticle
{
    public int Number { get; set; }

    public string MessageId { get; set; }

    public string From { get; set; }

    public string Subject { get; set; }

    public string Date { get; set; }

    public string Newsgroups { get; set; }

    public string References { get; set; } = "";

    public int Bytes { get; set; }

    public int Lines { get; set; }

    public string Body { get; set; }
}
