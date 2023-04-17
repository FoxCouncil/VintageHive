using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace VintageHive.Processors.LocalServer;

internal class LocalServerFileProvider : IFileProvider
{
    readonly string _subPath;

    public LocalServerFileProvider(string subPath)
    {
        _subPath = subPath;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        throw new NotImplementedException();
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var path = subpath.Replace(".liquid", string.Empty);

        var fullPath = Path.Combine(_subPath, path);

        return new LocalServerFile(fullPath);
    }

    public IChangeToken Watch(string filter)
    {
        throw new NotImplementedException();
    }
}
