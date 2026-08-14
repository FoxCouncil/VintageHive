// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using VintageHive.Proxy.Transfer;

namespace VintageHive.Proxy.Oscar.FileTransfer;

/// <summary>
/// The AIM adapter of the file-transfer relays (relaytransfers): AOL's rendezvous-proxy behavior. When a
/// SendFile rendezvous proposal is relayed, the connectivity is rewritten to a freshly allocated relay
/// listener and the UseARS proxy flag is set, so both the proposer and the peer dial the relay and speak the
/// rendezvous-proxy handshake rather than attempting a direct (behind NAT, impossible) peer connection. The
/// relay pairs the two by the 8-byte rendezvous cookie and splices the OFT stream.
/// <para>
/// UNVERIFIED CHOREOGRAPHY, DOCUMENTED ON PURPOSE. The 0x044a peer-proxy frame layout is taken from libpurple
/// (peer.h / peer_proxy.c) and the rendezvous TLVs from mk6i's wire/snacs.go, but mk6i deliberately does NOT
/// implement the proxy - it rewrites the requester IP and tells operators to port-forward, which walled-garden
/// members cannot do - so there is no reference for two NAT'd clients both dialing this proxy. The CREATE/
/// CREATED/JOIN/READY handling and the both-dial-out rewrite are reasoned from those layouts, not read off a
/// capture, and isolated here so a real client trace can correct them in one place. The rewrite, the SendFile
/// gate, and the cookie pairing are exercised at the byte level by the tests.
/// </para>
/// </summary>
public static class OscarFileTransferRelay
{
    const ushort PeerProxyVersion = 0x044a;

    const ushort ProxyTypeError = 0x0001;

    const ushort ProxyTypeCreate = 0x0002;

    const ushort ProxyTypeCreated = 0x0003;

    const ushort ProxyTypeJoin = 0x0004;

    const ushort ProxyTypeReady = 0x0005;

    static readonly TimeSpan CookieTtl = TimeSpan.FromMinutes(5);

    sealed class Pending
    {
        public IPAddress Ip;

        public ushort Port;

        public DateTimeOffset Seen;
    }

    // Cookie (hex) -> the relay endpoint allocated for that transfer, so the reciprocal accept rewrites to the
    // SAME relay the propose allocated instead of standing up a second listener.
    static readonly ConcurrentDictionary<string, Pending> Transfers = new(StringComparer.Ordinal);

    /// <summary>
    /// If this rendezvous is a SendFile message, rewrites its connectivity to the relay and (on the initial
    /// propose) allocates the listener and starts the proxy session, returning the rewritten TLV 0x05 bytes.
    /// Returns null to leave the fragment untouched - not SendFile, the relay could not allocate, or an accept
    /// for a transfer the relay never took. The caller then forwards the original bytes.
    /// </summary>
    public static byte[] TryRewrite(OscarRendezvous rendezvous, OscarSession session)
    {
        if (!rendezvous.IsSendFile)
        {
            return null;
        }

        Prune();

        var cookieKey = Convert.ToHexString(rendezvous.Cookie);

        if (rendezvous.MessageType == OscarRendezvous.MessageTypeCancel)
        {
            Transfers.TryRemove(cookieKey, out _);

            return null;
        }

        if (rendezvous.MessageType == OscarRendezvous.MessageTypePropose)
        {
            var listener = TransferRelay.Instance.TryAllocateListener(IPAddress.Any);

            if (listener == null)
            {
                // Disabled, capped, or the range is exhausted: leave the proposal untouched so the clients
                // fall back to a direct attempt, exactly as with the flag off.
                return null;
            }

            var advertised = TransferRelay.ResolveAdvertisedAddress(ParseLocalIp(session?.Client?.LocalIP));
            var port = (ushort)listener.Port;

            Transfers[cookieKey] = new Pending { Ip = advertised, Port = port, Seen = DateTimeOffset.UtcNow };

            rendezvous.PointAtRelay(advertised, port);

            _ = HostAsync(listener, rendezvous.Cookie, port, advertised, CancellationToken.None);

            return rendezvous.Serialize();
        }

        // Accept (or a re-propose): rewrite to the relay the propose already stood up, correlating on cookie.
        if (Transfers.TryGetValue(cookieKey, out var pending))
        {
            rendezvous.PointAtRelay(pending.Ip, pending.Port);

            return rendezvous.Serialize();
        }

        return null;
    }

    /// <summary>
    /// The rendezvous-proxy session: accept two connections whose peer-proxy frame carries the transfer's
    /// cookie, answer CREATE with CREATED and both with READY, then splice the OFT stream under the relay
    /// bounds. Torn down on the TTL if the second side never arrives.
    /// </summary>
    internal static async Task HostAsync(RelayListener listener, byte[] cookie, ushort port, IPAddress advertised, CancellationToken outerCt)
    {
        var connections = new List<(Socket Socket, NetworkStream Stream)>();

        try
        {
            using var ttlCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);

            ttlCts.CancelAfter(TransferRelay.TicketTtl);

            while (connections.Count < 2)
            {
                Socket socket;

                try
                {
                    socket = await listener.AcceptAsync(ttlCts.Token);
                }
                catch (Exception)
                {
                    break;
                }

                var stream = new NetworkStream(socket);

                var frame = await ReadFrameAsync(stream, ttlCts.Token);

                if (frame == null || !CookieMatches(frame.Value.Type, frame.Value.Payload, cookie))
                {
                    try { stream.Dispose(); } catch { }
                    try { socket.Close(); } catch { }

                    continue;
                }

                if (frame.Value.Type == ProxyTypeCreate)
                {
                    await WriteFrameAsync(stream, ProxyTypeCreated, CreatedPayload(port, advertised), ttlCts.Token);
                }

                connections.Add((socket, stream));
            }

            if (connections.Count < 2)
            {
                return;
            }

            // Both present: tell each the peer is ready, then splice.
            foreach (var connection in connections)
            {
                await WriteFrameAsync(connection.Stream, ProxyTypeReady, Array.Empty<byte>(), outerCt);
            }

            await TransferRelay.SpliceAsync(connections[0].Stream, connections[1].Stream, outerCt);
        }
        catch (Exception)
        {
            // A bound breach or dropped peer ends the transfer; surfaced to a client only as a closed socket.
        }
        finally
        {
            foreach (var connection in connections)
            {
                try { connection.Stream.Dispose(); } catch { }
                try { connection.Socket.Close(); } catch { }
            }

            listener.Dispose();
        }
    }

    static bool CookieMatches(ushort type, byte[] payload, byte[] expected)
    {
        var cookie = ExtractCookie(type, payload);

        return cookie != null && cookie.AsSpan().SequenceEqual(expected);
    }

    // CREATE payload: snLen(1) sn(snLen) cookie(8) cap(...). JOIN payload: snLen(1) sn(snLen) pin(2) cookie(8) cap(...).
    static byte[] ExtractCookie(ushort type, byte[] payload)
    {
        if (payload.Length < 1)
        {
            return null;
        }

        var snLen = payload[0];

        var offset = 1 + snLen;

        if (type == ProxyTypeJoin)
        {
            offset += 2; // PIN
        }

        if (payload.Length < offset + 8)
        {
            return null;
        }

        return payload[offset..(offset + 8)];
    }

    static byte[] CreatedPayload(ushort port, IPAddress ip)
    {
        var payload = new byte[6];

        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), port);

        ip.MapToIPv4().GetAddressBytes().CopyTo(payload, 2);

        return payload;
    }

    static async Task<(ushort Type, byte[] Payload)?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var lengthBuffer = await ReadExactAsync(stream, 2, ct);

        if (lengthBuffer == null)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBuffer);

        // header after the length word is version(2) type(2) unknown(4) flags(2) = 10 bytes.
        if (length < 10 || length > 4096)
        {
            return null;
        }

        var rest = await ReadExactAsync(stream, length, ct);

        if (rest == null)
        {
            return null;
        }

        var version = BinaryPrimitives.ReadUInt16BigEndian(rest.AsSpan(0, 2));

        if (version != PeerProxyVersion)
        {
            return null;
        }

        var type = BinaryPrimitives.ReadUInt16BigEndian(rest.AsSpan(2, 2));

        return (type, rest[10..]);
    }

    static async Task WriteFrameAsync(Stream stream, ushort type, byte[] payload, CancellationToken ct)
    {
        var body = new byte[10 + payload.Length];

        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(0, 2), PeerProxyVersion);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(2, 2), type);
        // unknown(4) and flags(2) stay zero.
        payload.CopyTo(body, 10);

        var frame = new byte[2 + body.Length];

        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), (ushort)body.Length);

        body.CopyTo(frame, 2);

        await stream.WriteAsync(frame, ct);

        await stream.FlushAsync(ct);
    }

    static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];

        var offset = 0;

        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);

            if (read <= 0)
            {
                return null;
            }

            offset += read;
        }

        return buffer;
    }

    static IPAddress ParseLocalIp(string localIp)
    {
        return IPAddress.TryParse(localIp, out var parsed) ? parsed : IPAddress.Loopback;
    }

    static void Prune()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in Transfers.ToArray())
        {
            if (now - pair.Value.Seen > CookieTtl)
            {
                Transfers.TryRemove(pair.Key, out _);
            }
        }
    }

    // Referenced only to keep the error type meaningful if a future revision emits it; the proxy currently
    // rejects a bad frame by dropping the connection rather than sending PROXY_ERROR.
    internal static ushort ErrorType => ProxyTypeError;
}
