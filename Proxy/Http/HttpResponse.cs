// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using HeyRed.Mime;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Dynamic;
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

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public Dictionary<string, string> Headers { get; private set; } = new();

    public ReadOnlyDictionary<string, string> Cookies => Request.Cookies;

    public Guid SessionId { get; set; } = Guid.Empty;

    public dynamic Session { get; set; } = new ExpandoObject();

    public TemplateContext Context { get; }

    public Stream Stream { get; set; }

    public byte[] Body { get; internal set; }

    public bool Cache { get; set; } = true;

    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(60);

    public bool Handled { get; set; }

    public string ErrorMessage { get; set; }

    readonly Dictionary<string, string> deltaCookies = new();

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

    public HttpResponse SetBodyString(string body, string type = HttpContentTypeMimeType.Text.Html)
    {
        SetBodyData(Encoding.GetBytes(body), type);

        Handled = true;

        return this;
    }

    public HttpResponse SetBodyData(byte[] body, string type = HttpContentTypeMimeType.Application.OctetStream)
    {
        Body = body ?? throw new ArgumentNullException(nameof(body));

        Headers.AddOrUpdate(HttpHeaderName.ContentLength, Body.Length.ToString());
        Headers.AddOrUpdate(HttpHeaderName.ContentType, type);

        Handled = true;

        return this;
    }

    public HttpResponse SetStreamForDownload(FileStream stream, string type = HttpContentTypeMimeType.Application.OctetStream)
    {
        var disposition = "attachment";

        if (InlineDispositions.Contains(type))
        {
            disposition = "inline";
        }

        var contentDisposition = $"{disposition}; filename=\"{Path.GetFileName(stream.Name)}\"";

        Headers.AddOrUpdate(HttpHeaderName.ContentDisposition, contentDisposition);
        Headers.AddOrUpdate(HttpHeaderName.ContentLength, stream.Length.ToString());

        return SetBodyStream(stream, type);
    }

    public HttpResponse SetStream(Stream stream, string type = HttpContentTypeMimeType.Application.OctetStream)
    {
        Headers.AddOrUpdate(HttpHeaderName.ContentLength, stream.Length.ToString());

        return SetBodyStream(stream, type);
    }

    public HttpResponse SetBodyStream(Stream stream, string type = HttpContentTypeMimeType.Application.OctetStream)
    {
        Cache = false; // DO NOT OVERLOAD SQLite

        Stream = stream;

        Headers.AddOrUpdate(HttpHeaderName.ContentType, type);

        Handled = true;

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

        return SetBodyString(JsonSerializer.Serialize(json), HttpContentTypeMimeType.Application.Json);
    }

    public HttpResponse SetJsonSuccess(object data, bool success = true)
    {
        return SetJson(new { success, data });
    }

    public HttpResponse SetJsonSuccess(bool success = true)
    {
        return SetJson(new { success });
    }

    public HttpResponse SetRedirect(string foundUri = null)
    {
        return SetLocation(foundUri, HttpStatusCode.MovedPermanently);
    }

    public HttpResponse SetFound(string foundUri = null)
    {
        return SetLocation(foundUri);
    }

    public HttpResponse SetLocation(string location, HttpStatusCode statusCode = HttpStatusCode.Found)
    {
        if (location == null && Request.Headers.ContainsKey("Referer"))
        {
            location = Request.Headers["Referer"];
        }
        else location ??= "/";

        StatusCode = statusCode;

        if (Body == null || Body.Length == 0)
        {
            SetBodyString($"<h1>arf 42{Random.Shared.NextDouble()}</h1>\r\n\r\n");
        }

        Headers.AddOrUpdate(HttpHeaderName.Location, location);

        Handled = true;

        return this;
    }

    public HttpResponse SetCookie(string name, string content)
    {
        if (Cookies.TryGetValue(name, out string value) && value == content)
        {
            return this;
        }

        deltaCookies.Add(name, content);

        return this;
    }

    public byte[] GetResponseEncodedData()
    {
        var outputBuilder = new StringBuilder();

        outputBuilder.Append($"{Version} {(int)StatusCode} {StatusCode}{HttpSeperator}");

        foreach (var newCookie in deltaCookies)
        {
            Headers.Add("Set-Cookie", $"{newCookie.Key}={newCookie.Value}");
        }

        foreach (var header in Headers)
        {
            outputBuilder.Append($"{header.Key}: {header.Value}{HttpSeperator}");
        }

        outputBuilder.Append($"{HttpHeaderName.XTraceId}: {Request.ListenerSocket.TraceId}{HttpSeperator}"); // Tracing

        outputBuilder.Append($"{HttpHeaderName.Server}: {Mind.ProductName}/{Mind.ProductVersion}"); // Fuck them


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

    public async Task SetExternal(string url = "")
    {
        if (string.IsNullOrEmpty(url))
        {
            url = Request.Uri.ToString();
        }

        using var httpClient = HttpClientUtils.GetHttpClient(Request, new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        });

        // Preserve the original method and body - the old code forwarded EVERYTHING as GET and threw when a content
        // header (Content-Type/Length) was fed to DefaultRequestHeaders, so all POSTs through DialNine/Helper failed.
        var requestMessage = new HttpRequestMessage(new HttpMethod(Request.Method), url);

        if (Request.BodyData != null && Request.BodyData.Length > 0)
        {
            requestMessage.Content = new ByteArrayContent(Request.BodyData);
        }

        foreach (var header in Request.Headers)
        {
            if (IsHopByHopHeader(header.Key))
            {
                continue;
            }

            // TryAddWithoutValidation splits content headers to Content; never throws on an unknown/odd header
            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var externalResponse = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);

        CacheTtl = externalResponse.Headers.CacheControl?.MaxAge ?? TimeSpan.FromSeconds(300);

        var mimeType = externalResponse.Content.Headers.ContentType?.MediaType ?? MimeTypesMap.GetMimeType(url);

        foreach (var header in externalResponse.Headers)
        {
            Headers.AddOrUpdate(header.Key, header.Value.FirstOrDefault());
        }

        SetBodyStream(externalResponse.Content.ReadAsStream(), mimeType); // Trial Run

        // SetBodyData(await externalResponse.Content.ReadAsByteArrayAsync(), mimeType);
    }

    // Hop-by-hop headers must not be forwarded to the upstream; Host is set by HttpClient from the target URL
    private static bool IsHopByHopHeader(string name)
    {
        return name.Equals("Host", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
            || name.Equals("TE", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
    }
}