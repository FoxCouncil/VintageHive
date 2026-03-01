// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.Json;

namespace UsenetCurator.Sources;

internal class DiscoveredGroup
{
    public string Name { get; init; }

    public string Collection { get; init; }

    public long SizeBytes { get; init; }
}

internal static class CollectionDiscovery
{
    public static readonly string[] AllCollections =
    [
        "usenet-comp",
        "usenet-rec",
        "usenet-alt",
        "usenet-sci",
        "usenet-news",
        "usenet-misc",
        "usenet-soc",
        "usenet-talk",
    ];

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    static CollectionDiscovery()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("UsenetCurator/1.0 (VintageHive; +https://github.com/FoxCouncil/VintageHive)");
    }

    public static async Task<List<DiscoveredGroup>> DiscoverGroupsAsync(IEnumerable<string> collections, CancellationToken ct)
    {
        var tasks = collections.Select(c => FetchCollectionAsync(c, ct)).ToList();
        var results = await Task.WhenAll(tasks);

        return results.SelectMany(r => r).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<List<DiscoveredGroup>> FetchCollectionAsync(string collection, CancellationToken ct)
    {
        var groups = new List<DiscoveredGroup>();
        var url = $"https://archive.org/metadata/{collection}/files";

        Console.WriteLine($"  Discovering groups from {collection}...");

        try
        {
            using var response = await Http.GetAsync(url, ct);

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("result", out var resultArray))
            {
                foreach (var file in resultArray.EnumerateArray())
                {
                    if (!file.TryGetProperty("name", out var nameProp))
                    {
                        continue;
                    }

                    var fileName = nameProp.GetString();

                    if (fileName == null || !fileName.EndsWith(".mbox.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Extract group name: "comp.lang.c.mbox.zip" → "comp.lang.c"
                    var groupName = fileName[..^".mbox.zip".Length];

                    long sizeBytes = 0;

                    if (file.TryGetProperty("size", out var sizeProp))
                    {
                        if (sizeProp.ValueKind == JsonValueKind.Number)
                        {
                            sizeBytes = sizeProp.GetInt64();
                        }
                        else if (sizeProp.ValueKind == JsonValueKind.String && long.TryParse(sizeProp.GetString(), out var parsed))
                        {
                            sizeBytes = parsed;
                        }
                    }

                    groups.Add(new DiscoveredGroup
                    {
                        Name = groupName,
                        Collection = collection,
                        SizeBytes = sizeBytes,
                    });
                }
            }

            Console.WriteLine($"  Found {groups.Count} groups in {collection}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARNING: Failed to discover {collection}: {ex.Message}");
        }

        return groups;
    }
}
