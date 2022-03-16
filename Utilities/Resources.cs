using System.Reflection;
using System.Text;

namespace VintageHive.Utilities;

internal static class Resources
{
    const string ResourcesStaticsPath = "VintageHive.Statics.";

    public static readonly Assembly AppResourcesPath = typeof(Resources).Assembly;

    public static readonly Dictionary<string, byte[]> Statics = new Dictionary<string, byte[]>();

    public static void Initialize()
    {
        var resources = AppResourcesPath.GetManifestResourceNames();

        foreach (var name in resources)
        {
            if (name.StartsWith(ResourcesStaticsPath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = name.Substring(ResourcesStaticsPath.Length);

                var lastPeriodCharIdx = relativePath.LastIndexOf(".");

                var directoryPath = relativePath[0..lastPeriodCharIdx].Replace(".", "/");

                relativePath = directoryPath + relativePath[lastPeriodCharIdx..];

                Statics.Add(relativePath, GetManifestResourceData(name));
            }
        }
    }

    public static string GetStaticsResourceString(string path)
    {
        if (Statics.ContainsKey(path))
        {
            return Encoding.UTF8.GetString(Statics[path]);
        }

        return null;
    }

    public static byte[] GetStaticsResourceData(string path)
    {
        if (Statics.ContainsKey(path))
        {
            return Statics[path];
        }

        return null;
    }

    private static byte[] GetManifestResourceData(string embedPath)
    {
        using var ms = new MemoryStream();

        AppResourcesPath.GetManifestResourceStream(embedPath).CopyTo(ms);

        return ms.ToArray();
    }
}