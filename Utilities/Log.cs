// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities;

internal static class Log
{
    public const string LEVEL_ERROR = "error";
    public const string LEVEL_INFO = "info";
    public const string LEVEL_REQUEST = "request";
    public const string LEVEL_DEBUG = "debug";

    static Log()
    {
        WriteLine(LEVEL_INFO, "LOG", "Vintage Hive Startup!", string.Empty);
    }

    public static void WriteLine()
    {
        Console.WriteLine();
    }

    public static void WriteLine(string level, string system, string message, string traceId)
    {
        var logItem = new LogItem(level, system, message, traceId);

        WriteLine(logItem);
    }

    public static void WriteLine(LogItem logItem)
    {
        Mind.Db?.WriteLog(logItem);

        if (/*logItem.Level == LEVEL_DEBUG || */logItem.Level == LEVEL_REQUEST)
        {
            return;
        }

        Console.WriteLine(logItem.ToString());
    }

    public static void WriteException(string system, Exception e, string traceId)
    {
        var logItem = new LogItem(LEVEL_ERROR, system, e.Message, traceId);

        Console.WriteLine(logItem.ToString());
        Console.WriteLine(e);
    }
}
