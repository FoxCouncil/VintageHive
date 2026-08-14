// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Data.Types;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Yahoo;
using VintageHive.Proxy.Yahoo.FileTransfer;
using VintageHive.Proxy.Transfer;
using static VintageHive.Proxy.Http.HttpUtilities;
using HttpStatusCode = VintageHive.Proxy.Http.HttpStatusCode;

namespace VintageHive.Processors;

/// <summary>
/// The Yahoo! adapter of the file-transfer relays (relaytransfers). Unlike MSN and AIM this generation is not
/// P2P at all: the sender HTTP-POSTs the file to a Yahoo! filetransfer host and the recipient fetches a URL, so
/// there is no relay listener and no port range - just this web endpoint. The sender's upload lands in
/// <see cref="YahooFileTransfers"/> under a token with a TTL, the recipient is told the download URL over a
/// YMSG 0x46 notify, and the first GET of that URL takes the payload and expires it.
/// <para>
/// Gated on <see cref="ConfigNames.RelayTransfers"/>: with the flag off the host is not claimed at all, so the
/// request falls through the processor chain exactly as today (a period client would simply fail its transfer,
/// which is the pre-feature behavior).
/// </para>
/// </summary>
public static class YahooFileTransferProcessor
{
    // The compiled-in Yahoo! filetransfer hosts. DNS interception already resolves any name to the hive, so
    // claiming these by name is all that is needed to answer for them (mirrors PagerHosts).
    const string PrimaryHost = "filetransfer.msg.yahoo.com";

    const string AlternateHost = "filetransfer.pager.yahoo.com";

    const string UploadPath = "/notifyft";

    const string DownloadPath = "/relayft";

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        if (!IsFileTransferHost(req.Host))
        {
            return false;
        }

        // Flag off: do not claim the host - let the request fall through, so nothing changes for a deployment
        // that has not opted in.
        if (!TransferRelay.Enabled)
        {
            return false;
        }

        res.Cache = false;

        var path = req.Uri.AbsolutePath;

        if (req.Method == HttpMethodName.Post && path.Equals(UploadPath, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleUploadAsync(req, res);
        }

        if (req.Method == HttpMethodName.Get && path.Equals(DownloadPath, StringComparison.OrdinalIgnoreCase))
        {
            return HandleDownload(req, res);
        }

        // A claimed host with an unclaimed path: 404 here rather than fall through to the outward processors.
        res.SetStatusCode(HttpStatusCode.NotFound).SetBodyData(Array.Empty<byte>(), HttpContentTypeMimeType.Application.OctetStream);

        return true;
    }

    static bool IsFileTransferHost(string host)
    {
        return string.Equals(host, PrimaryHost, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, AlternateHost, StringComparison.OrdinalIgnoreCase);
    }

    // POST /notifyft - the sender's upload. The body is a YMSG 0x46 packet (metadata) immediately followed by
    // the raw file bytes, which is the authentic /notifyft shape; a multipart/form-data body and query-string
    // metadata are also accepted so a differing client build still works. The recipient learns the URL over a
    // 0x46 notify; if they are offline the file simply expires (no store-and-forward, by design).
    static async Task<bool> HandleUploadAsync(HttpRequest req, HttpResponse res)
    {
        var body = req.BodyData ?? Array.Empty<byte>();

        string from = null;
        string to = null;
        string fileName = null;
        string message = string.Empty;
        byte[] fileBytes = null;

        // Authentic shape: a leading YMSG packet, then the file as the tail.
        if (TryReadLeadingYmsgPacket(body, out var packet, out var tail))
        {
            from = packet.Get(1) ?? packet.Get(0);
            to = packet.Get(5);
            fileName = packet.Get(27);
            message = packet.Get(14) ?? string.Empty;
            fileBytes = tail;
        }
        else
        {
            // Metadata from the query string; the file body raw, or the first file part of a multipart body.
            from = Query(req, "from");
            to = Query(req, "to");
            fileName = Query(req, "filename");

            fileBytes = TryExtractMultipartFile(req, body, ref fileName) ?? body;
        }

        if (string.IsNullOrEmpty(to) || fileBytes == null || fileBytes.Length == 0)
        {
            res.SetStatusCode(HttpStatusCode.BadRequest).SetBodyData(Array.Empty<byte>(), HttpContentTypeMimeType.Application.OctetStream);

            return true;
        }

        // Size cap: the relay's per-transfer byte bound applies here exactly as it does to the streamed
        // protocols, so one oversized upload cannot pin unbounded memory.
        if (fileBytes.Length > TransferRelay.MaxBytesPerTransfer)
        {
            res.SetStatusCode(HttpStatusCode.PayloadTooLarge).SetBodyData(Array.Empty<byte>(), HttpContentTypeMimeType.Application.OctetStream);

            return true;
        }

        if (string.IsNullOrEmpty(fileName))
        {
            fileName = "file";
        }

        // Resolve like sign-on so alice and alice@<hosted domain> are one recipient; a name resolving to
        // nothing is stored and notified against the presented token, indistinguishable from a real member.
        var recipient = YahooSessionRegistry.ResolveAccountUsername(to) ?? to;

        var token = YahooFileTransfers.Store(from ?? string.Empty, recipient, fileName, fileBytes);

        var url = $"http://{req.Host}{DownloadPath}?token={token}";

        // Notify the recipient if they are online on any Yahoo! transport. If not, the file just expires.
        var session = YahooSessionRegistry.GetByUsername(recipient);

        if (session != null)
        {
            await session.DeliverFileTransferAsync(from ?? string.Empty, fileName, fileBytes.Length, message, url);
        }

        Mind.Db?.RequestsTrack(req.ListenerSocket, req.UserAgent, "YMSGFT", $"upload {from} -> {recipient} ({fileBytes.Length} bytes)", nameof(YahooFileTransferProcessor));

        res.SetBodyData(Array.Empty<byte>(), HttpContentTypeMimeType.Application.OctetStream);

        return true;
    }

    // GET /relayft?token=... - the recipient's download. Single-serve: Take removes the payload, so the URL
    // cannot be replayed and an expired token is a plain 404.
    static bool HandleDownload(HttpRequest req, HttpResponse res)
    {
        var token = Query(req, "token");

        var entry = YahooFileTransfers.Take(token);

        if (entry == null)
        {
            res.SetStatusCode(HttpStatusCode.NotFound).SetBodyData(Array.Empty<byte>(), HttpContentTypeMimeType.Application.OctetStream);

            return true;
        }

        res.SetBodyData(entry.Data, HttpContentTypeMimeType.Application.OctetStream);

        res.Headers.AddOrUpdate(HttpHeaderName.ContentDisposition, $"attachment; filename=\"{SanitiseFileName(entry.FileName)}\"");

        Mind.Db?.RequestsTrack(req.ListenerSocket, req.UserAgent, "YMSGFT", $"download {entry.To} ({entry.Data.Length} bytes)", nameof(YahooFileTransferProcessor));

        return true;
    }

    static bool TryReadLeadingYmsgPacket(byte[] body, out YmsgPacket packet, out byte[] tail)
    {
        packet = null;
        tail = null;

        if (body.Length < YmsgPacket.HeaderSize)
        {
            return false;
        }

        var header = body[..YmsgPacket.HeaderSize];

        if (!YmsgPacket.HasMagic(header))
        {
            return false;
        }

        var bodyLength = YmsgPacket.BodyLength(header);

        if (YmsgPacket.HeaderSize + bodyLength > body.Length)
        {
            return false;
        }

        packet = YmsgPacket.Decode(header, body[YmsgPacket.HeaderSize..(YmsgPacket.HeaderSize + bodyLength)]);
        tail = body[(YmsgPacket.HeaderSize + bodyLength)..];

        return true;
    }

    // Minimal binary-safe multipart extraction: returns the bytes of the first part carrying a filename, and
    // adopts that filename when the caller had none. Returns null when the body is not multipart or has no file
    // part, leaving the caller to treat the whole body as the file.
    static byte[] TryExtractMultipartFile(HttpRequest req, byte[] body, ref string fileName)
    {
        if (!req.Headers.TryGetValue(HttpHeaderName.ContentType, out var contentType) || contentType == null || !contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var marker = "boundary=";
        var idx = contentType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (idx < 0)
        {
            return null;
        }

        var boundary = contentType[(idx + marker.Length)..].Trim().Trim('"');

        if (boundary.Length == 0)
        {
            return null;
        }

        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);

        var starts = FindAll(body, delimiter);

        for (var i = 0; i + 1 < starts.Count; i++)
        {
            // Part spans from just after this delimiter (and its CRLF) to just before the next delimiter.
            var headerStart = starts[i] + delimiter.Length;

            var headerEnd = IndexOf(body, "\r\n\r\n"u8.ToArray(), headerStart);

            if (headerEnd < 0)
            {
                continue;
            }

            var partHeaders = Encoding.ASCII.GetString(body, headerStart, headerEnd - headerStart);

            if (!partHeaders.Contains("filename=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dataStart = headerEnd + 4;

            // Trim the trailing CRLF that precedes the next boundary delimiter.
            var dataEnd = starts[i + 1];

            if (dataEnd >= 2 && body[dataEnd - 2] == (byte)'\r' && body[dataEnd - 1] == (byte)'\n')
            {
                dataEnd -= 2;
            }

            if (dataEnd < dataStart)
            {
                continue;
            }

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = ParseFileName(partHeaders);
            }

            return body[dataStart..dataEnd];
        }

        return null;
    }

    static string ParseFileName(string partHeaders)
    {
        var marker = "filename=\"";
        var start = partHeaders.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return null;
        }

        start += marker.Length;

        var end = partHeaders.IndexOf('"', start);

        return end < 0 ? null : partHeaders[start..end];
    }

    // Strip path separators and quotes so a crafted filename cannot escape the Content-Disposition field or
    // suggest a path to the client.
    static string SanitiseFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "file";
        }

        var cleaned = new string(value.Where(c => c is not ('"' or '\\' or '/' or '\r' or '\n')).ToArray());

        return string.IsNullOrEmpty(cleaned) ? "file" : cleaned;
    }

    static List<int> FindAll(byte[] haystack, byte[] needle)
    {
        var hits = new List<int>();
        var from = 0;

        while (true)
        {
            var at = IndexOf(haystack, needle, from);

            if (at < 0)
            {
                break;
            }

            hits.Add(at);
            from = at + needle.Length;
        }

        return hits;
    }

    static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (var i = start; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    static string Query(HttpRequest req, string name)
    {
        if (req.QueryParams == null)
        {
            return null;
        }

        return req.QueryParams.TryGetValue(name, out var value) ? value : null;
    }
}
