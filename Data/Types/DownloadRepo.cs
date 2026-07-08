// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

/// <summary>
/// A user-added download repository: a named local directory browsable from the download center.
/// Stored as a JSON list under <see cref="ConfigNames.DownloadRepos"/>.
/// </summary>
public class DownloadRepo
{
    public string ShortName { get; set; }

    public string Name { get; set; }

    public string Path { get; set; }
}
