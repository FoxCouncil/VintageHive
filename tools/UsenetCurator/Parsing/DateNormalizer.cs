// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Globalization;
using System.Text.RegularExpressions;

namespace UsenetCurator.Parsing;

internal static partial class DateNormalizer
{
    // Timezone abbreviation map (common ones from Usenet era)
    private static readonly Dictionary<string, string> TimezoneOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"]  = "+0000", ["UTC"]  = "+0000", ["UT"]   = "+0000",
        ["EST"]  = "-0500", ["EDT"]  = "-0400",
        ["CST"]  = "-0600", ["CDT"]  = "-0500",
        ["MST"]  = "-0700", ["MDT"]  = "-0600",
        ["PST"]  = "-0800", ["PDT"]  = "-0700",
        ["BST"]  = "+0100", ["CET"]  = "+0100", ["CEST"] = "+0200",
        ["MET"]  = "+0100", ["MEST"] = "+0200",
        ["EET"]  = "+0200", ["EEST"] = "+0300",
        ["JST"]  = "+0900", ["KST"]  = "+0900",
        ["IST"]  = "+0530", ["NZST"] = "+1200", ["NZDT"] = "+1300",
        ["AEST"] = "+1000", ["AEDT"] = "+1100",
        ["ACST"] = "+0930", ["ACDT"] = "+1030",
        ["AWST"] = "+0800",
        ["HST"]  = "-1000", ["AKST"] = "-0900", ["AKDT"] = "-0800",
        ["AST"]  = "-0400", ["ADT"]  = "-0300",
        ["NST"]  = "-0330", ["NDT"]  = "-0230",
    };

    private static readonly string[] ExactFormats =
    [
        // RFC 2822 / RFC 1123
        "ddd, dd MMM yyyy HH:mm:ss zzz",
        "ddd, d MMM yyyy HH:mm:ss zzz",
        "dd MMM yyyy HH:mm:ss zzz",
        "d MMM yyyy HH:mm:ss zzz",

        // Without timezone
        "ddd, dd MMM yyyy HH:mm:ss",
        "ddd, d MMM yyyy HH:mm:ss",
        "dd MMM yyyy HH:mm:ss",
        "d MMM yyyy HH:mm:ss",

        // Two-digit year variants
        "ddd, dd MMM yy HH:mm:ss zzz",
        "ddd, d MMM yy HH:mm:ss zzz",
        "dd MMM yy HH:mm:ss zzz",
        "d MMM yy HH:mm:ss zzz",
        "ddd, dd MMM yy HH:mm:ss",
        "ddd, d MMM yy HH:mm:ss",
        "dd MMM yy HH:mm:ss",
        "d MMM yy HH:mm:ss",

        // Hyphenated (Mon, 17-Dec-84 19:48:54 EST)
        "ddd, dd-MMM-yy HH:mm:ss zzz",
        "ddd, d-MMM-yy HH:mm:ss zzz",
        "ddd, dd-MMM-yy HH:mm:ss",
        "ddd, d-MMM-yy HH:mm:ss",

        // ISO-ish
        "yyyy-MM-dd HH:mm:ss zzz",
        "yyyy-MM-dd HH:mm:ss",

        // ctime format: Thu Jun  1 05:28:25 1989
        "ddd MMM d HH:mm:ss yyyy",
        "ddd MMM dd HH:mm:ss yyyy",
    ];

    /// <summary>
    /// Attempts to parse a Usenet date string into a DateTimeOffset.
    /// Handles various historical formats from the 1980s-1990s.
    /// </summary>
    public static DateTimeOffset? TryParse(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
        {
            return null;
        }

        // Clean up the date string
        dateStr = dateStr.Trim();

        // Remove parenthesized comments like "(EST)" at the end
        dateStr = ParenCommentRegex().Replace(dateStr, "").Trim();

        // Replace timezone abbreviations with numeric offsets
        dateStr = ReplaceTimezoneAbbreviations(dateStr);

        // Try standard parsing first
        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var result))
        {
            return result;
        }

        // Try exact formats
        if (DateTimeOffset.TryParseExact(dateStr, ExactFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result))
        {
            return result;
        }

        // Try as DateTime (no timezone info) and assume UTC
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
        {
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        return null;
    }

    /// <summary>
    /// Formats a DateTimeOffset as RFC 2822 date string.
    /// </summary>
    public static string ToRfc2822(DateTimeOffset dto)
    {
        return dto.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    private static string ReplaceTimezoneAbbreviations(string dateStr)
    {
        // Look for a timezone abbreviation at the end of the string
        var match = TrailingTzRegex().Match(dateStr);

        if (match.Success)
        {
            var tzAbbr = match.Groups[1].Value;

            if (TimezoneOffsets.TryGetValue(tzAbbr, out var offset))
            {
                dateStr = dateStr[..match.Index] + offset;
            }
        }

        return dateStr;
    }

    [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
    private static partial Regex ParenCommentRegex();

    [GeneratedRegex(@"\s+([A-Z]{2,5})\s*$")]
    private static partial Regex TrailingTzRegex();
}
