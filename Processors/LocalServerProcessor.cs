using Fluid;
using Fluid.Ast;
using Fluid.Values;
using HeyRed.Mime;
using Humanizer;
using System.Net;
using System.Net.Sockets;
using System.Web;
using VintageHive.Processors.LocalServer;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;
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
            var host = req.Uri.Host.Replace("ftp.", string.Empty).ToLower();

            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            var hostPath = Path.Combine("hosting", host);

            if (!VFS.DirectoryExists(hostPath))
            {
                VFS.DirectoryCreate(hostPath);
            }

            // Todo; authentication
            await req.SendUserLoggedIn();

            req.SetupServer(hostPath);

            while (req.IsActive)
            {
                var payload = await req.FetchCommand();

                if (payload == null)
                {
                    continue;
                }

                var command = payload.Item1.ToUpper();
                var args = payload.Item2;

                switch (command)
                {
                    default:
                    {
                        await req.SendResponse(FtpResponseCode.CommandNotImplemented, $"Command [{command}, {args}] not implemented");
                    }
                    break;
                }
            }

            req.StopServer();

            return true;
        }

        return false;
    }

    public static async Task<bool> ProcessHttpsRequest(HttpRequest req, HttpResponse res)
    {
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
            var wwwFolder = Path.Combine(vfsFolder, "www");

            if (VFS.DirectoryExists(vfsFolder) && VFS.DirectoryExists(wwwFolder))
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

                    using var fileStream = VFS.FileReadStream(vfsFile);

                    res.SetBodyFileStream(fileStream, mimetype);

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
            resourceFile = requestFilePath.Replace(Path.DirectorySeparatorChar, '.');

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
