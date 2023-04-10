namespace VintageHive.Proxy.Ftp;

public enum FtpResponseCode
{
    DataConnectionAlreadyOpen = 125,
    FileStatusOkay = 150,
    CommandSuccess = 200,
    CommandNotImplementedSite = 202,
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
    PathnameCreated = 257,
    UsernameOkay = 331,
    RequestedFileActionPendingMore = 350,
    InvalidUsernameOrPassword = 430,
    SyntaxError = 501,
    CommandNotImplemented = 502,
    BadSequenceOfCommands = 503,
    RequestedActionNotTaken = 550
}
