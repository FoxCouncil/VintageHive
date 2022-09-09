using System.Net;
using System.Net.Sockets;
using VintageHive.Proxy.Security;

namespace VintageHive.Network;

public class ListenerSocket
{
    public bool IsConnected => RawSocket.Connected;

    public string IPAddress => ((IPEndPoint)RawSocket.LocalEndPoint).Address.MapToIPv4().ToString();

    public Socket RawSocket { get; set; }

    public bool IsSecure { get; set; } = false;

    public NetworkStream Stream { get; set; }

    public SslStream SecureStream { get; set; }

}
