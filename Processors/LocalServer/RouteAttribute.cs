// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Processors.LocalServer;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
internal class RouteAttribute : Attribute
{
    public string Path { get; private set; }

    public HttpMethod Method { get; set; }

    public RouteAttribute(string path)
    {
        Path = path;
        Method = HttpMethod.Post;
    }
}
