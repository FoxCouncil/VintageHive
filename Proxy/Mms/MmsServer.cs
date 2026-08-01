// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Mms;

internal class MmsServer : Listener
{

    // ProcessConnection below drives the whole session; there is nothing for the base read loop to do.
    protected override bool OwnsConnection => true;
    public MmsServer(IPAddress listenAddress, int port)
        : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, false)
    {
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var session = new MmsSession(connection);

        await session.RunAsync();

        return null;
    }
}
