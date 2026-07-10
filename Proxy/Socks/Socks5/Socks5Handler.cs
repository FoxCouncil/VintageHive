// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using VintageHive.Network;

namespace VintageHive.Proxy.Socks.Socks5;

internal static class Socks5Handler
{
    // RFC 1929 username/password sub-negotiation uses its OWN version byte 0x01, NOT the SOCKS 0x05.
    private const byte AuthSubnegotiationVersion = 0x01;
    private const byte AuthStatusSuccess = 0x00;
    private const byte AuthStatusFailure = 0x01;
    private const byte NoAcceptableMethod = 0xFF;

    public static async Task HandleAsync(ListenerSocket connection, byte firstByte, bool requireAuth, CancellationToken ct)
    {
        var stream = connection.Stream;
        var traceId = connection.TraceId.ToString();

        // -- Greeting phase ----------------------------------------------
        // First byte (0x05) already consumed by the dispatcher.
        // Next byte: number of auth methods, then that many method bytes.

        var nMethods = await ReadByteAsync(stream, ct);

        if (nMethods == -1)
        {
            return;
        }

        var methods = new byte[nMethods];

        if (!await ReadExactAsync(stream, methods, ct))
        {
            return;
        }

        // When auth is required, only accept username/password (0x02); otherwise accept no-auth (0x00). Never silently fall back.
        var wanted = requireAuth ? (byte)Socks5AuthType.UsernamePassword : (byte)Socks5AuthType.None;
        var selectedAuth = methods.Contains(wanted) ? wanted : NoAcceptableMethod;

        await stream.WriteAsync(new byte[] { 0x05, selectedAuth }, ct);

        if (selectedAuth == NoAcceptableMethod)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), requireAuth ? "Client offered no username/password auth method" : "No acceptable auth method", traceId);

            return;
        }

        if (selectedAuth == (byte)Socks5AuthType.UsernamePassword && !await AuthenticateAsync(connection, stream, traceId, ct))
        {
            return;
        }

        // -- Request phase -----------------------------------------------
        // [VER(1), CMD(1), RSV(1), ATYP(1), ...ADDR, PORT(2)]

        var header = new byte[4];

        if (!await ReadExactAsync(stream, header, ct))
        {
            return;
        }

        if (header[0] != 0x05)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Bad version in request: 0x{header[0]:X2}", traceId);

            return;
        }

        var command = (Socks5CommandType)header[1];
        var addressType = (Socks5AddressType)header[3];

        if (command != Socks5CommandType.Connect)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Unsupported command: {command}", traceId);

            await SendReplyAsync(stream, Socks5ReplyType.CommandNotSupported, ct);

            return;
        }

        // Parse destination address
        IPAddress destAddress;
        string destHost = null;

        switch (addressType)
        {
            case Socks5AddressType.IPv4:
            {
                var addrBytes = new byte[4];

                if (!await ReadExactAsync(stream, addrBytes, ct))
                {
                    return;
                }

                destAddress = new IPAddress(addrBytes);
            }
            break;

            case Socks5AddressType.DomainName:
            {
                var nameLen = await ReadByteAsync(stream, ct);

                if (nameLen == -1)
                {
                    return;
                }

                var nameBytes = new byte[nameLen];

                if (!await ReadExactAsync(stream, nameBytes, ct))
                {
                    return;
                }

                destHost = Encoding.ASCII.GetString(nameBytes);

                try
                {
                    var addresses = await System.Net.Dns.GetHostAddressesAsync(destHost, ct);

                    destAddress = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                               ?? addresses.First();
                }
                catch (Exception ex)
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"DNS resolve failed for {destHost}: {ex.Message}", traceId);

                    await SendReplyAsync(stream, Socks5ReplyType.HostUnreachable, ct);

                    return;
                }
            }
            break;

            case Socks5AddressType.IPv6:
            {
                var addrBytes = new byte[16];

                if (!await ReadExactAsync(stream, addrBytes, ct))
                {
                    return;
                }

                destAddress = new IPAddress(addrBytes);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Unsupported address type: 0x{(byte)addressType:X2}", traceId);

                await SendReplyAsync(stream, Socks5ReplyType.AddressTypeNotSupported, ct);

                return;
            }
        }

        // Parse port (2 bytes, big-endian)
        var portBytes = new byte[2];

        if (!await ReadExactAsync(stream, portBytes, ct))
        {
            return;
        }

        var destPort = BinaryPrimitives.ReadUInt16BigEndian(portBytes);

        var displayHost = destHost ?? destAddress.ToString();

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"CONNECT {displayHost}:{destPort}", traceId);

        // -- Connect to target -------------------------------------------
        Socket targetSocket = null;

        try
        {
            targetSocket = new Socket(destAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            await targetSocket.ConnectAsync(destAddress, destPort, ct);
        }
        catch (Exception ex)
        {
            targetSocket?.Dispose();

            Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Connect failed to {displayHost}:{destPort}: {ex.Message}", traceId);

            var reply = ex is SocketException sex ? MapSocketError(sex.SocketErrorCode) : Socks5ReplyType.GeneralFailure;

            await SendReplyAsync(stream, reply, ct);

            return;
        }

        // Own the socket via its stream immediately so a throw on the reply write or request tracking disposes it
        using var targetStream = new NetworkStream(targetSocket, ownsSocket: true);

        // Send success reply with bound address
        var localEp = (IPEndPoint)targetSocket.LocalEndPoint;
        var boundAddr = localEp.Address.MapToIPv4().GetAddressBytes();
        var boundPort = (ushort)localEp.Port;

        var successReply = new byte[10];

        successReply[0] = 0x05;
        successReply[1] = (byte)Socks5ReplyType.Succeeded;
        successReply[2] = 0x00;
        successReply[3] = (byte)Socks5AddressType.IPv4;

        Buffer.BlockCopy(boundAddr, 0, successReply, 4, 4);
        BinaryPrimitives.WriteUInt16BigEndian(successReply.AsSpan(8), boundPort);

        await stream.WriteAsync(successReply, ct);

        // Track the request
        Mind.Db.RequestsTrack(connection, "N/A", "SOCKS5", $"{displayHost}:{destPort}", nameof(Socks5Handler));

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Tunnel established to {displayHost}:{destPort}", traceId);

        // -- Bidirectional tunnel ----------------------------------------
        await SocksProxy.TunnelAsync(stream, targetStream);

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Tunnel closed to {displayHost}:{destPort}", traceId);
    }

    // RFC 1929: client sends VER(0x01), ULEN, UNAME, PLEN, PASSWD; server replies VER(0x01), STATUS (0x00 ok, non-zero fail).
    private static async Task<bool> AuthenticateAsync(ListenerSocket connection, Stream stream, string traceId, CancellationToken ct)
    {
        var version = await ReadByteAsync(stream, ct);

        if (version != AuthSubnegotiationVersion)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Bad auth sub-negotiation version: 0x{version:X2}", traceId);

            // Wrong version means we cannot trust the framing; reply failure and close rather than guess.
            await stream.WriteAsync(new byte[] { AuthSubnegotiationVersion, AuthStatusFailure }, ct);

            return false;
        }

        var username = await ReadLengthPrefixedStringAsync(stream, ct);
        var password = await ReadLengthPrefixedStringAsync(stream, ct);

        if (username == null || password == null)
        {
            return false;
        }

        // RFC 1929 allows zero-length fields; reject them explicitly. UserFetch with an empty password matches on username alone, which would be an auth bypass.
        var authenticated = username.Length > 0 && password.Length > 0 && Mind.Db?.UserFetch(username, password) != null;

        await stream.WriteAsync(new byte[] { AuthSubnegotiationVersion, authenticated ? AuthStatusSuccess : AuthStatusFailure }, ct);

        if (!authenticated)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Authentication failed for user '{username}'", traceId);

            return false;
        }

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(Socks5Handler), $"Authenticated user '{username}'", traceId);

        return true;
    }

    private static async Task<string> ReadLengthPrefixedStringAsync(Stream stream, CancellationToken ct)
    {
        var length = await ReadByteAsync(stream, ct);

        if (length == -1)
        {
            return null;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        var buffer = new byte[length];

        if (!await ReadExactAsync(stream, buffer, ct))
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task SendReplyAsync(Stream stream, Socks5ReplyType reply, CancellationToken ct)
    {
        // Minimal reply: VER, REP, RSV, ATYP=IPv4, ADDR=0.0.0.0, PORT=0
        var buf = new byte[10];

        buf[0] = 0x05;
        buf[1] = (byte)reply;
        buf[2] = 0x00;
        buf[3] = (byte)Socks5AddressType.IPv4;
        // bytes 4-9 are already zeroed

        await stream.WriteAsync(buf, ct);
    }

    private static Socks5ReplyType MapSocketError(SocketError error)
    {
        return error switch
        {
            SocketError.ConnectionRefused => Socks5ReplyType.ConnectionRefused,
            SocketError.NetworkUnreachable => Socks5ReplyType.NetworkUnreachable,
            SocketError.HostUnreachable => Socks5ReplyType.HostUnreachable,
            SocketError.TimedOut => Socks5ReplyType.TtlExpired,
            _ => Socks5ReplyType.GeneralFailure,
        };
    }

    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken ct)
    {
        var buf = new byte[1];
        var read = await stream.ReadAsync(buf, ct);

        return read == 1 ? buf[0] : -1;
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
