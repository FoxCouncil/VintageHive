// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsenetCurator.Sources;

namespace UsenetCurator.Export;

internal static class JsonExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Exports groups.json (plain) and articles.json.gz (GZip compressed)
    /// to the specified output directory.
    /// </summary>
    public static void Export(
        Dictionary<string, List<RawArticle>> allGroups,
        List<GroupDefinition> groupDefs,
        string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        ExportGroups(groupDefs, allGroups, outputDir);
        ExportArticles(allGroups, outputDir);
    }

    private static void ExportGroups(
        List<GroupDefinition> groupDefs,
        Dictionary<string, List<RawArticle>> allGroups,
        string outputDir)
    {
        var groups = new List<ExportGroup>();

        foreach (var def in groupDefs)
        {
            if (allGroups.ContainsKey(def.Name) && allGroups[def.Name].Count > 0)
            {
                groups.Add(new ExportGroup
                {
                    Name = def.Name,
                    Description = def.Description,
                });
            }
        }

        var json = JsonSerializer.Serialize(groups, JsonOptions);
        var path = Path.Combine(outputDir, "groups.json");

        File.WriteAllText(path, json, Encoding.UTF8);

        Console.WriteLine($"  Wrote {path} ({groups.Count} groups, {json.Length / 1024.0:F1} KB)");
    }

    private static void ExportArticles(
        Dictionary<string, List<RawArticle>> allGroups,
        string outputDir)
    {
        // Build the export dictionary with numbered articles
        var exportData = new Dictionary<string, List<ExportArticle>>();
        var totalArticles = 0;

        foreach (var (groupName, articles) in allGroups)
        {
            if (articles.Count == 0)
            {
                continue;
            }

            var exportArticles = new List<ExportArticle>();

            for (var i = 0; i < articles.Count; i++)
            {
                var article = articles[i];
                var body = article.Body ?? "";
                var bytes = Encoding.ASCII.GetByteCount(body);
                var lineCount = body.Split('\n').Length;

                exportArticles.Add(new ExportArticle
                {
                    Number = i + 1,
                    MessageId = article.MessageId,
                    From = article.From,
                    Subject = article.Subject,
                    Date = article.Date,
                    References = string.IsNullOrEmpty(article.References) ? "" : article.References,
                    Body = body,
                });
            }

            exportData[groupName] = exportArticles;
            totalArticles += exportArticles.Count;
        }

        // Serialize to JSON
        var json = JsonSerializer.Serialize(exportData, CompactJsonOptions);

        // Write GZip compressed
        var gzipPath = Path.Combine(outputDir, "articles.json.gz");

        using (var fileStream = File.Create(gzipPath))
        using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
        {
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            gzipStream.Write(jsonBytes);
        }

        var compressedSize = new FileInfo(gzipPath).Length;
        var uncompressedSize = Encoding.UTF8.GetByteCount(json);
        var ratio = (1.0 - (double)compressedSize / uncompressedSize) * 100;

        Console.WriteLine($"  Wrote {gzipPath}");
        Console.WriteLine($"    {totalArticles} articles across {exportData.Count} groups");
        Console.WriteLine($"    Uncompressed: {uncompressedSize / (1024.0 * 1024.0):F1} MB");
        Console.WriteLine($"    Compressed:   {compressedSize / (1024.0 * 1024.0):F1} MB ({ratio:F0}% reduction)");

        // Also write an uncompressed copy for debugging
        var jsonPath = Path.Combine(outputDir, "articles.json");

        File.WriteAllText(jsonPath, json, Encoding.UTF8);

        Console.WriteLine($"  Wrote {jsonPath} (uncompressed copy for debugging)");
    }

    private class ExportGroup
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }

    private class ExportArticle
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("messageId")]
        public string MessageId { get; set; }

        [JsonPropertyName("from")]
        public string From { get; set; }

        [JsonPropertyName("subject")]
        public string Subject { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; }

        [JsonPropertyName("references")]
        public string References { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; }
    }
}
