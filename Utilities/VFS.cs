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
}
