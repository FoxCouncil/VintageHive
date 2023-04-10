namespace VintageHive.Proxy.Ftp;

public static class FtpCommand
{
    public const string AbortActiveFileTransfer = "ABOR";

    public const string AuthenticationUsername = "USER";

    public const string Site = "SITE";

    public const string Open = "OPEN";

    public const string SystemType = "SYST";

    public const string FeatureList = "FEAT";

    public const string PrintWorkingDirectory = "PWD";

    public const string TransferMode = "TYPE";

    public const string PassiveMode = "PASV";

    public const string ListInfo = "LIST";

    public const string Quit = "QUIT";

    public const string ChangeWorkingDirectory = "CWD";

    public const string MakeDirectory = "MKD";

    public const string RetrieveFile = "RETR";

    public const string StoreFile = "STOR";

    public const string RestartTransfer = "REST";

    public const string DeleteFile = "DELE";

    public const string RenameFrom = "RNFR";

    public const string RenameTo = "RNTO";

    public const string Bark = "ARF";
}
