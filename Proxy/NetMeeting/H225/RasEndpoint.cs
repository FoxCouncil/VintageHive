// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// Represents a registered H.323 endpoint in the gatekeeper's registry.
/// Created on RegistrationConfirm, removed on UnregistrationConfirm or TTL expiry.
/// </summary>
internal class RasEndpoint
{
    /// <summary>Unique identifier assigned by the gatekeeper.</summary>
    public string EndpointId { get; init; }

    /// <summary>Call signaling address(es) reported by the endpoint.</summary>
    public IPEndPoint[] CallSignalAddresses { get; init; }

    /// <summary>RAS address(es) reported by the endpoint.</summary>
    public IPEndPoint[] RasAddresses { get; init; }

    /// <summary>Display aliases (h323-ID or e164).</summary>
    public string[] Aliases { get; set; }

    /// <summary>When this registration expires (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Whether this registration is still valid.</summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
