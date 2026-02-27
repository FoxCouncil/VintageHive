// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities;

internal static class RepoUtils
{
    // TODO: Allow users to add custom repositories via config file or UI
    public static Dictionary<string, Tuple<string, string>> Get()
    {
        var repos = new Dictionary<string, Tuple<string, string>>
        {
            { "downloads", new Tuple<string, string>("Downloads Folder", VFS.DownloadsPath) },
            // { "vault", new Tuple<string, string>("Vault", "O:\\Retro\\computer\\vault") },
        };

        return repos;
    }
}
