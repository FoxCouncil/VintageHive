// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Chat;

/// <summary>
/// One inbound instant message addressed to a registered chat service, normalized across networks: plain
/// text, no wire formatting (the OSCAR path strips AIM's HTML wrapper before building this).
/// </summary>
public sealed class ChatServiceMessage
{
    /// <summary>The member the message came from - a username on IM networks, a nick on IRC.</summary>
    public string SenderUsername { get; init; } = string.Empty;

    /// <summary>Which network carried it, using the PresenceRegistry names: "Yahoo", "OSCAR", "IRC".</summary>
    public string Network { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
}
