// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using VintageHive.Network;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  Test helpers: concrete UdpListener subclasses
// ──────────────────────────────────────────────────────────

/// <summary>Simple echo server: returns the received datagram unchanged.</summary>
internal class EchoUdpServer : UdpListener
{
    public int DatagramsReceived;

    public EchoUdpServer(IPAddress address, int port) : base(address, port) { }

    public override Task<byte[]> ProcessDatagram(IPEndPoint remoteEndPoint, byte[] data, int length)
    {
        Interlocked.Increment(ref DatagramsReceived);
        var echo = new byte[length];
        Array.Copy(data, echo, length);
        return Task.FromResult(echo);
    }
}

/// <summary>Silent server: receives datagrams but returns no response.</summary>
internal class SilentUdpServer : UdpListener
{
    public int DatagramsReceived;

    public SilentUdpServer(IPAddress address, int port) : base(address, port) { }

    public override Task<byte[]> ProcessDatagram(IPEndPoint remoteEndPoint, byte[] data, int length)
    {
        Interlocked.Increment(ref DatagramsReceived);
        return Task.FromResult<byte[]>(null!);
    }
}

/// <summary>Transform server: returns data with each byte incremented by 1.</summary>
internal class TransformUdpServer : UdpListener
{
    public TransformUdpServer(IPAddress address, int port) : base(address, port) { }

    public override Task<byte[]> ProcessDatagram(IPEndPoint remoteEndPoint, byte[] data, int length)
    {
        var result = new byte[length];

        for (var i = 0; i < length; i++)
        {
            result[i] = (byte)(data[i] + 1);
        }

        return Task.FromResult(result);
    }
}

/// <summary>Server that records the remote endpoint of each datagram.</summary>
internal class EndpointTrackingServer : UdpListener
{
    public readonly List<IPEndPoint> ReceivedFrom = new();

    public EndpointTrackingServer(IPAddress address, int port) : base(address, port) { }

    public override Task<byte[]> ProcessDatagram(IPEndPoint remoteEndPoint, byte[] data, int length)
    {
        lock (ReceivedFrom)
        {
            ReceivedFrom.Add(remoteEndPoint);
        }

        return Task.FromResult(data);
    }
}

// ──────────────────────────────────────────────────────────
//  UdpListener base class tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class UdpListenerTests
{
    private static readonly IPAddress Loopback = IPAddress.Loopback;

    [TestMethod]
    public async Task EchoServer_RoundTrip()
    {
        var server = new EchoUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            using var client = new UdpClient();
            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            await client.SendAsync(payload, payload.Length, serverEndpoint);

            client.Client.ReceiveTimeout = 3000;
            var result = await client.ReceiveAsync();

            CollectionAssert.AreEqual(payload, result.Buffer);
            Assert.AreEqual(1, server.DatagramsReceived);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task EchoServer_MultipleDatagrams()
    {
        var server = new EchoUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            for (var i = 0; i < 5; i++)
            {
                var payload = new byte[] { (byte)i, (byte)(i + 10) };
                await client.SendAsync(payload, payload.Length, serverEndpoint);

                var result = await client.ReceiveAsync();
                CollectionAssert.AreEqual(payload, result.Buffer,
                    $"Round-trip failed for datagram {i}");
            }

            Assert.AreEqual(5, server.DatagramsReceived);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task TransformServer_ModifiesResponse()
    {
        var server = new TransformUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            var payload = new byte[] { 0x00, 0x01, 0x0A, 0xFF };
            await client.SendAsync(payload, payload.Length, serverEndpoint);

            var result = await client.ReceiveAsync();

            CollectionAssert.AreEqual(
                new byte[] { 0x01, 0x02, 0x0B, 0x00 },
                result.Buffer);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task SilentServer_ReceivesButNoResponse()
    {
        var server = new SilentUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            using var client = new UdpClient();
            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            var payload = new byte[] { 0x01, 0x02, 0x03 };
            await client.SendAsync(payload, payload.Length, serverEndpoint);

            // Wait for the server to process the datagram
            await Task.Delay(200);

            Assert.AreEqual(1, server.DatagramsReceived);

            // Verify no response comes back: race ReceiveAsync against a short timeout.
            // ReceiveAsync doesn't respect ReceiveTimeout, so we use Task.WhenAny.
            var receiveTask = client.ReceiveAsync();
            var winner = await Task.WhenAny(receiveTask, Task.Delay(500));

            Assert.AreNotEqual(receiveTask, winner,
                "Server should not have responded — ReceiveAsync should not complete");
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task EchoServer_LargeDatagram()
    {
        var server = new EchoUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            // 8KB datagram — well within UDP limit but larger than typical
            var payload = new byte[8192];

            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i & 0xFF);
            }

            await client.SendAsync(payload, payload.Length, serverEndpoint);

            var result = await client.ReceiveAsync();
            CollectionAssert.AreEqual(payload, result.Buffer);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task EchoServer_SingleByte()
    {
        var server = new EchoUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            var payload = new byte[] { 0x42 };
            await client.SendAsync(payload, payload.Length, serverEndpoint);

            var result = await client.ReceiveAsync();
            CollectionAssert.AreEqual(payload, result.Buffer);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task EndpointTracking_RecordsRemoteAddress()
    {
        var server = new EndpointTrackingServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            await client.SendAsync(new byte[] { 0x01 }, 1, serverEndpoint);
            await client.ReceiveAsync();

            Assert.AreEqual(1, server.ReceivedFrom.Count);
            Assert.AreEqual(IPAddress.Loopback, server.ReceivedFrom[0].Address);
            Assert.IsTrue(server.ReceivedFrom[0].Port > 0);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public void StartStop_Lifecycle()
    {
        var server = new EchoUdpServer(Loopback, 0);

        server.Start();
        Assert.IsTrue(server.WaitForStart());
        Assert.IsTrue(server.IsListening);
        Assert.IsTrue(server.BoundPort > 0);

        server.Stop();

        // Give the thread a moment to exit
        Thread.Sleep(100);

        Assert.IsFalse(server.IsListening);
    }

    [TestMethod]
    public void DoubleStart_Throws()
    {
        var server = new EchoUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            Assert.ThrowsExactly<InvalidOperationException>(() => server.Start());
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public void BoundPort_BeforeStart_IsZero()
    {
        var server = new EchoUdpServer(Loopback, 0);
        Assert.AreEqual(0, server.BoundPort);
    }

    [TestMethod]
    public async Task MultipleClients_SequentialRequests()
    {
        var server = new EchoUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            // Each client sends one datagram
            for (var c = 0; c < 3; c++)
            {
                using var client = new UdpClient();
                client.Client.ReceiveTimeout = 3000;

                var payload = new byte[] { (byte)(0xA0 + c) };
                await client.SendAsync(payload, payload.Length, serverEndpoint);

                var result = await client.ReceiveAsync();
                CollectionAssert.AreEqual(payload, result.Buffer);
            }

            Assert.AreEqual(3, server.DatagramsReceived);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task EphemeralPort_ResolvesAfterStart()
    {
        // Bind to port 0 → OS assigns an ephemeral port
        var server = new EchoUdpServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            // BoundPort should now be a real port number
            Assert.IsTrue(server.BoundPort > 0);
            Assert.IsTrue(server.BoundPort < 65536);

            // Verify it actually works on that port
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEndpoint = new IPEndPoint(Loopback, server.BoundPort);

            var payload = new byte[] { 0xCC };
            await client.SendAsync(payload, payload.Length, serverEndpoint);

            var result = await client.ReceiveAsync();
            CollectionAssert.AreEqual(payload, result.Buffer);
        }
        finally
        {
            server.Stop();
        }
    }
}
