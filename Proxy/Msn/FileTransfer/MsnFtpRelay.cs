// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net.Sockets;
using VintageHive.Proxy.Transfer;

namespace VintageHive.Proxy.Msn.FileTransfer;

/// <summary>
/// The MSNFTP half of the MSN file-transfer relay. Both members are made to dial OUT to the allocated relay
/// listener (the invite rewrite points the file-sender at the relay; an injected role-flip message points the
/// receiver at it too), so the relay is the sole TCP server and terminates MSNFTP with each side rather than
/// blindly splicing - the handshake is asymmetric and the relay has to play the receiver's role toward the
/// sender and the sender's role toward the receiver.
/// <para>
/// UNVERIFIED CHOREOGRAPHY, DOCUMENTED ON PURPOSE. MSN itself never relayed old-style MSNFTP - its server
/// relay arrived with MSNP2P on MSNP8+, a generation MsnServer does not serve - so there is no captured
/// reference for two NAT'd MSNP2-7 peers both dialing a relay. The termination below (VER/USR ticket check on
/// each leg, then classify the data-ready side by who sends FIL unprompted, then FIL/TFR toward the receiver,
/// then pump) is reasoned from the one-listener MSNFTP handshake, not read off a capture. It is isolated here
/// so a real client trace can correct it in one place. The rewrite and the ticket pairing are exercised at the
/// byte level by the tests; only the live client's willingness to connect out after an injected role-flip is
/// the open question.
/// </para>
/// </summary>
public static class MsnFtpRelay
{
    sealed class Leg
    {
        public Leg(Socket socket, Stream stream, string account)
        {
            Socket = socket;
            Stream = stream;
            Account = account;
        }

        public Socket Socket { get; }

        public Stream Stream { get; }

        public string Account { get; }

        public void Close()
        {
            try { Stream.Dispose(); } catch { }
            try { Socket.Close(); } catch { }
        }
    }

    /// <summary>
    /// Owns the listener for the life of the transfer. Accepts two connections whose MSNFTP USR carries the
    /// ticket, classifies which is the file-sender, completes the FIL/TFR handshake toward the receiver, then
    /// pumps the data stream sender to receiver under the relay bounds. Torn down on the TTL if the second side
    /// never arrives.
    /// </summary>
    public static async Task HostAsync(RelayListener listener, string ticket, long knownFileSize, CancellationToken outerCt)
    {
        var legs = new List<Leg>();

        try
        {
            using var ttlCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);

            ttlCts.CancelAfter(TransferRelay.TicketTtl);

            // Phase 1: accept two connections, each VER/USR-verified against the ticket. A wrong ticket is
            // dropped and the wait continues, so a stranger cannot claim the transfer.
            while (legs.Count < 2)
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

                Leg leg = null;

                try
                {
                    leg = await VerifyAsync(socket, ticket, ttlCts.Token);
                }
                catch (Exception)
                {
                    leg = null;
                }

                if (leg == null)
                {
                    try { socket.Close(); } catch { }

                    continue;
                }

                legs.Add(leg);
            }

            if (legs.Count < 2)
            {
                return;
            }

            // Phase 2: classify. The file-sender pushes "FIL <size>" unprompted; the receiver stays silent
            // waiting for it. Read the next line from both at once - whoever yields FIL first is the sender,
            // and the other's still-pending read becomes the read that later collects its TFR.
            var readA = ReadLineAsync(legs[0].Stream, ttlCts.Token);
            var readB = ReadLineAsync(legs[1].Stream, ttlCts.Token);

            var firstDone = await Task.WhenAny(readA, readB);

            string firstLine;

            try
            {
                firstLine = await firstDone;
            }
            catch
            {
                return;
            }

            if (firstLine == null || !firstLine.StartsWith("FIL", StringComparison.Ordinal))
            {
                return;
            }

            var sender = firstDone == readA ? legs[0] : legs[1];
            var receiver = firstDone == readA ? legs[1] : legs[0];
            var receiverPendingRead = firstDone == readA ? readB : readA;

            var size = ParseFilSize(firstLine, knownFileSize);

            // Phase 3: the relay is the MSNFTP server toward the receiver - announce the file, collect TFR
            // (the receiver's already-pending read completes with it) - and toward the sender it is the
            // receiver, so TFR back to the sender starts the data flowing.
            await WriteLineAsync(receiver.Stream, $"FIL {size}", outerCt);

            try
            {
                await receiverPendingRead; // "TFR"
            }
            catch
            {
                return;
            }

            await WriteLineAsync(sender.Stream, "TFR", outerCt);

            // Phase 4: pump the data blocks (and the trailing BYE) one direction, sender to receiver, under
            // the shared byte budget and idle timeout.
            var budget = new StrongBoxLong(TransferRelay.MaxBytesPerTransfer);

            await TransferRelay.PumpAsync(sender.Stream, receiver.Stream, budget, outerCt);
        }
        catch (Exception)
        {
            // A bound breach or a dropped peer ends the transfer; nothing here is surfaced to a client as
            // anything but a closed connection.
        }
        finally
        {
            foreach (var leg in legs)
            {
                leg.Close();
            }

            listener.Dispose();
        }
    }

    // VER MSNFTP / USR <account> <cookie>: the relay answers VER and checks the ticket. Returns null (reject)
    // on anything that is not this exact opening with the matching cookie.
    static async Task<Leg> VerifyAsync(Socket socket, string ticket, CancellationToken ct)
    {
        var stream = new NetworkStream(socket);

        var ver = await ReadLineAsync(stream, ct);

        if (ver == null || !ver.StartsWith("VER", StringComparison.Ordinal))
        {
            return null;
        }

        await WriteLineAsync(stream, "VER MSNFTP", ct);

        var usr = await ReadLineAsync(stream, ct);

        if (usr == null)
        {
            return null;
        }

        var parts = usr.Split(' ');

        if (parts.Length < 3 || parts[0] != "USR" || parts[2] != ticket)
        {
            return null;
        }

        return new Leg(socket, stream, parts[1]);
    }

    static long ParseFilSize(string filLine, long fallback)
    {
        var parts = filLine.Split(' ');

        if (parts.Length >= 2 && long.TryParse(parts[1], out var size) && size >= 0)
        {
            return size;
        }

        return fallback;
    }

    // MSNFTP handshake lines are short CRLF-terminated ASCII. Read one byte at a time so the read stops exactly
    // at the newline and never swallows the binary data blocks that follow the handshake.
    static async Task<string> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var builder = new StringBuilder();

        var one = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), ct);

            if (read <= 0)
            {
                return builder.Length == 0 ? null : builder.ToString();
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            if (one[0] != (byte)'\r')
            {
                builder.Append((char)one[0]);
            }
        }

        return builder.ToString();
    }

    static async Task WriteLineAsync(Stream stream, string line, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");

        await stream.WriteAsync(bytes, ct);

        await stream.FlushAsync(ct);
    }
}
