// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities;

internal static class Log
{
    public const string LEVEL_ERROR = "error";
    public const string LEVEL_INFO = "info";
    public const string LEVEL_REQUEST = "request";
    public const string LEVEL_DEBUG = "debug";
    public const string LEVEL_VERBOSE = "verbose";

    private static readonly object _fileLock = new();
    private static StreamWriter _fileWriter;
    private static bool _fileInitAttempted;

    static Log()
    {
        WriteLine(LEVEL_INFO, "LOG", $"Vintage Hive v{Mind.ApplicationVersion} Startup!", string.Empty);
    }

    public static void WriteLine()
    {
        SendConsole(string.Empty);
    }

    public static void WriteLine(string level, string system, string message, string traceId = "")
    {
        var logItem = new LogItem(level, system, message, traceId);

        WriteLine(logItem);
    }

    public static void WriteLine(LogItem logItem)
    {
        Mind.Db?.WriteLog(logItem);

        SendFile(logItem.ToString());

        if ((logItem.Level == LEVEL_DEBUG && !Mind.IsDebug) || logItem.Level == LEVEL_REQUEST || logItem.Level == LEVEL_VERBOSE)
        {
            return;
        }

        SendConsole(logItem.ToString());
    }

    public static void WriteException(string system, Exception e, string traceId)
    {
        var logItem = new LogItem(LEVEL_ERROR, system, e.Message, traceId);

        SendFile(logItem.ToString());
        SendFile(e.ToString());

        SendConsole(logItem.ToString());
        SendConsole(e);
    }

    static void SendFile(string msg)
    {
        if (!_fileInitAttempted)
        {
            lock (_fileLock)
            {
                if (!_fileInitAttempted)
                {
                    _fileInitAttempted = true;
                    try
                    {
                        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vfs", "data");
                        Directory.CreateDirectory(logDir);
                        var logPath = Path.Combine(logDir, "vintagehive.log");
                        _fileWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };
                    }
                    catch { }
                }
            }
        }

        if (_fileWriter != null)
        {
            lock (_fileLock)
            {
                try { _fileWriter.WriteLine(msg); } catch { }
            }
        }
    }

    static void SendConsole(string msg)
    {
        if (Mind.MainThread != null)
        {
            Mind.MainThread.Post(_ => Console.WriteLine(msg), null);
        }
        else
        {
            Console.WriteLine(msg);
        }
    }

    static void SendConsole(Exception msg)
    {
        if (Mind.MainThread != null)
        {
            Mind.MainThread.Post(_ => Console.WriteLine(msg), null);
        }
        else
        {
            Console.WriteLine(msg);
        }
    }
}
