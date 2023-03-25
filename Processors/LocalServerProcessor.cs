using Fluid;
using Fluid.Ast;
using Fluid.Values;
using HeyRed.Mime;
using Humanizer;
using System.Diagnostics;
using System.Web;
using VintageHive.Processors.LocalServer;
using VintageHive.Processors.LocalServer.Controllers;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;

namespace VintageHive.Processors;

internal static class LocalServerProcessor
{
    static readonly FluidParser fluidParser = new();

    static LocalServerProcessor()
    {
        TemplateOptions.Default.MemberAccessStrategy = new UnsafeMemberAccessStrategy();
        TemplateOptions.Default.Filters.AddFilter("bytes", (input, arguments, context) => {
            return StringValue.Create(((long)input.ToNumberValue()).Bytes().Humanize());
        });
        TemplateOptions.Default.Filters.AddFilter("urlencode", (input, arguments, context) => {
            return StringValue.Create(HttpUtility.UrlEncode(input.ToStringValue()));
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
            var host = req.Uri.Host.Replace("ftp.", string.Empty);


            await req.SendInvalidUsernameOrPassword();
        }

        return false;
    }

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        if (req.Uri.IsLoopback)
        {
            var newRedirectedUri = new UriBuilder(req.Uri)
            {
                Host = "admin"
            };

            req.Uri = newRedirectedUri.Uri;
        }
        else if (req.Uri.Host == "admin")
        {
            return false;
        }

        var site = ControllerManager.Fetch(req, res);

        if (site != null)
        {
            return await ProcessController(req, res, site);
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

        var isEmbeddedResource = !Debugger.IsAttached && Resources.Statics.ContainsKey(req.Uri.Host + fileRequestPath);

        var filePath = fileRequestPath;

        if (!isEmbeddedResource)
        {
            var fullFilePath = new FileInfo(PathCombine(site.RootDirectory.FullName, fileRequestPath));

            if (!fullFilePath.Exists)
            {
                res.SetNotFound();

                return false;
            }

            filePath = fullFilePath.FullName;
        }

        var mimetype = MimeTypesMap.GetMimeType(filePath);

        switch (mimetype)
        {
            case "text/html":
            {
                var source = isEmbeddedResource ? Resources.GetStaticsResourceString(fileRequestPath) : await File.ReadAllTextAsync(filePath);

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
                res.SetBodyData(isEmbeddedResource ? Resources.GetStaticsResourceData(fileRequestPath) : await File.ReadAllBytesAsync(filePath), mimetype);
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
}
