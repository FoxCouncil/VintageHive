// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace VintageHive.Proxy.Yahoo.Pager;

/// <summary>
/// The sign-on tokens handed out by /config/ncclogin and carried by everything the Pager does afterwards.
/// <para>
/// The reference client (libyahoo 0.18.4, descended from the yppro2.c prototype pager code) takes the whole
/// <c>Set-Cookie: Y=...</c> value, pulls the <c>n=</c> sub-value out of it, and sends that as the first field
/// of its LOGON packet. So this token is the only part of the cookie that is load-bearing, and it is the
/// bearer credential for the session: whoever holds it is the account until it expires.
/// </para>
/// <para>
/// Deliberately in memory. It is session state, and every Pager session dies with the process anyway - there
/// is nothing here worth surviving a restart, and plenty worth not persisting.
/// </para>
/// </summary>
public static class PagerLoginTokens
{
    // Sliding: a client that is actively polling keeps its session, one that walked away loses it. The
    // transport commit renews on every /notify/ POST via Resolve.
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    // Period cookies look like "n=5liucman434ue" - lowercase alphanumeric, no punctuation. Minting outside
    // that character class risks a client parser that was never written to expect it (the YMSG challenge
    // alphabet was exactly this kind of trap), so the shape is matched rather than guessed at.
    const string TokenAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    const int TokenLength = 13;

    sealed class Entry
    {
        public string Username;

        public DateTimeOffset Expires;
    }

    static readonly ConcurrentDictionary<string, Entry> Tokens = new(StringComparer.Ordinal);

    /// <summary>Issues a fresh token for a verified sign-on.</summary>
    public static string Mint(string username)
    {
        Sweep();

        var token = RandomToken(TokenLength);

        Tokens[token] = new Entry { Username = username, Expires = DateTimeOffset.UtcNow + Lifetime };

        return token;
    }

    /// <summary>
    /// The account a token belongs to, or null when it is unknown or expired. Renews the lifetime on a hit,
    /// so an active session does not expire out from under a client that is still polling.
    /// </summary>
    public static string Resolve(string token)
    {
        if (string.IsNullOrEmpty(token) || !Tokens.TryGetValue(token, out var entry))
        {
            return null;
        }

        if (entry.Expires <= DateTimeOffset.UtcNow)
        {
            Tokens.TryRemove(token, out _);

            return null;
        }

        entry.Expires = DateTimeOffset.UtcNow + Lifetime;

        return entry.Username;
    }

    /// <summary>Invalidates a token. Sign-off, and the supersede path once the transport is wired.</summary>
    public static void Revoke(string token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            Tokens.TryRemove(token, out _);
        }
    }

    /// <summary>Drops every token for an account. A fresh sign-on must not leave the old one usable.</summary>
    public static void RevokeAllFor(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            return;
        }

        foreach (var pair in Tokens.ToArray())
        {
            if (string.Equals(pair.Value.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                Tokens.TryRemove(pair.Key, out _);
            }
        }
    }

    // Called on mint rather than on a timer: sign-ons are rare and the table is tiny, so a sweep here costs
    // nothing and there is no background task to own.
    public static void Sweep()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in Tokens.ToArray())
        {
            if (pair.Value.Expires <= now)
            {
                Tokens.TryRemove(pair.Key, out _);
            }
        }
    }

    internal static void Clear()
    {
        Tokens.Clear();
    }

    // Rejection sampling over a byte at a time so every character is uniform over the alphabet. A plain
    // modulo of 256 would bias the first 4 letters, which is not a real attack here but is free to avoid.
    internal static string RandomToken(int length)
    {
        var chars = new char[length];

        Span<byte> buffer = stackalloc byte[1];

        for (var i = 0; i < length; i++)
        {
            int value;

            do
            {
                RandomNumberGenerator.Fill(buffer);

                value = buffer[0];
            }
            while (value >= 256 - (256 % TokenAlphabet.Length));

            chars[i] = TokenAlphabet[value % TokenAlphabet.Length];
        }

        return new string(chars);
    }
}
