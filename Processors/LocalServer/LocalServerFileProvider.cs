// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace VintageHive.Processors.LocalServer;

internal class LocalServerFileProvider : IFileProvider
{
    // The controller directory of the request currently rendering. Held per async flow so the single
    // provider instance shared on TemplateOptions.Default resolves the correct domain's partials even
    // when different domains render concurrently. (Assigning a fresh FileProvider to the shared
    // options object per request instead - as this used to - races across threads.)
    static readonly AsyncLocal<string> currentRoot = new();

    public static void SetCurrentRoot(string rootDirectory)
    {
        currentRoot.Value = rootDirectory;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        // Templates are resolved by path via GetFileInfo; directory enumeration isn't used.
        return NotFoundDirectoryContents.Singleton;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var path = subpath.Replace(".liquid", string.Empty);

        var fullPath = Path.Combine(currentRoot.Value ?? string.Empty, path);

        return new LocalServerFile(fullPath);
    }

    public IChangeToken Watch(string filter)
    {
        return NullChangeToken.Singleton;
    }
}
