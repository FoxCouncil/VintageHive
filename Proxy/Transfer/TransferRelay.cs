// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using VintageHive.Data.Types;

namespace VintageHive.Proxy.Transfer;

/// <summary>
/// The shared core of the NAT-crossing file-transfer relays (relaytransfers). Every period IM file transfer
/// negotiates a direct peer connection and advertises the address the client believes it holds - a LAN
/// address behind NAT - so two members on different networks can never connect. The era's own answer was a
/// server in the middle (Microsoft from MSNP8+, AOL's rendezvous proxy, Yahoo's HTTP filetransfer hosts). A
/// walled network needs the same, because outbound-only is the sole reachable direction for both parties.
/// <para>
/// This class owns the transport-agnostic pieces: allocating a per-transfer listener on the member-reachable
/// address from the configured port range, minting the short-lived ticket that pairs the two sides, the
/// bounded bidirectional pump, and the mandatory safety rails (max concurrent transfers, max bytes per
/// transfer, idle timeout, and a TTL after which an unclaimed listener is dropped). The three protocol
/// adapters (MSN, AIM, Yahoo) speak their own handshakes on top of these primitives. Nothing is buffered to
/// disk and nothing is stored for an offline recipient - the relay streams through, both sides live.
/// </para>
/// <para>
/// Gated entirely on <see cref="ConfigNames.RelayTransfers"/>: with the flag off, <see cref="Enabled"/> is
/// false, every allocation returns null, and each adapter falls back to passing the invitation through
/// untouched - exactly today's behavior.
/// </para>
/// </summary>
public sealed class TransferRelay
{
    public static TransferRelay Instance { get; } = new();

    // Read per operation (like the FTP passive range and the roster switches) so a ConfigSet applies without a
    // restart. RelayTransfers off is the whole compatibility contract: no listener is ever allocated.
    public static bool Enabled => Mind.Db?.ConfigGet<bool>(ConfigNames.RelayTransfers) ?? false;

    static int PortLow => Mind.Db?.ConfigGet<int>(ConfigNames.RelayPortLow) ?? 1810;

    static int PortHigh => Mind.Db?.ConfigGet<int>(ConfigNames.RelayPortHigh) ?? 1819;

    // Deliberately NOT config keys. These are operational safety rails, not knobs an operator tunes per
    // deployment: a transfer that blows past any of them is a bug or an abuse, not a preference. They are
    // internal statics rather than consts so the test sweep can shrink them (via InternalsVisibleTo) to
    // prove each bound is actually enforced without moving hundreds of megabytes or waiting minutes.
    internal static int MaxConcurrentTransfers = 16;

    internal static long MaxBytesPerTransfer = 100L * 1024 * 1024; // 100 MB

    internal static TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    // How long an allocated listener waits for BOTH sides to connect before it is torn down and its ticket
    // dropped. A transfer the recipient never accepts must not pin a port forever.
    internal static TimeSpan TicketTtl = TimeSpan.FromSeconds(120);

    const string TicketAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    const int TicketLength = 24;

    // The ports currently bound by live transfers, so two concurrent allocations never race onto the same one.
    readonly ConcurrentDictionary<int, byte> _reservedPorts = new();

    int _activeCount;

    /// <summary>Transfers holding a listener right now. Diagnostic and the concurrency-cap counter.</summary>
    public int ActiveCount => Volatile.Read(ref _activeCount);

    /// <summary>
    /// Reserves a concurrency slot and binds a fresh listener on a free port in the configured range. Returns
    /// null when the flag is off, the concurrency cap is reached, or the whole range is busy - the adapter
    /// then leaves the invitation untouched, so a relay that cannot allocate degrades to today's direct-attempt
    /// behavior rather than failing the transfer outright.
    /// </summary>
    public RelayListener TryAllocateListener(IPAddress bindAddress)
    {
        if (!Enabled)
        {
            return null;
        }

        if (Interlocked.Increment(ref _activeCount) > MaxConcurrentTransfers)
        {
            Interlocked.Decrement(ref _activeCount);

            return null;
        }

        for (var port = PortLow; port <= PortHigh; port++)
        {
            if (!_reservedPorts.TryAdd(port, 0))
            {
                continue;
            }

            try
            {
                var listener = new TcpListener(bindAddress, port);

                listener.Start();

                return new RelayListener(this, listener, port);
            }
            catch (SocketException)
            {
                // Port busy at the OS level (something outside the relay holds it); release the reservation
                // and try the next one.
                _reservedPorts.TryRemove(port, out _);
            }
        }

        // Range exhausted: give the slot back.
        Interlocked.Decrement(ref _activeCount);

        return null;
    }

    // Called once by RelayListener.Dispose; releasing twice would corrupt the active count, which Dispose's
    // interlocked guard prevents.
    internal void Release(int port)
    {
        _reservedPorts.TryRemove(port, out _);

        Interlocked.Decrement(ref _activeCount);
    }

    /// <summary>A fresh transfer ticket: the bearer token that pairs the two sides of one transfer.</summary>
    public static string MintTicket()
    {
        var chars = new char[TicketLength];

        Span<byte> buffer = stackalloc byte[1];

        for (var i = 0; i < TicketLength; i++)
        {
            int value;

            do
            {
                RandomNumberGenerator.Fill(buffer);

                value = buffer[0];
            }
            while (value >= 256 - (256 % TicketAlphabet.Length));

            chars[i] = TicketAlphabet[value % TicketAlphabet.Length];
        }

        return new string(chars);
    }

    /// <summary>
    /// The address to advertise to clients for a relayed transfer: the address DnsProxy answers with (by
    /// definition reachable, since clients found the server through it), rejecting the wildcard bind. Mirrors
    /// MsnServer.AdvertisedAddress and GopherServer.ResolveAdvertisedHost - the same "member-reachable
    /// address" the rest of the server already computes this way. Falls back to <paramref name="fallback"/>
    /// (a live connection's local address) when the operator left the IP at the wildcard default.
    /// </summary>
    public static IPAddress ResolveAdvertisedAddress(IPAddress fallback)
    {
        var configured = Mind.Db?.ConfigGet<string>(ConfigNames.IpAddress);

        if (!string.IsNullOrWhiteSpace(configured) && IPAddress.TryParse(configured, out var parsed) && !parsed.Equals(IPAddress.Any))
        {
            return parsed;
        }

        return fallback;
    }

    /// <summary>
    /// Copies one direction of a transfer with the mandatory bounds: at most <paramref name="budget"/> bytes
    /// (shared across both directions of a splice via the boxed counter) and no single read idle for longer
    /// than <see cref="IdleTimeout"/>. Returns when the source ends; throws <see cref="RelayBoundExceeded"/>
    /// on a cap or idle breach so the caller tears the whole transfer down rather than trusting a stalled or
    /// oversized peer.
    /// </summary>
    public static async Task PumpAsync(Stream from, Stream to, StrongBoxLong budget, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];

        while (true)
        {
            int read;

            using (var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                idleCts.CancelAfter(IdleTimeout);

                try
                {
                    read = await from.ReadAsync(buffer, idleCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new RelayBoundExceeded("idle timeout");
                }
            }

            if (read <= 0)
            {
                return;
            }

            if (budget.Decrement(read) < 0)
            {
                throw new RelayBoundExceeded("size cap");
            }

            await to.WriteAsync(buffer.AsMemory(0, read), ct);

            await to.FlushAsync(ct);
        }
    }

    /// <summary>
    /// Pumps both directions between two already-handshaked parties until either side ends or a bound is
    /// breached, then cancels the other direction and returns. The byte budget is shared, so the cap is on the
    /// whole transfer, not per direction.
    /// </summary>
    public static async Task SpliceAsync(Stream a, Stream b, CancellationToken ct)
    {
        var budget = new StrongBoxLong(MaxBytesPerTransfer);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var forward = PumpAsync(a, b, budget, cts.Token);
        var reverse = PumpAsync(b, a, budget, cts.Token);

        await Task.WhenAny(forward, reverse);

        cts.Cancel();

        try
        {
            await Task.WhenAll(forward, reverse);
        }
        catch
        {
            // The loser of the race is cancelled (or already faulted on the bound that ended the transfer);
            // neither outcome is an error worth surfacing past the winner.
        }
    }
}

/// <summary>A shared, atomically-decrementing byte budget for a transfer. Returns the remaining balance after
/// each decrement so a caller can detect the moment it goes negative (the cap was breached).</summary>
public sealed class StrongBoxLong
{
    long _value;

    public StrongBoxLong(long initial)
    {
        _value = initial;
    }

    public long Decrement(long by)
    {
        return Interlocked.Add(ref _value, -by);
    }

    public long Value => Interlocked.Read(ref _value);
}

/// <summary>Thrown when a transfer breaches a relay bound (size cap or idle timeout). Caught by the relay
/// session, which tears the transfer down; never surfaced to a client as anything but a dropped connection.</summary>
public sealed class RelayBoundExceeded : Exception
{
    public RelayBoundExceeded(string reason) : base($"Relay bound exceeded: {reason}") { }
}

/// <summary>
/// A bound per-transfer listener plus its reserved concurrency slot. Disposing it stops the listener and
/// returns the slot and the port to the pool exactly once, however the transfer ended.
/// </summary>
public sealed class RelayListener : IDisposable
{
    readonly TransferRelay _owner;

    readonly TcpListener _listener;

    int _disposed;

    public int Port { get; }

    internal RelayListener(TransferRelay owner, TcpListener listener, int port)
    {
        _owner = owner;
        _listener = listener;
        Port = port;
    }

    /// <summary>Accepts the next inbound connection, or throws when the listener is torn down (TTL, cap, done).</summary>
    public Task<Socket> AcceptAsync(CancellationToken ct)
    {
        return _listener.AcceptSocketAsync(ct).AsTask();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _listener.Stop();
        }
        catch
        {
            // Already gone.
        }

        _owner.Release(Port);
    }
}
