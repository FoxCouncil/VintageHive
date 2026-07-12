// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Presence;

namespace VintageHive.Proxy.Msn;

// Projects authenticated MSN notification-server sessions into the shared PresenceRegistry.
public sealed class MsnPresenceProvider : IPresenceProvider
{
    public string Network => "MSN";

    public IEnumerable<PresenceEntry> Online()
    {
        foreach (var session in MsnServer.NsSessions.Values.ToArray())
        {
            // Hidden sessions were announced to peers as FLN; the registry (and so Finger's public list)
            // must not contradict that.
            if (!session.IsAuthenticated || string.IsNullOrEmpty(session.Account) || session.Status is MsnStatus.Offline or MsnStatus.Hidden)
            {
                continue;
            }

            yield return Project(session);
        }
    }

    public PresenceEntry Find(string username)
    {
        if (string.IsNullOrEmpty(username) || !MsnServer.NsSessions.TryGetValue(username.ToLowerInvariant(), out var session))
        {
            return null;
        }

        // Mirror Online(): a session that authenticated but has not yet sent CHG is still Offline and must
        // not be reported as online (its default FLN status would otherwise map to Online), and a Hidden
        // session must look offline to cross-protocol consumers too.
        if (!session.IsAuthenticated || session.Status is MsnStatus.Offline or MsnStatus.Hidden)
        {
            return null;
        }

        return Project(session);
    }

    static PresenceEntry Project(MsnSession session)
    {
        return new PresenceEntry
        {
            Username = session.Account,
            Network = "MSN",
            Status = MsnStatus.ToPresenceStatus(session.Status),
            SignOnTime = session.SignOnTime,
            IdleSeconds = session.GetCurrentIdleSeconds(),
            AwayMessage = string.Empty,
            PlanText = string.Empty
        };
    }
}
