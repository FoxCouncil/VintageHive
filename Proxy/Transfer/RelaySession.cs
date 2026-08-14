// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net.Sockets;

namespace VintageHive.Proxy.Transfer;

/// <summary>Which end of a transfer a connected party is. The pump uses it to orient one-directional data
/// (MSNFTP, OFT): bytes flow from the <see cref="Sender"/> to the <see cref="Receiver"/>.</summary>
public enum RelayRole
{
    Sender,
    Receiver,
}

/// <summary>One connected, handshake-verified side of a transfer: its role, and the stream the pump reads
/// from or writes to (positioned by the handshake past any protocol preamble it consumed).</summary>
public sealed class RelayParty
{
    public RelayParty(RelayRole role, Socket socket, Stream stream)
    {
        Role = role;
        Socket = socket;
        Stream = stream;
    }

    public RelayRole Role { get; }

    public Socket Socket { get; }

    public Stream Stream { get; }

    public void Close()
    {
        try { Stream.Dispose(); } catch { }
        try { Socket.Close(); } catch { }
    }
}

/// <summary>
/// Drives one relayed transfer over an allocated <see cref="RelayListener"/>: accept connections, verify each
/// with the adapter's handshake, and once two verified parties are present hand them to the adapter's pump.
/// This is the "accept exactly two connections, match them by ticket, then pump" core, shared by the MSN and
/// AIM adapters - each supplies a handshake that reads and checks its own protocol's carriage of the ticket
/// (MSNFTP's AuthCookie, the AIM proxy frame's cookie) and a pump that moves the bytes the way its wire wants.
/// </summary>
public static class RelaySession
{
    /// <summary>
    /// Owns the listener for the life of the transfer and disposes it when done. Accepts until two parties
    /// pass <paramref name="handshake"/> or <see cref="TransferRelay.TicketTtl"/> elapses; a connection whose
    /// handshake rejects it (wrong or missing ticket) is dropped and the wait continues, so a stranger probing
    /// the port cannot claim someone else's transfer. If the second side never arrives the listener is torn
    /// down and the ticket is dropped. With both present, <paramref name="pump"/> runs to completion, then both
    /// sides are closed.
    /// </summary>
    public static async Task HostAsync(RelayListener listener, Func<Socket, CancellationToken, Task<RelayParty>> handshake, Func<RelayParty, RelayParty, CancellationToken, Task> pump, CancellationToken outerCt)
    {
        var parties = new List<RelayParty>();

        try
        {
            using var ttlCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);

            ttlCts.CancelAfter(TransferRelay.TicketTtl);

            while (parties.Count < 2)
            {
                Socket socket;

                try
                {
                    socket = await listener.AcceptAsync(ttlCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // TTL elapsed (or the whole relay is shutting down) before both sides connected.
                    break;
                }
                catch (Exception)
                {
                    // Listener torn down under us.
                    break;
                }

                RelayParty party = null;

                try
                {
                    party = await handshake(socket, ttlCts.Token);
                }
                catch (Exception)
                {
                    party = null;
                }

                if (party == null)
                {
                    try { socket.Close(); } catch { }

                    continue;
                }

                parties.Add(party);
            }

            if (parties.Count < 2)
            {
                return;
            }

            // Orient by role, tolerating a protocol that labelled both the same (arrival order then decides)
            // rather than throwing mid-transfer.
            var sender = parties.FirstOrDefault(p => p.Role == RelayRole.Sender) ?? parties[0];
            var receiver = parties.FirstOrDefault(p => !ReferenceEquals(p, sender)) ?? parties[1];

            await pump(sender, receiver, outerCt);
        }
        finally
        {
            foreach (var party in parties)
            {
                party.Close();
            }

            listener.Dispose();
        }
    }
}
