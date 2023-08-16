// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using System.Reflection;
using System.Text.RegularExpressions;
using UAParser;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors.LocalServer;

internal static class ControllerManager
{
    static readonly Dictionary<DomainAttribute, Type> controllers = new();

    static readonly Parser parser = Parser.FromYaml(Resources.GetStaticsResourceString("ua-regexes.yaml"));

    static ControllerManager()
    {
        var controllers = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetTypeInfo().IsSubclassOf(typeof(Controller))).ToList();

        foreach (var controller in controllers)
        {
            var domains = (DomainAttribute[])Attribute.GetCustomAttributes(controller, typeof(DomainAttribute));

            foreach (var domain in domains)
            {
                ControllerManager.controllers.Add(domain, controller);
            }
        }
    }

    static bool TryGetControllerByDomain(string domain, out Type controller)
    {
        controller = null;

        var domainKeys = controllers.Keys.Where(x => x.Domain.ToLower().EndsWith(domain) || Regex.IsMatch(domain, WildCardToRegular(x.Domain)))?.ToList() ?? null;

        if (domainKeys == null)
        {
            return false;
        }

        if (!domainKeys.Any())
        {
            return false;
        }

        if (domainKeys.Count == 1)
        {
            controller = controllers[domainKeys[0]];
        }
        else
        {
            var exactMatch = domainKeys.FirstOrDefault(x => x.Domain.Equals(domain)) ?? null;

            if (exactMatch != null)
            {
                controller = controllers[exactMatch];
            }
            else
            {
                var wildcardMatch = domainKeys.FirstOrDefault(x => x.IsWildcard) ?? null;

                if (wildcardMatch == null)
                {
                    return false;
                }

                controller = controllers[wildcardMatch];
            }
        }

        return true;
    }

    public static Controller Fetch(HttpRequest request, HttpResponse response)
    {
        var name = request.Uri.Host;

        if (!TryGetControllerByDomain(name, out var controllerType))
        {
            return null;
        }

        var controller = (Controller)Activator.CreateInstance(controllerType);

        var rootControllerDirectory = Path.Combine("controllers/", name);

        controller.RootDirectory = rootControllerDirectory;
        controller.Request = request;
        controller.Response = response;
        controller.Response.Context.Options.FileProvider = new LocalServerFileProvider(rootControllerDirectory);

        var endpoint = ((IPEndPoint)request.ListenerSocket.RawSocket.LocalEndPoint).Address.MapToIPv4();
        var endpointPort = ((IPEndPoint)request.ListenerSocket.RawSocket.LocalEndPoint).Port;

        controller.Response.Context.SetValue("serverip", endpoint);
        controller.Response.Context.SetValue("serverport", endpointPort);
        controller.Response.Context.SetValue("appversion", Mind.ApplicationVersion);
        controller.Response.Context.SetValue("traceid", request.ListenerSocket.TraceId.ToString());

        var clientInfo = parser.Parse(request.UserAgent);

        controller.Response.Context.SetValue("clientip", request.ListenerSocket.RemoteIP);
        controller.Response.Context.SetValue("browserversion", clientInfo.UA.ToString());
        controller.Response.Context.SetValue("osversion", clientInfo.OS.ToString());
        controller.Response.Context.SetValue("deviceversion", clientInfo.Device.ToString());

        return controller;
    }

    private static string WildCardToRegular(string value)
    {
        return "^" + Regex.Escape(value).Replace("\\*", ".*") + "$";
    }
}
