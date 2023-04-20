// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net.Sockets;
using VintageHive.Network;

namespace VintageHive.Proxy.Socks;

internal class Socks5Proxy : Listener
{
    public Socks5Proxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    internal override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        await Task.Delay(0);

        var buffer = new byte[4096];

        var read = await connection.Stream.ReadAsync(buffer);

        var initialPacket = buffer[..read];

        if (initialPacket[0] != 5)
        {
            return null;
        }

        var totalAuthMethods = initialPacket[1];

        var authMethods = new List<Socks5AuthType>();

        for (var idx = 0; idx < totalAuthMethods; idx++)
        {
            authMethods.Add((Socks5AuthType)initialPacket[2 + idx]);
        }

        await connection.Stream.WriteAsync(new byte[2] { 0x05, 0x00 });

        read = await connection.Stream.ReadAsync(buffer);

        var conenctionPacket = buffer[..read];

        return null;
    }

    internal override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        await Task.Delay(0);
        
        return null;
    }
}
