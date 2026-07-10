// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Presence;

namespace VintageHive.Proxy.Yahoo;

// Projects live YmsgServer.Sessions into the shared PresenceRegistry so Finger and the dashboard see
// Yahoo! users alongside AIM/ICQ.
internal sealed class YahooPresenceProvider : IPresenceProvider
{
    public string Network => "Yahoo";

    public IEnumerable<PresenceEntry> Online()
    {
        foreach (var session in YmsgServer.Sessions.Values.ToArray())
        {
            if (!session.IsAuthenticated || string.IsNullOrEmpty(session.Username))
            {
                continue;
            }

            yield return Project(session);
        }
    }

    public PresenceEntry Find(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        foreach (var session in YmsgServer.Sessions.Values.ToArray())
        {
            if (session.IsAuthenticated && string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return Project(session);
            }
        }

        return null;
    }

    static PresenceEntry Project(YmsgSession session)
    {
        return new PresenceEntry
        {
            Username = session.Username,
            Network = "Yahoo",
            Status = YmsgServer.MapToPresenceStatus(session.YahooStatus),
            SignOnTime = session.SignOnTime,
            IdleSeconds = session.GetCurrentIdleSeconds(),
            AwayMessage = session.CustomStatusMessage,
            PlanText = string.Empty
        };
    }
}
