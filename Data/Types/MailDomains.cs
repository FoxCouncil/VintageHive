// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;

namespace VintageHive.Data.Types;

/// <summary>
/// Runtime view over the <see cref="ConfigNames.ValidMailDomains"/> config entry: the list of mail
/// domains this host serves. Governs the mail surfaces only (POP3/IMAP/SMTP login, MAIL FROM,
/// RCPT TO, postmaster routing and identity); the intranet/controller [Domain] namespace stays on
/// the compile-time <see cref="HiveDomains"/> constants. Every member re-reads config so an
/// embedding host's ConfigSet takes effect without restart - do not cache the results.
/// </summary>
public static partial class MailDomains
{
    public static IReadOnlyList<string> All
    {
        get
        {
            var raw = Mind.Db.ConfigGet<string>(ConfigNames.ValidMailDomains);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new[] { HiveDomains.Base };
            }

            var domains = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return domains.Length > 0 ? domains : new[] { HiveDomains.Base };
        }
    }

    public static string Primary => All[0];

    public static bool IsHosted(string domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return false;
        }

        return All.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }

    // Login resolution seam shared by POP3 USER, IMAP LOGIN, and SMTP AUTH. A bare local part stays
    // valid; a qualified login must be a well-formed addr-spec (single @, non-empty parts, no
    // whitespace/CRLF smuggling - EmailAddress's anchored regex enforces that) whose domain is in the
    // hosted list, and resolves to its local part. A foreign or malformed domain is rejected here,
    // NEVER stripped down to its local part. Callers receive the domain separately so a future
    // per-domain user namespace (fred@domainA != fred@domainB) only has to widen this seam.
    public static bool TryResolveLogin(string login, out string localPart, out string domain)
    {
        localPart = null;
        domain = null;

        if (string.IsNullOrEmpty(login))
        {
            return false;
        }

        if (!login.Contains('@'))
        {
            localPart = login;

            return true;
        }

        if (!EmailAddress.TryParse(login, out var parsed))
        {
            return false;
        }

        if (!IsHosted(parsed.Domain))
        {
            return false;
        }

        localPart = parsed.User;
        domain = parsed.Domain;

        return true;
    }

    // Gate for operator-supplied domain lists (the admin panel field). EmailAddress's domain match is
    // deliberately loose ("anything but @, whitespace, >"), so this is the layer that keeps garbage -
    // LIKE wildcards, underscores, stray '@'s - out of the stored config. Normalization: split on
    // commas, trim, lowercase, dedupe keeping first occurrence (order matters - the first entry is the
    // primary domain the postmaster signs bounces with). An empty or all-whitespace input normalizes to
    // "" - the stored shape that makes All fall back to the built-in default - and reports success,
    // matching the admin panel's "empty restores the built-in" convention everywhere else.
    public static bool TryNormalizeList(string raw, out string normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            normalized = string.Empty;

            return true;
        }

        var entries = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var domains = new List<string>(entries.Length);

        foreach (var entry in entries)
        {
            var domain = entry.ToLowerInvariant();

            if (domain.Length > 253 || !HostnameRegex().IsMatch(domain))
            {
                return false;
            }

            if (!domains.Contains(domain))
            {
                domains.Add(domain);
            }
        }

        if (domains.Count == 0)
        {
            normalized = string.Empty;

            return true;
        }

        normalized = string.Join(",", domains);

        return true;
    }

    // Anchored: dot-separated labels of letters/digits/hyphens, no leading/trailing hyphen, 63 chars
    // per label. A single label ("hive") is allowed - era LANs use dotless names.
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex HostnameRegex();
}
