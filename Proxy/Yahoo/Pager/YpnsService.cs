// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Yahoo.Pager;

// Service codes, verbatim from libyahoo.h. Small integers rather than YMSG's 16-bit service words - this is a
// different protocol generation, not an earlier revision of the same one.
public static class YpnsService
{
    public const uint Logon = 1;
    public const uint Logoff = 2;
    public const uint IsAway = 3;
    public const uint IsBack = 4;
    public const uint Idle = 5;
    public const uint Message = 6;
    public const uint IdActivate = 7;
    public const uint IdDeactivate = 8;
    public const uint MailStat = 9;
    public const uint UserStat = 10;
    public const uint NewMail = 11;
    public const uint ChatInvite = 12;
    public const uint Calendar = 13;
    public const uint NewPersonalMail = 14;
    public const uint NewContact = 15;
    public const uint AddIdent = 16;
    public const uint AddIgnore = 17;
    public const uint Ping = 18;
    public const uint GroupRename = 19;
    public const uint SysMessage = 20;
    public const uint PassThrough2 = 22;
    public const uint ConfInvite = 24;
    public const uint ConfLogon = 25;
    public const uint ConfDecline = 26;
    public const uint ConfLogoff = 27;
    public const uint ConfAddInvite = 28;
    public const uint ConfMsg = 29;
    public const uint ChatLogon = 30;
    public const uint ChatLogoff = 31;
    public const uint ChatMsg = 32;
    public const uint GameLogon = 40;
    public const uint GameLogoff = 41;
    public const uint FileTransfer = 70;
}

// Message flags carried in the header's msgtype field.
public static class YpnsMsgType
{
    public const uint None = 0;
    public const uint Normal = 1;
    public const uint Bounce = 2;
    public const uint Status = 4;
    public const uint Offline = 5;
    public const uint SessionExpired = 6;
    public const uint DenyBuddy = 7;
    public const uint Invisible = 12;
}

// Status codes are NOT redeclared here, deliberately. libyahoo.h's status enum (AVAILABLE=0, BRB=1, BUSY=2,
// NOTATHOME=3, NOTATDESK=4, NOTINOFFICE=5, ONPHONE=6, ONVACATION=7, OUTTOLUNCH=8, STEPPEDOUT=9,
// INVISIBLE=12, CUSTOM=99, IDLE=999) is value-for-value identical to YmsgStatus, so the two protocol
// generations share one numbering and a shared session's YahooStatus goes onto the Pager wire unchanged.
// A second copy of the same numbers would only be somewhere for the two to drift apart.
