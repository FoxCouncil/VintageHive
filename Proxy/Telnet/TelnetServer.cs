// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;
using System;

namespace VintageHive.Proxy.Telnet;

public class TelnetServer : Listener
{
    // Inspiration: http://www.telnetbbsguide.com/bbs/connection/telnet/list/brief/

    public static readonly List<TelnetSession> Sessions = new();

    public TelnetServer(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    internal override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        // Grab instance of the network stream from incoming client connection.
        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"Client {connection.RemoteIP} connected.", "");

        var session = new TelnetSession(connection);
        Sessions.Add(session);

        // Enable NAWS (Negotiate About Window Size) option
        //byte[] nawsOption = new byte[] { 0xff, 0xfb, 0x1f, 0xff, 0xfd, 0x1f };
        //await stream.WriteAsync(nawsOption);

        await session.SendText("Welcome to the VintageHive Telnet Server!\r\n");
        await session.SendText("Enter a command (help, version, exit): ");

        var reader = new StreamReader(connection.Stream);

        while (connection.IsConnected)
        {
            var input = await reader.ReadLineAsync();

            if (input.Length > 0)
            {
                await session.ProcessCommand(input.ToString());
                await session.SendText("Enter a command (help, version, exit): ");
                input = string.Empty;
            }
            else if (input.StartsWith("\xff\xfa\x1f", StringComparison.Ordinal))
            {
                // Process NAWS (Negotiate About Window Size) option
                int width = input[3] * 256 + input[4];
                int height = input[5] * 256 + input[6];
                session.TermWidth = width;
                session.TermHeight = height;

                Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"NAWS option received for client {connection.RemoteIP}, extracted terminal resolution got {session.TermWidth}x{session.TermHeight}.", "");
            }
        }

        // Client has disconnected.
        Sessions.Remove(session);
        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"Client {connection.RemoteIP} disconnected.", "");
        return null;
    }
}
