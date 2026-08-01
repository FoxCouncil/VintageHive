// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;
using VintageHive.Proxy.Security;
using static VintageHive.Proxy.Http.HttpUtilities;
using VintageHiveHttpProcessDelegate = System.Func<VintageHive.Proxy.Http.HttpRequest, VintageHive.Proxy.Http.HttpResponse, System.Threading.Tasks.Task<bool>>;

namespace VintageHive.Proxy.Http;

public class HttpProxy : Listener
{
    static readonly Dictionary<HttpStatusCode, string> ErrorPages = LoadErrorPages();

    private static Dictionary<HttpStatusCode, string> LoadErrorPages()
    {
        var pages = new Dictionary<HttpStatusCode, string>();

        using (var notFoundStream = new StreamReader(typeof(HttpProxy).Assembly.GetManifestResourceStream("VintageHive.Statics.errors.404.html")))
        {
            pages[HttpStatusCode.NotFound] = notFoundStream.ReadToEnd();
        }

        using (var serverErrorStream = new StreamReader(typeof(HttpProxy).Assembly.GetManifestResourceStream("VintageHive.Statics.errors.500.html")))
        {
            pages[HttpStatusCode.InternalServerError] = serverErrorStream.ReadToEnd();
        }

        return pages;
    }

    public readonly List<VintageHiveHttpProcessDelegate> Handlers = new();

    public HttpProxy(IPAddress listenAddress, int port, bool secure) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, secure)
    {
        if (secure)
        {
            CertificateAuthority.Init();
        }
    }

    public HttpProxy Use(VintageHiveHttpProcessDelegate delegateFunc)
    {
        Handlers.Add(delegateFunc);

        return this;
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var httpRequest = await HttpRequest.Build(connection, Encoding, data[..read]);

        if (!httpRequest.IsValid)
        {
            // Unsupported/unparseable request (e.g. legacy clients like RealPlayer polling with malformed query strings).
            // Log the request line only - not the whole header dump - at WARN, tagged with the connection trace id.
            // Redacted: an unparseable request line is still a request line, and a client that fumbles its
            // sign-on GET would otherwise write its password straight into the log table.
            var requestLine = UrlRedactor.Redact(Encoding.GetString(data[..read]).Split('\n')[0].Trim());

            Log.WriteLine(Log.LEVEL_WARN, nameof(HttpProxy), $"Unsupported request: {requestLine}", connection?.TraceId.ToString() ?? "");

            return null;
        }

        var httpResponse = new HttpResponse(httpRequest);

        // A credential-bearing URL is never cached, in either direction. The key is the raw URL, so caching a
        // sign-on GET would persist the plaintext password into the cache database as a key that outlives the
        // process - and, worse, two members signing in to the same endpoint share that key, so one would be
        // served the other's login response. Redacting the key does not fix the second problem; not caching does.
        var key = UrlRedactor.ContainsCredentials(httpRequest.Uri.ToString()) ? null : $"HPC-{httpRequest.Method}-{httpRequest.Uri}";

        var cachedResponse = key == null ? null : Mind.Cache.GetHttpProxy(key);

        if (cachedResponse == null)
        {
            var handled = false;

            try
            {
                foreach (var handler in Handlers)
                {
                    if (handled = await handler(httpRequest, httpResponse))
                    {
                        // Only log genuinely proxied traffic - skip requests served locally by the
                        // hive.com intranet/admin portal and its subdomains (admin/radio/api/ads.hive.com).
                        var host = httpRequest.Uri.Host;

                        if (host != HiveDomains.Intranet && !host.EndsWith(HiveDomains.DotSuffix))
                        {
                            Mind.Db.RequestsTrack(httpRequest.ListenerSocket, httpRequest.UserAgent, "HTTP", httpRequest.Uri.ToString(), handler.Method.DeclaringType.Name);
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteException(GetType().Name, ex, connection.TraceId.ToString());

                ProcessErrorResponse(httpRequest, httpResponse, HttpStatusCode.InternalServerError, ex);

                return httpResponse.GetResponseEncodedData();
            }

            // Only substitute the built-in 404 when the handler produced nothing. A processor that answers 404
            // WITH a page has already said what it meant to say; replacing it discards a deliberate response.
            var hasOwnBody = httpResponse.Body != null || httpResponse.Stream != null;

            if (!handled || (handled && httpResponse.StatusCode == HttpStatusCode.NotFound && !hasOwnBody))
            {
                handled = ProcessErrorResponse(httpRequest, httpResponse, HttpStatusCode.NotFound);
            }

            // A 404 is never cacheable, whoever authored it. Before the guard above, every 404 went through
            // ProcessErrorResponse, which clears Cache; a handler-authored body now skips that path, and a
            // cached miss keeps a published page "missing" for the full TTL.
            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                httpResponse.Cache = false;
            }

            if (connection == null)
            {
                // Nothing to send too..
                return null;
            }

            if (handled)
            {
                try
                {
                    // If the handler wrote directly to the socket (Handled=true but
                    // no Body or Stream set on the response), don't generate a
                    // duplicate HTTP response - just handle sessions and return null.
                    if (httpResponse.Handled && httpResponse.Body == null && httpResponse.Stream == null)
                    {
                        if (httpResponse.SessionId != Guid.Empty)
                        {
                            Mind.Db.WebSessionSet(httpResponse.SessionId, httpResponse.Session);
                        }

                        return null;
                    }

                    var buffer = httpResponse.GetResponseEncodedData();

                    if (buffer == null)
                    {
                        ProcessErrorResponse(httpRequest, httpResponse, HttpStatusCode.InternalServerError);

                        buffer = httpResponse.GetResponseEncodedData();
                    }

                    if (httpResponse.SessionId != Guid.Empty)
                    {
                        Mind.Db.WebSessionSet(httpResponse.SessionId, httpResponse.Session);
                    }

                    if (httpResponse.Cache && key != null)
                    {
                        Mind.Cache.SetHttpProxy(key, httpResponse.CacheTtl, Convert.ToBase64String(buffer));
                    }

                    if (httpResponse.Stream != null)
                    {
                        var socket = httpRequest.ListenerSocket;

                        if (socket.IsSecure)
                        {
                            await socket.SecureStream.WriteAsync(buffer);
                            await httpResponse.Stream.CopyToSslAsync(socket.SecureStream);
                        }
                        else
                        {
                            await socket.Stream.WriteAsync(buffer);
                            await httpResponse.Stream.CopyToAsync(socket.Stream);
                        }

                        return null;
                    }

                    return buffer;
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    // Routine client disconnect mid-write - nothing actionable
                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(HttpProxy), $"Response write aborted: {ex.Message}", connection.TraceId.ToString());
                }
                catch (Exception ex)
                {
                    // A genuine response-path fault that used to be swallowed at DEBUG
                    Log.WriteLine(Log.LEVEL_WARN, nameof(HttpProxy), $"Response write failed: {ex.Message}", connection.TraceId.ToString());
                }
                finally
                {
                    // Dispose the upstream content stream on every path (success, disconnect, fault). The fresh-per-request
                    // HttpClient releases its socket only when this stream is disposed. NEVER touch socket.Stream/SecureStream - the Listener owns those.
                    if (httpResponse.Stream != null)
                    {
                        try { await httpResponse.Stream.DisposeAsync(); } catch { }
                    }
                }
            }
        }
        else
        {
            Mind.Db.RequestsTrack(httpRequest.ListenerSocket, httpRequest.UserAgent, "HTTP", httpRequest.Uri.ToString(), "CACHED RESPONSE");

            try
            {
                // Log.WriteLine(Log.LEVEL_REQUEST, GetType().Name, $"({httpRequest.Uri}) [N/A]", connection.TraceId.ToString());

                // Note: the cache stores only the response bytes, not which processor produced them, so a
                // cached hit cannot be attributed in the admin log. Worth adding if that attribution is ever
                // wanted; not a defect.
                return Convert.FromBase64String(cachedResponse);
            }
            catch (Exception ex) { Log.WriteLine(Log.LEVEL_DEBUG, nameof(HttpProxy), $"Cached response decode failed: {ex.Message}", connection.TraceId.ToString()); }
        }

        // Only close the connection if keep-alive is not requested.
        // When IsKeepAlive=true, the Listener loop will read the next request.
        if (!connection.IsKeepAlive)
        {
            connection.RawSocket.Close();
        }

        return null;
    }

    private static bool ProcessErrorResponse(HttpRequest httpRequest, HttpResponse httpResponse, HttpStatusCode statusCode, Exception exception = null)
    {
        httpResponse.SetStatusCode(statusCode);

        var date = DateTime.UtcNow.ToUniversalTime().ToString("R");

        httpResponse.Headers.Add(HttpHeaderName.Date, date);

        Mind.Db.RequestsTrack(httpRequest.ListenerSocket, httpRequest.UserAgent, "HTTP", httpRequest.Uri.ToString(), $"ERROR {(int)statusCode}{(exception != null ? $": {exception}" : "")}");

        // A 404 is an expected miss (no capture / not proxied), not a system fault - keep it out of the ERROR stream.
        var level = statusCode == HttpStatusCode.NotFound ? Log.LEVEL_WARN : Log.LEVEL_ERROR;

        Log.WriteLine(level, nameof(HttpProxy), $"{(int)statusCode} {statusCode}: {UrlRedactor.Redact(httpRequest.Uri.ToString())}", httpRequest.ListenerSocket.TraceId.ToString());

        if (!ErrorPages.ContainsKey(statusCode))
        {
            var plainBody = $"{(int)statusCode} {statusCode}\n\n{UrlRedactor.Redact(httpRequest.Uri?.ToString())}\n\n{date}{string.Join("", Enumerable.Repeat("\n" + string.Join("", Enumerable.Repeat(" ", 80)), 20))}";

            httpResponse.SetBodyString(plainBody, HttpContentTypeMimeType.Text.Plain);

            return true;
        }

        var body = ErrorPages[statusCode];

        if (exception != null)
        {
            body = body.Replace("||ERROR||", exception.ToString());
        }

        var endpoint = ((IPEndPoint)httpRequest.ListenerSocket.RawSocket.LocalEndPoint).Address.MapToIPv4();
        var endpointPort = ((IPEndPoint)httpRequest.ListenerSocket.RawSocket.LocalEndPoint).Port;

        // The error page echoes the request back. That goes to the client that already knows its own password,
        // but the page is the thing a user screenshots into a bug report, so it gets the same treatment.
        var requestUrl = UrlRedactor.Redact(httpRequest.Uri?.ToString());

        var length = 12;

        if (requestUrl.Length > length)
        {
            requestUrl = requestUrl[..length] + requestUrl[length..].MakeHtmlAnchorLinksHappen();
        }

        body = body.Replace("||REQUEST||", requestUrl);
        // Read per request, not at load: ErrorPages is static readonly and would freeze the name at startup,
        // diverging from any runtime ConfigSet - and from an embedder that brands after Mind.Bootstrap().
        body = body.Replace("||PRODUCT||", Mind.ProductName);
        body = body.Replace("||VERSION||", Mind.ProductVersion);
        body = body.Replace("||ERROR_MESSAGE||", httpResponse.ErrorMessage);
        body = body.Replace("||HOST||", $"{endpoint}:{endpointPort}");
        body = body.Replace("||DATE||", date);
        body = body.Replace("||TRACEID||", httpRequest.ListenerSocket.TraceId.ToString());

        httpResponse.SetBodyString(body, HttpContentTypeMimeType.Text.Html);

        // Do not cache the error!
        httpResponse.Cache = false;

        return true;
    }
}