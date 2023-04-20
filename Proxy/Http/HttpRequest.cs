// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Web;
using VintageHive.Network;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Proxy.Http;

public sealed partial class HttpRequest : Request
{
    public static readonly HttpRequest Invalid = new() { IsValid = false };

    public IReadOnlyDictionary<string, string>? QueryParams { get; private set; }

    public IReadOnlyDictionary<string, string>? FormData { get; private set; }

    public ReadOnlyDictionary<string, string> Cookies { get; private set; }

    public string Raw { get; private set; }

    public string Host => Uri.Host.ToLower();

    public string Body { get; private set; } = "";

    public string UserAgent => Headers[HttpHeaderName.UserAgent] ?? "NA";

    public bool IsRelativeUri(string uri)
    {
        if (uri == null || uri.Length == 0)
        {
            return false;
        }

        return Uri.AbsolutePath.ToLower().Equals(uri);
    }

    public static HttpRequest Parse(string rawData, Encoding encoding, ListenerSocket listenerSocket = null)
    {
        var bodyPointer = rawData.IndexOf(HttpBodySeperator);

        var rawHeaders = bodyPointer == -1 ? rawData : rawData[..bodyPointer];

        var rawBody = bodyPointer == -1 ? string.Empty : rawData[(rawData.IndexOf(HttpBodySeperator) + HttpBodySeperator.Length)..].Trim().Replace("\0", string.Empty);

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

            if (!headers.ContainsKey(splitHeaderKV[0]))
            {
                headers.Add(splitHeaderKV[0], splitHeaderKV[1].Trim());
            }
        }

        var requestCookies = new Dictionary<string, string>();

        if (headers.TryGetValue(HttpHeaderName.Cookie, out string value))
        {
            var cookies = value.Split("; ");

            foreach (var cookie in cookies)
            {
                var cookieData = cookie.Split("=", 2);

                requestCookies.Add(cookieData[0], cookieData[1]);
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
                case HttpContentType.Application.XWwwFormUrlEncoded:
                {
                    formData = new Dictionary<string, string>(HttpUtility.ParseQueryString(rawBody).ToDictionary(), StringComparer.InvariantCultureIgnoreCase);
                }
                break;

                case HttpContentType.Multipart.FormData:
                {
                    var baseBoundryMarker = contentType[(contentType.IndexOf(';') + 11)..];
                    var startBoundryMarker = "--" + baseBoundryMarker;
                    var endBoundryMarker = startBoundryMarker + "--";

                    var boundries = rawBody.Split(endBoundryMarker, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    boundries = boundries.Select(b => b.Replace(startBoundryMarker, string.Empty).Trim()).ToArray();

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

        var qpIndex = uri.IndexOf("?");

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

        return new HttpRequest
        {
            IsValid = true,
            Raw = rawData,
            Encoding = encoding,
            Type = httpRequestLine[0],
            Uri = new Uri(uri),
            Version = httpRequestLine[2],
            Headers = headers,
            Cookies = new ReadOnlyDictionary<string, string>(requestCookies),
            QueryParams = queryParams,
            FormData = formData,
            Body = rawBody,
            ListenerSocket = listenerSocket
        };
    }

    internal static async Task<HttpRequest> Build(ListenerSocket socket, Encoding encoding, byte[] rawBytes)
    {
        if (socket == null || rawBytes == null)
        {
            return Invalid;
        }

        var rawRequest = encoding.GetString(rawBytes);

        if (!rawRequest.Contains("\r\n"))
        {
            return Invalid;
        }

        if (!rawRequest.Contains("\r\n\r\n"))
        {
            var buffer = new byte[4096];
            var read = await socket.Stream.ReadAsync(buffer);

            var extraData = encoding.GetString(buffer[..read]);

            rawRequest += extraData;
        }

        var newRequest = Parse(rawRequest, encoding, socket);

        return newRequest;
    }

    [GeneratedRegex("name=\"(.*?)\"")]
    private static partial Regex HttpContentBoundryRegex();
}
