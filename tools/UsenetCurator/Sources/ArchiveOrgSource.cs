// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.IO.Compression;
using UsenetCurator.Parsing;

namespace UsenetCurator.Sources;

internal class ArchiveOrgSource : IArticleSource
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(30),
    };

    static ArchiveOrgSource()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("UsenetCurator/1.0 (VintageHive; +https://github.com/FoxCouncil/VintageHive)");
    }

    public async Task<List<RawArticle>> FetchArticlesAsync(GroupDefinition group, string cachePath, int readLimit, CancellationToken ct)
    {
        var url = GroupManifest.GetDownloadUrl(group);
        var zipPath = Path.Combine(cachePath, $"{group.Name}.mbox.zip");

        // Download if not cached
        if (!File.Exists(zipPath))
        {
            Console.Write($"  Downloading {group.Name}.mbox.zip ... ");

            try
            {
                await DownloadFileAsync(url, zipPath, ct);

                var fileSize = new FileInfo(zipPath).Length;

                Console.WriteLine($"{fileSize / (1024.0 * 1024.0):F1} MB");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");

                // Clean up partial download
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                return [];
            }
        }
        else
        {
            var fileSize = new FileInfo(zipPath).Length;

            Console.WriteLine($"  Using cached {group.Name}.mbox.zip ({fileSize / (1024.0 * 1024.0):F1} MB)");
        }

        // Parse articles from the mbox inside the zip
        Console.Write($"  Parsing articles ... ");

        var articles = ParseMboxZip(zipPath, group, readLimit);

        Console.WriteLine($"{articles.Count} raw articles extracted");

        return articles;
    }

    private static List<RawArticle> ParseMboxZip(string zipPath, GroupDefinition group, int readLimit)
    {
        var articles = new List<RawArticle>();

        try
        {
            using var zipStream = File.OpenRead(zipPath);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            // Find the mbox entry (should be the main/only file)
            var mboxEntry = archive.Entries
                .FirstOrDefault(e => e.Name.EndsWith(".mbox", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e => e.Length > 0);

            if (mboxEntry == null)
            {
                Console.WriteLine("  WARNING: No mbox file found in zip");

                return articles;
            }

            using var mboxStream = mboxEntry.Open();

            foreach (var rawMessage in MboxParser.SplitMessages(mboxStream, readLimit))
            {
                var article = ArticleParser.Parse(rawMessage, group.Name);

                if (article != null && !string.IsNullOrEmpty(article.MessageId))
                {
                    articles.Add(article);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARNING: Error parsing mbox: {ex.Message}");
        }

        return articles;
    }

    private static async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        var tempPath = destPath + ".tmp";

        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

                    downloaded += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        var pct = (double)downloaded / totalBytes.Value * 100;

                        Console.Write($"\r  Downloading {Path.GetFileName(destPath)} ... {pct:F0}% ({downloaded / (1024.0 * 1024.0):F1} MB)  ");
                    }
                }
            }

            Console.Write("\r");

            // Streams are now closed, safe to move
            File.Move(tempPath, destPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }
}
