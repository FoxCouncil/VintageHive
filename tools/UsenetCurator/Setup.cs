// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using UsenetCurator.Sources;

namespace UsenetCurator;

internal static class Setup
{
    public static async Task<PipelineConfig> BuildAsync(Dictionary<string, string> options, CancellationToken ct)
    {
        var isCiMode = options.ContainsKey("ci") || options.ContainsKey("bundle");

        if (isCiMode)
        {
            return BuildCiConfig(options);
        }

        return await BuildFullConfig(options, ct);
    }

    private static PipelineConfig BuildCiConfig(Dictionary<string, string> options)
    {
        var outputDir = options.GetValueOrDefault("output", Path.Combine("..", "..", "Statics", "usenet"));

        // Parse optional year range override
        ParseYearRange(options, out var minYear, out var maxYear);

        // Parse optional max-per-group override
        var maxOverride = options.ContainsKey("max-per-group") ? int.Parse(options["max-per-group"]) : (int?)null;

        // Filter by --groups if specified
        var targetGroups = FilterManifestGroups(options);

        var maxPerGroup = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var readLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in targetGroups)
        {
            var cap = maxOverride ?? group.MaxArticles;

            maxPerGroup[group.Name] = cap;
            readLimits[group.Name] = cap * 20;
        }

        return new PipelineConfig
        {
            Groups = targetGroups,
            MaxPerGroup = maxPerGroup,
            ReadLimits = readLimits,
            MinYear = minYear,
            MaxYear = maxYear,
            OutputDir = outputDir,
            IsCiMode = true,
        };
    }

    private static async Task<PipelineConfig> BuildFullConfig(Dictionary<string, string> options, CancellationToken ct)
    {
        var outputDir = options.GetValueOrDefault("output", Path.Combine("data", "usenet"));

        // Parse optional year range override
        ParseYearRange(options, out var minYear, out var maxYear);

        // Parse optional max-per-group override (null = no cap)
        var maxOverride = options.ContainsKey("max-per-group") ? int.Parse(options["max-per-group"]) : (int?)null;

        List<GroupDefinition> targetGroups;

        if (options.ContainsKey("groups"))
        {
            // Specific groups requested — use manifest entries if they exist, otherwise create from name
            var requestedNames = options["groups"]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var manifestLookup = GroupManifest.Groups.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

            targetGroups = [];

            foreach (var name in requestedNames)
            {
                if (manifestLookup.TryGetValue(name, out var existing))
                {
                    targetGroups.Add(existing);
                }
                else
                {
                    // Try to guess the collection from the hierarchy prefix
                    var collection = GuessCollection(name);

                    targetGroups.Add(new GroupDefinition
                    {
                        Name = name,
                        Description = $"{name} discussions",
                        Collection = collection,
                        MaxArticles = 2000,
                    });
                }
            }
        }
        else
        {
            // Discover groups from archive.org
            var collections = GetCollections(options);

            Console.WriteLine($"Discovering groups from {collections.Count} collections...");
            Console.WriteLine();

            var discovered = await CollectionDiscovery.DiscoverGroupsAsync(collections, ct);

            Console.WriteLine();
            Console.WriteLine($"Discovered {discovered.Count} groups total");
            Console.WriteLine();

            targetGroups = discovered.Select(GroupManifest.FromDiscovered).ToList();
        }

        if (targetGroups.Count == 0)
        {
            Console.WriteLine("ERROR: No groups found.");

            return null;
        }

        var maxPerGroup = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var readLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in targetGroups)
        {
            maxPerGroup[group.Name] = maxOverride;
            readLimits[group.Name] = int.MaxValue;
        }

        return new PipelineConfig
        {
            Groups = targetGroups,
            MaxPerGroup = maxPerGroup,
            ReadLimits = readLimits,
            MinYear = minYear,
            MaxYear = maxYear,
            OutputDir = outputDir,
            IsCiMode = false,
        };
    }

    private static List<GroupDefinition> FilterManifestGroups(Dictionary<string, string> options)
    {
        var allGroups = GroupManifest.Groups.ToList();

        if (options.TryGetValue("groups", out var groupFilter))
        {
            var filterSet = new HashSet<string>(
                groupFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase
            );

            allGroups = allGroups.Where(g => filterSet.Contains(g.Name)).ToList();
        }

        if (options.TryGetValue("collections", out var collFilter))
        {
            var collNames = collFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.StartsWith("usenet-") ? c : $"usenet-{c}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            allGroups = allGroups.Where(g => collNames.Contains(g.Collection)).ToList();
        }

        return allGroups;
    }

    private static List<string> GetCollections(Dictionary<string, string> options)
    {
        if (options.TryGetValue("collections", out var collFilter))
        {
            return collFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.StartsWith("usenet-") ? c : $"usenet-{c}")
                .ToList();
        }

        return CollectionDiscovery.AllCollections.ToList();
    }

    private static void ParseYearRange(Dictionary<string, string> options, out int minYear, out int maxYear)
    {
        minYear = 1980;
        maxYear = 2005;

        if (options.TryGetValue("years", out var years))
        {
            var parts = years.Split('-');

            if (parts.Length == 2 && int.TryParse(parts[0], out var min) && int.TryParse(parts[1], out var max))
            {
                minYear = min;
                maxYear = max;
            }
            else
            {
                Console.WriteLine($"WARNING: Invalid --years format '{years}', using default 1980-2005");
            }
        }
    }

    private static string GuessCollection(string groupName)
    {
        var prefix = groupName.Split('.')[0].ToLowerInvariant();

        return prefix switch
        {
            "comp" => "usenet-comp",
            "rec" => "usenet-rec",
            "alt" => "usenet-alt",
            "sci" => "usenet-sci",
            "news" => "usenet-news",
            "misc" => "usenet-misc",
            "soc" => "usenet-soc",
            "talk" => "usenet-talk",
            _ => $"usenet-{prefix}",
        };
    }
}
