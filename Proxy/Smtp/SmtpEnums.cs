// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Smtp;

public static class SmtpEnums
{
    public const string EOL = "\r\n";

    public const string EOM = EOL + "." + EOL;

    public enum SmtpCommands
    {
        NONE,
        EHLO,
        HELO,
        MAIL,
        RCPT,
        DATA,
        RSET,
        NOOP,
        QUIT,
        AUTH,
        STARTTLS,
        VRFY,
        EXPN,
        HELP
    }

    public enum SmtpResponseCodes
    {
        ServiceReady = 220,
        ServiceClosingTransmissionChannel = 221,
        AuthenticationSuccessful = 235,
        RequestedMailActionCompleted = 250,
        UserNotLocalWillForward = 251,
        CannotVerifyUser = 252,

        AuthenticationChallenge = 334,
        StartMailInput = 354,
        
        SyntaxError = 500,
        CommandNotImplemented = 502,
        BadSequenceOfCommands = 503,
        CommandParameterNotImplemented = 504,
        AuthenticationRequired = 530,
        AuthenticationFailed = 535,
        MailboxUnavailable = 550,
        UserNotLocalTryForwarding = 551,
        ExceededStorageAllocation = 552,
        MailboxNameNotAllowed = 553,
        TransactionFailed = 554
    }
}

