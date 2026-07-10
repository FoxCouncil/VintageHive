// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Presence;

namespace VintageHive.Proxy.Msn;

// MSNP presence status codes (the 3-letter tokens carried by CHG/NLN/ILN) and their mapping to the
// protocol-neutral PresenceStatus.
internal static class MsnStatus
{
    public const string Online = "NLN";
    public const string Offline = "FLN";
    public const string Busy = "BSY";
    public const string Idle = "IDL";
    public const string Away = "AWY";
    public const string BeRightBack = "BRB";
    public const string OnThePhone = "PHN";
    public const string OutToLunch = "LUN";
    public const string Hidden = "HDN";

    public static PresenceStatus ToPresenceStatus(string code)
    {
        return code switch
        {
            Online => PresenceStatus.Online,
            Busy => PresenceStatus.Busy,
            Idle => PresenceStatus.Idle,
            Away => PresenceStatus.Away,
            BeRightBack => PresenceStatus.BeRightBack,
            OnThePhone => PresenceStatus.OnThePhone,
            OutToLunch => PresenceStatus.OutToLunch,
            Hidden => PresenceStatus.Invisible,
            _ => PresenceStatus.Online
        };
    }

    public static bool IsValid(string code)
    {
        return code is Online or Busy or Idle or Away or BeRightBack or OnThePhone or OutToLunch or Hidden;
    }
}
