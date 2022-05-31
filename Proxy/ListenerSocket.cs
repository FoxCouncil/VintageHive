using System.Net.Sockets;
using VintageHive.Proxy.Security;

namespace VintageHive.Proxy;

public class ListenerSocket
{
    public Socket RawSocket { get; set; }

    public bool IsSecure { get; set; } = false;

    public SslStream SecureStream { get; set; }

}
