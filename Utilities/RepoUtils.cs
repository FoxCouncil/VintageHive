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
    public static Dictionary<string, Tuple<string, string>> Get()
    {
        var repos = new Dictionary<string, Tuple<string, string>>
        {
            { "local", new Tuple<string, string>("Downloads Folder", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads")) },
            { "vault", new Tuple<string, string>("Retro Vault", "O:\\Retro\\computer\\vault") }
        };

        return repos;
    }
}
