using System.Text;
using System.Web;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Proxy.Http;

public sealed class HttpRequest : Request
{
    public IReadOnlyDictionary<string, string>? QueryParams { get; private set; }

    public string Body { get; private set; } = "";

    public bool IsRelativeUri(string uri)
    {
        if (uri == null || uri.Length == 0)
        {
            return false;
        }

        return Uri.AbsolutePath.ToLower().Equals(uri);
    }

    internal static HttpRequest Parse(ListenerSocket socket, Encoding encoding, byte[] rawBytes)
    {
        if (socket == null || rawBytes == null)
        {
            return (HttpRequest)Invalid;
        }

        var rawRequest = encoding.GetString(rawBytes);

        if (!rawRequest.Contains("\r\n") || !rawRequest.Contains("\r\n\r\n"))
        {
            return (HttpRequest)Invalid;
        }

        var rawHeaders = rawRequest[..rawRequest.IndexOf(HttpBodySeperator)];

        var rawBody = rawRequest[(rawRequest.IndexOf(HttpBodySeperator) + HttpBodySeperator.Length)..].Trim().Replace("\0", string.Empty);

        var parsedRequestArray = rawHeaders.Trim().Split("\r\n");

        var httpRequestLine = parsedRequestArray[0].Split(" ");

        if (httpRequestLine.Length != 3 || !HttpVerbs.Contains(httpRequestLine[0]) || !HttpVersions.Contains(httpRequestLine[2]))
        {
            return (HttpRequest)Invalid;
        }

        var headers = new Dictionary<string, string>();

        foreach (var header in parsedRequestArray.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var splitHeaderKV = header.Split(": ", 2);

            if (!headers.ContainsKey(splitHeaderKV[0]))
            {
                headers.Add(splitHeaderKV[0], splitHeaderKV[1]);
            }
        }

        var uri = httpRequestLine[1];

        Dictionary<string, string> queryParams;

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

        if (!uri.StartsWith("http") && headers.ContainsKey(HttpHeaderName.Host))
        {
            uri = (socket.IsSecure ? "https" : "http") + "://" + headers[HttpHeaderName.Host] + uri;
        }

        var newRequest = new HttpRequest
        {
            Type = httpRequestLine[0],
            Uri = new Uri(uri),
            Version = httpRequestLine[2],
            Headers = headers,
            QueryParams = queryParams,
            Body = rawBody,
            ListenerSocket = socket,            
            Encoding = encoding
        };

        return newRequest;
    }
}
