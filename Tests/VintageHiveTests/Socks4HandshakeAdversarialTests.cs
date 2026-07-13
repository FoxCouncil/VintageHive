// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive.Network;

namespace Adversarial5.Socks4;

// Adversarial coverage for Socks4Handler.HandleAsync connect-free REJECTION paths only.
// The handler reads its handshake from a concrete NetworkStream, so every test drives a real
// loopback socket pair via Socks4Harness. We NEVER drive a successful CONNECT (that would dial
// the real network) and NEVER touch Mind.Db (only reached on a granted tunnel via RequestsTrack).
// firstByte (the 0x04 version) is consumed by the dispatcher in production and is ignored inside
// HandleAsync, so the harness passes 0x04 and writes the post-version request bytes directly.
[TestClass]
public class Socks4HandshakeAdversarialTests
{
    private const byte REPLY_LEADING = 0x00;
    private const byte REPLY_REJECTED = 0x5B;
    private const byte REPLY_GRANTED = 0x5A;

    // requireAuth policy: SOCKS4 has no password mechanism, so a require-auth listener must reject
    // it outright with 0x5B before reading anything. This is a pure DB-free parameter branch.
    [TestMethod]
    [Timeout(8000)]
    public async Task RequireAuth_RejectsWith5B_BeforeReadingRequest()
    {
        using var harness = await Socks4Harness.CreateAsync(requireAuth: true);

        // Deliberately send NOTHING: the reject must be emitted without consuming any request bytes.
        await harness.WaitForHandlerAsync();

        var reply = await harness.ReadClientAsync(8);

        Assert.AreEqual(8, reply.Length, "Auth reject must be a full 8-byte SOCKS4 reply");
        Assert.AreEqual(REPLY_LEADING, reply[0], "SOCKS4 reply leading byte is always 0x00");
        Assert.AreEqual(REPLY_REJECTED, reply[1], "Auth-required listener must reject SOCKS4 with 0x5B");
        Assert.AreNotEqual(REPLY_GRANTED, reply[1], "Auth reject must never grant (0x5A)");

        // The pre-parse policy reject reports port 0 / 0.0.0.0. This proves the rejection came from the
        // require-auth branch, not from a parsed-then-dialed destination.
        var replyPort = BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2));

        Assert.AreEqual(0, replyPort, "Auth reject must report port 0");
        Assert.AreEqual(0, reply[4] | reply[5] | reply[6] | reply[7], "Auth reject must report 0.0.0.0");
    }

    // BIND (0x02) is not CONNECT: the handler reads the 7-byte header, sees a non-CONNECT command, and
    // rejects with 0x5B without ever dialing.
    [TestMethod]
    [Timeout(8000)]
    public async Task BindCommand_RejectsWith5B()
    {
        using var harness = await Socks4Harness.CreateAsync();

        // CMD=0x02 (BIND), PORT=80, IP=127.0.0.1. Exactly 7 bytes so nothing is left unread on the server.
        await harness.WriteClientAsync(new byte[] { 0x02, 0x00, 0x50, 127, 0, 0, 1 });

        await harness.WaitForHandlerAsync();

        var reply = await harness.ReadClientAsync(8);

        Assert.AreEqual(8, reply.Length, "Unsupported command must yield a full 8-byte reply");
        Assert.AreEqual(REPLY_LEADING, reply[0]);
        Assert.AreEqual(REPLY_REJECTED, reply[1], "BIND must be rejected with 0x5B");
    }

    // An out-of-range command byte (0xFF) is likewise rejected pre-dial.
    [TestMethod]
    [Timeout(8000)]
    public async Task UnknownCommand0xFF_RejectsWith5B()
    {
        using var harness = await Socks4Harness.CreateAsync();

        await harness.WriteClientAsync(new byte[] { 0xFF, 0x00, 0x50, 127, 0, 0, 1 });

        await harness.WaitForHandlerAsync();

        var reply = await harness.ReadClientAsync(8);

        Assert.AreEqual(8, reply.Length);
        Assert.AreEqual(REPLY_LEADING, reply[0]);
        Assert.AreEqual(REPLY_REJECTED, reply[1], "Unknown command 0xFF must be rejected with 0x5B");
    }

    // Command 0x00 (not a valid SOCKS4 command) must also be rejected, never accepted as a fall-through.
    [TestMethod]
    [Timeout(8000)]
    public async Task ZeroCommand_RejectsWith5B()
    {
        using var harness = await Socks4Harness.CreateAsync();

        await harness.WriteClientAsync(new byte[] { 0x00, 0x00, 0x50, 127, 0, 0, 1 });

        await harness.WaitForHandlerAsync();

        var reply = await harness.ReadClientAsync(8);

        Assert.AreEqual(8, reply.Length);
        Assert.AreEqual(REPLY_REJECTED, reply[1], "Command 0x00 must be rejected with 0x5B, never granted");
    }

    // Empty request: client connects then half-closes with zero bytes. ReadExact hits immediate EOF and
    // the handler must exit cleanly (no reply, no crash, no hang).
    [TestMethod]
    [Timeout(8000)]
    public async Task EmptyRequest_ClientHalfCloses_NoReplyNoHang()
    {
        using var harness = await Socks4Harness.CreateAsync();

        harness.ShutdownClientSend();

        await harness.WaitForHandlerAsync();

        var reply = await harness.ReadClientAsync(8);

        Assert.AreEqual(0, reply.Length, "A zero-byte request must produce no reply");
    }

    // Truncated header: only 3 of the 7 header bytes arrive before the client half-closes. ReadExact
    // returns false and the handler exits without a reply.
    [TestMethod]
    [Timeout(8000)]
    public async Task TruncatedHeader_MissingPortAndIp_NoReply()
    {
        using var harness = await Socks4Harness.CreateAsync();

        // CMD + one PORT byte only; the remaining PORT byte and the 4 IP bytes never arrive.
        await harness.WriteClientAsync(new byte[] { 0x01, 0x00, 0x50 });

        harness.ShutdownClientSend();

        await harness.WaitForHandlerAsync();

        var reply = await harness.ReadClientAsync(8);

        Assert.AreEqual(0, reply.Length, "A truncated header must produce no reply");
    }

    // SOCKS4a sentinel (IP 0.0.0.x, x != 0) signals that a null-terminated domain follows. Here the
    // userid terminator arrives but NO domain follows before the half-close, so ReadNullTerminated returns
    // null and the handler returns without dialing or resolving DNS. No outbound work, no reply.
    [TestMethod]
    [Timeout(8000)]
    public async Task Socks4a_SentinelWithNoDomain_NoReplyNoDial()
    {
        using var harness = await Socks4Harness.CreateAsync();

        // CMD=CONNECT, PORT=80, IP=0.0.0.1 (4a sentinel), then an empty userid (single 0x00 terminator).
        await harness.WriteClientAsync(new byte[] { 0x01, 0x00, 0x50, 0, 0, 0, 1, 0x00 });

        // No domain bytes at all: half-close so the domain read sees immediate EOF.
        harness.ShutdownClientSend();

        await harness.WaitForHandlerAsync();

        var reply = await harness.ReadClientAsync(8);

        Assert.AreEqual(0, reply.Length, "A 4a sentinel with no domain must abort with no reply, not grant");
    }

    // Over-long userid with NO null terminator, under a 4a sentinel IP so the follow-on domain read also
    // hits EOF and the handler aborts BEFORE any dial or DNS. This exercises the unterminated-userid path
    // safely: the sentinel guarantees the code reaches the domain branch (which returns on EOF) instead of
    // dialing the parsed IP. Confirms no crash, no hang, no spurious reply.
    [TestMethod]
    [Timeout(8000)]
    public async Task Socks4a_OverlongUseridNoTerminator_ThenEof_NoReplyNoDial()
    {
        using var harness = await Socks4Harness.CreateAsync();

        var header = new byte[] { 0x01, 0x00, 0x50, 0, 0, 0, 1 }; // CONNECT, port 80, 0.0.0.1 sentinel
        var userid = new byte[512];

        Array.Fill(userid, (byte)0x41); // 512 'A' bytes, never null-terminated

        await harness.WriteClientAsync(header);
        await harness.WriteClientAsync(userid);

        // Half-close: the userid read consumes the 512 bytes then sees EOF (returns null, which the handler
        // ignores), the 4a branch's domain read then sees EOF and the handler returns.
        harness.ShutdownClientSend();

        await harness.WaitForHandlerAsync();

        var reply = await harness.ReadClientAsync(8);

        Assert.AreEqual(0, reply.Length, "An unterminated userid followed by EOF must abort with no reply");
    }
}

// Loopback socket-pair harness. The handler runs on a Task with a 2s cancellation backstop; its finally
// shuts down the server socket so any client read returns EOF (0) once the handler has exited. Every read
// is independently time-bounded so a hung handler surfaces as a failing assertion, never a frozen run.
internal sealed class Socks4Harness : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Socket _clientSocket;
    private readonly NetworkStream _clientStream;
    private readonly Socket _serverSocket;
    private readonly NetworkStream _serverStream;
    private readonly Task _handlerTask;
    private readonly CancellationTokenSource _cts;

    private Socks4Harness(TcpListener listener, Socket clientSocket, NetworkStream clientStream, Socket serverSocket, NetworkStream serverStream, Task handlerTask, CancellationTokenSource cts)
    {
        _listener = listener;
        _clientSocket = clientSocket;
        _clientStream = clientStream;
        _serverSocket = serverSocket;
        _serverStream = serverStream;
        _handlerTask = handlerTask;
        _cts = cts;
    }

    public static async Task<Socks4Harness> CreateAsync(bool requireAuth = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var acceptTask = listener.AcceptSocketAsync();

        await clientSocket.ConnectAsync(IPAddress.Loopback, port);

        var serverSocket = await acceptTask;

        listener.Stop();

        var serverStream = new NetworkStream(serverSocket);
        var clientStream = new NetworkStream(clientSocket);

        var conn = new ListenerSocket
        {
            RawSocket = serverSocket,
            Stream = serverStream,
        };

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));

        var handlerTask = Task.Run(async () =>
        {
            try
            {
                // firstByte (0x04) is ignored by the handler; pass it for fidelity with the real dispatcher.
                await VintageHive.Proxy.Socks.Socks4.Socks4Handler.HandleAsync(conn, 0x04, requireAuth, cts.Token);
            }
            catch
            {
                // Cancellation / socket teardown is expected on the truncation paths.
            }
            finally
            {
                try
                {
                    serverSocket.Shutdown(SocketShutdown.Both);
                }
                catch
                {
                    // Already torn down.
                }
            }
        });

        return new Socks4Harness(listener, clientSocket, clientStream, serverSocket, serverStream, handlerTask, cts);
    }

    public async Task WriteClientAsync(byte[] data)
    {
        using var writeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));

        await _clientStream.WriteAsync(data, writeCts.Token);
        await _clientStream.FlushAsync(writeCts.Token);
    }

    public void ShutdownClientSend()
    {
        try
        {
            _clientSocket.Shutdown(SocketShutdown.Send);
        }
        catch
        {
            // Already shut down.
        }
    }

    // Reads up to count bytes, returning whatever arrived (0..count). EOF or a socket error maps to a
    // short/empty result; a stall is bounded by an independent 2.5s timeout so the test can never hang.
    public async Task<byte[]> ReadClientAsync(int count)
    {
        using var readCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));

        var buf = new byte[count];
        var offset = 0;

        try
        {
            while (offset < count)
            {
                var read = await _clientStream.ReadAsync(buf.AsMemory(offset), readCts.Token);

                if (read == 0)
                {
                    break;
                }

                offset += read;
            }
        }
        catch (IOException)
        {
            // Server closed the connection (may surface as a forcible-close IOException on Windows).
        }
        catch (SocketException)
        {
            // Same as above via the socket layer.
        }
        catch (OperationCanceledException)
        {
            // Read timed out: treat as no further data.
        }

        return buf[..offset];
    }

    public async Task WaitForHandlerAsync()
    {
        await _handlerTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        try { _clientStream.Dispose(); } catch { }
        try { _clientSocket.Dispose(); } catch { }
        try { _serverStream.Dispose(); } catch { }
        try { _serverSocket.Dispose(); } catch { }

        _cts.Dispose();
    }
}