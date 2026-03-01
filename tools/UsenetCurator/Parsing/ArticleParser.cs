// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using System.Text.RegularExpressions;
using UsenetCurator.Sources;

namespace UsenetCurator.Parsing;

internal static partial class ArticleParser
{
    /// <summary>
    /// Parses a raw RFC 822/850/1036 message into a RawArticle.
    /// Handles header continuation lines, various header name variants,
    /// and basic MIME content extraction.
    /// </summary>
    public static RawArticle Parse(string rawMessage, string fallbackGroup = "")
    {
        if (string.IsNullOrEmpty(rawMessage))
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyStart = -1;

        // Split into lines for header parsing
        var lines = rawMessage.Split('\n');
        var headerBuilder = new StringBuilder();
        string currentHeaderName = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Empty line marks end of headers
            if (line.Length == 0)
            {
                // Save last header
                if (currentHeaderName != null)
                {
                    headers[currentHeaderName] = headerBuilder.ToString().Trim();
                }

                bodyStart = i + 1;

                break;
            }

            // Continuation line (starts with whitespace)
            if ((line[0] == ' ' || line[0] == '\t') && currentHeaderName != null)
            {
                headerBuilder.Append(' ');
                headerBuilder.Append(line.Trim());

                continue;
            }

            // New header line
            var colonIdx = line.IndexOf(':');

            if (colonIdx > 0)
            {
                // Save previous header
                if (currentHeaderName != null)
                {
                    headers[currentHeaderName] = headerBuilder.ToString().Trim();
                }

                currentHeaderName = line[..colonIdx].Trim();
                headerBuilder.Clear();
                headerBuilder.Append(line[(colonIdx + 1)..].Trim());
            }
        }

        // Save last header if no body separator found
        if (currentHeaderName != null && !headers.ContainsKey(currentHeaderName))
        {
            headers[currentHeaderName] = headerBuilder.ToString().Trim();
        }

        // Extract body
        var body = "";

        if (bodyStart >= 0 && bodyStart < lines.Length)
        {
            body = string.Join("\n", lines[bodyStart..]).TrimEnd();
        }

        // Handle MIME multipart - extract text/plain part
        body = ExtractTextContent(headers, body);

        // Clean body for NNTP compatibility
        body = CleanBody(body);

        // Build article
        var article = new RawArticle
        {
            MessageId = GetHeader(headers, "Message-ID", "Message-Id", "Article-I.D."),
            From = CleanHeaderValue(GetHeader(headers, "From")),
            Subject = CleanHeaderValue(GetHeader(headers, "Subject")),
            Date = GetHeader(headers, "Date"),
            Newsgroups = GetHeader(headers, "Newsgroups") ?? fallbackGroup,
            References = GetHeader(headers, "References") ?? "",
            Body = body,
        };

        // Normalize Message-ID format
        article.MessageId = NormalizeMessageId(article.MessageId);

        // Parse the date
        article.ParsedDate = DateNormalizer.TryParse(article.Date);

        // If we got a parsed date, normalize the date string to RFC 2822
        if (article.ParsedDate.HasValue)
        {
            article.Date = DateNormalizer.ToRfc2822(article.ParsedDate.Value);
        }

        return article;
    }

    private static string GetHeader(Dictionary<string, string> headers, params string[] names)
    {
        foreach (var name in names)
        {
            if (headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeMessageId(string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return null;
        }

        messageId = messageId.Trim();

        // Handle "Article-I.D.: group.number" format -> convert to angle-bracket form
        if (!messageId.StartsWith('<'))
        {
            // Clean up any whitespace or trailing junk
            messageId = messageId.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            messageId = $"<{messageId}>";
        }

        // Strip anything after the closing '>' (e.g. "#1/1" multipart indicators)
        var closingBracket = messageId.IndexOf('>');

        if (closingBracket >= 0 && closingBracket < messageId.Length - 1)
        {
            messageId = messageId[..(closingBracket + 1)];
        }

        // Ensure it has the @ sign (some very old IDs don't)
        if (!messageId.Contains('@'))
        {
            // Insert a domain
            messageId = messageId.Replace(">", "@usenet>");
        }

        return messageId;
    }

    private static string ExtractTextContent(Dictionary<string, string> headers, string body)
    {
        if (!headers.TryGetValue("Content-Type", out var contentType))
        {
            return body;
        }

        contentType = contentType.ToLowerInvariant();

        // Simple text/plain - might need transfer decoding
        if (contentType.StartsWith("text/plain"))
        {
            return DecodeTransferEncoding(headers, body);
        }

        // Multipart - find text/plain part
        if (contentType.StartsWith("multipart/"))
        {
            var boundary = ExtractBoundary(contentType);

            if (boundary != null)
            {
                var textPart = FindTextPlainPart(body, boundary);

                if (textPart != null)
                {
                    return textPart;
                }
            }
        }

        // text/html or other - return as-is (filter will likely reject it)
        return body;
    }

    private static string DecodeTransferEncoding(Dictionary<string, string> headers, string body)
    {
        if (!headers.TryGetValue("Content-Transfer-Encoding", out var encoding))
        {
            return body;
        }

        encoding = encoding.Trim().ToLowerInvariant();

        if (encoding == "quoted-printable")
        {
            return DecodeQuotedPrintable(body);
        }

        // Base64 text content
        if (encoding == "base64")
        {
            try
            {
                var cleaned = body.Replace("\n", "").Replace("\r", "").Trim();
                var bytes = Convert.FromBase64String(cleaned);

                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return body;
            }
        }

        return body;
    }

    private static string DecodeQuotedPrintable(string input)
    {
        var result = new StringBuilder(input.Length);

        var lines = input.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Soft line break (trailing =)
            var softBreak = line.EndsWith('=');

            if (softBreak)
            {
                line = line[..^1];
            }

            var j = 0;

            while (j < line.Length)
            {
                if (line[j] == '=' && j + 2 < line.Length)
                {
                    var hex = line.Substring(j + 1, 2);

                    if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var b))
                    {
                        result.Append((char)b);
                        j += 3;

                        continue;
                    }
                }

                result.Append(line[j]);
                j++;
            }

            if (!softBreak && i < lines.Length - 1)
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }

    private static string ExtractBoundary(string contentType)
    {
        var match = BoundaryRegex().Match(contentType);

        return match.Success ? match.Groups[1].Value.Trim('"') : null;
    }

    private static string FindTextPlainPart(string body, string boundary)
    {
        var separator = "--" + boundary;
        var parts = body.Split(separator);

        foreach (var part in parts)
        {
            var trimmed = part.TrimStart('\r', '\n');

            if (trimmed.StartsWith("--"))
            {
                continue;
            }

            // Check if this part is text/plain
            var partLines = trimmed.Split('\n');
            var isTextPlain = false;
            var partBodyStart = -1;
            var partHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < partLines.Length; i++)
            {
                var line = partLines[i].TrimEnd('\r');

                if (line.Length == 0)
                {
                    partBodyStart = i + 1;

                    break;
                }

                var colonIdx = line.IndexOf(':');

                if (colonIdx > 0)
                {
                    partHeaders[line[..colonIdx].Trim()] = line[(colonIdx + 1)..].Trim();
                }
            }

            if (partHeaders.TryGetValue("Content-Type", out var ct) && ct.ToLowerInvariant().Contains("text/plain"))
            {
                isTextPlain = true;
            }
            else if (!partHeaders.ContainsKey("Content-Type") && partBodyStart >= 0)
            {
                // No Content-Type in part = assume text/plain
                isTextPlain = true;
            }

            if (isTextPlain && partBodyStart >= 0 && partBodyStart < partLines.Length)
            {
                var partBody = string.Join("\n", partLines[partBodyStart..]).TrimEnd();

                return DecodeTransferEncoding(partHeaders, partBody);
            }
        }

        return null;
    }

    private static string CleanBody(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "";
        }

        // Strip non-ASCII characters (VintageHive NNTP proxy expects ASCII)
        var cleaned = new StringBuilder(body.Length);

        foreach (var ch in body)
        {
            if (ch < 128)
            {
                cleaned.Append(ch);
            }
            else
            {
                // Replace common non-ASCII with ASCII equivalents
                cleaned.Append(ch switch
                {
                    '\u2018' or '\u2019' or '\u0060' or '\u00B4' => '\'',
                    '\u201C' or '\u201D' => '"',
                    '\u2013' or '\u2014' => '-',
                    '\u2026' => '.',
                    '\u00A0' => ' ',
                    _ => '?',
                });
            }
        }

        // Normalize line endings to \r\n for NNTP
        var result = cleaned.ToString().Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

        return result.TrimEnd();
    }

    private static string CleanHeaderValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Strip non-ASCII and tabs from header values (tabs break XOVER format)
        var cleaned = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (ch == '\t')
            {
                cleaned.Append(' ');
            }
            else if (ch < 128)
            {
                cleaned.Append(ch);
            }
            else
            {
                cleaned.Append('?');
            }
        }

        // Decode RFC 2047 encoded words (basic support)
        var result = cleaned.ToString();
        result = EncodedWordRegex().Replace(result, match =>
        {
            try
            {
                var charset = match.Groups[1].Value;
                var encoding = match.Groups[2].Value.ToUpperInvariant();
                var text = match.Groups[3].Value;

                if (encoding == "Q")
                {
                    // Quoted-printable (underscore = space)
                    text = text.Replace('_', ' ');
                    text = DecodeQuotedPrintable(text);
                }
                else if (encoding == "B")
                {
                    // Base64
                    var bytes = Convert.FromBase64String(text);
                    text = Encoding.GetEncoding(charset).GetString(bytes);
                }

                // Re-strip non-ASCII from decoded text
                var sb = new StringBuilder(text.Length);

                foreach (var c in text)
                {
                    sb.Append(c < 128 ? c : '?');
                }

                return sb.ToString();
            }
            catch
            {
                return match.Value;
            }
        });

        return result.Trim();
    }

    [GeneratedRegex(@"boundary=""?([^""\s;]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex BoundaryRegex();

    [GeneratedRegex(@"=\?([^?]+)\?([BbQq])\?([^?]+)\?=")]
    private static partial Regex EncodedWordRegex();
}
