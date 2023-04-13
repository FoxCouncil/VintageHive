using Fluid;
using System.Diagnostics;
using System.Reflection;
using UAParser;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors.LocalServer;

internal static class ControllerManager
{
    static readonly DirectoryInfo BaseDirectory = GetBaseDirectory();

    static readonly Dictionary<string, Type> _controllers = new();

    static readonly string _yaml = File.ReadAllText("ua-regexes.yaml");
    
    static readonly Parser _parser = Parser.FromYaml(_yaml);

    static ControllerManager()
    {
        var controllers = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetTypeInfo().IsSubclassOf(typeof(Controller))).ToList();

        foreach (var controller in controllers)
        {
            var name = controller.Name.ToLower().Replace("controller", ".com");

            _controllers.Add(name, controller);
        }
    }

    public static Controller Fetch(HttpRequest request, HttpResponse response)
    {
        var name = request.Uri.Host;

        if (!_controllers.ContainsKey(name))
        {
            return null;
        }

        var controller = (Controller)Activator.CreateInstance(_controllers[name]);

        controller.RootDirectory = new DirectoryInfo(Path.Combine(BaseDirectory.FullName, name));
        controller.Request = request;
        controller.Response = response;
        controller.Response.Context.Options.FileProvider = new LocalServerFileProvider(controller.RootDirectory);

        controller.Response.Context.SetValue("appversion", Mind.ApplicationVersion);

        var clientInfo = _parser.Parse(request.UserAgent);

        controller.Response.Context.SetValue("clientip", request.ListenerSocket.RemoteIP);
        controller.Response.Context.SetValue("browserversion", clientInfo.UA.ToString());
        controller.Response.Context.SetValue("osversion", clientInfo.OS.ToString());
        controller.Response.Context.SetValue("deviceversion", clientInfo.Device.ToString());

        return controller;
    }

    static DirectoryInfo GetBaseDirectory()
    {
        var path = "Statics/";

        if (Debugger.IsAttached)
        {
            path = "../../../" + path;
        }

        return new DirectoryInfo(path);
    }
}
