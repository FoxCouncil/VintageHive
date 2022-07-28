using System.Net.Sockets;
using VintageHive.Proxy.Security;

namespace VintageHive.Proxy;

public class ListenerSocket
{
    public bool IsConnected => RawSocket.Connected;

    public Socket RawSocket { get; set; }

    public bool IsSecure { get; set; } = false;

    public NetworkStream Stream { get; set; }

    public SslStream SecureStream { get; set; }

}
