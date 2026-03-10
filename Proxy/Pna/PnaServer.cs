// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Pna;

internal class PnaServer : Listener
{
    public PnaServer(IPAddress listenAddress)
        : base(listenAddress, 7070, SocketType.Stream, ProtocolType.Tcp, false)
    {
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var session = new PnaSession(connection);

        await session.RunAsync();

        return null;
    }
}
