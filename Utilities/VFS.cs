namespace VintageHive.Utilities;

public static class VFS
{
    private static readonly string AppDirectory = AppDomain.CurrentDomain.BaseDirectory;

    private static readonly string FilesPath = Path.Combine(AppDirectory, "./vfs");

    private static readonly string DownloadPath = Path.Combine(AppDirectory, "./downloads");

    public static void Init()
    {
        if (!Directory.Exists(FilesPath))
        {
            Log.WriteLine(Log.LEVEL_INFO, "VFS", $"Directory ({FilesPath}) doesn't exist, creating it,", "");

            Directory.CreateDirectory(FilesPath);
        }

        if (!Directory.Exists(DownloadPath))
        {
            Log.WriteLine(Log.LEVEL_INFO, "VFS", $"Directory ({DownloadPath}) doesn't exist, creating it,", "");

            Directory.CreateDirectory(DownloadPath);
        }
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

    public static bool DirectoryExists(string directoryPath)
    {
        if (!CheckPathIntegrity(directoryPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {directoryPath} path is not valid.", "");

            return false;
        }

        string combinedPath = Path.GetFullPath(Path.Combine(FilesPath, directoryPath));

        return Directory.Exists(combinedPath);
    }

    public static void DirectoryCreate(string directoryPath)
    {
        if (!CheckPathIntegrity(directoryPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {directoryPath} path is not valid.", "");

            return;
        }

        var combinedPath = Path.GetFullPath(Path.Combine(FilesPath, directoryPath));

        Directory.CreateDirectory(combinedPath);
    }

    public static void DirectoryMove(string fromPath, string toPath)
    {
        if (!CheckPathIntegrity(fromPath) || !CheckPathIntegrity(toPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {fromPath} or {toPath} paths are not valid.", "");

            return;
        }

        var combinedFromPath = Path.GetFullPath(Path.Combine(FilesPath, fromPath));
        var combinedToPath = Path.GetFullPath(Path.Combine(FilesPath, toPath));

        Directory.Move(combinedFromPath, combinedToPath);
    }

    public static string[] DirectoryList(string directoryPath)
    {
        if (!CheckPathIntegrity(directoryPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {directoryPath} path is not valid.", "");

            return null;
        }

        var combinedPath = Path.GetFullPath(Path.Combine(FilesPath, directoryPath));

        return Directory.GetDirectories(combinedPath).Concat(Directory.GetFiles(combinedPath)).ToArray();
    }

    public static bool FileExists(string filePath)
    {
        if (!CheckPathIntegrity(filePath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {filePath} path is not valid.", "");

            return false;
        }

        string combinedPath = Path.GetFullPath(Path.Combine(FilesPath, filePath));

        return File.Exists(combinedPath);
    }

    public static FileStream FileRead(string filePath)
    {
        if (!CheckPathIntegrity(filePath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {filePath} path is not valid.", "");

            return null;
        }

        string combinedPath = Path.GetFullPath(Path.Combine(FilesPath, filePath));

        return File.OpenRead(combinedPath);
    }

    public static FileStream FileWrite(string filePath)
    {
        if (!CheckPathIntegrity(filePath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {filePath} path is not valid.", "");

            return null;
        }

        string combinedPath = Path.GetFullPath(Path.Combine(FilesPath, filePath));

        return File.OpenWrite(combinedPath);
    }

    public static void FileDelete(string filePath)
    {
        if (!CheckPathIntegrity(filePath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {filePath} path is not valid.", "");

            return;
        }

        string combinedPath = Path.GetFullPath(Path.Combine(FilesPath, filePath));

        File.Delete(combinedPath);
    }

    public static void FileMove(string fromPath, string toPath)
    {
        if (!CheckPathIntegrity(fromPath) || !CheckPathIntegrity(toPath))
        {
            Log.WriteLine(Log.LEVEL_ERROR, "VFS", $"Error: {fromPath} or {toPath} paths are not valid.", "");

            return;
        }

        var combinedFromPath = Path.GetFullPath(Path.Combine(FilesPath, fromPath));
        var combinedToPath = Path.GetFullPath(Path.Combine(FilesPath, toPath));

        File.Move(combinedFromPath, combinedToPath);
    }
}
