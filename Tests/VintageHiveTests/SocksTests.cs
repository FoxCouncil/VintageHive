// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using VintageHive.Network;
using VintageHive.Proxy.Socks;
using VintageHive.Proxy.Socks.Socks4;
using VintageHive.Proxy.Socks.Socks5;

#pragma warning disable MSTEST0025 // Enum-to-byte casts are intentional — verifying wire-level constants

namespace Socks;

#region Enum Tests

[TestClass]
public class SocksEnumTests
{
    // RFC 1928 Section 4 — Command codes
    [TestMethod]
    public void Socks5CommandType_Connect_Is0x01()
    {
        Assert.AreEqual((byte)0x01, (byte)Socks5CommandType.Connect);
    }

    [TestMethod]
    public void Socks5CommandType_Bind_Is0x02()
    {
        Assert.AreEqual((byte)0x02, (byte)Socks5CommandType.Bind);
    }

    [TestMethod]
    public void Socks5CommandType_UdpAssociate_Is0x03()
    {
        Assert.AreEqual((byte)0x03, (byte)Socks5CommandType.UdpAssociate);
    }

    // RFC 1928 Section 4 — Address types
    [TestMethod]
    public void Socks5AddressType_IPv4_Is0x01()
    {
        Assert.AreEqual((byte)0x01, (byte)Socks5AddressType.IPv4);
    }

    [TestMethod]
    public void Socks5AddressType_DomainName_Is0x03()
    {
        Assert.AreEqual((byte)0x03, (byte)Socks5AddressType.DomainName);
    }

    [TestMethod]
    public void Socks5AddressType_IPv6_Is0x04()
    {
        Assert.AreEqual((byte)0x04, (byte)Socks5AddressType.IPv6);
    }

    // RFC 1928 Section 6 — Reply codes
    [TestMethod]
    public void Socks5ReplyType_Succeeded_Is0x00()
    {
        Assert.AreEqual((byte)0x00, (byte)Socks5ReplyType.Succeeded);
    }

    [TestMethod]
    public void Socks5ReplyType_GeneralFailure_Is0x01()
    {
        Assert.AreEqual((byte)0x01, (byte)Socks5ReplyType.GeneralFailure);
    }

    [TestMethod]
    public void Socks5ReplyType_ConnectionNotAllowed_Is0x02()
    {
        Assert.AreEqual((byte)0x02, (byte)Socks5ReplyType.ConnectionNotAllowed);
    }

    [TestMethod]
    public void Socks5ReplyType_NetworkUnreachable_Is0x03()
    {
        Assert.AreEqual((byte)0x03, (byte)Socks5ReplyType.NetworkUnreachable);
    }

    [TestMethod]
    public void Socks5ReplyType_HostUnreachable_Is0x04()
    {
        Assert.AreEqual((byte)0x04, (byte)Socks5ReplyType.HostUnreachable);
    }

    [TestMethod]
    public void Socks5ReplyType_ConnectionRefused_Is0x05()
    {
        Assert.AreEqual((byte)0x05, (byte)Socks5ReplyType.ConnectionRefused);
    }

    [TestMethod]
    public void Socks5ReplyType_TtlExpired_Is0x06()
    {
        Assert.AreEqual((byte)0x06, (byte)Socks5ReplyType.TtlExpired);
    }

    [TestMethod]
    public void Socks5ReplyType_CommandNotSupported_Is0x07()
    {
        Assert.AreEqual((byte)0x07, (byte)Socks5ReplyType.CommandNotSupported);
    }

    [TestMethod]
    public void Socks5ReplyType_AddressTypeNotSupported_Is0x08()
    {
        Assert.AreEqual((byte)0x08, (byte)Socks5ReplyType.AddressTypeNotSupported);
    }

    // RFC 1928 Section 3 — Auth methods
    [TestMethod]
    public void Socks5AuthType_None_Is0x00()
    {
        Assert.AreEqual(0, (int)Socks5AuthType.None);
    }

    [TestMethod]
    public void Socks5AuthType_GSSAPI_Is0x01()
    {
        Assert.AreEqual(1, (int)Socks5AuthType.GSSAPI);
    }

    [TestMethod]
    public void Socks5AuthType_UsernamePassword_Is0x02()
    {
        Assert.AreEqual(2, (int)Socks5AuthType.UsernamePassword);
    }
}

#endregion

#region Constructor Tests

[TestClass]
public class SocksProxyConstructorTests
{
    [TestMethod]
    public void Constructor_SetsCorrectProperties()
    {
        var ip = IPAddress.Parse("192.168.1.100");
        var port = 1996;

        var proxy = new SocksProxy(ip, port);

        Assert.AreEqual(ip, proxy.Address);
        Assert.AreEqual(port, proxy.Port);
        Assert.AreEqual(SocketType.Stream, proxy.SocketType);
        Assert.AreEqual(ProtocolType.Tcp, proxy.ProtocolType);
        Assert.IsFalse(proxy.IsSecure);
    }
}

#endregion

#region Tunnel Tests

[TestClass]
public class TunnelTests
{
    [TestMethod]
    public async Task TunnelAsync_CopiesDataBothDirections()
    {
        var clientToTarget = new byte[] { 0x48, 0x45, 0x4C, 0x4C, 0x4F }; // "HELLO"
        var targetToClient = new byte[] { 0x57, 0x4F, 0x52, 0x4C, 0x44 }; // "WORLD"

        using var clientRead = new MemoryStream();
        using var clientWrite = new MemoryStream(clientToTarget);
        using var targetRead = new MemoryStream();
        using var targetWrite = new MemoryStream(targetToClient);

        var clientStream = new DuplexStream(clientWrite, clientRead);
        var targetStream = new DuplexStream(targetWrite, targetRead);

        await SocksProxy.TunnelAsync(clientStream, targetStream);

        Assert.AreEqual(clientToTarget.Length, (int)targetRead.Length,
            "Client data was not forwarded to target");
        CollectionAssert.AreEqual(clientToTarget, targetRead.ToArray());

        Assert.AreEqual(targetToClient.Length, (int)clientRead.Length,
            "Target data was not forwarded to client");
        CollectionAssert.AreEqual(targetToClient, clientRead.ToArray());
    }

    [TestMethod]
    public async Task TunnelAsync_HandlesEmptyStreams()
    {
        using var clientRead = new MemoryStream();
        using var clientWrite = new MemoryStream(Array.Empty<byte>());
        using var targetRead = new MemoryStream();
        using var targetWrite = new MemoryStream(Array.Empty<byte>());

        var clientStream = new DuplexStream(clientWrite, clientRead);
        var targetStream = new DuplexStream(targetWrite, targetRead);

        await SocksProxy.TunnelAsync(clientStream, targetStream);

        Assert.AreEqual(0, (int)targetRead.Length);
        Assert.AreEqual(0, (int)clientRead.Length);
    }

    [TestMethod]
    public async Task TunnelAsync_LargePayload_CopiesCorrectly()
    {
        var payload = new byte[32768];
        new Random(42).NextBytes(payload);

        using var clientRead = new MemoryStream();
        using var clientWrite = new MemoryStream(payload);
        using var targetRead = new MemoryStream();
        using var targetWrite = new MemoryStream(Array.Empty<byte>());

        var clientStream = new DuplexStream(clientWrite, clientRead);
        var targetStream = new DuplexStream(targetWrite, targetRead);

        await SocksProxy.TunnelAsync(clientStream, targetStream);

        CollectionAssert.AreEqual(payload, targetRead.ToArray());
    }
}

#endregion

#region SOCKS5 Protocol Tests

[TestClass]
public class Socks5ProtocolTests
{
    [TestMethod]
    [Timeout(10000)]
    public async Task Socks5_NoAcceptableAuthMethod_RejectsWithFF()
    {
        using var pair = await SocketPair.CreateAsync();

        // Send greeting: 1 method, method=0x02 (username/password only — no NO_AUTH)
        await pair.ClientStream.WriteAsync(new byte[] { 0x01, 0x02 });

        var reply = await pair.ReadClientAsync(2);

        Assert.AreEqual((byte)0x05, reply[0], "Reply version should be 0x05");
        Assert.AreEqual((byte)0xFF, reply[1], "Should reject with 0xFF when no acceptable method");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks5_NoAuthGreeting_AcceptsWithMethod00()
    {
        using var pair = await SocketPair.CreateAsync();

        // Send greeting: 2 methods: 0x00 (no auth) and 0x02 (user/pass)
        await pair.ClientStream.WriteAsync(new byte[] { 0x02, 0x00, 0x02 });

        var reply = await pair.ReadClientAsync(2);

        Assert.AreEqual((byte)0x05, reply[0], "Reply version should be 0x05");
        Assert.AreEqual((byte)0x00, reply[1], "Should accept NO AUTH (0x00)");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks5_UnsupportedCommand_ReturnsCommandNotSupported()
    {
        using var pair = await SocketPair.CreateAsync();

        // Greeting: 1 method, no auth
        await pair.ClientStream.WriteAsync(new byte[] { 0x01, 0x00 });

        var greetReply = await pair.ReadClientAsync(2);

        Assert.AreEqual((byte)0x00, greetReply[1], "Should accept no auth");

        // Request: BIND command (0x02), IPv4, 127.0.0.1:80
        await pair.ClientStream.WriteAsync(new byte[]
        {
            0x05, 0x02, 0x00, 0x01,       // VER, CMD=BIND, RSV, ATYP=IPv4
            127, 0, 0, 1,                  // ADDR
            0x00, 0x50                     // PORT=80
        });

        var reply = await pair.ReadClientAsync(10);

        Assert.AreEqual((byte)0x05, reply[0], "Reply version should be 0x05");
        Assert.AreEqual((byte)Socks5ReplyType.CommandNotSupported, reply[1]);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks5_ConnectToClosedPort_ReturnsError()
    {
        using var pair = await SocketPair.CreateAsync();

        var closedPort = GetClosedPort();

        // Greeting
        await pair.ClientStream.WriteAsync(new byte[] { 0x01, 0x00 });

        var greetReply = await pair.ReadClientAsync(2);

        // Request: CONNECT to 127.0.0.1:closedPort
        var portHi = (byte)(closedPort >> 8);
        var portLo = (byte)(closedPort & 0xFF);

        await pair.ClientStream.WriteAsync(new byte[]
        {
            0x05, 0x01, 0x00, 0x01,       // VER, CMD=CONNECT, RSV, ATYP=IPv4
            127, 0, 0, 1,                  // 127.0.0.1
            portHi, portLo                 // closed port
        });

        var reply = await pair.ReadClientAsync(10);

        Assert.AreEqual((byte)0x05, reply[0], "Reply version should be 0x05");
        Assert.AreNotEqual((byte)Socks5ReplyType.Succeeded, reply[1],
            "Should NOT succeed connecting to a closed port");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks5_BadVersionInRequest_ClosesConnection()
    {
        using var pair = await SocketPair.CreateAsync();

        // Greeting
        await pair.ClientStream.WriteAsync(new byte[] { 0x01, 0x00 });

        var greetReply = await pair.ReadClientAsync(2);

        // Request with wrong version byte (0x04 instead of 0x05)
        await pair.ClientStream.WriteAsync(new byte[]
        {
            0x04, 0x01, 0x00, 0x01,
            127, 0, 0, 1,
            0x00, 0x50
        });

        // Handler returns without sending a reply — server socket closes.
        // On Windows, the read may throw IOException ("connection forcibly closed")
        // instead of returning 0. Both behaviors indicate the connection was closed.
        await pair.WaitForHandlerAsync();

        try
        {
            var buf = new byte[10];
            var read = await pair.ClientStream.ReadAsync(buf);

            Assert.AreEqual(0, read, "Should close connection on bad version in request phase");
        }
        catch (IOException)
        {
            // Expected on Windows — connection was closed by server
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks5_ClientDisconnectsDuringGreeting_HandlesGracefully()
    {
        using var pair = await SocketPair.CreateAsync();

        // Send partial greeting (just nMethods, no method bytes) then close
        await pair.ClientStream.WriteAsync(new byte[] { 0x01 });
        pair.ClientSocket.Shutdown(SocketShutdown.Send);

        // Handler should exit gracefully (no crash)
        await pair.WaitForHandlerAsync();
    }

    private static ushort GetClosedPort()
    {
        using var temp = new TcpListener(IPAddress.Loopback, 0);

        temp.Start();

        var port = (ushort)((IPEndPoint)temp.LocalEndpoint).Port;

        temp.Stop();

        return port;
    }
}

#endregion

#region SOCKS4 Protocol Tests

[TestClass]
public class Socks4ProtocolTests
{
    [TestMethod]
    [Timeout(10000)]
    public async Task Socks4_UnsupportedCommand_RejectsWith5B()
    {
        using var pair = await SocketPair.CreateAsync(socksVersion: 4);

        // CMD=0x02 (BIND, unsupported), port=80, IP=127.0.0.1, empty userid
        await pair.ClientStream.WriteAsync(new byte[]
        {
            0x02,                          // CMD = BIND
            0x00, 0x50,                    // PORT = 80
            127, 0, 0, 1,                  // IP
            0x00                           // Null-terminated userid
        });

        var reply = await pair.ReadClientAsync(8);

        Assert.AreEqual((byte)0x00, reply[0], "First byte of SOCKS4 reply is always 0x00");
        Assert.AreEqual((byte)0x5B, reply[1], "Should reject unsupported command with 0x5B");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks4_ConnectToClosedPort_RejectsWith5B()
    {
        using var pair = await SocketPair.CreateAsync(socksVersion: 4);

        var closedPort = GetClosedPort();
        var portHi = (byte)(closedPort >> 8);
        var portLo = (byte)(closedPort & 0xFF);

        await pair.ClientStream.WriteAsync(new byte[]
        {
            0x01,                          // CMD = CONNECT
            portHi, portLo,                // PORT
            127, 0, 0, 1,                  // IP
            0x00                           // Null-terminated userid
        });

        var reply = await pair.ReadClientAsync(8);

        Assert.AreEqual((byte)0x00, reply[0]);
        Assert.AreEqual((byte)0x5B, reply[1], "Should reject with 0x5B when connection fails");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks4_ReplyFormat_Is8BytesWithPortAndIP()
    {
        using var pair = await SocketPair.CreateAsync(socksVersion: 4);

        var closedPort = GetClosedPort();
        var portHi = (byte)(closedPort >> 8);
        var portLo = (byte)(closedPort & 0xFF);

        await pair.ClientStream.WriteAsync(new byte[]
        {
            0x01, portHi, portLo, 127, 0, 0, 1, 0x00
        });

        var reply = await pair.ReadClientAsync(8);

        // Verify reply structure: [0x00, status, port(2), ip(4)]
        Assert.AreEqual((byte)0x00, reply[0], "SOCKS4 reply byte 0 must be 0x00");

        // Port is bytes 2-3 (big-endian), IP is bytes 4-7 — both parseable
        var replyPort = BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2));
        var replyIp = new IPAddress(reply[4..8]);

        Assert.IsTrue(replyPort >= 0, "Reply port should be valid");
        Assert.IsNotNull(replyIp, "Reply IP should be parseable");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks4_WithUserid_ParsesCorrectly()
    {
        using var pair = await SocketPair.CreateAsync(socksVersion: 4);

        var closedPort = GetClosedPort();
        var portHi = (byte)(closedPort >> 8);
        var portLo = (byte)(closedPort & 0xFF);

        // SOCKS4 CONNECT with userid "testuser"
        var userid = System.Text.Encoding.ASCII.GetBytes("testuser");
        var request = new byte[7 + userid.Length + 1];

        request[0] = 0x01;                              // CMD = CONNECT
        request[1] = portHi;
        request[2] = portLo;
        request[3] = 127;
        request[4] = 0;
        request[5] = 0;
        request[6] = 1;

        Array.Copy(userid, 0, request, 7, userid.Length);

        request[7 + userid.Length] = 0x00;               // Null terminator

        await pair.ClientStream.WriteAsync(request);

        var reply = await pair.ReadClientAsync(8);

        Assert.AreEqual((byte)0x5B, reply[1], "Should reject (closed port) even with userid");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Socks4_ClientDisconnectsEarly_HandlesGracefully()
    {
        using var pair = await SocketPair.CreateAsync(socksVersion: 4);

        // Send partial header (only CMD + 1 port byte) then close
        await pair.ClientStream.WriteAsync(new byte[] { 0x01, 0x00 });
        pair.ClientSocket.Shutdown(SocketShutdown.Send);

        await pair.WaitForHandlerAsync();
    }

    private static ushort GetClosedPort()
    {
        using var temp = new TcpListener(IPAddress.Loopback, 0);

        temp.Start();

        var port = (ushort)((IPEndPoint)temp.LocalEndpoint).Port;

        temp.Stop();

        return port;
    }
}

#endregion

#region Version Dispatch Tests

[TestClass]
public class SocksVersionDispatchTests
{
    [TestMethod]
    [Timeout(10000)]
    public async Task Dispatch_Version5_RouteToSocks5Handler()
    {
        using var pair = await SocketPair.CreateAsync();

        // Send SOCKS5 greeting with only user/pass (no NO_AUTH)
        await pair.ClientStream.WriteAsync(new byte[] { 0x01, 0x02 });

        var reply = await pair.ReadClientAsync(2);

        Assert.AreEqual((byte)0x05, reply[0], "SOCKS5 handler should respond with version 0x05");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Dispatch_Version4_RouteToSocks4Handler()
    {
        using var pair = await SocketPair.CreateAsync(socksVersion: 4);

        var closedPort = GetClosedPort();
        var portHi = (byte)(closedPort >> 8);
        var portLo = (byte)(closedPort & 0xFF);

        await pair.ClientStream.WriteAsync(new byte[]
        {
            0x01, portHi, portLo, 127, 0, 0, 1, 0x00
        });

        var reply = await pair.ReadClientAsync(8);

        Assert.AreEqual((byte)0x00, reply[0], "SOCKS4 handler should respond with 0x00 as first byte");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Dispatch_UnknownVersion_ReturnsNull()
    {
        // Create a socket pair manually for the ProcessConnection test
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var acceptTask = listener.AcceptSocketAsync();

        await client.ConnectAsync(IPAddress.Loopback, port);

        using var serverSocket = await acceptTask;

        listener.Stop();

        var serverStream = new NetworkStream(serverSocket);
        var clientStream = client.GetStream();

        var listenerSocket = new ListenerSocket
        {
            RawSocket = serverSocket,
            Stream = serverStream,
        };

        // Send unknown version byte (0x03)
        await clientStream.WriteAsync(new byte[] { 0x03 });

        var proxy = new SocksProxy(IPAddress.Loopback, 0);
        var result = await proxy.ProcessConnection(listenerSocket);

        Assert.IsNull(result, "ProcessConnection should return null for unknown version");
    }

    private static ushort GetClosedPort()
    {
        using var temp = new TcpListener(IPAddress.Loopback, 0);

        temp.Start();

        var port = (ushort)((IPEndPoint)temp.LocalEndpoint).Port;

        temp.Stop();

        return port;
    }
}

#endregion

#region Helpers

/// <summary>
/// Creates a connected socket pair with a SOCKS handler running on the server side.
/// After the handler exits, the server socket is shut down so client reads return 0.
/// </summary>
internal class SocketPair : IDisposable
{
    public Socket ClientSocket { get; private init; } = null!;

    public NetworkStream ClientStream { get; private init; } = null!;

    private Socket ServerSocket { get; init; } = null!;

    private Task HandlerTask { get; init; } = null!;

    private CancellationTokenSource Cts { get; init; } = null!;

    public static async Task<SocketPair> CreateAsync(int socksVersion = 5)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        var acceptTask = listener.AcceptSocketAsync();

        await clientSocket.ConnectAsync(IPAddress.Loopback, port);

        var serverSocket = await acceptTask;

        listener.Stop();

        var serverStream = new NetworkStream(serverSocket);
        var clientStream = new NetworkStream(clientSocket);

        var listenerSocket = new ListenerSocket
        {
            RawSocket = serverSocket,
            Stream = serverStream,
        };

        // Write version byte from client, then consume it on the server side.
        // This simulates what SocksProxy.ProcessConnection does: it reads the first
        // byte to detect the SOCKS version, then passes it to the handler.
        var versionByte = (byte)socksVersion;

        await clientStream.WriteAsync(new byte[] { versionByte });

        var consumed = new byte[1];

        await serverStream.ReadExactlyAsync(consumed);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        // Start handler with cleanup: shut down server socket when handler exits
        // so client reads return 0 instead of blocking forever
        Task handlerTask;

        if (socksVersion == 5)
        {
            handlerTask = Task.Run(async () =>
            {
                try
                {
                    await Socks5Handler.HandleAsync(listenerSocket, versionByte, cts.Token);
                }
                catch { }
                finally
                {
                    try { serverSocket.Shutdown(SocketShutdown.Both); } catch { }
                }
            });
        }
        else
        {
            handlerTask = Task.Run(async () =>
            {
                try
                {
                    await Socks4Handler.HandleAsync(listenerSocket, versionByte, cts.Token);
                }
                catch { }
                finally
                {
                    try { serverSocket.Shutdown(SocketShutdown.Both); } catch { }
                }
            });
        }

        return new SocketPair
        {
            ClientSocket = clientSocket,
            ClientStream = clientStream,
            ServerSocket = serverSocket,
            HandlerTask = handlerTask,
            Cts = cts,
        };
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> bytes from the client stream,
    /// using the SocketPair's cancellation token as a timeout.
    /// </summary>
    public async Task<byte[]> ReadClientAsync(int count)
    {
        var buf = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await ClientStream.ReadAsync(buf.AsMemory(offset), Cts.Token);

            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return buf[..offset];
    }

    public async Task WaitForHandlerAsync()
    {
        await HandlerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        Cts?.Cancel();

        try { ClientStream?.Dispose(); } catch { }
        try { ClientSocket?.Close(); } catch { }
        try { ServerSocket?.Close(); } catch { }

        Cts?.Dispose();
    }
}

/// <summary>
/// A Stream that reads from one source and writes to another,
/// simulating a duplex network connection for tunnel testing.
/// </summary>
internal class DuplexStream : Stream
{
    private readonly Stream _readStream;
    private readonly Stream _writeStream;

    public DuplexStream(Stream readFrom, Stream writeTo)
    {
        _readStream = readFrom;
        _writeStream = writeTo;
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _readStream.Read(buffer, offset, count);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        return _readStream.ReadAsync(buffer, offset, count, ct);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        return _readStream.ReadAsync(buffer, ct);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _writeStream.Write(buffer, offset, count);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        return _writeStream.WriteAsync(buffer, offset, count, ct);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        return _writeStream.WriteAsync(buffer, ct);
    }

    public override void Flush()
    {
        _writeStream.Flush();
    }

    public override Task FlushAsync(CancellationToken ct)
    {
        return _writeStream.FlushAsync(ct);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

#endregion

#pragma warning restore MSTEST0025
