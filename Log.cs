using Spectre.Console;
using System.Diagnostics;
using VintageHive.Data.Types;

namespace VintageHive;

internal static class Log
{
    public const string LEVEL_ERROR = "error";
    public const string LEVEL_INFO = "info";
    public const string LEVEL_REQUEST = "request";
    public const string LEVEL_DEBUG = "debug";

    static Log()
    {
        WriteLine(LEVEL_INFO, "LOG", "<underline red>Vintage Hive</> <bold green>Startup</>!", string.Empty);
    }

    public static void WriteLine()
    {
        AnsiConsole.WriteLine();
    }

    public static void WriteLine(string level, string system, string message, string traceId)
    {
        var logItem = new LogItem(level, system, message, traceId);

        WriteLine(logItem);
    }

    public static void WriteLine(LogItem logItem)
    {
        Mind.Db?.WriteLog(logItem);

        if (logItem.Level == LEVEL_DEBUG)
        {
            return;
        }

        AnsiConsole.MarkupLine(AddFormatting(logItem.ToString()));
    }

    public static void WriteException(string system, Exception e, string traceId)
    {
        var logItem = new LogItem(LEVEL_ERROR, system, e.Message, traceId);
       
        AnsiConsole.MarkupLine(AddFormatting(logItem.ToString()));
        AnsiConsole.WriteException(e);
    }

    public static string AddFormatting(string str)
    {
        str = str
            .Replace("[", "[[")
            .Replace("]", "]]")
            .Replace("[[ERROR]]", "[[[bold red]ERROR[/]]]")
            .Replace("[[INFO]]", "[[[purple]INFO[/]]]")
            .Replace("[[REQUEST]]", "[[[grey]REQUEST[/]]]")
            .Replace("[[MIS]]", "[[[fuchsia]MIS[/]]]")
            .Replace("[[HIT]]", "[[[green]HIT[/]]]")
            .Replace("<", "[")
            .Replace(">", "]");

        return str;
    }
}
