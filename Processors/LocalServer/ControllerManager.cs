using System.Diagnostics;
using System.Reflection;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors.LocalServer;

internal static class ControllerManager
{
    static readonly DirectoryInfo BaseDirectory = GetBaseDirectory();

    static readonly Dictionary<string, Type> _controllers = new();

    static ControllerManager()
    {
        var controllers = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetTypeInfo().IsSubclassOf(typeof(Controller))).ToList();

        foreach (var controller in controllers)
        {
            var name = controller.Name.ToLower().Replace("controller", string.Empty);

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
