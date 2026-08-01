// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Yahoo;

// YMSG service (command) codes. Values from libyahoo2 / the Wireshark ymsg dissector.
public enum YmsgService : ushort
{
    Logon = 0x01,
    Logoff = 0x02,
    IsAway = 0x03,
    IsBack = 0x04,
    Message = 0x06,
    UserStat = 0x0A,
    Ping = 0x12,
    AddBuddy = 0x83,
    RemoveBuddy = 0x84,
    KeepAlive = 0x8A,
    AuthResp = 0x54,
    List = 0x55,
    Auth = 0x57,
    Verify = 0x4C,
    Notify = 0x4B
}

// YMSG buddy-presence status codes (body field 10). Header status is a separate concept.
internal static class YmsgStatus
{
    public const uint Available = 0x00000000;
    public const uint BeRightBack = 1;
    public const uint Busy = 2;
    public const uint NotAtHome = 3;
    public const uint NotAtDesk = 4;
    public const uint NotInOffice = 5;
    public const uint OnPhone = 6;
    public const uint OnVacation = 7;
    public const uint OutToLunch = 8;
    public const uint SteppedOut = 9;
    public const uint Invisible = 12;
    public const uint Custom = 99;
    public const uint Idle = 999;

    // Header-level status values. Duplicate shares LoginError's wire value: libyahoo2-lineage clients
    // recognize "signed in from another location" as a Logoff carrying status 0xFFFFFFFF (their API enum
    // value 99 is not the wire value; on the wire 99 means Custom).
    public const uint LoginError = 0xFFFFFFFF;
    public const uint Duplicate = 0xFFFFFFFF;
    public const uint OfflineMessage = 5;

    // Header status for a LIVE server-to-client delivery. Shares BeRightBack's wire value, but this is the
    // header word, not a presence code - real servers used 1 as the generic "server response" status.
    //
    // This is load-bearing, not cosmetic: YM 5.5.0.1244 silently DISCARDS any server-to-client Message whose
    // header status is not 1. Proved on the wire with an A/B probe - six variants injected into a signed-on
    // session, and only the three with status 1 rendered. Status 0 and mid-session status 5 were both
    // dropped regardless of which body fields were present, and field 15 and field order made no difference.
    // Escargot's YMSG front sends BRB(1) on every live Message and Notify for the same reason.
    public const uint LiveDelivery = 1;
}
