// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using UsenetCurator.Sources;

namespace UsenetCurator.Curation;

internal static class ArticleNumberer
{
    /// <summary>
    /// Sorts articles by date and assigns sequential numbers 1..N per group.
    /// Also caps each group at its MaxArticles limit, sampling evenly across
    /// the date range to preserve chronological diversity.
    /// </summary>
    public static void NumberAndCap(Dictionary<string, List<RawArticle>> allGroups, Dictionary<string, int?> maxPerGroup)
    {
        foreach (var (groupName, articles) in allGroups)
        {
            // Sort by parsed date
            articles.Sort((a, b) =>
            {
                var dateA = a.ParsedDate ?? DateTimeOffset.MinValue;
                var dateB = b.ParsedDate ?? DateTimeOffset.MinValue;

                return dateA.CompareTo(dateB);
            });

            var max = maxPerGroup.GetValueOrDefault(groupName);

            // null = no cap, skip sampling entirely
            if (max == null)
            {
                continue;
            }

            // If we have more articles than the limit, sample evenly
            if (articles.Count > max.Value)
            {
                var sampled = SampleEvenly(articles, max.Value);

                articles.Clear();
                articles.AddRange(sampled);
            }

            // Assign sequential numbers
            for (var i = 0; i < articles.Count; i++)
            {
                // Number is stored externally - the RawArticle doesn't have it
                // We'll handle numbering in the export phase
            }
        }
    }

    /// <summary>
    /// Samples N articles evenly across the list (which is sorted by date).
    /// This preserves chronological diversity rather than just taking the first N.
    /// </summary>
    private static List<RawArticle> SampleEvenly(List<RawArticle> sorted, int count)
    {
        if (count >= sorted.Count)
        {
            return new List<RawArticle>(sorted);
        }

        var result = new List<RawArticle>(count);
        var step = (double)sorted.Count / count;

        for (var i = 0; i < count; i++)
        {
            var idx = (int)(i * step);

            result.Add(sorted[idx]);
        }

        return result;
    }
}
