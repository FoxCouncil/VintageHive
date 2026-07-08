// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace VintageHive.Processors.LocalServer;

internal class LocalServerFileProvider : IFileProvider
{
    readonly string subPath;

    public LocalServerFileProvider(string subPath)
    {
        this.subPath = subPath;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        // Templates are resolved by path via GetFileInfo; directory enumeration isn't used.
        return NotFoundDirectoryContents.Singleton;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var path = subpath.Replace(".liquid", string.Empty);

        var fullPath = Path.Combine(subPath, path);

        return new LocalServerFile(fullPath);
    }

    public IChangeToken Watch(string filter)
    {
        return NullChangeToken.Singleton;
    }
}
