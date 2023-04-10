namespace VintageHive.Proxy.Ftp;

public static class FtpTransferType
{
    public const string ASCII = "A";

    public static string NameFromType(string args)
    {
        if (args == "A")
        {
            return "ASCII";
        }
        else if (args == "I")
        {
            return "BINARY";
        }

        return "N/A";
    }
}
