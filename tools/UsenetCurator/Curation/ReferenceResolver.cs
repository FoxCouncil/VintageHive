// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using UsenetCurator.Sources;

namespace UsenetCurator.Curation;

internal static class ReferenceResolver
{
    /// <summary>
    /// Cleans up References headers to only point to articles that exist
    /// in the curated set. Removes references to articles that were filtered out.
    /// </summary>
    public static void Resolve(Dictionary<string, List<RawArticle>> allGroups)
    {
        // Build global set of all Message-IDs
        var validIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in allGroups.Values)
        {
            foreach (var article in group)
            {
                validIds.Add(article.MessageId);
            }
        }

        // Clean up References in all articles
        var cleaned = 0;

        foreach (var group in allGroups.Values)
        {
            foreach (var article in group)
            {
                if (string.IsNullOrEmpty(article.References))
                {
                    continue;
                }

                // References can be a space-separated list of Message-IDs
                var refs = ParseMessageIdList(article.References);
                var validRefs = refs.Where(r => validIds.Contains(r)).ToList();

                if (validRefs.Count != refs.Count)
                {
                    cleaned++;
                }

                article.References = validRefs.Count > 0
                    ? string.Join(" ", validRefs)
                    : "";
            }
        }

        Console.WriteLine($"  References: cleaned {cleaned} articles with dangling references");
    }

    private static List<string> ParseMessageIdList(string references)
    {
        var ids = new List<string>();

        var current = 0;

        while (current < references.Length)
        {
            // Find next '<'
            var start = references.IndexOf('<', current);

            if (start < 0)
            {
                break;
            }

            var end = references.IndexOf('>', start);

            if (end < 0)
            {
                break;
            }

            ids.Add(references[start..(end + 1)]);
            current = end + 1;
        }

        return ids;
    }
}
