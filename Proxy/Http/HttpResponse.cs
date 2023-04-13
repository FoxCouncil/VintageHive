using Fluid;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Dynamic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Proxy.Http;

public sealed class HttpResponse
{
    const string SessionCookieName = "sessionid";

    public readonly static List<string> InlineDispositions = new() {
        // Plain Text
        "text/plain",
        "text/html",
        // Images
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/gif",
        "image/tif",
        // Audio
        "audio/mpeg",
    };

    public HttpRequest Request { get; private set; }

    public Socket Socket => Request.ListenerSocket.RawSocket;

    public Encoding Encoding { get; private set; } = Encoding.UTF8;

    public string Version { get; private set; } = "HTTP/1.1";

    public HttpStatusCode StatusCode { get; private set; } = HttpStatusCode.OK;

    public Dictionary<string, string> Headers { get; private set; } = new();

    public ReadOnlyDictionary<string, string> Cookies => Request.Cookies;

    public Guid SessionId { get; private set; } = Guid.Empty;

    public dynamic Session { get; private set; } = new ExpandoObject();

    public TemplateContext Context { get; }

    public Stream DownloadStream { get; internal set; }

    public byte[] Body { get; internal set; }

    public bool Cache { get; set; } = true;

    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(60);

    public bool Handled { get; set; }

    public string ErrorMessage { get; set; }

    Dictionary<string, string> _deltaCookies = new();

    public HttpResponse(HttpRequest request)
    {
        (Session as INotifyPropertyChanged).PropertyChanged += SessionPropertyChanged;

        Request = request;
        Encoding = request.Encoding ?? Encoding.UTF8;
        Version = request.Version;

        Context = new TemplateContext(new { Request, Response = this });

        if (Request.Cookies.ContainsKey(SessionCookieName))
        {
            if (Guid.TryParse(Request.Cookies[SessionCookieName], out var result))
            {
                SessionId = result;

                Session = Mind.Db.WebSessionGet(SessionId);
            }
        }
    }

    public HttpResponse SetBodyString(string body, string type = HttpContentType.Text.Html)
    {
        SetBodyData(Encoding.GetBytes(body), type);

        Handled = true;

        return this;
    }

    public HttpResponse SetBodyData(byte[] body, string type = HttpContentType.Application.OctetStream)
    {
        Body = body ?? throw new ArgumentNullException(nameof(body));

        Headers.AddOrUpdate(HttpHeaderName.ContentLength, Body.Length.ToString());
        Headers.AddOrUpdate(HttpHeaderName.ContentType, type);

        Handled = true;

        return this;
    }

    public HttpResponse SetBodyFileStream(FileStream stream, string type = HttpContentType.Application.OctetStream)
    {
        Cache = false; // DO NOT OVERLOAD SQLite

        DownloadStream = stream;

        var disposition = "attachment";

        if (InlineDispositions.Contains(type))
        {
            disposition = "inline";
        }

        var contentDisposition = $"{disposition}; filename=\"{Path.GetFileName(stream.Name)}\"";

        Headers.AddOrUpdate(HttpHeaderName.ContentDisposition, contentDisposition);

        Headers.AddOrUpdate(HttpHeaderName.ContentLength, DownloadStream.Length.ToString());
        Headers.AddOrUpdate(HttpHeaderName.ContentType, type);

        return this;
    }

    public HttpResponse SetEncoding(Encoding encoding)
    {
        Encoding = encoding;

        return this;
    }

    public HttpResponse SetStatusCode(HttpStatusCode statusCode)
    {
        StatusCode = statusCode;

        return this;
    }

    public HttpResponse SetOk()
    {
        StatusCode = HttpStatusCode.OK;

        return this;
    }

    public HttpResponse SetNotFound()
    {
        StatusCode = HttpStatusCode.NotFound;

        return this;
    }

    internal HttpResponse SetForbidden()
    {
        StatusCode = HttpStatusCode.Forbidden;

        return this;
    }

    public HttpResponse SetJson(object json)
    {
        StatusCode = HttpStatusCode.OK;

        return SetBodyString(JsonSerializer.Serialize(json), HttpContentType.Application.Json);
    }

    public HttpResponse SetJsonSuccess(object data, bool success = true)
    {
        return SetJson(new { success, data });
    }

    public HttpResponse SetJsonSuccess(bool success = true)
    {
        return SetJson(new { success });
    }

    public HttpResponse SetFound(string foundUri = null)
    {
        if (foundUri == null && Request.Headers.ContainsKey("Referer"))
        {
            foundUri = Request.Headers["Referer"];
        }
        else if (foundUri == null)
        {
            foundUri = "/";
        }

        StatusCode = HttpStatusCode.Found;

        if (Body == null || Body.Length == 0)
        {
            SetBodyString($"<h1>arf 42{Random.Shared.NextDouble()}</h1>\r\n\r\n");
        }

        Headers.AddOrUpdate(HttpHeaderName.Location, foundUri);

        Handled = true;

        return this;
    }

    public HttpResponse SetCookie(string name, string content)
    {
        if (Cookies.ContainsKey(name) && Cookies[name] == content)
        {
            return this;
        }

        _deltaCookies.Add(name, content);

        return this;
    }

    public byte[] GetResponseEncodedData()
    {
        var outputBuilder = new StringBuilder();

        outputBuilder.Append($"{Version} {(int)StatusCode} {StatusCode}{HttpSeperator}");

        foreach (var newCookie in _deltaCookies)
        {
            Headers.Add("Set-Cookie", $"{newCookie.Key}={newCookie.Value}");
        }

        foreach (var header in Headers)
        {
            outputBuilder.Append($"{header.Key}: {header.Value}{HttpSeperator}");
        }

        outputBuilder.Append($"{HttpHeaderName.Server}: VintageHive/{Mind.ApplicationVersion}"); // Fuck them

        outputBuilder.Append(HttpBodySeperator);

        var headerData = Encoding.GetBytes(outputBuilder.ToString());

        if (Body != null && Body.Length > 0)
        {
            return headerData.Concat(Body).ToArray();
        }

        return headerData;
    }

    public void InitSession()
    {
        if (SessionId == Guid.Empty)
        {
            SessionId = Guid.NewGuid();

            SetCookie(SessionCookieName, SessionId.ToString() + "; Path=/; HttpOnly");
        }
    }

    public bool HasSession(string name) => (Session as IDictionary<string, object>).ContainsKey(name);

    public void RemoveSession(string name) => (Session as IDictionary<string, object>).Remove(name);

    private void SessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InitSession();

        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"{e.PropertyName} changed!", "");
    }
}