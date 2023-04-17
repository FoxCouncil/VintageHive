namespace VintageHive.Utilities;

internal static class RepoUtils
{
    public static Dictionary<string, Tuple<string, string>> Get()
    {
        var repos = new Dictionary<string, Tuple<string, string>>
        {
            { "downloads", new Tuple<string, string>("Downloads Folder", VFS.DownloadPath) },
        };

        return repos;
    }
}
