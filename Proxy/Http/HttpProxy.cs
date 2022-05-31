using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive.Data.Cache;
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

    public Encoding Encoding { get; set; } = Encoding.UTF8;

    public ICacheDb CacheDb { get; set; }

    public HttpProxy(IPAddress listenAddress, int port, bool secure) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, secure) { }

    public HttpProxy Use(VintageHiveHttpProcessDelegate delegateFunc)
    {
        Handlers.Add(delegateFunc);

        return this;
    }

    internal override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var httpRequest = HttpRequest.Parse(connection, Encoding, data[..read]);

        var httpResponse = new HttpResponse(httpRequest);

        var key = $"PC-{httpRequest.Uri}";

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
                ProcessErrorResponse(httpRequest, httpResponse, HttpStatusCode.InternalServerError, ex);

                return httpResponse.GetResponseEncodedData();
            }

            if (!handled)
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

                    if (httpResponse.Cache)
                    {
                        CacheDb?.Set<string>(key, httpResponse.CacheTtl, Convert.ToBase64String(buffer));
                    }

                    return buffer;
                }
                catch (Exception) { }
            }
        }
        else
        {
            // Console.WriteLine("Cache  HIT: " + key);

            try
            {
                Console.WriteLine($"[{"Proxy Cached",15} Request] ({httpRequest.Uri}) [N/A]");

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

        body = body.Replace("||REQUEST||", httpRequest.Uri?.ToString());
        body = body.Replace("||VERSION||", ApplicationVersion);
        body = body.Replace("||HOST||", httpRequest.ListenerSocket.RawSocket.LocalEndPoint?.ToString());
        body = body.Replace("||DATE||", date);

        httpResponse.SetBodyString(body, HttpContentType.Text.Html);

        // Do not cache the error!
        httpResponse.Cache = false;

        return true;
    }
}