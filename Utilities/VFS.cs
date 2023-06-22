// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.IO;

namespace VintageHive.Utilities;

public static class VFS
{
    public const string DebugStaticsPathHelper = "../../../../Statics/";

    public const string BasePath = "vfs/";

    public const string DataPath = "data/";

    public const string StaticsPath = "statics/";

    public const string DownloadsPath = "downloads/";

    public const string HostingPath = "hosting/";

    private static readonly string appDirectory = AppDomain.CurrentDomain.BaseDirectory;

    // The base directory to store all changeable and user play-with-able files, in a FileSystem agnostic way
    private static readonly string filesPath = Path.Combine(appDirectory, "vfs");

    // Used to store all read/write SQLite database files
    private static readonly string dataPath = Path.Combine(filesPath, "data");

    // Used to alter/extend already existing "internal" websites or host global assets
    private static readonly string staticsPath = Path.Combine(filesPath, "statics");

    // Used to put files up for download quickly from local hive site
    private static readonly string downloadsPath = Path.Combine(filesPath, "downloads");

    // Used to emulate an ISP, with domain support
    private static readonly string hostingPath = Path.Combine(filesPath, "hosting");

    public static void Init()
    {
        var systemDirectories = new[] { filesPath, downloadsPath, staticsPath, dataPath, hostingPath };

        foreach (var directory in systemDirectories)
        {
            if (Directory.Exists(directory))
            {
                continue;
            }

            Log.WriteLine(Log.LEVEL_INFO, "VFS", $"Directory ({directory}) doesn't exist, creating it,", "");

            Directory.CreateDirectory(directory);

            var readmePath = $"docs.vfs_{Path.GetFileName(directory).ToLower()}_readme.txt";

            if (Resources.HasFile(readmePath))
            {
                File.WriteAllText(Path.Combine(directory, "readme.txt"), Resources.GetStaticsResourceString(readmePath));
            }
        }
    }

    static void WriteDebugLine(string what, string path)
    {
        Log.WriteLine(Log.LEVEL_DEBUG, nameof(VFS), $"{what}({path})", string.Empty);
    }

    public static bool DirectoryExists(string directoryPath)
    {
        WriteDebugLine(nameof(DirectoryExists), directoryPath);

        if (!TryGetFileSystemPath(directoryPath, out var combinedPath))
        {
            return false;
        }

        return Directory.Exists(combinedPath);
    }

    public static void DirectoryCreate(string directoryPath)
    {
        WriteDebugLine(nameof(DirectoryCreate), directoryPath);

        if (!TryGetFileSystemPath(directoryPath, out var combinedPath))
        {
            return;
        }

        Directory.CreateDirectory(combinedPath);
    }

    public static void DirectoryDelete(string directoryPath)
    {
        WriteDebugLine(nameof(DirectoryDelete), directoryPath);

        if (!TryGetFileSystemPath(directoryPath, out var combinedPath))
        {
            return;
        }

        Directory.Delete(combinedPath);
    }

    public static void DirectoryMove(string fromPath, string toPath)
    {
        WriteDebugLine(nameof(DirectoryMove), $"{fromPath} -> {toPath}");

        if (!TryGetFileSystemPath(fromPath, out var combinedFromPath) || !TryGetFileSystemPath(toPath, out var combinedToPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"DirectoryMove Error: {fromPath} or {toPath} paths are not valid.", "");

            return;
        }

        Directory.Move(combinedFromPath, combinedToPath);
    }

    public static string[] DirectoryList(string directoryPath)
    {
        WriteDebugLine(nameof(DirectoryList), directoryPath);

        if (!TryGetFileSystemPath(directoryPath, out var combinedPath))
        {
            return null;
        }

        return Directory.GetDirectories(combinedPath).Concat(Directory.GetFiles(combinedPath)).ToArray();
    }

    public static bool FileExists(string filePath)
    {
        WriteDebugLine(nameof(FileExists), filePath);

        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return false;
        }

        return File.Exists(combinedPath);
    }

    public static async Task<string> FileReadStringAsync(string filePath)
    {
        WriteDebugLine(nameof(FileReadStringAsync), filePath);

        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(combinedPath);
    }

    public static string FileReadString(string filePath)
    {
        WriteDebugLine(nameof(FileReadString), filePath);

        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return File.ReadAllText(combinedPath);
    }

    public static FileStream FileReadStream(string filePath)
    {
        WriteDebugLine(nameof(FileReadStream), filePath);

        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return File.OpenRead(combinedPath);
    }

    public static async Task<byte[]> FileReadDataAsync(string filePath)
    {
        WriteDebugLine(nameof(FileReadDataAsync), filePath);

        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(combinedPath);
    }

    public static FileStream FileWrite(string filePath)
    {
        WriteDebugLine(nameof(FileWrite), filePath);

        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return File.OpenWrite(combinedPath);
    }

    public static void FileDelete(string filePath)
    {
        WriteDebugLine(nameof(FileDelete), filePath);

        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return;
        }

        File.Delete(combinedPath);
    }

    public static void FileMove(string fromPath, string toPath)
    {
        WriteDebugLine(nameof(FileMove), $"{fromPath} -> {toPath}");

        if (!TryGetFileSystemPath(fromPath, out var combinedFromPath) || !TryGetFileSystemPath(toPath, out var combinedToPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"FileMove Error: {fromPath} or {toPath} paths are not valid.", "");

            return;
        }

        File.Move(combinedFromPath, combinedToPath);
    }

    public static string GetVirtualPath(string location, string filePath)
    {
        return Path.GetFullPath(Path.Combine(location, filePath)).Replace(appDirectory, string.Empty);
    }

    internal static string GetFullPath(string location)
    {
        return Path.GetFullPath(Path.Combine(filesPath, location));
    }

    private static bool TryGetFileSystemPath(string virtualPath, out string fileSystemPath, string traceId = "")
    {
        fileSystemPath = null;

        if (!CheckPathIntegrity(virtualPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {virtualPath} path is not valid.", traceId);

            return false;
        }

        var possibleDebugPath = Path.Combine(appDirectory, DebugStaticsPathHelper);

        if (virtualPath.StartsWith(StaticsPath[..^1]) && Directory.Exists(possibleDebugPath) && Mind.IsDebug && !Mind.IsDocker)
        {
            var pathNegateLength = StaticsPath.Length;

            fileSystemPath = Path.GetFullPath(Path.Combine(possibleDebugPath, virtualPath[pathNegateLength..]));
        }
        else
        {
            fileSystemPath = Path.GetFullPath(Path.Combine(filesPath, virtualPath));
        }

        return true;
    }

    private static bool CheckPathIntegrity(string path)
    {
        if (Path.IsPathRooted(path))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", "Error: Absolute paths are not allowed.", "");

            return false;
        }

        if (path.Contains(".."))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", "Error: Relative parental paths are not allowed.", "");

            return false;
        }

        return true;
    }
}