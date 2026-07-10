// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Presence;

// A single aggregation point for cross-protocol presence. Each messenger server registers a provider;
// consumers (Finger today, the admin dashboard later) read the union without knowing any protocol.
public static class PresenceRegistry
{
    static readonly object Gate = new();

    // An ordered list, not a dictionary, so Find() resolves a multi-network user deterministically by
    // registration order (OSCAR is registered first in Mind.Init, preserving Finger's pre-refactor result).
    // Re-registering the same network replaces in place rather than duplicating.
    static readonly List<IPresenceProvider> Providers = new();

    public static void Register(IPresenceProvider provider)
    {
        lock (Gate)
        {
            var index = Providers.FindIndex(p => string.Equals(p.Network, provider.Network, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                Providers[index] = provider;
            }
            else
            {
                Providers.Add(provider);
            }
        }
    }

    public static void Unregister(string network)
    {
        lock (Gate)
        {
            Providers.RemoveAll(p => string.Equals(p.Network, network, StringComparison.OrdinalIgnoreCase));
        }
    }

    static IPresenceProvider[] Snapshot()
    {
        lock (Gate)
        {
            return Providers.ToArray();
        }
    }

    public static IEnumerable<PresenceEntry> Online()
    {
        foreach (var provider in Snapshot())
        {
            foreach (var entry in provider.Online())
            {
                yield return entry;
            }
        }
    }

    public static PresenceEntry Find(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        foreach (var provider in Snapshot())
        {
            var entry = provider.Find(username);

            if (entry != null)
            {
                return entry;
            }
        }

        return null;
    }
}
