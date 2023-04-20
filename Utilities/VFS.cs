namespace VintageHive.Utilities;

public static class VFS
{
    public static readonly string AppDirectory = AppDomain.CurrentDomain.BaseDirectory;

    // The base directory to store all changeable and user play-with-able files, in a FileSystem agnostic way
    public static readonly string FilesPath = Path.Combine(AppDirectory, "vfs");

    // Used to put files up for download quickly from local hive site
    public static readonly string DownloadPath = Path.Combine(FilesPath, "downloads");

    // Used to alter/extend already existing "internal" websites or host global assets
    public static readonly string StaticsPath = Path.Combine(FilesPath, "statics");

    // Used to store all read/write SQLite database files
    public static readonly string DataPath = Path.Combine(FilesPath, "data");

    // Used to emulate an ISP, with domain support
    public static readonly string HostingPath = Path.Combine(FilesPath, "hosting");

    public static void Init()
    {
        var systemDirectories = new[] { FilesPath, DownloadPath, StaticsPath, DataPath, HostingPath };

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

    public static bool DirectoryExists(string directoryPath)
    {
        if (!TryGetFileSystemPath(directoryPath, out var combinedPath))
        {
            return false;
        }

        return Directory.Exists(combinedPath);
    }

    public static void DirectoryCreate(string directoryPath)
    {
        if (!TryGetFileSystemPath(directoryPath, out var combinedPath))
        {
            return;
        }

        Directory.CreateDirectory(combinedPath);
    }

    public static void DirectoryDelete(string directoryPath)
    {
        if (!TryGetFileSystemPath(directoryPath, out var combinedPath))
        {
            return;
        }

        Directory.Delete(combinedPath);
    }

    public static void DirectoryMove(string fromPath, string toPath)
    {
        if (!TryGetFileSystemPath(fromPath, out var combinedFromPath) || !TryGetFileSystemPath(toPath, out var combinedToPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"DirectoryMove Error: {fromPath} or {toPath} paths are not valid.", "");

            return;
        }

        Directory.Move(combinedFromPath, combinedToPath);
    }

    public static string[] DirectoryList(string directoryPath)
    {
        if (!TryGetFileSystemPath(directoryPath, out var combinedPath))
        {
            return null;
        }

        return Directory.GetDirectories(combinedPath).Concat(Directory.GetFiles(combinedPath)).ToArray();
    }

    public static bool FileExists(string filePath)
    {
        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return false;
        }

        return File.Exists(combinedPath);
    }

    public static async Task<string> FileReadStringAsync(string filePath)
    {
        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(combinedPath);
    }

    public static string FileReadString(string filePath)
    {
        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return File.ReadAllText(combinedPath);
    }

    public static FileStream FileReadStream(string filePath)
    {
        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return File.OpenRead(combinedPath);
    }


    public static async Task<byte[]> FileReadDataAsync(string filePath)
    {
        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(combinedPath);
    }

    public static FileStream FileWrite(string filePath)
    {
        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return null;
        }

        return File.OpenWrite(combinedPath);
    }

    public static void FileDelete(string filePath)
    {
        if (!TryGetFileSystemPath(filePath, out var combinedPath))
        {
            return;
        }

        File.Delete(combinedPath);
    }

    public static void FileMove(string fromPath, string toPath)
    {
        if (!TryGetFileSystemPath(fromPath, out var combinedFromPath) || !TryGetFileSystemPath(toPath, out var combinedToPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"FileMove Error: {fromPath} or {toPath} paths are not valid.", "");

            return;
        }

        File.Move(combinedFromPath, combinedToPath);
    }

    static bool TryGetFileSystemPath(string virtualPath, out string fileSystemPath, string traceId = "")
    {
        fileSystemPath = null;

        if (!CheckPathIntegrity(virtualPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {virtualPath} path is not valid.", traceId);

            return false;
        }

        fileSystemPath = Path.GetFullPath(Path.Combine(FilesPath, virtualPath));

        return true;
    }

    static bool CheckPathIntegrity(string path)
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
