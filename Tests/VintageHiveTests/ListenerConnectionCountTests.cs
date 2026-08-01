// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// The accept loop's generic catch logged and then fell through instead of continuing, so a non-SocketException
// from AcceptAsync carried a null connection into Task.Run. Both Interlocked.Increment and the NetworkStream
// constructor sat OUTSIDE the try/finally, so that iteration incremented the active-connection count, threw,
// and never ran the matching decrement - the admin dashboard's per-service count drifted upward permanently
// and the loop hot-spun.
//
// The invariant worth pinning is the one the count exists for: however a connection ends - cleanly, refused
// mid-handshake, or reset between accept and setup - the counter must come back to where it started.
//
// HONEST SCOPE, established by mutation testing rather than assumed:
//
// None of these three reproduce the generic-catch fallthrough, and re-introducing the ENTIRE original shape
// (no `continue`, NetworkStream built outside the try) leaves all three green. Two reasons, both worth
// knowing before someone writes a fourth test expecting otherwise:
//
//  1. An accepted socket still reports Connected after the peer resets, so the NetworkStream constructor does
//     not throw there - the failure surfaces later, on read, which was always inside the try.
//  2. Closing the listening socket out from under AcceptAsync - what Stop() does, and the only other way to
//     fail an accept on demand - raises SocketException, which the FIRST catch already handled correctly with
//     its own continue. The generic catch is only reachable by an exception type nothing here can provoke.
//
// So the leak fix is defence in depth and is structurally guaranteed rather than test-provable: the increment
// now sits immediately before the try and the stream construction inside it, so the finally cannot be skipped
// whatever throws. What these tests DO pin is the observable contract - the count balances across abrupt
// resets and ordinary traffic, and Stop() unblocks the accept loop, ends the thread, and deregisters the
// listener from the admin dashboard. That last one is real coverage for the shutdown path that did not exist.

using System.Net;
using System.Net.Sockets;
using VintageHive.Network;

namespace Adversarial5.ListenerCount;

[TestClass]
public class ListenerConnectionCountTests
{
    // Accepts, answers nothing, and hangs up. Enough to drive the base class's whole per-connection path.
    private sealed class NullListener : Listener
    {
        public NullListener(int port) : base(IPAddress.Loopback, port, SocketType.Stream, ProtocolType.Tcp, false) { }

        public override Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
        {
            return Task.FromResult<byte[]>(null);
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail(because);
    }

    [TestMethod]
    [Timeout(60000)]
    public async Task ConnectionsThatDieDuringSetup_DoNotLeakTheActiveCount()
    {
        var listener = new NullListener(FreePort());

        listener.Start();

        await WaitUntil(() => listener.IsListening, "The listener never started.");

        // A burst of connections that are torn down with a reset the instant they are accepted. This is the
        // shape that makes per-connection setup throw before the handler is ever reached.
        for (var i = 0; i < 40; i++)
        {
            var client = new TcpClient();

            try
            {
                await client.ConnectAsync(IPAddress.Loopback, listener.Port);

                // Zero linger turns Close into an RST rather than a graceful FIN.
                client.Client.LingerState = new LingerOption(true, 0);
            }
            catch (SocketException)
            {
                // The listener is mid-accept; nothing to assert about this one.
            }
            finally
            {
                client.Dispose();
            }
        }

        await WaitUntil(() => listener.ActiveConnections == 0, $"Active connections settled at {listener.ActiveConnections} rather than 0, so a connection that died during setup leaked its count.");

        Assert.AreEqual(0, listener.ActiveConnections);

        listener.Stop();
    }

    // Covers the shutdown path itself, which had no coverage at all because Stop() did not exist. Closing the
    // listening socket under a parked AcceptAsync surfaces as SocketException, so this exercises the FIRST
    // catch rather than the generic one - see the note at the top of this file.
    [TestMethod]
    [Timeout(60000)]
    public async Task StoppingAListener_UnblocksTheAcceptLoopWithoutLeaking()
    {
        var listener = new NullListener(FreePort());

        listener.Start();

        await WaitUntil(() => listener.IsListening, "The listener never started.");

        var before = listener.ActiveConnections;

        listener.Stop();

        await WaitUntil(() => !listener.IsListening, "Stop() did not clear IsListening.");

        // The accept loop must actually exit rather than spin on a dead socket.
        await WaitUntil(() => listener.ProcessThread == null || !listener.ProcessThread.IsAlive, "The accept loop is still running after Stop(), so it is spinning on a closed socket.");

        Assert.AreEqual(before, listener.ActiveConnections, $"Stopping the listener changed the active-connection count from {before} to {listener.ActiveConnections}, so the accept failure leaked one.");

        Assert.IsFalse(Listener.ActiveListeners.Contains(listener), "A stopped listener is still being reported as active to the admin dashboard.");
    }

    // The ordinary path, as the control: a connection that is served and closed normally must also balance.
    [TestMethod]
    [Timeout(60000)]
    public async Task ConnectionsThatCompleteNormally_DoNotLeakTheActiveCount()
    {
        var listener = new NullListener(FreePort());

        listener.Start();

        await WaitUntil(() => listener.IsListening, "The listener never started.");

        for (var i = 0; i < 10; i++)
        {
            using var client = new TcpClient();

            await client.ConnectAsync(IPAddress.Loopback, listener.Port);

            await client.GetStream().WriteAsync("hello\r\n"u8.ToArray());

            client.Client.Shutdown(SocketShutdown.Both);
        }

        await WaitUntil(() => listener.ActiveConnections == 0, $"Active connections settled at {listener.ActiveConnections} rather than 0 after ordinary traffic.");

        Assert.AreEqual(0, listener.ActiveConnections);

        listener.Stop();
    }
}
