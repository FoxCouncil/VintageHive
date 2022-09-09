using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VintageHive.Processors.LocalServer;
using VintageHive.Proxy.Http;

namespace VintageHive.Utilities;

internal static class RepoUtils
{
    public static Dictionary<string, Tuple<string, DirectoryInfo>> Get()
    {
        var repos = new Dictionary<string, Tuple<string, DirectoryInfo>>();

        var localDownloadsFolder = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "download"));

        if (localDownloadsFolder.Exists)
        {
            repos.Add("local", new Tuple<string, DirectoryInfo>("Download Folder", localDownloadsFolder));
        }

        var vaultFolder = new DirectoryInfo("O:\\Retro\\Retro Computers\\vault");

        if (vaultFolder.Exists)
        {
            repos.Add("vault", new Tuple<string, DirectoryInfo>("Retro Vault", vaultFolder));
        }

        return repos;
    }
}
