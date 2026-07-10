// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Web;
using VintageHive.Network;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Proxy.Http;

public sealed partial class HttpRequest : Request
{
    public const string HttpNewLine = "\r\n";

    public const string HttpBodySplit = HttpNewLine + HttpNewLine;

    public static readonly HttpRequest Invalid = new() { IsValid = false };

    public IReadOnlyDictionary<string, string>? QueryParams { get; private set; }

    public IReadOnlyDictionary<string, string>? FormData { get; private set; }

    public ReadOnlyDictionary<string, string> Cookies { get; private set; }

    public string Raw { get; private set; }

    public string Host => Uri.Host.ToLower();

    public string Method => Type.ToUpper();

    public string Body { get; private set; } = "";

    public byte[] BodyData { get; private set; }

    public string UserAgent => Headers.TryGetValue(HttpHeaderName.UserAgent, out var userAgent) ? userAgent : "NA";

    public bool IsRelativeUri(string uri)
    {
        if (uri == null || uri.Length == 0)
        {
            return false;
        }

        return Uri.AbsolutePath.ToLower().Equals(uri);
    }

    public static HttpRequest Parse(byte[] rawData, string rawRequest, Encoding encoding, ListenerSocket listenerSocket = null)
    {
        var bodyPointer = rawRequest.IndexOf(HttpBodySeperator);

        var rawHeaders = bodyPointer == -1 ? rawRequest : rawRequest[..bodyPointer];

        var rawBodyData = bodyPointer == -1 ? string.Empty : rawRequest[(bodyPointer + HttpBodySeperator.Length)..];

        var rawBody = bodyPointer == -1 ? string.Empty : rawBodyData;

        var parsedRequestArray = rawHeaders.Trim().Split("\r\n");

        if (parsedRequestArray.Length == 1)
        {
            parsedRequestArray = rawHeaders.Trim().Split("\n"); // eww Netscape
        }

        var httpRequestLine = parsedRequestArray[0].Split(" ");

        if (httpRequestLine.Length != 3 || !HttpVerbs.Contains(httpRequestLine[0]) || !HttpVersions.Contains(httpRequestLine[2]))
        {
            return Invalid;
        }

        var headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var header in parsedRequestArray.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var splitHeaderKV = header.Split(":", 2);

            // Skip malformed (colon-less) header lines rather than indexing [1] into thin air
            if (splitHeaderKV.Length != 2 || string.IsNullOrWhiteSpace(splitHeaderKV[0]))
            {
                continue;
            }

            if (!headers.ContainsKey(splitHeaderKV[0]))
            {
                headers.Add(splitHeaderKV[0], splitHeaderKV[1].Trim());
            }
            else
            {
                // Per HTTP spec, combine duplicate headers with comma separator
                headers[splitHeaderKV[0]] += ", " + splitHeaderKV[1].Trim();
            }
        }

        var requestCookies = new Dictionary<string, string>();

        if (headers.TryGetValue(HttpHeaderName.Cookie, out string value))
        {
            var cookies = value.Split("; ");

            foreach (var cookie in cookies)
            {
                var cookieData = cookie.Split("=", 2);

                // Skip valueless cookie fragments; last-wins for duplicate names (browsers send path-scoped dupes)
                if (cookieData.Length != 2 || cookieData[0].Length == 0)
                {
                    continue;
                }

                requestCookies[cookieData[0]] = cookieData[1];
            }
        }

        var formData = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

        if (headers.TryGetValue(HttpHeaderName.ContentType, out string contentType))
        {
            var baseContentType = contentType;

            if (contentType.Contains(';'))
            {
                baseContentType = contentType[..contentType.IndexOf(';')];
            }

            switch (baseContentType)
            {
                case HttpContentTypeMimeType.Application.XWwwFormUrlEncoded:
                {
                    formData = new Dictionary<string, string>(HttpUtility.ParseQueryString(rawBody).ToDictionary(), StringComparer.InvariantCultureIgnoreCase);
                }
                break;

                case HttpContentTypeMimeType.Multipart.FormData:
                {
                    if (rawBody.Length < 11)
                    {
                        break;
                    }

                    var baseBoundryMarker = contentType[(contentType.IndexOf(';') + 11)..];
                    var startBoundryMarker = "--" + baseBoundryMarker;
                    var endBoundryMarker = startBoundryMarker + "--";

                    var boundriesRaw = rawBody.Split(endBoundryMarker, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

                    var boundries = boundriesRaw.Split(startBoundryMarker, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    var dict = new Dictionary<string, string>();

                    foreach (var boundry in boundries)
                    {
                        var boundryLines = boundry.Split("\r\n\r\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        var name = HttpContentBoundryRegex().Match(boundryLines[0]).Groups[1].Value;

                        dict.Add(name, boundryLines[1]);
                    }

                    formData = dict;
                }
                break;
            }
        }

        Dictionary<string, string> queryParams;

        var uri = httpRequestLine[1];

        var qpIndex = uri.IndexOf('?');

        if (qpIndex != -1)
        {
            var rawQueryParams = uri[qpIndex..];

            queryParams = HttpUtility.ParseQueryString(rawQueryParams).ToDictionary();
        }
        else
        {
            queryParams = new Dictionary<string, string>();
        }

        var httpVerb = httpRequestLine[0];

        if (httpVerb != HttpMethodName.Connect && !uri.StartsWith("http") && headers.TryGetValue(HttpHeaderName.Host, out var host))
        {
            if (listenerSocket != null)
            {
                uri = (listenerSocket.IsSecure ? "https" : "http") + "://" + host + uri;
            }
            else
            {
                uri = "http://" + host + uri;
            }
        }
        else if (httpVerb == HttpMethodName.Connect)
        {
            uri = (uri.EndsWith(":443") ? "https://" : "http://") + uri;
        }

        // No Host on an origin-form request leaves uri relative; Uri.TryCreate(Absolute) even succeeds as
        // file:/// on Linux, so the scheme guard is required to reject it rather than build a bogus request.
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            return Invalid;
        }

        return new HttpRequest
        {
            IsValid = true,
            Raw = rawRequest,
            Encoding = encoding,
            Type = httpRequestLine[0],
            Uri = parsedUri,
            Version = httpRequestLine[2],
            Headers = headers,
            Cookies = new ReadOnlyDictionary<string, string>(requestCookies),
            QueryParams = queryParams,
            FormData = formData,
            Body = rawBody,
            BodyData = encoding.GetBytes(rawBody),
            ListenerSocket = listenerSocket
        };
    }

    internal const int MaxHeaderBytes = 64 * 1024;

    // Printers POST multi-MB IPP PDFs through Build, so the body cap is deliberately generous. Raise if a legitimate print job is larger.
    internal const int MaxRequestBodySize = 64 * 1024 * 1024;

    internal static async Task<HttpRequest> Build(ListenerSocket socket, Encoding encoding, byte[] rawBytes)
    {
        if (socket == null || rawBytes == null)
        {
            return Invalid;
        }

        var data = new MemoryStream();
        data.Write(rawBytes, 0, rawBytes.Length);

        // Reject non-HTTP garbage (e.g. RealPlayer probes) up front so it can't hold the read loop open
        if (!StartsWithHttpVerb(data.GetBuffer(), (int)data.Length))
        {
            return Invalid;
        }

        // Phase 1 - read until the header block is complete. Headers split across TCP segments used to be parsed
        // truncated; now we wait for the CRLFCRLF terminator (or LFLF for LF-only Netscape clients).
        var headerEnd = FindHeaderTerminator(data.GetBuffer(), (int)data.Length);

        while (headerEnd == -1)
        {
            if (data.Length > MaxHeaderBytes)
            {
                return Invalid;
            }

            var chunk = await ReadMoreAsync(socket);

            if (chunk.Length == 0)
            {
                return Invalid; // client closed before the headers completed (also breaks the old FIN hot loop)
            }

            data.Write(chunk, 0, chunk.Length);

            headerEnd = FindHeaderTerminator(data.GetBuffer(), (int)data.Length);
        }

        // Phase 2 - if a body is declared, read (counting BYTES) until we have all of it. Reads the decrypted
        // SecureStream under TLS instead of the raw ciphertext, and breaks on a client FIN instead of spinning.
        var headerText = encoding.GetString(data.GetBuffer(), 0, headerEnd);
        var contentLengthMatch = HttpContentLengthParseRegex().Match(headerText);

        if (contentLengthMatch.Success && int.TryParse(contentLengthMatch.Groups[1].Value, out var expectedBodyLength) && expectedBodyLength > 0)
        {
            if (expectedBodyLength > MaxRequestBodySize)
            {
                return Invalid;
            }

            while (data.Length - headerEnd < expectedBodyLength)
            {
                var chunk = await ReadMoreAsync(socket);

                if (chunk.Length == 0)
                {
                    return Invalid; // truncated body
                }

                data.Write(chunk, 0, chunk.Length);
            }
        }

        // Decode exactly once over contiguous bytes: no split-multibyte corruption, no char-vs-byte length mixing
        var allBytes = data.ToArray();

        return Parse(allBytes, encoding.GetString(allBytes), encoding, socket);
    }

    // Reads one chunk, using the decrypted SecureStream when the socket is TLS. The 4096-byte buffer is mandatory:
    // SslStream.ReadAsync copies up to a 4096-byte plaintext chunk per call.
    static async Task<byte[]> ReadMoreAsync(ListenerSocket socket)
    {
        var buffer = new byte[4096];

        var read = socket.IsSecure
            ? await socket.SecureStream.ReadAsync(buffer)
            : await socket.Stream.ReadAsync(buffer);

        return read <= 0 ? Array.Empty<byte>() : buffer[..read];
    }

    // Index just past the header terminator (CRLFCRLF, or LFLF for LF-only clients), or -1 if not present yet.
    static int FindHeaderTerminator(byte[] buffer, int length)
    {
        for (var i = 0; i + 1 < length; i++)
        {
            if (i + 3 < length && buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n' && buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
            {
                return i + 4;
            }

            if (buffer[i] == (byte)'\n' && buffer[i + 1] == (byte)'\n')
            {
                return i + 2;
            }
        }

        return -1;
    }

    // True when the bytes so far are (a prefix of) a known "VERB " token, so an incomplete first read still passes.
    static bool StartsWithHttpVerb(byte[] buffer, int length)
    {
        foreach (var verb in HttpVerbs)
        {
            var token = verb + " ";
            var n = Math.Min(length, token.Length);
            var match = true;

            for (var i = 0; i < n; i++)
            {
                if (buffer[i] != (byte)token[i])
                {
                    match = false;

                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex("name=\"(.*?)\"")]
    private static partial Regex HttpContentBoundryRegex();

    [GeneratedRegex("Content-Length:\\s*(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex HttpContentLengthParseRegex();
}
