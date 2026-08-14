// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace VintageHive.Proxy.Yahoo.FileTransfer;

/// <summary>
/// The short-lived store behind the Yahoo! HTTP file-transfer relay (relaytransfers). A sender POSTs a file to
/// the filetransfer host; it lands here under a random token with a TTL, the recipient is told the URL, and the
/// first GET of that URL takes the payload and removes it. Deliberately in memory and single-serve: nothing is
/// buffered for an offline recipient (out of scope) and a downloaded file does not linger.
/// <para>
/// Modelled on <see cref="Pager.PagerLoginTokens"/> - a static concurrent table with a per-entry expiry and a
/// sweep on write, no background timer to own. The payload is held as bytes rather than on disk because these
/// are transient and small-by-cap; the size cap is the caller's (the processor rejects an oversized upload
/// before it ever calls Store).
/// </para>
/// </summary>
public static class YahooFileTransfers
{
    // Mutable internal rather than a public const so the test sweep can shrink it (via InternalsVisibleTo) to
    // prove a token expires and cannot be refetched after its TTL without waiting ten real minutes.
    internal static TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    const string TokenAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    const int TokenLength = 24;

    public sealed class Entry
    {
        public string From;

        public string To;

        public string FileName;

        public byte[] Data;

        public DateTimeOffset Expires;
    }

    static readonly ConcurrentDictionary<string, Entry> Transfers = new(StringComparer.Ordinal);

    /// <summary>Stores a payload and returns its token. Sweeps expired entries first so the table stays small.</summary>
    public static string Store(string from, string to, string fileName, byte[] data)
    {
        Sweep();

        var token = RandomToken();

        Transfers[token] = new Entry
        {
            From = from,
            To = to,
            FileName = fileName,
            Data = data,
            Expires = DateTimeOffset.UtcNow + Lifetime,
        };

        return token;
    }

    /// <summary>
    /// Removes and returns the payload for a token, or null when it is unknown, already fetched, or expired.
    /// Single-serve: the remove is the point, so a URL cannot be replayed and a fetched file cannot be
    /// re-fetched.
    /// </summary>
    public static Entry Take(string token)
    {
        if (string.IsNullOrEmpty(token) || !Transfers.TryRemove(token, out var entry))
        {
            return null;
        }

        if (entry.Expires <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return entry;
    }

    public static void Sweep()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in Transfers.ToArray())
        {
            if (pair.Value.Expires <= now)
            {
                Transfers.TryRemove(pair.Key, out _);
            }
        }
    }

    internal static void Clear()
    {
        Transfers.Clear();
    }

    // Rejection sampling for a uniform token, same shape as PagerLoginTokens.RandomToken.
    static string RandomToken()
    {
        var chars = new char[TokenLength];

        Span<byte> buffer = stackalloc byte[1];

        for (var i = 0; i < TokenLength; i++)
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
