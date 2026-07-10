// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Presence;

namespace VintageHive.Proxy.Oscar;

// Projects the live OscarServer.Sessions registry into protocol-neutral PresenceEntry values for the
// shared PresenceRegistry. OSCAR's internal buddy/IM delivery keeps using OscarServer.Sessions directly.
internal sealed class OscarPresenceProvider : IPresenceProvider
{
    public string Network => "OSCAR";

    public IEnumerable<PresenceEntry> Online()
    {
        foreach (var session in OscarServer.Sessions.Values.ToArray())
        {
            // Pre-auth sessions have no screen name; skip them so the registry never shows ghosts.
            if (string.IsNullOrEmpty(session.ScreenName))
            {
                continue;
            }

            yield return Project(session);
        }
    }

    public PresenceEntry Find(string username)
    {
        var session = OscarServer.Sessions.GetByScreenName(username);

        return session == null ? null : Project(session);
    }

    static PresenceEntry Project(OscarSession session)
    {
        return new PresenceEntry
        {
            Username = session.ScreenName,
            // Numeric screen names are ICQ UINs; everything else is an AIM screen name.
            Network = session.ScreenName.All(char.IsDigit) ? "ICQ" : "AIM",
            Status = MapStatus(session.Status),
            SignOnTime = session.SignOnTime,
            IdleSeconds = session.GetCurrentIdleSeconds(),
            AwayMessage = session.AwayMessage ?? string.Empty,
            PlanText = session.Profile ?? string.Empty
        };
    }

    static PresenceStatus MapStatus(OscarSessionOnlineStatus status)
    {
        return status switch
        {
            OscarSessionOnlineStatus.Online => PresenceStatus.Online,
            OscarSessionOnlineStatus.Away => PresenceStatus.Away,
            OscarSessionOnlineStatus.DoNotDisturb => PresenceStatus.DoNotDisturb,
            OscarSessionOnlineStatus.NotAvailable => PresenceStatus.NotAvailable,
            OscarSessionOnlineStatus.Occupied => PresenceStatus.Occupied,
            OscarSessionOnlineStatus.FreeToChat => PresenceStatus.FreeToChat,
            OscarSessionOnlineStatus.Invisible => PresenceStatus.Invisible,
            _ => PresenceStatus.Online
        };
    }
}
