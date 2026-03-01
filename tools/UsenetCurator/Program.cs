// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using UsenetCurator;
using UsenetCurator.Curation;
using UsenetCurator.Export;
using UsenetCurator.Sources;

// Parse command-line arguments
var options = ParseArgs(args);

Console.WriteLine("==============================================");
Console.WriteLine("  UsenetCurator - VintageHive Archive Fetcher");
Console.WriteLine("==============================================");
Console.WriteLine();

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nCancelling...");
};

// Build pipeline config from CLI args (handles both full and CI mode)
var config = await Setup.BuildAsync(options, cts.Token);

if (config == null)
{
    return 1;
}

var modeName = config.IsCiMode ? "CI" : "FULL";

Console.WriteLine($"  Mode:    {modeName}");
Console.WriteLine($"  Output:  {Path.GetFullPath(config.OutputDir)}");
Console.WriteLine($"  Years:   {config.MinYear}-{config.MaxYear}");
Console.WriteLine($"  Groups:  {config.Groups.Count}");
Console.WriteLine();

return await RunPipeline(config, cts.Token);

// ==========================================================================
// Pipeline
// ==========================================================================

static async Task<int> RunPipeline(PipelineConfig config, CancellationToken ct)
{
    var cachePath = Path.Combine(AppContext.BaseDirectory, "cache");

    Directory.CreateDirectory(cachePath);
    Directory.CreateDirectory(config.OutputDir);

    var source = new ArchiveOrgSource();
    var allArticles = new Dictionary<string, List<RawArticle>>(StringComparer.OrdinalIgnoreCase);

    // Phase 1: Download and parse
    Console.WriteLine("PHASE 1: Download and Parse");
    Console.WriteLine("---------------------------");

    foreach (var group in config.Groups)
    {
        if (ct.IsCancellationRequested)
        {
            break;
        }

        Console.WriteLine($"\n[{group.Name}]");

        var readLimit = config.ReadLimits.GetValueOrDefault(group.Name, int.MaxValue);

        try
        {
            var articles = await source.FetchArticlesAsync(group, cachePath, readLimit, ct);

            if (articles.Count > 0)
            {
                allArticles[group.Name] = articles;

                Console.WriteLine($"  Got {articles.Count} raw articles");
            }
            else
            {
                Console.WriteLine($"  WARNING: No articles found for {group.Name}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  Cancelled.");

            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERROR: {ex.Message}");
        }
    }

    if (ct.IsCancellationRequested)
    {
        Console.WriteLine("\nAborted by user.");

        return 1;
    }

    // Phase 2: Filter
    Console.WriteLine("\n\nPHASE 2: Filter");
    Console.WriteLine("---------------");

    foreach (var (groupName, articles) in allArticles.ToList())
    {
        var filtered = ArticleFilter.Filter(articles, groupName, config.MinYear, config.MaxYear);

        allArticles[groupName] = filtered;
    }

    // Phase 3: Number and cap
    Console.WriteLine("\n\nPHASE 3: Number and Cap");
    Console.WriteLine("-----------------------");

    ArticleNumberer.NumberAndCap(allArticles, config.MaxPerGroup);

    foreach (var (groupName, articles) in allArticles)
    {
        Console.WriteLine($"  {groupName}: {articles.Count} articles");
    }

    // Phase 4: Resolve references
    Console.WriteLine("\n\nPHASE 4: Resolve References");
    Console.WriteLine("---------------------------");

    ReferenceResolver.Resolve(allArticles);

    // Phase 5: Add alt.hive.help
    Console.WriteLine("\n\nPHASE 5: Add alt.hive.help");
    Console.WriteLine("--------------------------");

    var helpArticles = HiveHelpGenerator.Generate();

    allArticles[HiveHelpGenerator.GroupName] = helpArticles;

    Console.WriteLine($"  Generated {helpArticles.Count} help articles");

    // Build full group definition list (archive groups + hive help)
    var allGroupDefs = config.Groups
        .Where(g => allArticles.ContainsKey(g.Name) && allArticles[g.Name].Count > 0)
        .ToList();

    allGroupDefs.Insert(0, new GroupDefinition
    {
        Name = HiveHelpGenerator.GroupName,
        Description = HiveHelpGenerator.GroupDescription,
        Collection = "local",
        MaxArticles = 100,
    });

    // Phase 6: Deduplicate Message-IDs across all groups
    Console.WriteLine("\n\nPHASE 6: Deduplicate");
    Console.WriteLine("--------------------");

    var seenIds = new HashSet<string>(StringComparer.Ordinal);
    var dupeCount = 0;

    foreach (var (groupName, articles) in allArticles)
    {
        var deduped = new List<RawArticle>();

        foreach (var article in articles)
        {
            if (seenIds.Add(article.MessageId))
            {
                deduped.Add(article);
            }
            else
            {
                dupeCount++;
            }
        }

        allArticles[groupName] = deduped;
    }

    Console.WriteLine($"  Removed {dupeCount} duplicate Message-IDs");

    // Phase 7: Export
    Console.WriteLine("\n\nPHASE 7: Export");
    Console.WriteLine("---------------");

    JsonExporter.Export(allArticles, allGroupDefs, config.OutputDir);

    // Summary
    var totalArticles = allArticles.Values.Sum(a => a.Count);
    var totalGroups = allArticles.Count(g => g.Value.Count > 0);

    Console.WriteLine($"\n\nDONE!");
    Console.WriteLine($"  {totalGroups} groups, {totalArticles} articles");
    Console.WriteLine($"  Output: {Path.GetFullPath(config.OutputDir)}");

    if (!config.IsCiMode)
    {
        Console.WriteLine($"\nRestart VintageHive to load the new data.");
    }

    return 0;
}

// ==========================================================================
// Arg parsing
// ==========================================================================

static Dictionary<string, string> ParseArgs(string[] cliArgs)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < cliArgs.Length; i++)
    {
        var current = cliArgs[i];

        // Handle -ci shorthand
        if (current == "-ci")
        {
            result["ci"] = "true";

            continue;
        }

        if (current.StartsWith("--"))
        {
            var key = current[2..];

            // Boolean flags (no value)
            if (key == "bundle" || key == "ci")
            {
                result[key == "bundle" ? "ci" : key] = "true";

                continue;
            }

            // Key-value pairs
            if (i + 1 < cliArgs.Length && !cliArgs[i + 1].StartsWith("-"))
            {
                result[key] = cliArgs[++i];
            }
            else
            {
                result[key] = "true";
            }
        }
    }

    return result;
}
