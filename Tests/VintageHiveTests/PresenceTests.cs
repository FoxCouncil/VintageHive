// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Presence;

namespace Presence;

internal sealed class FakePresenceProvider : IPresenceProvider
{
    private readonly Dictionary<string, PresenceEntry> _entries;

    public FakePresenceProvider(string network, params string[] usernames)
    {
        Network = network;
        _entries = usernames.ToDictionary(
            u => u,
            u => new PresenceEntry { Username = u, Network = network, Status = PresenceStatus.Online },
            StringComparer.OrdinalIgnoreCase);
    }

    public string Network { get; }

    public IEnumerable<PresenceEntry> Online() => _entries.Values;

    public PresenceEntry Find(string username) => _entries.TryGetValue(username, out var e) ? e : null;
}

[TestClass]
public class PresenceRegistryTests
{
    // The registry is static and shared; unregister the fakes so their permanently-online users don't leak
    // into other tests (e.g. Finger's "no users online" assertion).
    [TestCleanup]
    public void Cleanup()
    {
        foreach (var network in new[] { "PTestPrimary", "PTestSecondary", "PTestReplace", "PTestAgg1", "PTestAgg2" })
        {
            PresenceRegistry.Unregister(network);
        }
    }

    [TestMethod]
    public void Find_ResolvesByRegistrationOrder_FirstRegisteredWins()
    {
        // Two networks both have "ptest_dual" online; the first-registered provider must win deterministically.
        PresenceRegistry.Register(new FakePresenceProvider("PTestPrimary", "ptest_dual"));
        PresenceRegistry.Register(new FakePresenceProvider("PTestSecondary", "ptest_dual"));

        var entry = PresenceRegistry.Find("ptest_dual");

        Assert.IsNotNull(entry);
        Assert.AreEqual("PTestPrimary", entry.Network, "First-registered provider must win for a multi-network user");
    }

    [TestMethod]
    public void Register_SameNetwork_ReplacesInPlace()
    {
        PresenceRegistry.Register(new FakePresenceProvider("PTestReplace", "old_user"));
        PresenceRegistry.Register(new FakePresenceProvider("PTestReplace", "new_user"));

        Assert.IsNull(PresenceRegistry.Find("old_user"), "Re-registering a network should replace, not keep the old provider");
        Assert.IsNotNull(PresenceRegistry.Find("new_user"));
    }

    [TestMethod]
    public void Online_AggregatesAcrossProviders()
    {
        PresenceRegistry.Register(new FakePresenceProvider("PTestAgg1", "pt_agg_a"));
        PresenceRegistry.Register(new FakePresenceProvider("PTestAgg2", "pt_agg_b"));

        var users = PresenceRegistry.Online().Select(e => e.Username).ToList();

        CollectionAssert.Contains(users, "pt_agg_a");
        CollectionAssert.Contains(users, "pt_agg_b");
    }

    [TestMethod]
    public void Find_NullOrUnknown_ReturnsNull()
    {
        Assert.IsNull(PresenceRegistry.Find(null!));
        Assert.IsNull(PresenceRegistry.Find("nobody_pt_xyz"));
    }
}
