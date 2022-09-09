namespace VintageHive.Processors.LocalServer;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
internal class ControllerAttribute : Attribute
{
    public string Path { get; private set; }

    public HttpMethod Method { get; set; }

    public ControllerAttribute(string path)
    {
        Path = path;
        Method = HttpMethod.Post;
    }
}
