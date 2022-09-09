using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace VintageHive.Processors.LocalServer;

internal class LocalServerFileProvider : IFileProvider
{
    readonly DirectoryInfo BaseDirectory;

    public LocalServerFileProvider(DirectoryInfo baseDirectory)
    {
        BaseDirectory = baseDirectory;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        throw new NotImplementedException();
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var fullPath = Path.Combine(BaseDirectory.FullName, subpath).Replace(".liquid", string.Empty);

        return new LocalServerFile(subpath, fullPath);
    }

    public IChangeToken Watch(string filter)
    {
        throw new NotImplementedException();
    }
}
