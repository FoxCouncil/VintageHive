using AngleSharp.Io;
using System.Reflection;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors.LocalServer;

public abstract class Controller
{
    internal DirectoryInfo RootDirectory { get; set; }

    internal HttpRequest Request { get; set; }

    internal HttpResponse Response { get; set; }

    internal dynamic Session => Response.Session;

    public bool HasSession(string name) => (Session as IDictionary<string, object>).ContainsKey(name);

    readonly Dictionary<string, MethodInfo> _methods;

    public Controller()
    {
        var type = GetType();

        var rootDirectory = type.Name.ToLower().Replace("controller", string.Empty);

        // RootDirectory = directory;

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
        if (_methods.ContainsKey(rawPath))
        {
            Response.Context.Options.FileProvider = new LocalServerFileProvider(RootDirectory);

            await CallInitial(rawPath);

            await (Task)_methods[rawPath].Invoke(this, null);            
        }
    }
}
