// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using UsenetCurator.Sources;

namespace UsenetCurator.Curation;

internal static class ArticleFilter
{
    private const int MaxBodyBytes = 10_240;

    private const double MaxNonAsciiRatio = 0.10;

    private static readonly string[] BinaryMarkers =
    [
        "begin 644 ",
        "begin 755 ",
        "begin 600 ",
        "Content-Transfer-Encoding: base64",
    ];

    private static readonly string[] UuencodePatterns =
    [
        "begin ",
        "M`",   // Common uuencoded line start
    ];

    /// <summary>
    /// Filters a list of raw articles, removing binary posts, oversized posts,
    /// posts with excessive non-ASCII content, and posts missing required fields.
    /// </summary>
    public static List<RawArticle> Filter(List<RawArticle> articles, string groupName, int minYear, int maxYear)
    {
        var results = new List<RawArticle>();
        var rejected = new Dictionary<string, int>();

        foreach (var article in articles)
        {
            var reason = GetRejectReason(article, minYear, maxYear);

            if (reason != null)
            {
                rejected[reason] = rejected.GetValueOrDefault(reason) + 1;

                continue;
            }

            results.Add(article);
        }

        if (rejected.Count > 0)
        {
            var reasons = string.Join(", ", rejected.Select(r => $"{r.Key}: {r.Value}"));

            Console.WriteLine($"  Filtered {groupName}: kept {results.Count}/{articles.Count} (rejected: {reasons})");
        }

        return results;
    }

    private static string GetRejectReason(RawArticle article, int minYear, int maxYear)
    {
        // Missing required fields
        if (string.IsNullOrWhiteSpace(article.MessageId))
        {
            return "no-msgid";
        }

        if (string.IsNullOrWhiteSpace(article.From))
        {
            return "no-from";
        }

        if (string.IsNullOrWhiteSpace(article.Subject))
        {
            return "no-subject";
        }

        if (string.IsNullOrWhiteSpace(article.Body))
        {
            return "no-body";
        }

        if (string.IsNullOrWhiteSpace(article.Date))
        {
            return "no-date";
        }

        // Date must be parseable
        if (!article.ParsedDate.HasValue)
        {
            return "bad-date";
        }

        // Reject dates outside the configured year range
        var year = article.ParsedDate.Value.Year;

        if (year < minYear || year > maxYear)
        {
            return "out-of-range";
        }

        // Body size check
        if (article.Body.Length > MaxBodyBytes)
        {
            return "too-long";
        }

        // Binary content detection
        foreach (var marker in BinaryMarkers)
        {
            if (article.Body.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return "binary";
            }
        }

        // Uuencode detection (body lines that look like uuencoded data)
        var bodyLines = article.Body.Split('\n');
        var uuLines = 0;

        foreach (var line in bodyLines)
        {
            var trimmed = line.TrimEnd('\r');

            if (trimmed.Length == 61 && trimmed[0] == 'M')
            {
                uuLines++;

                if (uuLines > 5)
                {
                    return "uuencode";
                }
            }
        }

        // Non-ASCII ratio check
        var nonAscii = 0;

        foreach (var ch in article.Body)
        {
            if (ch > 127)
            {
                nonAscii++;
            }
        }

        if (article.Body.Length > 0 && (double)nonAscii / article.Body.Length > MaxNonAsciiRatio)
        {
            return "non-ascii";
        }

        // Subject sanity (reject control messages, cancels, etc.)
        if (article.Subject.StartsWith("cmsg ", StringComparison.OrdinalIgnoreCase) ||
            article.Subject.StartsWith("cancel ", StringComparison.OrdinalIgnoreCase))
        {
            return "control-msg";
        }

        return null;
    }
}
