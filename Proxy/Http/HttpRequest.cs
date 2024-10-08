﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

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

    public string UserAgent => Headers[HttpHeaderName.UserAgent] ?? "NA";

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

        var rawBodyData = rawRequest[(rawRequest.IndexOf(HttpBodySeperator) + HttpBodySeperator.Length)..];

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

        return new HttpRequest
        {
            IsValid = true,
            Raw = rawRequest,
            Encoding = encoding,
            Type = httpRequestLine[0],
            Uri = new Uri(uri),
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

    internal static async Task<HttpRequest> Build(ListenerSocket socket, Encoding encoding, byte[] rawBytes)
    {
        if (socket == null || rawBytes == null)
        {
            return Invalid;
        }

        var rawRequest = encoding.GetString(rawBytes);

        if (!rawRequest.Contains(HttpNewLine))
        {
            return Invalid;
        }

        if (!rawRequest.StartsWith("GET"))
        {
            var contentLengthMatch = HttpContentLengthParseRegex().Match(rawRequest);

            if (contentLengthMatch.Success)
            {
                var expectedLength = contentLengthMatch.Success ? Convert.ToInt32(contentLengthMatch.Groups[1].Value) : 0;
                var contentLength = rawRequest[(rawRequest.IndexOf(HttpBodySeperator) + HttpBodySeperator.Length)..].Length;

                while (expectedLength > contentLength)
                {
                    var buffer = new byte[4096];
                    var read = await socket.Stream.ReadAsync(buffer);

                    var extraData = encoding.GetString(buffer[..read]);

                    contentLength += read;
                    rawRequest += extraData;
                }
            }
        }

        var newRequest = Parse(rawBytes, rawRequest, encoding, socket);

        return newRequest;
    }

    [GeneratedRegex("name=\"(.*?)\"")]
    private static partial Regex HttpContentBoundryRegex();

    [GeneratedRegex("Content\\-Length\\:\\s(\\d+)\r\n")]
    private static partial Regex HttpContentLengthParseRegex();
}
