using Microsoft.Extensions.FileProviders;

namespace VintageHive.Processors.LocalServer;

public class LocalServerFile : IFileInfo
{
    public string Name { get; }

    public string PhysicalPath { get; }

    public bool Exists { get; }

    public long Length => _contents?.Length ?? -1;

    public DateTimeOffset LastModified => DateTimeOffset.MinValue;

    public bool IsDirectory => false;

    byte[] _contents;

    public LocalServerFile(string name, string fullPath)
    {
        Name = name;
        PhysicalPath = fullPath;
        Exists = File.Exists(fullPath);

        if (Exists)
        {
            _contents = File.ReadAllBytes(fullPath);
        }
    }

    public Stream CreateReadStream()
    {
        return new MemoryStream(_contents);
    }
}
