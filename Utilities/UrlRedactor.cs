// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;

namespace VintageHive.Utilities;

/// <summary>
/// Strips credentials out of request URLs on their way into any durable sink: the requests table, the log
/// tables, and the error pages that echo a request back.
/// <para>
/// Period clients put passwords in query strings. The pre-YMSG Yahoo Pager signs in with
/// <c>GET /config/ncclogin?login=&lt;user&gt;&amp;passwd=&lt;plaintext&gt;</c>, so a string that is merely
/// "the request we saw" becomes a plaintext credential at rest the moment anything writes it down. Redaction
/// happens at the sink rather than at each call site so a protocol added later inherits it for free.
/// </para>
/// <para>
/// This is deliberately NOT used to build cache keys. Two different passwords for the same login URL redact
/// to the same string, so keying a cache on the redacted form would serve one member's session cookie to
/// another. Callers that cache must use <see cref="ContainsCredentials"/> and skip caching instead.
/// </para>
/// </summary>
public static class UrlRedactor
{
    public const string Placeholder = "[REDACTED]";

    // Password-carrying parameter names only. Deliberately narrow: this rewrites text a human later reads to
    // debug a request, so a false positive costs real diagnostic value. Session tokens are not included -
    // they are short-lived and appear in URLs the archive paths legitimately need to reproduce.
    const string SensitiveParameterNames = "passwd|password|pwd";

    // Anchored on the separator that actually starts a query parameter, so a path segment or a value that
    // merely ends in "pwd" is left alone. The value class stops at the next separator, at a fragment, and at
    // whitespace, which is what makes this safe to run over a whole request line and not just a bare URL.
    static readonly Regex SensitiveParameter = new(
        $@"([?&;])({SensitiveParameterNames})=[^&#\s]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Replaces the value of every password-carrying query parameter with <see cref="Placeholder"/>. Accepts
    /// any text containing a URL (a bare URL, or a whole request line), and returns the input unchanged when
    /// there is nothing to redact.
    /// </summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Cheap reject first: the overwhelming majority of tracked requests have no query string at all, and
        // this runs on every single one of them.
        if (text.IndexOf('=') < 0)
        {
            return text;
        }

        try
        {
            return SensitiveParameter.Replace(text, $"$1$2={Placeholder}");
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological input must never cost us the audit record itself. Drop the query entirely -
            // losing detail is the safe failure here, keeping a password is not.
            var queryStart = text.IndexOf('?');

            return queryStart < 0 ? text : text[..queryStart] + "?" + Placeholder;
        }
    }

    /// <summary>
    /// True when the text carries a password-bearing query parameter. Used to keep credential-bearing
    /// requests out of the response cache entirely, both so the password never lands in a cache key and so
    /// one member's login response can never be replayed to another.
    /// </summary>
    public static bool ContainsCredentials(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('=') < 0)
        {
            return false;
        }

        try
        {
            return SensitiveParameter.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed: treat an untestable URL as sensitive so it is not cached.
            return true;
        }
    }
}
