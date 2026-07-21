// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

/// <summary>
/// Runtime view over the <see cref="ConfigNames.ValidMailDomains"/> config entry: the list of mail
/// domains this host serves. Governs the mail surfaces only (POP3/IMAP/SMTP login, MAIL FROM,
/// RCPT TO, postmaster routing and identity); the intranet/controller [Domain] namespace stays on
/// the compile-time <see cref="HiveDomains"/> constants. Every member re-reads config so an
/// embedding host's ConfigSet takes effect without restart - do not cache the results.
/// </summary>
public static class MailDomains
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
}
