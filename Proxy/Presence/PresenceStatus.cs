// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Presence;

// Protocol-neutral presence status. The OSCAR-equivalent values keep the same names/order as
// OscarSessionOnlineStatus so Finger's rendering stays byte-identical; the tail covers Yahoo/MSN states.
public enum PresenceStatus
{
    Online,
    Away,
    DoNotDisturb,
    NotAvailable,
    Occupied,
    FreeToChat,
    Invisible,
    Idle,
    Busy,
    OnThePhone,
    OutToLunch,
    BeRightBack
}
