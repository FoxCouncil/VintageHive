using Spectre.Console;

namespace VintageHive;

internal static class Display
{
    static Display()
    {
        AnsiConsole.Markup("[underline red]Vintage Hive[/] Startup!\n\n");
    }

    public static void WriteLog()
    {
        AnsiConsole.WriteLine();
    }

    public static void WriteLog(string log)
    {
        AnsiConsole.WriteLine($"LOG {log}");
    }

    public static void WriteException(Exception e)
    {
        WriteLog();
        WriteLog("=============================[EXCEPTION BOUNDRY]=============================");
        AnsiConsole.WriteException(e);
        WriteLog("=============================[EXCEPTION BOUNDRY]=============================");
        WriteLog();
    }
}
