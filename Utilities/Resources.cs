// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Reflection;

namespace VintageHive.Utilities;

internal static class Resources
{
    const string ResourcesStaticsPath = "VintageHive.Statics.";

    public static readonly Assembly AppResourcesPath = typeof(Resources).Assembly;

    public static readonly Dictionary<string, byte[]> Statics = new();

    public static void Initialize()
    {
        var resources = AppResourcesPath.GetManifestResourceNames();

        foreach (var name in resources)
        {
            if (name.StartsWith(ResourcesStaticsPath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = name[ResourcesStaticsPath.Length..];

                var resourceData = GetManifestResourceData(name);

                Statics.Add(relativePath, resourceData);
            }
        }
    }

    public static bool HasFile(string path)
    {
        return Statics.ContainsKey(path);
    }

    public static string GetStaticsResourceString(string path)
    {
        if (Statics.TryGetValue(path, out byte[] value))
        {
            return Encoding.UTF8.GetString(value);
        }

        return null;
    }

    public static byte[] GetStaticsResourceData(string path)
    {
        if (Statics.TryGetValue(path, out byte[] value))
        {
            return value;
        }

        return null;
    }

    static byte[] GetManifestResourceData(string embedPath)
    {
        using var ms = new MemoryStream();

        AppResourcesPath.GetManifestResourceStream(embedPath).CopyTo(ms);

        return ms.ToArray();
    }
}