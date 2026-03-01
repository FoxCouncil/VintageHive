// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using VintageHive.Network;

namespace VintageHive.Proxy.Socks.Socks4;

internal static class Socks4Handler
{
    private const byte CMD_CONNECT = 0x01;
    private const byte REPLY_GRANTED = 0x5A;
    private const byte REPLY_REJECTED = 0x5B;

    public static async Task HandleAsync(ListenerSocket connection, byte firstByte, CancellationToken ct)
    {
        var stream = connection.Stream;
        var traceId = connection.TraceId.ToString();

        // First byte (0x04) already consumed by the dispatcher.
        // Remaining request: [CMD(1), PORT(2), IP(4), ...USERID, 0x00]

        var header = new byte[7]; // CMD + PORT(2) + IP(4)

        if (!await ReadExactAsync(stream, header, ct))
        {
            return;
        }

        var command = header[0];

        if (command != CMD_CONNECT)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks4Handler), $"Unsupported command: 0x{command:X2}", traceId);

            await SendReplyAsync(stream, REPLY_REJECTED, 0, IPAddress.Any, ct);

            return;
        }

        var destPort = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1));
        var ipBytes = header[3..7];
        var destAddress = new IPAddress(ipBytes);

        // Read userid (null-terminated)
        await ReadNullTerminatedAsync(stream, ct);

        // SOCKS4a: if IP is 0.0.0.x (x != 0), a domain name follows
        string destHost = null;

        if (ipBytes[0] == 0 && ipBytes[1] == 0 && ipBytes[2] == 0 && ipBytes[3] != 0)
        {
            var domainBytes = await ReadNullTerminatedAsync(stream, ct);

            if (domainBytes == null)
            {
                return;
            }

            destHost = Encoding.ASCII.GetString(domainBytes);

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(destHost, ct);

                destAddress = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                           ?? addresses.First();
            }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks4Handler), $"DNS resolve failed for {destHost}: {ex.Message}", traceId);

                await SendReplyAsync(stream, REPLY_REJECTED, destPort, destAddress, ct);

                return;
            }
        }

        var displayHost = destHost ?? destAddress.ToString();

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks4Handler), $"CONNECT {displayHost}:{destPort}", traceId);

        // ── Connect to target ───────────────────────────────────────────
        Socket targetSocket;

        try
        {
            targetSocket = new Socket(destAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            await targetSocket.ConnectAsync(destAddress, destPort, ct);
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks4Handler), $"Connect failed to {displayHost}:{destPort}: {ex.Message}", traceId);

            await SendReplyAsync(stream, REPLY_REJECTED, destPort, destAddress, ct);

            return;
        }

        // Send success reply
        var localEp = (IPEndPoint)targetSocket.LocalEndPoint;

        await SendReplyAsync(stream, REPLY_GRANTED, (ushort)localEp.Port, localEp.Address.MapToIPv4(), ct);

        // Track the request
        Mind.Db.RequestsTrack(connection, "N/A", "SOCKS4", $"{displayHost}:{destPort}", nameof(Socks4Handler));

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks4Handler), $"Tunnel established to {displayHost}:{destPort}", traceId);

        // ── Bidirectional tunnel ────────────────────────────────────────
        using var targetStream = new NetworkStream(targetSocket, ownsSocket: true);

        await SocksProxy.TunnelAsync(stream, targetStream);

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks4Handler), $"Tunnel closed to {displayHost}:{destPort}", traceId);
    }

    private static async Task SendReplyAsync(Stream stream, byte status, ushort port, IPAddress address, CancellationToken ct)
    {
        // SOCKS4 reply: [0x00, STATUS, PORT(2), IP(4)]
        var reply = new byte[8];

        reply[0] = 0x00;
        reply[1] = status;

        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), port);

        var addrBytes = address.MapToIPv4().GetAddressBytes();

        Buffer.BlockCopy(addrBytes, 0, reply, 4, 4);

        await stream.WriteAsync(reply, ct);
    }

    private static async Task<byte[]> ReadNullTerminatedAsync(Stream stream, CancellationToken ct)
    {
        var result = new List<byte>();
        var buf = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buf, ct);

            if (read == 0)
            {
                return null;
            }

            if (buf[0] == 0x00)
            {
                return result.ToArray();
            }

            result.Add(buf[0]);
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);

            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
