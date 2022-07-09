using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VintageHive.Proxy.Socks;

internal class Socks5Proxy : Listener
{
    public Socks5Proxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    internal override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        await Task.Delay(0);

        return null;
    }

    internal override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        await Task.Delay(0);
        
        return null;
    }
}
