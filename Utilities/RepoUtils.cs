// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities;

internal static class RepoUtils
{
    // The built-in downloads folder; its key is reserved and cannot be removed or reused.
    public const string BuiltInKey = "downloads";

    /// <summary>All repositories keyed by short name: the built-in downloads folder plus user-added ones.</summary>
    public static Dictionary<string, Tuple<string, string>> Get()
    {
        var repos = new Dictionary<string, Tuple<string, string>>
        {
            { BuiltInKey, new Tuple<string, string>("Downloads Folder", VFS.DownloadsPath) },
        };

        foreach (var repo in GetCustom())
        {
            if (!string.IsNullOrWhiteSpace(repo.ShortName) && !repos.ContainsKey(repo.ShortName))
            {
                repos[repo.ShortName] = new Tuple<string, string>(repo.Name, repo.Path);
            }
        }

        return repos;
    }

    /// <summary>The user-added repositories only (built-in excluded).</summary>
    public static List<DownloadRepo> GetCustom()
    {
        return Mind.Db.ConfigGet<List<DownloadRepo>>(ConfigNames.DownloadRepos) ?? new List<DownloadRepo>();
    }

    /// <summary>Add a custom repository. Returns false on invalid input, a duplicate/reserved key, or a missing directory.</summary>
    public static bool Add(string shortName, string name, string path)
    {
        shortName = shortName?.Trim().ToLowerInvariant();
        name = name?.Trim();
        path = path?.Trim();

        if (string.IsNullOrWhiteSpace(shortName) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (shortName == BuiltInKey || !IsValidShortName(shortName) || !Directory.Exists(path))
        {
            return false;
        }

        var custom = GetCustom();

        if (custom.Any(r => string.Equals(r.ShortName, shortName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        custom.Add(new DownloadRepo { ShortName = shortName, Name = name, Path = path });

        Mind.Db.ConfigSet(ConfigNames.DownloadRepos, custom);

        return true;
    }

    /// <summary>Remove a custom repository by short name. Returns false if it wasn't found (the built-in can't be removed).</summary>
    public static bool Remove(string shortName)
    {
        shortName = shortName?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(shortName) || shortName == BuiltInKey)
        {
            return false;
        }

        var custom = GetCustom();

        if (custom.RemoveAll(r => string.Equals(r.ShortName, shortName, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        Mind.Db.ConfigSet(ConfigNames.DownloadRepos, custom);

        return true;
    }

    // Short names go into URLs and are matched against the repo dictionary, so keep them path/URL-safe.
    private static bool IsValidShortName(string shortName)
    {
        return shortName.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }
}
