// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;
using VintageHive.Proxy.Socks.Socks4;
using VintageHive.Proxy.Socks.Socks5;

namespace VintageHive.Proxy.Socks;

internal class SocksProxy : Listener
{
    private const int BUFFER_SIZE = 8192;

    public SocksProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var stream = connection.Stream;
        var traceId = connection.TraceId.ToString();

        // Read the first byte to determine SOCKS version
        var buf = new byte[1];
        var read = await stream.ReadAsync(buf);

        if (read == 0)
        {
            return null;
        }

        var version = buf[0];

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            switch (version)
            {
                case 0x05:
                {
                    await Socks5Handler.HandleAsync(connection, version, cts.Token);
                }
                break;

                case 0x04:
                {
                    await Socks4Handler.HandleAsync(connection, version, cts.Token);
                }
                break;

                default:
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(SocksProxy), $"Unknown SOCKS version: 0x{version:X2}", traceId);
                }
                break;
            }
        }
        catch (OperationCanceledException)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(SocksProxy), "Connection timed out", traceId);
        }
        catch (Exception ex) when (ex is IOException || ex is SocketException)
        {
            // Normal: client or target disconnected
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(SocksProxy), ex, traceId);
        }

        return null;
    }

    /// <summary>
    /// Bidirectional stream forwarding. Copies data in both directions
    /// until either side closes or an error occurs.
    /// </summary>
    internal static async Task TunnelAsync(Stream clientStream, Stream targetStream)
    {
        var clientToTarget = CopyStreamAsync(clientStream, targetStream);
        var targetToClient = CopyStreamAsync(targetStream, clientStream);

        // When either direction finishes, we're done
        await Task.WhenAny(clientToTarget, targetToClient);
    }

    private static async Task CopyStreamAsync(Stream source, Stream destination)
    {
        var buffer = new byte[BUFFER_SIZE];

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer);

                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read));
                await destination.FlushAsync();
            }
        }
        catch (Exception ex) when (ex is IOException || ex is SocketException)
        {
            // One side closed — expected during tunnel teardown
        }
    }
}
