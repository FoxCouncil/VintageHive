using System.Reflection;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors.LocalServer;

public abstract class Controller
{
    internal DirectoryInfo RootDirectory { get; set; }

    internal HttpRequest Request { get; set; }

    internal HttpResponse Response { get; set; }

    internal dynamic Session => Response.Session;

    readonly Dictionary<string, MethodInfo> _methods;

    public Controller()
    {
        var type = GetType();

        var rootDirectory = type.Name.ToLower().Replace("controller", string.Empty);

        _methods = type
            .GetMethods()
            .Where(y => y.GetCustomAttributes().OfType<ControllerAttribute>().Any())
            .ToDictionary(z => z.GetCustomAttribute<ControllerAttribute>().Path);
    }

    public virtual async Task CallInitial(string rawPath)
    {
        await Task.Delay(0);
    }

    public async Task CallMethod(string rawPath)
    {
        await CallInitial(rawPath);

        if (!Response.Handled && _methods.ContainsKey(rawPath))
        {
            await (Task)_methods[rawPath].Invoke(this, null);
        }
    }
}
