// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Presence;

// Implemented by each messenger server so the shared PresenceRegistry can aggregate live presence
// across protocols. Adapters project their own session state into protocol-neutral PresenceEntry values.
public interface IPresenceProvider
{
    // A stable label for this provider's network, used as the registration key (e.g. "OSCAR").
    string Network { get; }

    // Every currently-online user on this network.
    IEnumerable<PresenceEntry> Online();

    // The online entry for a username, or null if that user is not currently online on this network.
    PresenceEntry Find(string username);
}
