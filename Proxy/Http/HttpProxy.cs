using System.Net;
using System.Net.Sockets;
using VintageHive.Network;
using static VintageHive.Proxy.Http.HttpUtilities;
using VintageHiveHttpProcessDelegate = System.Func<VintageHive.Proxy.Http.HttpRequest, VintageHive.Proxy.Http.HttpResponse, System.Threading.Tasks.Task<bool>>;

namespace VintageHive.Proxy.Http;

public class HttpProxy : Listener
{
    static readonly Dictionary<HttpStatusCode, string> ErrorPages = new()
    {
        { HttpStatusCode.NotFound, new StreamReader(typeof(HttpProxy).Assembly.GetManifestResourceStream("VintageHive.Statics.errors.404.html")).ReadToEnd() },
        { HttpStatusCode.InternalServerError, new StreamReader(typeof(HttpProxy).Assembly.GetManifestResourceStream("VintageHive.Statics.errors.500.html")).ReadToEnd() }
    };

    public readonly List<VintageHiveHttpProcessDelegate> Handlers = new();

    public HttpProxy(IPAddress listenAddress, int port, bool secure) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, secure) { }

    public HttpProxy Use(VintageHiveHttpProcessDelegate delegateFunc)
    {
        Handlers.Add(delegateFunc);

        return this;
    }

    internal override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var httpRequest = await HttpRequest.Parse(connection, Encoding, data[..read]);

        if (!httpRequest.IsValid)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(HttpProxy), $"Unhandled type of HTTP request; {Encoding.GetString(data[..read])}", httpRequest.ListenerSocket.TraceId.ToString());

            return null;
        }

        var httpResponse = new HttpResponse(httpRequest);

        var key = $"HPC-{httpRequest.Uri}";

        var cachedResponse = Mind.Cache.GetHttpProxy(key);

        if (cachedResponse == null)
        {
            var handled = false;

            try
            {
                foreach (var handler in Handlers)
                {
                    if (handled = await handler(httpRequest, httpResponse))
                    {
                        Mind.Db.RequestsTrack(httpRequest.ListenerSocket, httpRequest.Headers[HttpHeaderName.UserAgent], "HTTP", httpRequest.Uri.ToString(), handler.Method.DeclaringType.Name);

                        break;
                    }
                }
            }
            catch(Exception ex)
            {
                Log.WriteException(GetType().Name, ex, connection.TraceId.ToString());

                ProcessErrorResponse(httpRequest, httpResponse, HttpStatusCode.InternalServerError, ex);

                return httpResponse.GetResponseEncodedData();
            }

            if (!handled || (handled && httpResponse.StatusCode == HttpStatusCode.NotFound))
            {
                // TODO: Add Error Handling...
                handled = ProcessErrorResponse(httpRequest, httpResponse, HttpStatusCode.NotFound);
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

                    if (httpResponse.Cache)
                    {
                        Mind.Cache.SetHttpProxy(key, httpResponse.CacheTtl, Convert.ToBase64String(buffer));
                    }

                    if (httpResponse.DownloadStream != null)
                    {
                        await httpRequest.ListenerSocket.Stream.WriteAsync(buffer);

                        await httpResponse.DownloadStream.CopyToAsync(httpRequest.ListenerSocket.Stream);

                        httpResponse.DownloadStream.Close();

                        return null;
                    }    

                    return buffer;
                }
                catch (Exception) { }
            }
        }
        else
        {
            Mind.Db.RequestsTrack(httpRequest.ListenerSocket, httpRequest.Headers[HttpHeaderName.UserAgent], "HTTP", httpRequest.Uri.ToString(), "CACHED RESPONSE");

            try
            {
                // Log.WriteLine(Log.LEVEL_REQUEST, GetType().Name, $"({httpRequest.Uri}) [N/A]", connection.TraceId.ToString());

                // TODO: Store metadata about what made the cached response.
                return Convert.FromBase64String(cachedResponse);
            }
            catch (Exception) { }
        }

        // TODO: Keep-Alive respect?
        connection.RawSocket.Close();

        return null;
    }

    private static bool ProcessErrorResponse(HttpRequest httpRequest, HttpResponse httpResponse, HttpStatusCode statusCode, Exception exception = null)
    {
        httpResponse.SetStatusCode(statusCode);

        var date = DateTime.UtcNow.ToUniversalTime().ToString("R");

        httpResponse.Headers.Add(HttpHeaderName.Date, date);

        Mind.Db.RequestsTrack(httpRequest.ListenerSocket, httpRequest.Headers[HttpHeaderName.UserAgent], "HTTP", httpRequest.Uri.ToString(), $"ERROR {(int)statusCode}{(exception != null ? $": {exception}" : "")}");
        
        Log.WriteLine(Log.LEVEL_ERROR, nameof(HttpProxy), $" Unhandled Request {(int)statusCode} {statusCode}: {httpRequest.Uri}", httpRequest.ListenerSocket.TraceId.ToString());

        if (!ErrorPages.ContainsKey(statusCode))
        {
            var plainBody = $"{(int)statusCode} {statusCode}\n\n{httpRequest.Uri}\n\n{date}{string.Join("", Enumerable.Repeat("\n" + string.Join("", Enumerable.Repeat(" ", 80)), 20))}";

            httpResponse.SetBodyString(plainBody, HttpContentType.Text.Plain);

            return true;
        }

        var body = ErrorPages[statusCode];

        if (exception != null)
        {
            body = body.Replace("||ERROR||", exception.ToString());
        }

        var endpoint = ((IPEndPoint)httpRequest.ListenerSocket.RawSocket.LocalEndPoint).Address.MapToIPv4();
        var endpointPort = ((IPEndPoint)httpRequest.ListenerSocket.RawSocket.LocalEndPoint).Port;

        var requestUrl = httpRequest.Uri?.ToString();
        
        var length = 12;

        if (requestUrl.Length > length)
        {
            requestUrl = requestUrl[..length] + requestUrl[length..].MakeHtmlAnchorLinksHappen();
        }

        body = body.Replace("||REQUEST||", requestUrl);
        body = body.Replace("||VERSION||", Mind.ApplicationVersion);
        body = body.Replace("||ERROR_MESSAGE||", httpResponse.ErrorMessage);
        body = body.Replace("||HOST||", $"{endpoint}:{endpointPort}");
        body = body.Replace("||DATE||", date);
        body = body.Replace("||TRACEID||", httpRequest.ListenerSocket.TraceId.ToString());

        httpResponse.SetBodyString(body, HttpContentType.Text.Html);

        // Do not cache the error!
        httpResponse.Cache = false;

        return true;
    }
}