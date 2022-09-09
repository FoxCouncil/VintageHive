using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive.Data.Cache;
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

    public static readonly string ApplicationVersion = typeof(HttpProxy).Assembly.GetName().Version?.ToString() ?? "NA";

    public readonly List<VintageHiveHttpProcessDelegate> Handlers = new();

    public ICacheDb CacheDb { get; set; }

    public HttpProxy(IPAddress listenAddress, int port, bool secure) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, secure) { }

    public HttpProxy Use(VintageHiveHttpProcessDelegate delegateFunc)
    {
        Handlers.Add(delegateFunc);

        return this;
    }

    internal override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var httpRequest = await HttpRequest.Parse(connection, Encoding, data[..read]);

        var httpResponse = new HttpResponse(httpRequest);

        var key = $"HPC-{httpRequest.Uri}";

        var cachedResponse = CacheDb?.Get<string>(key);

        if (cachedResponse == null)
        {
            var handled = false;

            try
            {
                foreach (var handler in Handlers)
                {
                    if (handled = await handler(httpRequest, httpResponse))
                    {
                        break;
                    }
                }
            }
            catch(Exception ex)
            {
                Display.WriteException(ex);

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
                        Db.Sessions.Set(httpResponse.SessionId, httpResponse.Session);
                    }

                    if (httpResponse.Cache)
                    {
                        CacheDb?.Set<string>(key, httpResponse.CacheTtl, Convert.ToBase64String(buffer));
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
            // Display.WriteLog("Cache  HIT: " + key);

            try
            {
                Display.WriteLog($"[{"HTTP Proxy Cached",15} Request] ({httpRequest.Uri}) [N/A]");

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

        body = body.Replace("||REQUEST||", httpRequest.Uri?.ToString());
        body = body.Replace("||VERSION||", ApplicationVersion);
        body = body.Replace("||HOST||", $"{endpoint}:{endpointPort}");
        body = body.Replace("||DATE||", date);

        httpResponse.SetBodyString(body, HttpContentType.Text.Html);

        // Do not cache the error!
        httpResponse.Cache = false;

        return true;
    }
}