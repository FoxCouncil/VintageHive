// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using Fluid.Ast;
using Fluid.Values;
using HeyRed.Mime;
using Humanizer;
using System.Net.Sockets;
using System.Web;
using VintageHive.Processors.LocalServer;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using HttpStatusCode = VintageHive.Proxy.Http.HttpStatusCode;

namespace VintageHive.Processors;

internal static class LocalServerProcessor
{
    static readonly FluidParser fluidParser = new();

    static LocalServerProcessor()
    {
        TemplateOptions.Default.MemberAccessStrategy = new UnsafeMemberAccessStrategy();
        TemplateOptions.Default.Filters.AddFilter("bytes", (input, arguments, context) =>
        {
            return StringValue.Create(((long)input.ToNumberValue()).Bytes().Humanize());
        });
        TemplateOptions.Default.Filters.AddFilter("urlencode", (input, arguments, context) =>
        {
            return StringValue.Create(HttpUtility.UrlEncode(input.ToStringValue()));
        });
        TemplateOptions.Default.Filters.AddFilter("wmotostring", (input, arguments, context) =>
        {
            return StringValue.Create(WeatherUtils.ConvertWmoCodeToString((int)input.ToNumberValue()));
        });

        TemplateOptions.Default.Filters.AddFilter("pathlinksplit", (input, arguments, context) =>
        {
            var path = input.ToStringValue();

            if (path == "/")
            {
                return input;
            }

            dynamic model = context.Model.ToObjectValue();
            var repo = model.Request.QueryParams["repo"];

            var pathParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (pathParts.Length > 1)
            {
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    pathParts[i] = $"<a href=\"download.html?repo={HttpUtility.UrlEncode(repo)}&path={HttpUtility.UrlEncode(path[..(path.IndexOf(pathParts[i]) + pathParts[i].Length)] + "/")}\">{pathParts[i]}</a>";
                }
            }

            return StringValue.Create("/" + string.Join('/', pathParts) + "/");
        });

        fluidParser.RegisterEmptyTag("displayMessage", static async (writer, encoder, context) =>
        {
            var response = (await context.Model.GetValueAsync("Response", context)).ToObjectValue() as HttpResponse;

            if (response.HasSession("error"))
            {
                writer.Write($"<div class=\"alert alert-danger alert-dismissible fade show\" role=\"alert\">");
                writer.Write($"{response.Session.error}");
                writer.Write($"<button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"alert\" aria-label=\"Close\"></button>");
                writer.Write($"</div>");

                response.RemoveSession("error");
            }

            return Completion.Normal;
        });
    }

    public static async Task<bool> ProcessFtpRequest(FtpRequest req)
    {
        if (req.Uri.Host.StartsWith("ftp."))
        {
            // let's play ISP
            var hostDomain = req.Uri.Host.Replace("ftp.", string.Empty).ToLower();

            if (string.IsNullOrWhiteSpace(hostDomain))
            {
                return false;
            }

            var hostPath = Path.Combine("hosting", hostDomain);

            if (!VFS.DirectoryExists(hostPath))
            {
                VFS.DirectoryCreate(hostPath);
            }

            // Todo; authentication
            await req.SendUserLoggedIn();

            var isActive = true;
            var transferType = FtpTransferType.ASCII;
            var dataListener = new TcpListener(IPAddress.Parse(req.ListenerSocket.LocalIP), 0);

            TcpClient dataClient = null;

            var basePath = hostPath;
            var currentPath = "/";
            var resumeOffset = long.MinValue;
            var renamePath = "";

            while (isActive)
            {
                var payload = await req.FetchCommand();

                if (payload == null)
                {
                    break;
                }

                var command = payload.Item1.ToUpper();
                var args = payload.Item2;

                switch (command)
                {
                    case FtpCommand.SystemType:
                    {
                        await req.SendResponse(FtpResponseCode.NameSystemType, "UNIX");
                    }
                    break;

                    case FtpCommand.FeatureList:
                    {
                        await req.SendFeatureListResponse(FtpResponseCode.SystemStatus, new[] { "SIZE" });
                    }
                    break;

                    case FtpCommand.PrintWorkingDirectory:
                    {
                        await req.SendResponse(FtpResponseCode.PathnameCreated, $"\"{currentPath}\" is the current directory");
                    }
                    break;

                    case FtpCommand.ChangeWorkingDirectory:
                    {
                        var relPath = args.StartsWith("/") ? args : Path.Combine(currentPath, args);

                        if (string.IsNullOrWhiteSpace(relPath))
                        {
                            relPath = "/";
                        }

                        var path = Path.Combine(basePath, PathGetReal(relPath));

                        if (VFS.DirectoryExists(path))
                        {
                            currentPath = relPath;

                            await req.SendResponse(FtpResponseCode.RequestedFileActionOkay, "CWD command successful");
                        }
                        else
                        {
                            await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, "Directory not found");
                        }
                    }
                    break;

                    case FtpCommand.MakeDirectory:
                    {
                        var path = Path.Combine(basePath, PathGetReal(currentPath), PathGetReal(args));

                        if (!VFS.DirectoryExists(path))
                        {
                            VFS.DirectoryCreate(path);

                            await req.SendResponse(FtpResponseCode.PathnameCreated, $"\"{args}\" directory created");
                        }
                        else
                        {
                            await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, "Failed to create directory: already exists");
                        }
                    }
                    break;

                    case FtpCommand.DeleteDirectory:
                    {
                        var path = Path.Combine(basePath, PathGetReal(args));

                        if (VFS.DirectoryExists(path))
                        {
                            try
                            {
                                VFS.DirectoryDelete(path);

                                await req.SendResponse(FtpResponseCode.RequestedFileActionOkay, $"\"{args}\" directory deleted");
                            }
                            catch (Exception e)
                            {
                                await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to deleted directory: {e.Message}");

                                Log.WriteLine(Log.LEVEL_ERROR, nameof(LocalServerProcessor), e.Message, req.ListenerSocket.TraceId.ToString());
                            }
                        }
                        else
                        {
                            await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to deleted directory: \"{args}\" doesn't exists");
                        }
                    }
                    break;

                    case FtpCommand.DeleteFile:
                    {
                        var path = Path.Combine(basePath, PathGetReal(args));

                        if (VFS.FileExists(path))
                        {
                            try
                            {
                                VFS.FileDelete(path);

                                await req.SendResponse(FtpResponseCode.RequestedFileActionOkay, $"{args} file deleted");
                            }
                            catch (Exception ex)
                            {
                                await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to retrieve file: {ex.Message}");
                            }
                        }
                        else
                        {
                            await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, "File Not Found");
                        }
                    }
                    break;

                    case FtpCommand.RenameFrom:
                    {
                        var path = Path.Combine(basePath, PathGetReal(args));

                        if (VFS.FileExists(path) || VFS.DirectoryExists(path))
                        {
                            renamePath = path;

                            await req.SendResponse(FtpResponseCode.RequestedFileActionPendingMore, "File exists, ready for destination name");
                        }
                        else
                        {
                            await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, "File does not exist");
                        }
                    }
                    break;

                    case FtpCommand.RenameTo:
                    {
                        if (string.IsNullOrEmpty(renamePath))
                        {
                            await req.SendResponse(FtpResponseCode.BadSequenceOfCommands, "Bad sequence of commands: RNFR must be issued first");

                            break;
                        }

                        var path = Path.Combine(basePath, PathGetReal(args));

                        try
                        {
                            if (VFS.FileExists(renamePath))
                            {
                                VFS.FileMove(renamePath, path);

                                await req.SendResponse(FtpResponseCode.RequestedFileActionOkay, $"File renamed from {renamePath.Replace(basePath, string.Empty)} to {path.Replace(basePath, string.Empty)}");
                            }
                            else if (VFS.DirectoryExists(renamePath))
                            {
                                VFS.DirectoryMove(renamePath, path);

                                await req.SendResponse(FtpResponseCode.RequestedFileActionOkay, $"Directory renamed from {renamePath.Replace(basePath, string.Empty)} to {path.Replace(basePath, string.Empty)}");
                            }
                            else
                            {
                                await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, $"File or directory does not exist");
                            }
                        }
                        catch (Exception ex)
                        {
                            await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to rename file or directory: {ex.Message}");
                        }
                        finally
                        {
                            renamePath = string.Empty;
                        }
                    }
                    break;

                    case FtpCommand.TransferMode:
                    {
                        transferType = args;

                        await req.SendResponse(FtpResponseCode.CommandSuccess, $"Type set to {FtpTransferType.NameFromType(args)}");
                    }
                    break;

                    case FtpCommand.PassiveMode:
                    {
                        dataListener.Start();

                        IPEndPoint endpoint = (IPEndPoint)dataListener.LocalEndpoint;

                        var address = endpoint.Address.GetAddressBytes();
                        var port = endpoint.Port;

                        resumeOffset = 0;

                        await req.SendResponse(FtpResponseCode.EnteringPassiveMode, $"Entering Passive Mode ({address[0]},{address[1]},{address[2]},{address[3]},{port >> 8},{port & 0xff})");
                    }
                    break;

                    case FtpCommand.ListInfo:
                    {
                        await req.SendResponse(FtpResponseCode.FileStatusOkay, $"Opening {FtpTransferType.NameFromType(transferType)} mode data connection for file list");

                        dataClient = dataListener.AcceptTcpClient();

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

                        dataWriter.Close();
                        dataClient.Close();
                        dataListener.Stop();

                        await req.SendResponse(FtpResponseCode.DataConnectionClosing, "Transfer complete");
                    }
                    break;

                    case FtpCommand.RestartTransfer:
                    {
                        if (long.TryParse(args, out var result))
                        {
                            if (result == 0)
                            {
                                await req.SendResponse(FtpResponseCode.RequestedFileActionPendingMore, "Clearing transfer restart offset");
                            }
                            else
                            {
                                resumeOffset = result;

                                await req.SendResponse(FtpResponseCode.RequestedFileActionPendingMore, $"Restarting transfer at {resumeOffset}");
                            }

                        }
                        else
                        {
                            await req.SendResponse(FtpResponseCode.SyntaxError, $"Syntax error in parameters or arguments for REST");
                        }
                    }
                    break;

                    case FtpCommand.RetrieveFile:
                    {
                        await req.SendResponse(FtpResponseCode.FileStatusOkay, $"Opening {FtpTransferType.NameFromType(transferType)} mode data connection for file transfer");

                        dataClient = dataListener.AcceptTcpClient();

                        using var dataStream = dataClient.GetStream();

                        var path = Path.Combine(basePath, PathGetReal(currentPath), args);

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
                                await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to retrieve file: {ex.Message}");

                                dataClient.Close();
                                dataListener.Stop();

                                break;
                            }
                        }
                        else
                        {
                            await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to retrieve file: File Does Not Exist");

                            dataClient.Close();
                            dataListener.Stop();

                            break;
                        }

                        dataClient.Close();
                        dataListener.Stop();

                        await req.SendResponse(FtpResponseCode.DataConnectionClosing, "Transfer complete");
                    }
                    break;

                    case FtpCommand.StoreFile:
                    {
                        await req.SendResponse(FtpResponseCode.FileStatusOkay, $"Opening {FtpTransferType.NameFromType(transferType)} mode data connection for file storage");

                        dataClient = dataListener.AcceptTcpClient();

                        using var dataStream = dataClient.GetStream();

                        var path = Path.Combine(basePath, PathGetReal(currentPath), args);

                        try
                        {
                            using var fileStream = VFS.FileWrite(path);

                            await dataStream.CopyToAsync(fileStream);
                        }
                        catch (Exception ex)
                        {
                            await req.SendResponse(FtpResponseCode.RequestedActionNotTaken, $"Failed to retrieve file: {ex.Message}");

                            dataClient.Close();
                            dataListener.Stop();

                            break;
                        }

                        dataClient.Close();
                        dataListener.Stop();

                        await req.SendResponse(FtpResponseCode.DataConnectionClosing, "Transfer complete");
                    }
                    break;

                    case FtpCommand.Quit:
                    {
                        await req.SendResponse(FtpResponseCode.ServerClosingControlConnection, "Goodbye");

                        isActive = false;
                    }
                    break;

                    case FtpCommand.Bark:
                    {
                        await req.SendResponse(FtpResponseCode.CommandSuccess, "Bark! Bark!");
                    }
                    break;

                    default:
                    {
                        await req.SendResponse(FtpResponseCode.CommandNotImplemented, "Command not implemented");
                    }
                    break;
                }
            }

            if (dataClient != null && dataClient.Connected)
            {
                dataClient.Close();
            }

            dataListener?.Stop();

            return true;
        }

        return false;
    }

    public static async Task<bool> ProcessHttpsRequest(HttpRequest req, HttpResponse res)
    {
        await Task.Delay(0);

        return RunLocalHostedSites(req, res);
    }

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        if (req.Uri.IsLoopback)
        {
            var newRedirectedUri = new UriBuilder(req.Uri)
            {
                Host = "admin.hive.com"
            };

            req.Uri = newRedirectedUri.Uri;
        }
        else if (req.Uri.Host == "admin.hive.com")
        {
            return false;
        }

        var site = ControllerManager.Fetch(req, res);

        if (site != null)
        {
            return await ProcessController(req, res, site);
        }

        return RunLocalHostedSites(req, res);
    }

    private static bool RunLocalHostedSites(HttpRequest req, HttpResponse res)
    {
        var host = req.Uri.Host;

        if (host.StartsWith("www"))
        {
            var vfsFolder = host.Replace("www.", string.Empty);
            var wwwFolder = Path.Combine("hosting", vfsFolder, "www");

            if (VFS.DirectoryExists(wwwFolder))
            {
                var file = req.Uri.AbsolutePath;

                if (file == "/")
                {
                    file = "/index.html";
                }

                var vfsFile = Path.Combine(wwwFolder, PathGetReal(file));

                if (VFS.FileExists(vfsFile))
                {
                    var mimetype = MimeTypesMap.GetMimeType(file);

                    var fileStream = VFS.FileReadStream(vfsFile);

                    res.SetStreamForDownload(fileStream, mimetype);

                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<bool> ProcessController(HttpRequest req, HttpResponse res, Controller site)
    {
        res.Cache = false;

        var fileRequestPath = req.Uri.AbsolutePath;

        if (fileRequestPath == "/")
        {
            fileRequestPath = "/index.html";
        }

        await site.CallMethod(fileRequestPath);

        if (res.Handled)
        {
            return true;
        }

        var requestFilePath = Path.Combine("controllers/", req.Uri.Host+"/", Path.IsPathRooted(fileRequestPath) ? fileRequestPath[1..] : fileRequestPath);
        
        var isReplacedFile = VFS.FileExists(requestFilePath);
        
        var resourceFile = "";

        if (!isReplacedFile)
        {
            resourceFile = requestFilePath.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');

            if (!Resources.Statics.ContainsKey(resourceFile))
            {
                res.SetNotFound();

                return false;
            }
        }

        var mimetype = MimeTypesMap.GetMimeType(requestFilePath);

        switch (mimetype)
        {
            case "text/html":
            {
                var source = isReplacedFile ? await VFS.FileReadStringAsync(requestFilePath) : Resources.GetStaticsResourceString(resourceFile);

                if (fluidParser.TryParse(source, out var template, out var error))
                {
                    res.SetBodyString(await template.RenderAsync(res.Context), "text/html; charset=utf-8");
                }
                else
                {
                    res.SetStatusCode(HttpStatusCode.InternalServerError).SetBodyString(error);
                }
            }
            break;

            default:
            {
                res.SetBodyData(isReplacedFile ? await VFS.FileReadDataAsync(requestFilePath) : Resources.GetStaticsResourceData(resourceFile), mimetype);
            }
            break;
        }

        return true;
    }

    static string PathCombine(string path1, string path2)
    {
        if (Path.IsPathRooted(path2))
        {
            path2 = path2.TrimStart(Path.DirectorySeparatorChar);
            path2 = path2.TrimStart(Path.AltDirectorySeparatorChar);
        }

        return Path.Combine(path1, path2);
    }

    static string PathGetReal(string virtualPath)
    {
        return virtualPath.TrimStart('/').Replace('/', '\\');
    }
}
