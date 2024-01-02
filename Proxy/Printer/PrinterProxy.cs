using VintageHive.Network;

namespace VintageHive.Proxy.Printer;

internal class PrinterProxy : Listener
{
    public PrinterProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        await Task.Delay(0);

        return null;
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var output = Encoding.ASCII.GetString(data, 0, read);

        await Task.Delay(0);

        return null;
    }
}
