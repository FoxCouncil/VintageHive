// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Yahoo.Pager;

/// <summary>
/// The hostnames a period Yahoo! Pager talks to before YMSG existed. Unlike <see cref="Data.Types.HiveDomains"/>
/// these are the real Yahoo! names: the client has them compiled in and never asks where to go, so a plane that
/// wants to serve this client generation has to answer for them. VintageHive owns them internally rather than
/// exposing a processor for an embedder to mount, because the Pager transport shares
/// <see cref="YahooSessionRegistry"/> with YMSG and that table is not something to hand across a host boundary.
/// <para>
/// Captured from a real client (Yahoo! Pager on Win98, intercept-DNS), in the order it issues them.
/// </para>
/// </summary>
public static class PagerHosts
{
    /// <summary>Sign-on. Credentials arrive as plaintext login= / passwd= query parameters.</summary>
    public const string Auth = "msg.edit.yahoo.com";

    /// <summary>Client version check. HEAD then GET of /clients.html.</summary>
    public const string Update = "update.pager.yahoo.com";

    /// <summary>The polled message transport: repeated POSTs to /notify/.</summary>
    public const string Notify = "http.messenger.yahoo.com";

    /// <summary>
    /// The transport host libyahoo targets. Same protocol, different name for the same service, so it is
    /// claimed alongside the captured one rather than left to fall through to the archive.
    /// </summary>
    public const string NotifyAlternate = "http.pager.yahoo.com";

    public static bool IsPagerHost(string host)
    {
        return string.Equals(host, Auth, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, Update, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, Notify, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, NotifyAlternate, StringComparison.OrdinalIgnoreCase);
    }
}
