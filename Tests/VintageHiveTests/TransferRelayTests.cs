// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Proxy.Transfer;

namespace Transfer;

internal static class RelayTestEnv
{
    private static readonly object Gate = new();
    private static bool _ready;

    public static void Ensure()
    {
        lock (Gate)
        {
            if (_ready)
            {
                return;
            }

            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "vfs", "data"));

            if (Mind.Db == null)
            {
                var setter = typeof(Mind).GetProperty(nameof(Mind.Db))!.GetSetMethod(nonPublic: true)!;
                setter.Invoke(null, new object[] { new HiveDbContext() });
            }

            _ready = true;
        }
    }
}

[TestClass]
public class TransferRelayCoreTests
{
    private int _savedConcurrency;
    private long _savedBytes;
    private TimeSpan _savedIdle;
    private TimeSpan _savedTtl;

    [TestInitialize]
    public void Setup()
    {
        RelayTestEnv.Ensure();

        _savedConcurrency = TransferRelay.MaxConcurrentTransfers;
        _savedBytes = TransferRelay.MaxBytesPerTransfer;
        _savedIdle = TransferRelay.IdleTimeout;
        _savedTtl = TransferRelay.TicketTtl;

        Mind.Db!.ConfigSet(ConfigNames.RelayTransfers, true);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortLow, 43110);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortHigh, 43119);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TransferRelay.MaxConcurrentTransfers = _savedConcurrency;
        TransferRelay.MaxBytesPerTransfer = _savedBytes;
        TransferRelay.IdleTimeout = _savedIdle;
        TransferRelay.TicketTtl = _savedTtl;

        Mind.Db!.ConfigSet(ConfigNames.RelayTransfers, false);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortLow, 1810);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortHigh, 1819);
    }

    [TestMethod]
    public void FlagOff_TryAllocate_ReturnsNull()
    {
        Mind.Db!.ConfigSet(ConfigNames.RelayTransfers, false);

        Assert.IsNull(TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback), "With the flag off no listener may be allocated - the whole compatibility contract");
    }

    [TestMethod]
    public void TryAllocate_BindsWithinTheConfiguredRange()
    {
        using var listener = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);

        Assert.IsNotNull(listener);
        Assert.IsTrue(listener.Port is >= 43110 and <= 43119, $"Allocated port {listener.Port} is outside the configured range");
    }

    [TestMethod]
    public void ConcurrencyCap_IsEnforced()
    {
        TransferRelay.MaxConcurrentTransfers = 3;

        var held = new List<RelayListener>();

        try
        {
            for (var i = 0; i < 3; i++)
            {
                var l = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);

                Assert.IsNotNull(l, $"Allocation {i} within the cap must succeed");

                held.Add(l);
            }

            Assert.IsNull(TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback), "The transfer past the concurrency cap must be refused");

            // Freeing one slot lets the next allocation through again.
            held[0].Dispose();
            held.RemoveAt(0);

            var recovered = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);

            Assert.IsNotNull(recovered, "Releasing a slot must let a new transfer allocate");

            held.Add(recovered);
        }
        finally
        {
            foreach (var l in held)
            {
                l.Dispose();
            }
        }
    }

    [TestMethod]
    public void MintTicket_ProducesDistinctTokensOfTheRightShape()
    {
        var a = TransferRelay.MintTicket();
        var b = TransferRelay.MintTicket();

        Assert.AreNotEqual(a, b);
        Assert.AreEqual(24, a.Length);
        Assert.IsTrue(a.All(char.IsLetterOrDigit), "Ticket must be URL/handshake-safe alphanumerics");
    }

    [TestMethod]
    public async Task PumpAsync_EnforcesTheSizeCap()
    {
        var (client, server) = await LoopbackPairAsync();

        try
        {
            var budget = new StrongBoxLong(8);
            var sink = new MemoryStream();

            var pump = TransferRelay.PumpAsync(server.GetStream(), sink, budget, CancellationToken.None);

            // 16 bytes against an 8-byte budget must trip the cap.
            await client.GetStream().WriteAsync(new byte[16]);
            await client.GetStream().FlushAsync();

            var threw = false;

            try
            {
                await pump;
            }
            catch (RelayBoundExceeded)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "Exceeding the byte budget must throw RelayBoundExceeded");
        }
        finally
        {
            client.Dispose();
            server.Dispose();
        }
    }

    [TestMethod]
    public async Task PumpAsync_EnforcesTheIdleTimeout()
    {
        TransferRelay.IdleTimeout = TimeSpan.FromMilliseconds(250);

        var (client, server) = await LoopbackPairAsync();

        try
        {
            var budget = new StrongBoxLong(1024);
            var sink = new MemoryStream();

            // The client connects but never sends; the pump must trip the idle timeout, not hang.
            var threw = false;

            try
            {
                await TransferRelay.PumpAsync(server.GetStream(), sink, budget, CancellationToken.None);
            }
            catch (RelayBoundExceeded)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "A stalled source must trip the idle timeout");
        }
        finally
        {
            client.Dispose();
            server.Dispose();
        }
    }

    [TestMethod]
    public async Task SpliceAsync_MovesBytesBothDirections()
    {
        // Two independent loopback pairs; the relay splices the server ends, so writing into one client's
        // socket must come out of the other's.
        var (clientA, serverA) = await LoopbackPairAsync();
        var (clientB, serverB) = await LoopbackPairAsync();

        var splice = TransferRelay.SpliceAsync(serverA.GetStream(), serverB.GetStream(), CancellationToken.None);

        try
        {
            await clientA.GetStream().WriteAsync(Encoding.ASCII.GetBytes("a->b"));
            await clientA.GetStream().FlushAsync();

            var buf = new byte[4];
            await ReadExactAsync(clientB.GetStream(), buf);
            Assert.AreEqual("a->b", Encoding.ASCII.GetString(buf));

            await clientB.GetStream().WriteAsync(Encoding.ASCII.GetBytes("b->a"));
            await clientB.GetStream().FlushAsync();

            var buf2 = new byte[4];
            await ReadExactAsync(clientA.GetStream(), buf2);
            Assert.AreEqual("b->a", Encoding.ASCII.GetString(buf2));
        }
        finally
        {
            clientA.Dispose();
            serverA.Dispose();
            clientB.Dispose();
            serverB.Dispose();

            try { await splice; } catch { }
        }
    }

    // -- RelaySession end-to-end over a real allocated listener ----------------

    // A trivial handshake: the party sends its ticket then a role byte ('S'/'R') as one line; a mismatch is
    // rejected. This exercises the core's "match by ticket, refuse strangers, pump" behavior without any
    // protocol adapter.
    static async Task<RelayParty> LineHandshake(Socket socket, string ticket, CancellationToken ct)
    {
        var stream = new NetworkStream(socket);

        var line = await ReadLineAsync(stream, ct);

        var parts = line.Split(' ');

        if (parts.Length < 2 || parts[0] != ticket)
        {
            return null;
        }

        var role = parts[1] == "S" ? RelayRole.Sender : RelayRole.Receiver;

        return new RelayParty(role, socket, stream);
    }

    [TestMethod]
    public async Task HostAsync_PairsTwoMatchingParties_ThenSplices()
    {
        using var listener = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);
        var ticket = TransferRelay.MintTicket();

        var host = RelaySession.HostAsync(listener, (s, ct) => LineHandshake(s, ticket, ct), (a, b, ct) => TransferRelay.SpliceAsync(a.Stream, b.Stream, ct), CancellationToken.None);

        using var sender = ConnectTo(listener.Port);
        using var receiver = ConnectTo(listener.Port);

        await SendLineAsync(sender, $"{ticket} S");
        await SendLineAsync(receiver, $"{ticket} R");

        await sender.GetStream().WriteAsync(Encoding.ASCII.GetBytes("payload!"));
        await sender.GetStream().FlushAsync();

        var buf = new byte[8];
        await ReadExactAsync(receiver.GetStream(), buf);

        Assert.AreEqual("payload!", Encoding.ASCII.GetString(buf), "Bytes from the sender must reach the receiver once both handshakes match");

        sender.Dispose();
        receiver.Dispose();

        try { await host; } catch { }
    }

    [TestMethod]
    public async Task HostAsync_RefusesAWrongTicket_AndStillPairsTheRealParties()
    {
        using var listener = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);
        var ticket = TransferRelay.MintTicket();

        var host = RelaySession.HostAsync(listener, (s, ct) => LineHandshake(s, ticket, ct), (a, b, ct) => TransferRelay.SpliceAsync(a.Stream, b.Stream, ct), CancellationToken.None);

        // A stranger presents the wrong ticket and must be dropped, not paired.
        using (var intruder = ConnectTo(listener.Port))
        {
            await SendLineAsync(intruder, "not-the-ticket S");

            // The intruder's connection is closed by the host; a read returns EOF.
            var eof = await intruder.GetStream().ReadAsync(new byte[1]);
            Assert.AreEqual(0, eof, "A wrong-ticket connection must be refused and closed");
        }

        using var sender = ConnectTo(listener.Port);
        using var receiver = ConnectTo(listener.Port);

        await SendLineAsync(sender, $"{ticket} S");
        await SendLineAsync(receiver, $"{ticket} R");

        await sender.GetStream().WriteAsync(Encoding.ASCII.GetBytes("realdata"));
        await sender.GetStream().FlushAsync();

        var buf = new byte[8];
        await ReadExactAsync(receiver.GetStream(), buf);

        Assert.AreEqual("realdata", Encoding.ASCII.GetString(buf), "After refusing the stranger the relay must still pair the two real parties");

        sender.Dispose();
        receiver.Dispose();

        try { await host; } catch { }
    }

    [TestMethod]
    public async Task HostAsync_DropsAnUnclaimedTicketAfterTtl()
    {
        TransferRelay.TicketTtl = TimeSpan.FromMilliseconds(300);

        using var listener = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);
        var port = listener.Port;
        var ticket = TransferRelay.MintTicket();

        var host = RelaySession.HostAsync(listener, (s, ct) => LineHandshake(s, ticket, ct), (a, b, ct) => TransferRelay.SpliceAsync(a.Stream, b.Stream, ct), CancellationToken.None);

        // Only one side ever connects; after the TTL the host tears the listener down and returns.
        using var lonely = ConnectTo(port);
        await SendLineAsync(lonely, $"{ticket} S");

        await host; // completes when the TTL fires; if it hangs the test times out

        // The listener/port is released - a fresh allocation can bind the same port again.
        using var again = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);
        Assert.IsNotNull(again, "After an unclaimed ticket is dropped its slot must be free again");
    }

    // -- helpers ---------------------------------------------------------------

    static async Task<(TcpClient client, TcpClient server)> LoopbackPairAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var client = new TcpClient();
        var acceptTask = listener.AcceptTcpClientAsync();

        await client.ConnectAsync(IPAddress.Loopback, port);

        var server = await acceptTask;

        listener.Stop();

        return (client, server);
    }

    static TcpClient ConnectTo(int port)
    {
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        return client;
    }

    static async Task SendLineAsync(TcpClient client, string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\n");
        await client.GetStream().WriteAsync(bytes);
        await client.GetStream().FlushAsync();
    }

    static async Task<string> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), ct);

            if (read <= 0 || one[0] == (byte)'\n')
            {
                break;
            }

            sb.Append((char)one[0]);
        }

        return sb.ToString();
    }

    static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;

        using var cts = new CancellationTokenSource(5000);

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cts.Token);

            if (read <= 0)
            {
                throw new IOException("Closed mid-read");
            }

            offset += read;
        }
    }
}
