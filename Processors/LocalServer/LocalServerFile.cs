// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Extensions.FileProviders;
using Resources = VintageHive.Utilities.Resources;

namespace VintageHive.Processors.LocalServer;

public class LocalServerFile : IFileInfo
{
    public string Name { get; } = "";

    public string PhysicalPath { get; }

    public bool Exists { get; }

    public long Length => -1;

    public DateTimeOffset LastModified => DateTimeOffset.MinValue;

    public bool IsDirectory => false;

    readonly bool IsPhysical;

    readonly bool IsVirtual;

    readonly string VirtualPath;

    public LocalServerFile(string path)
    {
        PhysicalPath = VFS.GetVirtualPath(VFS.StaticsPath, path);
        IsPhysical = VFS.FileExists(PhysicalPath);

        VirtualPath = path.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        IsVirtual = Resources.HasFile(VirtualPath);
;
        Exists = IsVirtual || IsPhysical;
    }

    public Stream CreateReadStream()
    {
        return IsPhysical ? VFS.FileReadStream(PhysicalPath) : new MemoryStream(Resources.GetStaticsResourceData(VirtualPath));
    }
}
