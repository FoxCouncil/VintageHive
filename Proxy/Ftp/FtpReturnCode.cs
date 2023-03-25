namespace VintageHive.Proxy.Ftp;

public enum FtpReturnCode
{
    DataConnectionAlreadyOpen = 125,
    FileStatusOkay = 150,
    CommandNotImplemented = 202,
    SystemStatus = 211,
    DirectoryStatus = 212,
    FileStatus = 213,
    HelpMessage = 214,
    NameSystemType = 215,
    ServiceReadyForNewUser = 220,
    ServerClosingControlConnection = 221,
    DataConnectionOpen = 225,
    DataConnectionClosing = 226,
    EnteringPassiveMode = 227,
    EnteringLongPassiveMode = 228,
    EnteringExtendedPassiveMode = 229,
    UserLoggedIn = 230,
    RequestedFileActionOkay = 250,
    UsernameOkay = 331,
    InvalidUsernameOrPassword = 430
}
