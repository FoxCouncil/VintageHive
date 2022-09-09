using System.Text;
using VintageHive.Network;
using VintageHive.Proxy.Http;

namespace VintageHive.Proxy.Ftp;

public sealed class FtpRequest : Request
{
    internal static FtpRequest ParseFtpOverHttp(ListenerSocket socket, Encoding encoding, byte[] rawBytes)
    {
        if (socket == null || rawBytes == null)
        {
            return (FtpRequest)Invalid;
        }

        var rawRequest = encoding.GetString(rawBytes);

        if (!rawRequest.Contains(HttpUtilities.HttpSeperator) || !rawRequest.Contains(HttpUtilities.HttpBodySeperator))
        {
            return (FtpRequest)Invalid;
        }

        var rawHeaders = rawRequest[..rawRequest.IndexOf(HttpUtilities.HttpBodySeperator)];

        var parsedRequestArray = rawHeaders.Trim().Split("\r\n");

        var httpRequestLine = parsedRequestArray[0].Split(" ");

        if (httpRequestLine.Length != 3 || !HttpUtilities.HttpVerbs.Contains(httpRequestLine[0]) || !HttpUtilities.HttpVersions.Contains(httpRequestLine[2]))
        {
            return (FtpRequest)Invalid;
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

        var newRequest = new FtpRequest
        {
            Type = httpRequestLine[0],
            Uri = new Uri(uri),
            Version = httpRequestLine[2],
            Headers = headers,
            ListenerSocket = socket,
            Encoding = encoding,
            IsValid = true
        };

        return newRequest;
    }
}
