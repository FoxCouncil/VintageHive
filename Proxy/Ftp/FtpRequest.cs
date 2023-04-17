using AngleSharp.Text;
using System.Net.Sockets;
using System.Net;
using System.Text;
using VintageHive.Network;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;
using System.Net.NetworkInformation;

namespace VintageHive.Proxy.Ftp;

public sealed class FtpRequest : Request
{
    const int PORT_RANGE_MIN = 1900;

    const int PORT_RANGE_MAX = 1910;

    public static readonly FtpRequest Invalid = new() { IsValid = false };

    public bool IsActive { get; private set; } = true;

    public string TransferType { get; set; } = FtpTransferType.ASCII;

    public FtpRequestConnectionType ConnectionType { get; private set; } = FtpRequestConnectionType.Unknown;

    public string InitialCommand => Type;

    TcpListener dataListener;

    TcpClient dataClient;

    string basePath;

    string currentPath = "/";

    string renamePath;

    long resumeOffset = long.MinValue;

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

    internal void SetupServer(string path)
    {
        basePath = path;
        dataListener = new TcpListener(IPAddress.Parse(ListenerSocket.LocalIP), GetAvailablePortInRange(PORT_RANGE_MIN, PORT_RANGE_MAX));
        dataClient = null;
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

        var parsedResponse = rawResponse.SplitSpaces();

        if (parsedResponse == null || parsedResponse.Length == 0)
        {
            throw new InvalidOperationException("client returned garbage");
        }

        var command = parsedResponse[0].ToUpper();
        var arguments = parsedResponse.Length >= 2 ? string.Join(' ', parsedResponse[1..]) : string.Empty;

        switch (command)
        {
            case FtpCommand.SystemType:
            {
                await SendResponse(FtpResponseCode.NameSystemType, "UNIX");
            }
            break;

            case FtpCommand.FeatureList:
            {
                await SendFeatureListResponse(FtpResponseCode.SystemStatus, new[] { "SIZE" });
            }
            break;

            case FtpCommand.PrintWorkingDirectory:
            {
                await SendResponse(FtpResponseCode.PathnameCreated, $"\"{currentPath}\" is the current directory");
            }
            break;

            case FtpCommand.ChangeWorkingDirectory:
            {
                var relPath = arguments.StartsWith("/") ? arguments : Path.Combine(currentPath, arguments);

                if (string.IsNullOrWhiteSpace(relPath))
                {
                    relPath = "/";
                }

                var path = Path.Combine(basePath, PathGetReal(relPath));

                if (VFS.DirectoryExists(path))
                {
                    currentPath = relPath;

                    await SendResponse(FtpResponseCode.RequestedFileActionOkay, "CWD command successful");
                }
                else
                {
                    await SendResponse(FtpResponseCode.RequestedActionNotTaken, "Directory not found");
                }
            }
            break;

            case FtpCommand.MakeDirectory:
            {
                var path = Path.Combine(basePath, PathGetReal(currentPath), PathGetReal(arguments));

                if (!VFS.DirectoryExists(path))
                {
                    VFS.DirectoryCreate(path);

                    await SendResponse(FtpResponseCode.PathnameCreated, $"\"{arguments}\" directory created");
                }
                else
                {
                    await SendResponse(FtpResponseCode.RequestedActionNotTaken, "Failed to create directory: already exists");
                }
            }
            break;

            case FtpCommand.DeleteFile:
            {
                var path = Path.Combine(basePath, PathGetReal(arguments));

                if (VFS.FileExists(path))
                {
                    try
                    {
                        VFS.FileDelete(path);

                        await SendResponse(FtpResponseCode.RequestedFileActionOkay, $"{arguments} file deleted");
                    }
                    catch (Exception ex)
                    {
                        await SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to retrieve file: {ex.Message}");
                    }
                }
                else
                {
                    await SendResponse(FtpResponseCode.RequestedActionNotTaken, "File Not Found");
                }
            }
            break;

            case FtpCommand.RenameFrom:
            {
                var path = Path.Combine(basePath, PathGetReal(arguments));

                if (VFS.FileExists(path) || VFS.DirectoryExists(path))
                {
                    renamePath = path;

                    await SendResponse(FtpResponseCode.RequestedFileActionPendingMore, "File exists, ready for destination name");
                }
                else
                {
                    await SendResponse(FtpResponseCode.RequestedActionNotTaken, "File does not exist");
                }
            }
            break;

            case FtpCommand.RenameTo:
            {
                if (string.IsNullOrEmpty(renamePath))
                {
                    await SendResponse(FtpResponseCode.BadSequenceOfCommands, "Bad sequence of commands: RNFR must be issued first");

                    break;
                }

                var path = Path.Combine(basePath, PathGetReal(arguments));

                try
                {
                    if (VFS.FileExists(renamePath))
                    {
                        VFS.FileMove(renamePath, path);

                        await SendResponse(FtpResponseCode.RequestedFileActionOkay, $"File renamed from {renamePath.Replace(basePath, string.Empty)} to {path.Replace(basePath, string.Empty)}");
                    }
                    else if (VFS.DirectoryExists(renamePath))
                    {
                        VFS.DirectoryMove(renamePath, path);

                        await SendResponse(FtpResponseCode.RequestedFileActionOkay, $"Directory renamed from {renamePath.Replace(basePath, string.Empty)} to {path.Replace(basePath, string.Empty)}");
                    }
                    else
                    {
                        await SendResponse(FtpResponseCode.RequestedActionNotTaken, $"File or directory does not exist");
                    }
                }
                catch (Exception ex)
                {
                    await SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to rename file or directory: {ex.Message}");
                }
                finally
                {
                    renamePath = string.Empty;
                }
            }
            break;

            case FtpCommand.TransferMode:
            {
                TransferType = arguments;

                await SendResponse(FtpResponseCode.CommandSuccess, $"Type set to {FtpTransferType.NameFromType(arguments)}");
            }
            break;

            case FtpCommand.PassiveMode:
            {
                dataListener.Start();

                IPEndPoint endpoint = (IPEndPoint)dataListener.LocalEndpoint;

                var address = endpoint.Address.GetAddressBytes();
                var port = endpoint.Port;

                resumeOffset = 0;

                await SendResponse(FtpResponseCode.EnteringPassiveMode, $"Entering Passive Mode ({address[0]},{address[1]},{address[2]},{address[3]},{port >> 8},{port & 0xff})");
            }
            break;

            case FtpCommand.ListInfo:
            {
                await SendResponse(FtpResponseCode.FileStatusOkay, $"Opening {FtpTransferType.NameFromType(TransferType)} mode data connection for file list");

                dataClient = await dataListener.AcceptTcpClientAsync();  

                using var dataStream = dataClient.GetStream();
                using var dataWriter = new StreamWriter(dataStream);

                var path = Path.Combine(basePath, PathGetReal(currentPath));

                var directoryList = VFS.DirectoryList(path);

                foreach (var directoryItem in directoryList)
                {
                    if (Directory.Exists(directoryItem))
                    {
                        var info = new DirectoryInfo(directoryItem);

                        string permissions = "drwxrwxrwx";

                        string owner = "100";
                        string group = "100";
                        string size = "0";
                        string date = info.LastWriteTime.ToString("MMM dd HH:mm");
                        string name = info.Name;

                        string line = string.Format("{0} {1} {2,8} {3,9} {4,12} {5} {6}", permissions, 1, owner, group, size, date, name);

                        dataWriter.WriteLine(line);
                    }
                    else if (File.Exists(directoryItem))
                    {
                        var info = new FileInfo(directoryItem);

                        string permissions = "-rw-rw-rw-";

                        string owner = "100";
                        string group = "100";
                        string size = info.Length.ToString();
                        string date = info.LastWriteTime.ToString("MMM dd HH:mm");
                        string name = info.Name;

                        string line = string.Format("{0} {1} {2,8} {3,9} {4,12} {5} {6}", permissions, 1, owner, group, size, date, name);

                        dataWriter.WriteLine(line);
                    }
                    else
                    {
                        throw new ApplicationException("What?");
                    }
                }

                dataClient.Close();
                dataListener.Stop();

                await SendResponse(FtpResponseCode.DataConnectionClosing, "Transfer complete");
            }
            break;

            case FtpCommand.RestartTransfer:
            {
                if (long.TryParse(arguments, out var result))
                {
                    if (result == 0)
                    {
                        await SendResponse(FtpResponseCode.RequestedFileActionPendingMore, "Clearing transfer restart offset");
                    }
                    else
                    {
                        resumeOffset = result;

                        await SendResponse(FtpResponseCode.RequestedFileActionPendingMore, $"Restarting transfer at {resumeOffset}");
                    }

                }
                else
                {
                    await SendResponse(FtpResponseCode.SyntaxError, $"Syntax error in parameters or arguments for REST");
                }
            }
            break;

            case FtpCommand.RetrieveFile:
            {
                await SendResponse(FtpResponseCode.FileStatusOkay, $"Opening {FtpTransferType.NameFromType(TransferType)} mode data connection for file transfer");

                dataClient = await dataListener.AcceptTcpClientAsync();

                using var dataStream = dataClient.GetStream();

                var path = Path.Combine(basePath, PathGetReal(currentPath), arguments);

                if (VFS.FileExists(path))
                {
                    using var fileStream = VFS.FileReadStream(path);

                    fileStream.Position = resumeOffset;

                    try
                    {
                        await fileStream.CopyToAsync(dataStream);
                    }
                    catch (Exception ex)
                    {
                        await SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to retrieve file: {ex.Message}");

                        dataClient.Close();
                        dataListener.Stop();

                        break;
                    }
                }
                else
                {
                    await SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to retrieve file: File Does Not Exist");

                    dataClient.Close();
                    dataListener.Stop();

                    break;
                }

                dataClient.Close();
                dataListener.Stop();

                await SendResponse(FtpResponseCode.DataConnectionClosing, "Transfer complete");
            }
            break;

            case FtpCommand.StoreFile:
            {
                await SendResponse(FtpResponseCode.FileStatusOkay, $"Opening {FtpTransferType.NameFromType(TransferType)} mode data connection for file storage");

                dataClient = await dataListener.AcceptTcpClientAsync();

                using var dataStream = dataClient.GetStream();

                var path = Path.Combine(basePath, PathGetReal(currentPath), arguments);

                try
                {
                    using var fileStream = VFS.FileWrite(path);

                    await dataStream.CopyToAsync(fileStream);
                }
                catch (Exception ex)
                {
                    await SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to retrieve file: {ex.Message}");

                    dataClient.Close();
                    dataListener.Stop();

                    break;
                }

                dataClient.Close();
                dataListener.Stop();

                await SendResponse(FtpResponseCode.DataConnectionClosing, "Transfer complete");
            }
            break;

            case FtpCommand.Quit:
            {
                await SendResponse(FtpResponseCode.ServerClosingControlConnection, "Goodbye");

                IsActive = false;
            }
            break;

            case FtpCommand.Bark:
            {
                await SendResponse(FtpResponseCode.CommandSuccess, "Bark! Bark!");
            }
            break;

            default:
            {
                return new Tuple<string, string>(command, arguments);
            }
        }

        return null;
    }

    internal void StopServer()
    {
        if (dataClient != null && dataClient.Connected)
        {
            dataClient.Close();
        }

        dataListener?.Stop();
    }

    internal async Task SendResponse(FtpResponseCode command, string args)
    {
        var cmd = $"{(int)command} {args}\r\n";

        await SendRawResponse(cmd);
    }

    internal async Task SendFeatureListResponse(FtpResponseCode command, string[] args)
    {
        var cmd = $"{(int)command}-Features\r\n";
        
        foreach (var arg in args)
        {
            cmd += $" {arg}\r\n";
        }
        
        cmd += $"{(int)command} End\r\n";

        await SendRawResponse(cmd);
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

            if (newRequest.ProxyPassword != "PENIS")
            {
                await newRequest.SendInvalidUsernameOrPassword();

                return Invalid;
            }

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

        await newRequest.SendResponse(FtpResponseCode.ServiceReadyForNewUser, $"{newRequest.Uri.Host}'s VintageHive FTP Server");

        proxyRequest = await newRequest.FetchCommand();

        await newRequest.FetchUsernameAndPassword(proxyRequest.Item2, false);

        return newRequest;
    }

    static string PathGetReal(string virtualPath)
    {
        return virtualPath.TrimStart('/').Replace('/', '\\');
    }

    public static int GetAvailablePortInRange(int minPort, int maxPort)
    {
        if (minPort < 0 || maxPort > ushort.MaxValue || minPort > maxPort)
        {
            throw new ArgumentException("Invalid port range specified");
        }

        var properties = IPGlobalProperties.GetIPGlobalProperties();

        // Collecting all used ports
        var usedPorts = new HashSet<int>(properties.GetActiveTcpConnections()
                                          .Select(c => c.LocalEndPoint.Port)
                                          .Concat(properties.GetActiveTcpListeners().Select(l => l.Port))
                                          .Concat(properties.GetActiveUdpListeners().Select(u => u.Port)));

        // Finding the first available port within the specified range
        for (int port = minPort; port <= maxPort; port++)
        {
            if (!usedPorts.Contains(port))
            {
                return port;
            }
        }

        // No available ports found within the specified range
        return -1;
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
