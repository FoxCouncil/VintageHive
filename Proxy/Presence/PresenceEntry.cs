// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Presence;

// A protocol-neutral snapshot of one online user, projected by an IPresenceProvider so cross-protocol
// consumers (Finger, the admin dashboard) can enumerate presence without knowing any messenger's types.
public sealed class PresenceEntry
{
    public string Username { get; init; } = string.Empty;

    // The network the user is signed in on, e.g. "AIM", "ICQ", "Yahoo", "MSN".
    public string Network { get; init; } = string.Empty;

    public PresenceStatus Status { get; init; }

    public DateTimeOffset SignOnTime { get; init; }

    public uint IdleSeconds { get; init; }

    public string AwayMessage { get; init; } = string.Empty;

    // The user's free-form profile/plan text, if the network exposes one (OSCAR does; Yahoo/MSN may not).
    public string PlanText { get; init; } = string.Empty;
}
