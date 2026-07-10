// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using HeyRed.Mime;
using VintageHive.Network;
using VintageHive.Proxy.Finger;

namespace VintageHive.Proxy.Gopher;

internal class GopherServer : Listener
{
    const string ProxyPrefix = "/g/";

    const int MaxRequestBytes = 8192;

    const int MaxMenuBytes = 4 * 1024 * 1024;

    const int ConnectTimeoutMs = 10000;

    // Bounds the initial selector read so an idle/slowloris client cannot pin the floating task and socket forever.
    const int RequestReadTimeoutMs = 30000;

    static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(10);

    // Tracks whether any response byte reached the client, so a mid-stream failure aborts (RST) instead of appending an error onto partial content.
    sealed class ClientWrite
    {
        public bool Started;
    }

    public GopherServer(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, false) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var traceId = connection.TraceId.ToString();

        // Captured before the close below; RemoteEndPoint is unreadable on a closed socket.
        var remoteAddress = connection.RemoteAddress;

        Log.WriteLine(Log.LEVEL_INFO, nameof(GopherServer), $"Client connected from {remoteAddress}", traceId);

        using var connectionCts = new CancellationTokenSource(SessionTimeout);

        var token = connectionCts.Token;
        var abort = false;

        try
        {
            string request;

            using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                readCts.CancelAfter(RequestReadTimeoutMs);

                request = await ReadRequestAsync(connection, readCts.Token);
            }

            if (request != null)
            {
                if (IsHttpProxyRequest(request))
                {
                    abort = await HandleHttpProxyRequestAsync(connection, request, token, traceId);
                }
                else
                {
                    var newlineIndex = request.IndexOf('\n');
                    var selectorLine = (newlineIndex < 0 ? request : request[..newlineIndex]).TrimEnd('\r');

                    abort = await HandleNativeRequestAsync(connection, selectorLine, token, traceId);
                }
            }
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(GopherServer), ex, traceId);
        }
        finally
        {
            CloseClient(connection, abort);
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(GopherServer), $"Client disconnected from {remoteAddress}", traceId);

        return null;
    }

    static void CloseClient(ListenerSocket connection, bool abort)
    {
        try
        {
            if (abort)
            {
                // Partial content already streamed then the transfer failed: RST so EOF-means-done clients see a failed fetch, not a truncated success.
                connection.RawSocket.LingerState = new LingerOption(true, 0);
            }
            else
            {
                // RFC 1436 clients treat server-close as end-of-transfer; the Listener base never closes after ProcessConnection, so close here.
                connection.RawSocket.Shutdown(SocketShutdown.Send);
            }
        }
        catch
        {
            // Socket may already be torn down by the client.
        }

        try
        {
            connection.RawSocket.Close();
        }
        catch
        {
            // Ignore teardown races.
        }
    }

    static async Task<string> ReadRequestAsync(ListenerSocket connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];

        using var received = new MemoryStream();

        while (received.Length < MaxRequestBytes)
        {
            var read = await connection.Stream.ReadAsync(buffer, cancellationToken);

            if (read <= 0)
            {
                break;
            }

            received.Write(buffer, 0, read);

            var text = Encoding.Latin1.GetString(received.GetBuffer(), 0, (int)received.Length);

            if (IsHttpProxyRequest(text))
            {
                if (text.Contains("\r\n\r\n") || text.Contains("\n\n"))
                {
                    return text;
                }
            }
            else if (text.Contains('\n'))
            {
                return text;
            }
        }

        return received.Length == 0 ? null : Encoding.Latin1.GetString(received.ToArray());
    }

    internal static bool IsHttpProxyRequest(string request)
    {
        return request != null && request.StartsWith("GET ", StringComparison.Ordinal);
    }

    async Task<bool> HandleNativeRequestAsync(ListenerSocket connection, string selectorLine, CancellationToken token, string traceId)
    {
        Log.WriteLine(Log.LEVEL_INFO, nameof(GopherServer), $"Selector: \"{selectorLine}\"", traceId);

        var tabIndex = selectorLine.IndexOf('\t');
        var selector = tabIndex < 0 ? selectorLine : selectorLine[..tabIndex];
        var search = tabIndex < 0 ? null : selectorLine[(tabIndex + 1)..];

        if (TryParseProxySelector(selector, out var type, out var host, out var port, out var remoteSelector))
        {
            if (IsSelfTarget(host, port, connection))
            {
                await TryWriteErrorMenuAsync(connection, "Refusing to proxy a gopher request back to this server.");

                return false;
            }

            Mind.Db?.RequestsTrack(connection, "N/A", "GOPHER", $"gopher://{host}:{port}/{type}{remoteSelector}", nameof(GopherServer));

            return await RelayRemoteNativeAsync(connection, type, host, port, remoteSelector, search, ResolveAdvertisedHost(connection.LocalIP), token, traceId);
        }

        Mind.Db?.RequestsTrack(connection, "N/A", "GOPHER", selector.Length == 0 ? "/" : selector, nameof(GopherServer));

        string wireResponse;

        try
        {
            var local = await BuildLocalContentAsync(selector, ResolveAdvertisedHost(connection.LocalIP), Port, connection.RemoteIP);

            if (local == null)
            {
                wireResponse = FinalizeMenu(BuildErrorMenu($"'{selector}' does not exist on this server."));
            }
            else if (local.Value.Type == '1')
            {
                wireResponse = FinalizeMenu(local.Value.Content);
            }
            else
            {
                wireResponse = FormatTextDocument(local.Value.Content);
            }
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(GopherServer), ex, traceId);

            wireResponse = FinalizeMenu(BuildErrorMenu("Internal server error."));
        }

        await connection.Stream.WriteAsync(Encoding.Latin1.GetBytes(wireResponse), token);

        return false;
    }

    async Task<bool> HandleHttpProxyRequestAsync(ListenerSocket connection, string request, CancellationToken token, string traceId)
    {
        var lineEnd = request.IndexOf('\n');
        var requestLine = (lineEnd < 0 ? request : request[..lineEnd]).TrimEnd('\r');

        Log.WriteLine(Log.LEVEL_INFO, nameof(GopherServer), $"Proxy request: \"{requestLine}\"", traceId);

        var parts = requestLine.Split(' ');

        if (parts.Length != 3 || !TryParseGopherUrl(parts[1], out var host, out var port, out var type, out var selector, out var search))
        {
            await WriteHttpResponseAsync(connection, "400 Bad Request", "text/html", Encoding.Latin1.GetBytes(BuildHtmlError("400 Bad Request", "Only gopher:// URLs are supported by this proxy.")), token);

            return false;
        }

        Mind.Db?.RequestsTrack(connection, "N/A", "GOPHER", parts[1], nameof(GopherServer));

        if (IsLocalHost(host, connection) && port == Port)
        {
            // A rewritten /g/ selector can arrive here via a saved bookmark pointing at this proxy; unwrap it to its real target and relay.
            if (TryParseProxySelector(selector, out var proxiedType, out var proxiedHost, out var proxiedPort, out var proxiedSelector))
            {
                type = proxiedType;
                host = proxiedHost;
                port = proxiedPort;
                selector = proxiedSelector;
            }
            else
            {
                try
                {
                    var local = await BuildLocalContentAsync(selector, host, Port, connection.RemoteIP);

                    if (local == null)
                    {
                        await WriteHttpResponseAsync(connection, "404 Not Found", "text/html", Encoding.Latin1.GetBytes(BuildHtmlError("404 Not Found", $"'{HtmlEncode(selector)}' does not exist on this server.")), token);
                    }
                    else if (local.Value.Type == '1')
                    {
                        await WriteHttpResponseAsync(connection, "200 OK", "text/html", Encoding.Latin1.GetBytes(MenuToHtml(local.Value.Content, parts[1])), token);
                    }
                    else
                    {
                        await WriteHttpResponseAsync(connection, "200 OK", "text/plain", Encoding.Latin1.GetBytes(local.Value.Content), token);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteException(nameof(GopherServer), ex, traceId);

                    await WriteHttpResponseAsync(connection, "500 Internal Server Error", "text/html", Encoding.Latin1.GetBytes(BuildHtmlError("500 Internal Server Error", "The hive could not build this page.")), token);
                }

                return false;
            }
        }

        if (IsSelfTarget(host, port, connection))
        {
            await WriteHttpResponseAsync(connection, "508 Loop Detected", "text/html", Encoding.Latin1.GetBytes(BuildHtmlError("508 Loop Detected", "Refusing to proxy a gopher request back to this server.")), token);

            return false;
        }

        if (type == '7' && search == null)
        {
            await WriteHttpResponseAsync(connection, "200 OK", "text/html", Encoding.Latin1.GetBytes(BuildSearchPrompt(parts[1])), token);

            return false;
        }

        return await RelayRemoteHttpAsync(connection, type, host, port, selector, search, parts[1], token, traceId);
    }

    async Task<bool> RelayRemoteNativeAsync(ListenerSocket connection, char type, string host, int port, string selector, string search, string advertisedHost, CancellationToken token, string traceId)
    {
        var progress = new ClientWrite();

        try
        {
            using var remote = await ConnectRemoteAsync(host, port, token);
            var remoteStream = remote.GetStream();

            await SendSelectorAsync(remoteStream, selector, search, token);

            if (IsMenuType(type))
            {
                var payload = await ReadMenuAsync(remoteStream, token);
                var rewritten = RewriteMenu(Encoding.Latin1.GetString(payload), advertisedHost, Port);

                progress.Started = true;

                await connection.Stream.WriteAsync(Encoding.Latin1.GetBytes(rewritten), token);
            }
            else
            {
                await CopyUntilCloseAsync(remoteStream, connection.Stream, progress, token);
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(GopherServer), $"Remote fetch failed for gopher://{host}:{port}/{type}{selector}: {ex.Message}", traceId);

            if (progress.Started)
            {
                // Partial payload already streamed; appending an error would corrupt an EOF-terminated download. Abort instead.
                return true;
            }

            await TryWriteErrorMenuAsync(connection, $"Could not reach gopher://{host}:{port}/ ({ex.Message})");

            return false;
        }
    }

    async Task<bool> RelayRemoteHttpAsync(ListenerSocket connection, char type, string host, int port, string selector, string search, string originalUrl, CancellationToken token, string traceId)
    {
        var progress = new ClientWrite();

        try
        {
            using var remote = await ConnectRemoteAsync(host, port, token);
            var remoteStream = remote.GetStream();

            await SendSelectorAsync(remoteStream, selector, search, token);

            if (IsMenuType(type))
            {
                var payload = await ReadMenuAsync(remoteStream, token);
                var html = MenuToHtml(Encoding.Latin1.GetString(payload), originalUrl);

                progress.Started = true;

                await WriteHttpResponseAsync(connection, "200 OK", "text/html", Encoding.Latin1.GetBytes(html), token);
            }
            else if (type == '0')
            {
                // Browser mode: render text as a document, stripping the gopher wire framing (dot-stuffing + terminator) the raw stream carries.
                var payload = await ReadBoundedAsync(remoteStream, MaxMenuBytes, token);
                var text = UnstuffGopherText(Encoding.Latin1.GetString(payload));

                progress.Started = true;

                await WriteHttpResponseAsync(connection, "200 OK", "text/plain", Encoding.Latin1.GetBytes(text), token);
            }
            else
            {
                var header = $"HTTP/1.0 200 OK\r\nServer: VintageHive\r\nContent-Type: {GetHttpContentType(type, selector)}\r\nConnection: close\r\n\r\n";

                progress.Started = true;

                await connection.Stream.WriteAsync(Encoding.Latin1.GetBytes(header), token);
                await CopyUntilCloseAsync(remoteStream, connection.Stream, progress, token);
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(GopherServer), $"Remote fetch failed for {originalUrl}: {ex.Message}", traceId);

            if (progress.Started)
            {
                // 200 header (and possibly body) already sent; appending a 502 into the body would produce a corrupt close-delimited response. Abort instead.
                return true;
            }

            await TryWriteHttpErrorAsync(connection, "502 Bad Gateway", $"Could not reach gopher://{HtmlEncode(host)}:{port}/ ({HtmlEncode(ex.Message)})");

            return false;
        }
    }

    static async Task<TcpClient> ConnectRemoteAsync(string host, int port, CancellationToken token)
    {
        var client = new TcpClient();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            cts.CancelAfter(ConnectTimeoutMs);

            await client.ConnectAsync(host, port, cts.Token);

            client.NoDelay = true;

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    static async Task SendSelectorAsync(NetworkStream remoteStream, string selector, string search, CancellationToken cancellationToken)
    {
        var requestLine = search == null ? selector : $"{selector}\t{search}";

        await remoteStream.WriteAsync(Encoding.Latin1.GetBytes($"{requestLine}\r\n"), cancellationToken);
    }

    static async Task<byte[]> ReadBoundedAsync(NetworkStream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var received = new MemoryStream();

        var buffer = new byte[8192];

        while (received.Length < maxBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read <= 0)
            {
                break;
            }

            received.Write(buffer, 0, read);
        }

        return received.ToArray();
    }

    static async Task<byte[]> ReadMenuAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var received = new MemoryStream();

        var buffer = new byte[8192];

        while (received.Length < MaxMenuBytes)
        {
            int read;

            try
            {
                read = await stream.ReadAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // A server that never closes after a menu would otherwise stall until SessionTimeout; return what we have instead of discarding it.
                break;
            }

            if (read <= 0)
            {
                break;
            }

            received.Write(buffer, 0, read);

            if (EndsWithGopherTerminator(received))
            {
                break;
            }
        }

        return received.ToArray();
    }

    static bool EndsWithGopherTerminator(MemoryStream received)
    {
        var buffer = received.GetBuffer();
        var length = (int)received.Length;

        bool TailIs(string tail)
        {
            if (length < tail.Length)
            {
                return false;
            }

            for (var i = 0; i < tail.Length; i++)
            {
                if (buffer[length - tail.Length + i] != (byte)tail[i])
                {
                    return false;
                }
            }

            return true;
        }

        return TailIs("\r\n.\r\n") || TailIs("\n.\n") || (length == 3 && TailIs(".\r\n")) || (length == 2 && TailIs(".\n"));
    }

    internal static string UnstuffGopherText(string wire)
    {
        var sb = new StringBuilder();

        foreach (var line in (wire ?? string.Empty).ReplaceLineEndings("\n").Split('\n'))
        {
            if (line == ".")
            {
                // Lone dot is the RFC 1436 end-of-transfer marker, not content.
                break;
            }

            // RFC 1436 doubles any leading dot on the wire; undo it for display.
            sb.Append(line.StartsWith('.') ? line[1..] : line);
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    static async Task CopyUntilCloseAsync(NetworkStream source, NetworkStream destination, ClientWrite progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);

            if (read <= 0)
            {
                break;
            }

            progress.Started = true;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
    }

    static async Task WriteHttpResponseAsync(ListenerSocket connection, string status, string contentType, byte[] body, CancellationToken cancellationToken)
    {
        var header = $"HTTP/1.0 {status}\r\nServer: VintageHive\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";

        await connection.Stream.WriteAsync(Encoding.Latin1.GetBytes(header), cancellationToken);
        await connection.Stream.WriteAsync(body, cancellationToken);
    }

    static async Task TryWriteErrorMenuAsync(ListenerSocket connection, string message)
    {
        try
        {
            using var cts = new CancellationTokenSource(5000);

            await connection.Stream.WriteAsync(Encoding.Latin1.GetBytes(FinalizeMenu(BuildErrorMenu(message))), cts.Token);
        }
        catch
        {
            // Client is already gone; nothing left to report to.
        }
    }

    static async Task TryWriteHttpErrorAsync(ListenerSocket connection, string status, string message)
    {
        try
        {
            using var cts = new CancellationTokenSource(5000);

            await WriteHttpResponseAsync(connection, status, "text/html", Encoding.Latin1.GetBytes(BuildHtmlError(status, message)), cts.Token);
        }
        catch
        {
            // Client is already gone; nothing left to report to.
        }
    }

    static string ResolveAdvertisedHost(string localIp)
    {
        // Behind Docker/NAT the accepted socket's local IP is the container-internal address and is undialable from the LAN.
        // Prefer the operator-configured IP (the same signal DNS interception needs) when it is a real address.
        var configured = Mind.Db?.ConfigGet<string>(ConfigNames.IpAddress);

        if (!string.IsNullOrWhiteSpace(configured) && configured != IPAddress.Any.ToString())
        {
            return configured;
        }

        return localIp;
    }

    static bool IsLocalHost(string host, ListenerSocket connection)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == connection.LocalIP)
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip))
        {
            return true;
        }

        var configured = Mind.Db?.ConfigGet<string>(ConfigNames.IpAddress);

        return !string.IsNullOrWhiteSpace(configured) && configured != IPAddress.Any.ToString() && host == configured;
    }

    bool IsSelfTarget(string host, int port, ListenerSocket connection)
    {
        return port == Port && IsLocalHost(host, connection);
    }

    internal static bool IsMenuType(char type)
    {
        return type is '1' or '7';
    }

    internal static bool IsProxyableType(char type)
    {
        // Telnet (8/T), CSO (2), errors (3), info lines (i) and Gopher+ (+) point at things that are not gopher fetches.
        return type is not ('i' or '3' or '8' or 'T' or '2' or '+');
    }

    internal static string BuildProxySelector(char type, string host, int port, string selector)
    {
        return $"{ProxyPrefix}{type}/{host}:{port}/{selector}";
    }

    internal static bool TryParseProxySelector(string selector, out char type, out string host, out int port, out string remoteSelector)
    {
        type = '1';
        host = null;
        port = 70;
        remoteSelector = string.Empty;

        if (selector == null || !selector.StartsWith(ProxyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = selector[ProxyPrefix.Length..];

        if (rest.Length < 2 || rest[1] != '/')
        {
            return false;
        }

        type = rest[0];

        var authorityAndSelector = rest[2..];
        var slashIndex = authorityAndSelector.IndexOf('/');
        var authority = slashIndex < 0 ? authorityAndSelector : authorityAndSelector[..slashIndex];

        remoteSelector = slashIndex < 0 ? string.Empty : authorityAndSelector[(slashIndex + 1)..];

        var colonIndex = authority.LastIndexOf(':');

        if (colonIndex >= 0)
        {
            if (!int.TryParse(authority[(colonIndex + 1)..], out port))
            {
                return false;
            }

            host = authority[..colonIndex];
        }
        else
        {
            host = authority;
        }

        return !string.IsNullOrWhiteSpace(host) && port is >= 1 and <= 65535;
    }

    internal static bool TryParseGopherUrl(string url, out string host, out int port, out char type, out string selector, out string search)
    {
        host = null;
        port = 70;
        type = '1';
        selector = string.Empty;
        search = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("gopher", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        host = uri.Host;
        port = uri.Port > 0 ? uri.Port : 70;

        // Operate on the still-escaped path: gopher selectors are raw octets (RFC 4266), so decode %XX to bytes ourselves rather than as UTF-8.
        var rawPath = uri.AbsolutePath;

        if (rawPath.Length <= 1)
        {
            return true;
        }

        type = rawPath[1];

        var rawSelector = rawPath.Length > 2 ? rawPath[2..] : string.Empty;

        // A tab (escaped %09 or literal) splits selector from a search string; a leading '?' does the same for isindex submissions on type 7.
        var tabIndex = IndexOfTab(rawSelector, out var tabLength);

        if (tabIndex >= 0)
        {
            search = PercentDecodeLatin1(rawSelector[(tabIndex + tabLength)..], plusToSpace: false);
            rawSelector = rawSelector[..tabIndex];
        }
        else if (type == '7')
        {
            // .NET's gopher URI parser folds '?' into the path as '%3F'; browser isindex forms arrive here form-encoded.
            var queryIndex = IndexOfQuery(rawSelector, out var queryLength);

            if (queryIndex >= 0)
            {
                search = PercentDecodeLatin1(rawSelector[(queryIndex + queryLength)..], plusToSpace: true);
                rawSelector = rawSelector[..queryIndex];
            }
        }

        selector = PercentDecodeLatin1(rawSelector, plusToSpace: false);

        return true;
    }

    static int IndexOfTab(string value, out int matchLength)
    {
        var literal = value.IndexOf('\t');
        var escaped = value.IndexOf("%09", StringComparison.OrdinalIgnoreCase);

        return PickEarliest(literal, 1, escaped, 3, out matchLength);
    }

    static int IndexOfQuery(string value, out int matchLength)
    {
        var literal = value.IndexOf('?');
        var escaped = value.IndexOf("%3F", StringComparison.OrdinalIgnoreCase);

        return PickEarliest(literal, 1, escaped, 3, out matchLength);
    }

    static int PickEarliest(int literalIndex, int literalLength, int escapedIndex, int escapedLength, out int matchLength)
    {
        if (literalIndex >= 0 && (escapedIndex < 0 || literalIndex <= escapedIndex))
        {
            matchLength = literalLength;

            return literalIndex;
        }

        matchLength = escapedLength;

        return escapedIndex;
    }

    internal static string PercentDecodeLatin1(string value, bool plusToSpace)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '%' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                sb.Append((char)((Uri.FromHex(value[i + 1]) << 4) | Uri.FromHex(value[i + 2])));

                i += 2;
            }
            else if (plusToSpace && c == '+')
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    internal static string RewriteMenu(string menuPayload, string proxyHost, int proxyPort)
    {
        var sb = new StringBuilder();

        foreach (var line in MenuLines(menuPayload))
        {
            var item = GopherMenuItem.Parse(line);

            // 'URL:' selectors are the web-link idiom: clients open them directly, so leave host/selector untouched.
            if (item == null || !IsProxyableType(item.Type) || string.IsNullOrWhiteSpace(item.Host) || item.Selector.StartsWith("URL:", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(line);
                sb.Append("\r\n");

                continue;
            }

            item.Selector = BuildProxySelector(item.Type, item.Host, item.Port, item.Selector);
            item.Host = proxyHost;
            item.Port = proxyPort;

            sb.Append(item.Serialize());
            sb.Append("\r\n");
        }

        sb.Append(".\r\n");

        return sb.ToString();
    }

    internal static string MenuToHtml(string menuPayload, string requestUrl)
    {
        var sb = new StringBuilder();

        sb.Append("<html><head><title>Gopher Menu</title></head><body>\r\n");
        sb.Append($"<h2>Gopher Menu: {HtmlEncode(requestUrl)}</h2>\r\n<hr>\r\n<pre>\r\n");

        foreach (var line in MenuLines(menuPayload))
        {
            var item = GopherMenuItem.Parse(line);

            if (item == null)
            {
                continue;
            }

            switch (item.Type)
            {
                case 'i':
                {
                    sb.Append(HtmlEncode(item.Display));
                }
                break;

                case '3':
                {
                    sb.Append($"<b>{HtmlEncode(item.Display)}</b>");
                }
                break;

                case '8':
                case 'T':
                {
                    // Host/Type come from a remote-controlled menu line; encode the whole URL so they cannot break out of the attribute.
                    sb.Append($"<a href=\"{HtmlEncode($"telnet://{item.Host}:{item.Port}/")}\">{HtmlEncode(item.Display)}</a>");
                }
                break;

                default:
                {
                    if (item.Selector.StartsWith("URL:", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append($"<a href=\"{HtmlEncode(item.Selector[4..])}\">{HtmlEncode(item.Display)}</a>");
                    }
                    else
                    {
                        sb.Append($"<a href=\"{HtmlEncode($"gopher://{item.Host}:{item.Port}/{item.Type}{EscapeSelector(item.Selector)}")}\">{HtmlEncode(item.Display)}</a>");

                        if (item.Type == '7')
                        {
                            sb.Append(" (search)");
                        }
                    }
                }
                break;
            }

            sb.Append("\r\n");
        }

        sb.Append("</pre>\r\n<hr>\r\n<i>VintageHive Gopher Service</i>\r\n</body></html>");

        return sb.ToString();
    }

    internal static string FinalizeMenu(string menuPayload)
    {
        var sb = new StringBuilder();

        foreach (var line in MenuLines(menuPayload))
        {
            sb.Append(line);
            sb.Append("\r\n");
        }

        sb.Append(".\r\n");

        return sb.ToString();
    }

    internal static string FormatTextDocument(string text)
    {
        var sb = new StringBuilder();

        foreach (var line in (text ?? string.Empty).ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith('.'))
            {
                sb.Append('.');
            }

            sb.Append(line);
            sb.Append("\r\n");
        }

        sb.Append(".\r\n");

        return sb.ToString();
    }

    static IEnumerable<string> MenuLines(string menuPayload)
    {
        foreach (var rawLine in (menuPayload ?? string.Empty).Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line == ".")
            {
                yield break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            yield return line;
        }
    }

    static async Task<(char Type, string Content)?> BuildLocalContentAsync(string selector, string host, int port, string clientIp)
    {
        switch (selector)
        {
            case "":
            case "/":
            {
                return ('1', BuildRootMenu(host, port));
            }

            case "/news":
            {
                return ('1', BuildNewsTopicsMenu(host, port));
            }

            case "/gopherspace":
            {
                return ('1', BuildGopherspaceMenu(host, port));
            }

            case "/weather":
            {
                return ('0', await BuildWeatherTextAsync(clientIp));
            }

            case "/users":
            {
                return ('0', FingerServer.BuildUserList());
            }

            case "/about":
            {
                return ('0', BuildAboutText());
            }
        }

        if (selector.StartsWith("/news/article/", StringComparison.Ordinal))
        {
            return ('0', await BuildNewsArticleTextAsync(selector["/news/article/".Length..]));
        }

        if (selector.StartsWith("/news/", StringComparison.Ordinal) && Enum.TryParse<GoogleNewsTopic>(selector["/news/".Length..], false, out var topic))
        {
            return ('1', await BuildNewsHeadlinesMenuAsync(topic, host, port));
        }

        return null;
    }

    internal static string BuildRootMenu(string host, int port)
    {
        var sb = new StringBuilder();

        AppendInfo(sb, "=========================================");
        AppendInfo(sb, "        VintageHive Gopher Server");
        AppendInfo(sb, "        Serving the hive since 1996");
        AppendInfo(sb, "=========================================");
        AppendInfo(sb, "");
        AppendItem(sb, '1', "News Headlines", "/news", host, port);
        AppendItem(sb, '0', "Weather Report", "/weather", host, port);
        AppendItem(sb, '0', "Who Is Online", "/users", host, port);
        AppendItem(sb, '0', "About This Server", "/about", host, port);
        AppendInfo(sb, "");
        AppendItem(sb, '1', "Explore Live Gopherspace", "/gopherspace", host, port);

        return sb.ToString();
    }

    internal static string BuildNewsTopicsMenu(string host, int port)
    {
        var sb = new StringBuilder();

        AppendInfo(sb, "News Headlines by Topic");
        AppendInfo(sb, "");

        foreach (var topic in Enum.GetValues<GoogleNewsTopic>())
        {
            AppendItem(sb, '1', $"{topic} News", $"/news/{topic}", host, port);
        }

        return sb.ToString();
    }

    static async Task<string> BuildNewsHeadlinesMenuAsync(GoogleNewsTopic topic, string host, int port)
    {
        var sb = new StringBuilder();

        AppendInfo(sb, $"{topic} News Headlines");
        AppendInfo(sb, "");

        var headlines = await NewsUtils.GetGoogleTopicArticles(topic);

        if (headlines == null || headlines.Count == 0)
        {
            AppendInfo(sb, "No headlines are available right now.");

            return sb.ToString();
        }

        foreach (var headline in headlines.Take(30))
        {
            var title = headline.Title?.ReplaceLineEndings(" ").Trim() ?? "(untitled)";

            if (title.Length > 70)
            {
                title = $"{title[..67]}...";
            }

            AppendItem(sb, '0', title, $"/news/article/{headline.Id}", host, port);
        }

        return sb.ToString();
    }

    static async Task<string> BuildNewsArticleTextAsync(string articleId)
    {
        var article = await NewsUtils.GetGoogleNewsArticle(articleId);

        if (article == null)
        {
            return "The article could not be loaded.";
        }

        var sb = new StringBuilder();

        sb.Append(article.Title?.ReplaceLineEndings(" "));
        sb.Append("\n\n");
        sb.Append(article.TextContent ?? "(no article text)");

        return sb.ToString();
    }

    static async Task<string> BuildWeatherTextAsync(string clientIp)
    {
        var tempUnits = Mind.Db.ConfigLocalGet<string>(clientIp, ConfigNames.TemperatureUnits);
        var distUnits = Mind.Db.ConfigLocalGet<string>(clientIp, ConfigNames.DistanceUnits);
        var location = Mind.Db.ConfigGet<GeoIp>(ConfigNames.Location);

        if (location == null)
        {
            return "No weather location is configured.\n\nVisit the hive.com settings page to set one.";
        }

        var weather = await WeatherUtils.GetDataByGeoIp(location, tempUnits, distUnits);

        if (weather == null)
        {
            return "Weather data is currently unavailable.";
        }

        var tempUnit = weather.HourlyUnits?.Temperature2m ?? "°";
        var windUnit = distUnits == WeatherUtils.DistanceUnits.Metric ? "km/h" : "mph";

        var sb = new StringBuilder();

        sb.Append($"Weather Report for {location.fullname}\n");
        sb.Append($"{new string('-', 50)}\n\n");
        sb.Append($"Current: {weather.CurrentWeather.Temperature}{tempUnit}, {WeatherUtils.ConvertWmoCodeToString(weather.CurrentWeather.Weathercode)}\n");
        sb.Append($"Wind: {weather.CurrentWeather.Windspeed} {windUnit}\n");

        if (weather.Daily?.Time != null)
        {
            sb.Append("\nForecast:\n");

            for (var i = 0; i < weather.Daily.Time.Count && i < 7; i++)
            {
                var day = DateTime.Parse(weather.Daily.Time[i]).ToString("ddd MMM dd");

                sb.Append($"{day}  {weather.Daily.Temperature2mMin[i],5:0.#}{tempUnit} - {weather.Daily.Temperature2mMax[i],5:0.#}{tempUnit}  {WeatherUtils.ConvertWmoCodeToString(weather.Daily.Weathercode[i])}\n");
            }
        }

        return sb.ToString();
    }

    internal static string BuildAboutText()
    {
        var sb = new StringBuilder();

        sb.Append("VintageHive Gopher Server\n");
        sb.Append($"{new string('-', 50)}\n\n");
        sb.Append("This is the gopher face of your VintageHive box.\n\n");
        sb.Append("The root menu serves hive content: news headlines,\n");
        sb.Append("the local weather report and who is online.\n\n");
        sb.Append("The Live Gopherspace menu relays real gopher servers\n");
        sb.Append("that are still alive on today's internet. Every menu\n");
        sb.Append("fetched from a remote server is rewritten so that all\n");
        sb.Append("of gopherspace stays browsable through this machine,\n");
        sb.Append("no matter how deep you burrow.\n\n");
        sb.Append("Point any gopher client at this host, port 70, or set\n");
        sb.Append("this host as the gopher proxy in your web browser.\n");

        return sb.ToString();
    }

    internal static string BuildGopherspaceMenu(string host, int port)
    {
        var sb = new StringBuilder();

        AppendInfo(sb, "Live Gopherspace");
        AppendInfo(sb, "");
        AppendInfo(sb, "These are real gopher servers, alive on today's");
        AppendInfo(sb, "internet, relayed through your VintageHive.");
        AppendInfo(sb, "");
        AppendProxiedItem(sb, '1', "Floodgap Systems", "gopher.floodgap.com", 70, "", host, port);
        AppendProxiedItem(sb, '7', "Veronica-2 Search", "gopher.floodgap.com", 70, "/v2/vs", host, port);
        AppendProxiedItem(sb, '1', "SDF Public Access UNIX", "sdf.org", 70, "", host, port);
        AppendProxiedItem(sb, '1', "Gopherpedia (Wikipedia)", "gopherpedia.com", 70, "", host, port);
        AppendProxiedItem(sb, '1', "The Gopher Club", "gopher.club", 70, "", host, port);
        AppendProxiedItem(sb, '1', "Bitreich", "bitreich.org", 70, "", host, port);

        return sb.ToString();
    }

    internal static string BuildErrorMenu(string message)
    {
        return $"3{message}\tfake\t(NULL)\t0";
    }

    static string BuildHtmlError(string status, string message)
    {
        return $"<html><head><title>{HtmlEncode(status)}</title></head><body><h1>{HtmlEncode(status)}</h1><p>{message}</p><hr><i>VintageHive Gopher Service</i></body></html>";
    }

    static string BuildSearchPrompt(string requestUrl)
    {
        return $"<html><head><title>Gopher Search</title></head><body><h2>Gopher Search: {HtmlEncode(requestUrl)}</h2><hr><isindex prompt=\"Search terms: \"><hr><i>VintageHive Gopher Service</i></body></html>";
    }

    internal static string GetHttpContentType(char type, string selector)
    {
        return type switch
        {
            '0' => "text/plain",
            'h' => "text/html",
            'g' => "image/gif",
            'I' => GuessBinaryContentType(selector, "image/jpeg"),
            's' => GuessBinaryContentType(selector, "audio/basic"),
            _ => GuessBinaryContentType(selector, "application/octet-stream")
        };
    }

    static string GuessBinaryContentType(string selector, string fallback)
    {
        try
        {
            var guessed = MimeTypesMap.GetMimeType(selector ?? string.Empty);

            return guessed == "application/octet-stream" ? fallback : guessed;
        }
        catch
        {
            return fallback;
        }
    }

    static void AppendInfo(StringBuilder sb, string text)
    {
        sb.Append($"i{text}\tfake\t(NULL)\t0\r\n");
    }

    static void AppendItem(StringBuilder sb, char type, string display, string selector, string host, int port)
    {
        sb.Append($"{type}{display?.Replace('\t', ' ')}\t{selector}\t{host}\t{port}\r\n");
    }

    static void AppendProxiedItem(StringBuilder sb, char type, string display, string remoteHost, int remotePort, string remoteSelector, string host, int port)
    {
        AppendItem(sb, type, display, BuildProxySelector(type, remoteHost, remotePort, remoteSelector), host, port);
    }

    static string HtmlEncode(string text)
    {
        return WebUtility.HtmlEncode(text ?? string.Empty);
    }

    static string EscapeSelector(string selector)
    {
        return Uri.EscapeDataString(selector ?? string.Empty).Replace("%2F", "/");
    }
}
