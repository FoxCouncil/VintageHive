// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Reflection;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors.LocalServer;

public abstract class Controller
{
    internal string RootDirectory { get; set; }

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
            .Where(y => y.GetCustomAttributes().OfType<RouteAttribute>().Any())
            .ToDictionary(z => z.GetCustomAttribute<RouteAttribute>().Path);
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
