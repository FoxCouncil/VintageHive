using System.Text;

namespace VintageHive.Proxy;

public class Request
{
    public static readonly Request Invalid = new() { IsValid = false };
    
    public bool IsValid { get; internal set; }

    public Uri? Uri { get; set; }

    public string Type { get; internal set; } = "";

    public string Version { get; internal set; } = "";

    public ListenerSocket? ListenerSocket { get; internal set; }

    public Encoding? Encoding { get; internal set; }

    public IReadOnlyDictionary<string, string>? Headers { get; internal set; }
}
