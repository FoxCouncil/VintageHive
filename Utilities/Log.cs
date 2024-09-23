// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Data.Types;

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

        if ((logItem.Level == LEVEL_DEBUG && !Mind.IsDebug) || logItem.Level == LEVEL_REQUEST)
        {
            return;
        }

        SendConsole(logItem.ToString());
    }

    public static void WriteException(string system, Exception e, string traceId)
    {
        var logItem = new LogItem(LEVEL_ERROR, system, e.Message, traceId);

        SendConsole(logItem.ToString());
        SendConsole(e);

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
