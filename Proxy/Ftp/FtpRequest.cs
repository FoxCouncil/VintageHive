// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using AngleSharp.Text;
using VintageHive.Network;
using VintageHive.Proxy.Http;

namespace VintageHive.Proxy.Ftp;

public sealed class FtpRequest : Request
{
    public static readonly FtpRequest Invalid = new() { IsValid = false };

    public FtpRequestConnectionType ConnectionType { get; private set; } = FtpRequestConnectionType.Unknown;

    public string InitialCommand => Type;

    internal async Task FetchUsernameAndPassword(string user, bool isProxyAuth = false)
    {
        await SendResponse(FtpResponseCode.UsernameOkay, $"Password required for {user}");

        var passwordResponse = await FetchCommand();

        if (passwordResponse.Item1 != "PASS")
        {
            throw new InvalidOperationException("client returned garbage");
        }

        if (isProxyAuth)
        {
            ProxyUsername = user;
            ProxyPassword = passwordResponse.Item2;
        }
        else
        {
            Username = user;
            Password = passwordResponse.Item2;
        }
    }

    internal async Task SendInvalidUsernameOrPassword()
    {
        await SendResponse(FtpResponseCode.InvalidUsernameOrPassword, "Invalid username or password");
    }

    internal async Task SendUserLoggedIn()
    {
        await SendResponse(FtpResponseCode.UserLoggedIn, "User logged in, proceed");
    }

    internal async Task<Tuple<string, string>> FetchCommand()
    {
        var rawResponse = await ReadRawResponseAsync();

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return null;
        }

        return ParseCommandLine(rawResponse);
    }

    // Split a raw FTP control line into (command, arguments). The verb is upper-cased (RFC 959 4.1
    // commands are case-insensitive); everything after it is rejoined as the argument.
    internal static Tuple<string, string> ParseCommandLine(string rawResponse)
    {
        var parsedResponse = rawResponse.SplitSpaces();

        if (parsedResponse == null || parsedResponse.Length == 0)
        {
            throw new InvalidOperationException("client returned garbage");
        }

        var command = parsedResponse[0].ToUpper();
        var arguments = parsedResponse.Length >= 2 ? string.Join(' ', parsedResponse[1..]) : string.Empty;

        return new Tuple<string, string>(command, arguments);
    }

    // RFC 959 4.2: a single-line reply is the 3-digit code, a space, then text, terminated by CRLF.
    internal static string FormatResponse(FtpResponseCode command, string args)
    {
        return $"{(int)command} {SanitizeLine(args)}\r\n";
    }

    // RFC 2389 FEAT: a multi-line reply - "<code>-Features", each feature indented by a space, then
    // "<code> End".
    internal static string FormatFeatureList(FtpResponseCode command, string[] args)
    {
        var cmd = $"{(int)command}-Features\r\n";

        foreach (var arg in args)
        {
            cmd += $" {SanitizeLine(arg)}\r\n";
        }

        cmd += $"{(int)command} End\r\n";

        return cmd;
    }

    // Strip embedded CR/LF so a value (e.g. an echoed path or feature string, which may legally contain
    // LF on POSIX) cannot forge an extra control-channel response line. A null value renders as empty.
    private static string SanitizeLine(string text)
    {
        return text is null ? string.Empty : text.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    internal async Task SendResponse(FtpResponseCode command, string args)
    {
        await SendRawResponse(FormatResponse(command, args));
    }

    internal async Task SendFeatureListResponse(FtpResponseCode command, string[] args)
    {
        await SendRawResponse(FormatFeatureList(command, args));
    }

    internal static async Task<FtpRequest> Parse(ListenerSocket socket, Encoding encoding, byte[] rawBytes, int read)
    {
        if (socket == null || rawBytes == null)
        {
            return Invalid;
        }

        var rawRequest = encoding.GetString(rawBytes, 0, read);

        if (!rawRequest.Contains(HttpUtilities.HttpSeperator))
        {
            return Invalid;
        }

        if (rawRequest[..3].ToUpper() == "GET" && rawRequest.Contains(HttpUtilities.HttpBodySeperator))
        {
            return ParseFtpOverHttp(socket, encoding, rawRequest);
        }

        if (!rawRequest.Contains(' '))
        {
            // Malformed FTP command
            return Invalid;
        }

        var rawFtpData = rawRequest.SplitSpaces();

        var initialFtpCommand = rawFtpData[0].ToUpper();

        var newRequest = new FtpRequest
        {
            Type = "RAWFTP",
            ConnectionType = FtpRequestConnectionType.Raw,
            Version = "FTP",
            ListenerSocket = socket,
            Encoding = encoding,
            IsValid = true
        };

        Tuple<string, string> proxyRequest;

        if (initialFtpCommand == FtpCommand.Open)
        {
            proxyRequest = new Tuple<string, string>(initialFtpCommand, rawFtpData[1]);
        }
        else if (initialFtpCommand == FtpCommand.AuthenticationUsername)
        {
            await newRequest.FetchUsernameAndPassword(rawFtpData[1], true);

            // Proxy-level authentication is not enforced; the target server's own credentials still apply downstream.
            await newRequest.SendUserLoggedIn();

            proxyRequest = await newRequest.FetchCommand();
        }
        else
        {
            return Invalid;
        }

        var url = proxyRequest.Item2;

        if (!url.Contains('.'))
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(FtpRequest), "FTPProxy; transparent proxy is not supported!", socket.TraceId.ToString());

            return Invalid;
        }

        if (!url.StartsWith("ftp://"))
        {
            url = $"ftp://{proxyRequest.Item2}";
        }

        newRequest.Uri = new Uri(url);

        await newRequest.SendResponse(FtpResponseCode.ServiceReadyForNewUser, $"{newRequest.Uri.Host}'s {Mind.ProductName} FTP Server");

        proxyRequest = await newRequest.FetchCommand();

        await newRequest.FetchUsernameAndPassword(proxyRequest.Item2, false);

        return newRequest;
    }

    private static FtpRequest ParseFtpOverHttp(ListenerSocket socket, Encoding encoding, string rawRequest)
    {
        var rawHeaders = rawRequest[..rawRequest.IndexOf(HttpUtilities.HttpBodySeperator)];

        var parsedRequestArray = rawHeaders.Trim().Split("\r\n");

        var httpRequestLine = parsedRequestArray[0].Split(" ");

        if (httpRequestLine.Length != 3 || !HttpUtilities.HttpVerbs.Contains(httpRequestLine[0]) || !HttpUtilities.HttpVersions.Contains(httpRequestLine[2]))
        {
            return Invalid;
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
            ConnectionType = FtpRequestConnectionType.OverHttp,
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
