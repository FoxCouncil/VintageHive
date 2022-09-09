using Fluid;
using Fluid.Values;
using HeyRed.Mime;
using System.Diagnostics;
using VintageHive.Processors.LocalServer;
using VintageHive.Processors.LocalServer.Controllers;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;

using Humanizer;
using System.Web;

namespace VintageHive.Processors;

internal static class LocalServerProcessor
{
    public static readonly FluidParser FluidParser = new();

    

    // 
    static readonly Dictionary<string, Controller> LocalSites = new()
    {
        { "admin", new AdminController() },
        { "hive", new HiveController() },
        { "kitchen", new KitchenController() },
    };

    static LocalServerProcessor()
    {
        TemplateOptions.Default.MemberAccessStrategy = new UnsafeMemberAccessStrategy();
        TemplateOptions.Default.Filters.AddFilter("bytes", (input, arguments, context) => {
            return StringValue.Create(((long)input.ToNumberValue()).Bytes().Humanize());
        });
        TemplateOptions.Default.Filters.AddFilter("urlencode", (input, arguments, context) => {
            return StringValue.Create(HttpUtility.UrlEncode(input.ToStringValue()));
        });
    }

    public static async Task<bool> ProcessRequest(HttpRequest req, HttpResponse res)
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

        if (!LocalSites.ContainsKey(req.Uri.Host))
        {
            return false;
        }

        res.Cache = false;

        var site = ControllerManager.Fetch(req, res);

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

                return true;
            }

            filePath = fullFilePath.FullName;
        }

        var mimetype = MimeTypesMap.GetMimeType(filePath);

        switch (mimetype)
        {
            case "text/html":
            {
                var source = isEmbeddedResource ? Resources.GetStaticsResourceString(fileRequestPath) : await File.ReadAllTextAsync(filePath);

                if (FluidParser.TryParse(source, out var template, out var error))
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
