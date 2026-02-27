// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Mms;

internal class MmsServer : Listener
{
    public MmsServer(IPAddress listenAddress)
        : base(listenAddress, 1755, SocketType.Stream, ProtocolType.Tcp, false)
    {
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var session = new MmsSession(connection);

        await session.RunAsync();

        return null;
    }
}
