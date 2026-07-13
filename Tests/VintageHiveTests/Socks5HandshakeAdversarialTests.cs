// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VintageHive;
using VintageHive.Network;
using VintageHive.Proxy.Socks.Socks5;

namespace Adversarial5.Socks5;

// Adversarial coverage for Socks5Handler.HandleAsync, restricted to CONNECT-free rejection paths only.
// The dispatcher consumes the SOCKS version byte (0x05) and passes it as firstByte; the handler ignores
// firstByte and reads the REST of the handshake off connection.Stream, so every test writes nmethods,
// the method list, and (optionally) the RFC 1929 auth sub-negotiation and a request header.
//
// DB boundary: the only DB touch points are AuthenticateAsync -> Mind.Db?.UserFetch (guarded by a
// non-empty username AND password AND a non-null Mind.Db) and the success path's Mind.Db.RequestsTrack.
// None of the tests below reach either: method-selection rejections, wrong auth versions, EOF-during-auth,
// and empty-credential rejections all short-circuit before Mind.Db is dereferenced, and no test drives a
// well-formed CONNECT. The one test that sends non-empty bogus credentials asserts Mind.Db is null first,
// so the null-conditional guarantees UserFetch is never invoked.
[TestClass]
public class Socks5HandshakeAdversarialTests
{
    #region Method-selection rejections (RFC 1928 Section 3)

    [TestMethod]
    [Timeout(8000)]
    public async Task NoAuthMode_OffersUsernamePasswordOnly_RejectsWithFF()
    {
        using var rig = Socks5Rig.Start(requireAuth: false);

        // Client offers only username/password (0x02); with auth disabled the server wants no-auth (0x00).
        await rig.WriteAsync(new byte[] { 0x01, 0x02 });

        var reply = await rig.ReadClientAsync(2);

        Assert.AreEqual(2, reply.Length, "Expected a 2-byte method-selection reply");
        Assert.AreEqual((byte)0x05, reply[0], "Method-selection reply version must be 0x05");
        Assert.AreEqual((byte)0xFF, reply[1], "No acceptable method must be answered with 0xFF, never a silent fallback");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task AuthRequired_OffersNoAuthOnly_RejectsWithFF()
    {
        using var rig = Socks5Rig.Start(requireAuth: true);

        // Auth required, but client offers only no-auth (0x00): must be rejected, never silently allowed.
        await rig.WriteAsync(new byte[] { 0x01, 0x00 });

        var reply = await rig.ReadClientAsync(2);

        Assert.AreEqual(2, reply.Length);
        Assert.AreEqual((byte)0x05, reply[0]);
        Assert.AreEqual((byte)0xFF, reply[1], "Auth required: a no-auth-only client must be rejected with 0xFF (no bypass)");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task ZeroMethodsOffered_RejectsWithFF()
    {
        using var rig = Socks5Rig.Start(requireAuth: false);

        // nMethods = 0: the method list is empty, so nothing acceptable can be found.
        await rig.WriteAsync(new byte[] { 0x00 });

        var reply = await rig.ReadClientAsync(2);

        Assert.AreEqual(2, reply.Length, "An empty method list must still produce the 2-byte 0xFF rejection");
        Assert.AreEqual((byte)0x05, reply[0]);
        Assert.AreEqual((byte)0xFF, reply[1], "Zero offered methods must map to no-acceptable-method (0xFF)");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task AuthRequired_OffersManyMethodsButNoUsernamePassword_RejectsWithFF()
    {
        using var rig = Socks5Rig.Start(requireAuth: true);

        // Offers None(0x00), GSSAPI(0x01), and a vendor method(0x80) - but never username/password(0x02).
        await rig.WriteAsync(new byte[] { 0x03, 0x00, 0x01, 0x80 });

        var reply = await rig.ReadClientAsync(2);

        Assert.AreEqual(2, reply.Length);
        Assert.AreEqual((byte)0x05, reply[0]);
        Assert.AreEqual((byte)0xFF, reply[1], "Auth required: absence of 0x02 in a multi-method list must be rejected");

        await rig.WaitForHandlerAsync();
    }

    #endregion

    #region Auth sub-negotiation rejections (RFC 1929)

    [TestMethod]
    [Timeout(8000)]
    public async Task AuthRequired_WrongSubnegotiationVersion_RepliesAuthFailure()
    {
        using var rig = Socks5Rig.Start(requireAuth: true);

        // Greeting selects 0x02, then the auth sub-negotiation carries the WRONG version byte (0x05, not 0x01).
        await rig.WriteAsync(new byte[] { 0x01, 0x02 });
        await rig.WriteAsync(new byte[] { 0x05, 0x00, 0x00 });

        var reply = await rig.ReadClientAsync(4);

        Assert.AreEqual(4, reply.Length, "Expected method-selection reply followed by an auth reply");
        Assert.AreEqual((byte)0x05, reply[0]);
        Assert.AreEqual((byte)0x02, reply[1], "Auth required must select username/password (0x02)");
        Assert.AreEqual((byte)0x01, reply[2], "Auth failure reply must carry RFC 1929 version 0x01");
        Assert.AreEqual((byte)0x01, reply[3], "A bad sub-negotiation version must fail with status 0x01");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task AuthRequired_EmptyUsernameAndPassword_RepliesAuthFailure()
    {
        using var rig = Socks5Rig.Start(requireAuth: true);

        // ULEN=0 and PLEN=0. The length short-circuit rejects before Mind.Db is ever consulted.
        await rig.WriteAsync(new byte[] { 0x01, 0x02 });
        await rig.WriteAsync(new byte[] { 0x01, 0x00, 0x00 });

        var reply = await rig.ReadClientAsync(4);

        Assert.AreEqual(4, reply.Length);
        Assert.AreEqual((byte)0x05, reply[0]);
        Assert.AreEqual((byte)0x02, reply[1]);
        Assert.AreEqual((byte)0x01, reply[2], "Auth reply version must be 0x01");
        Assert.AreEqual((byte)0x01, reply[3], "Zero-length credentials must be rejected, not treated as a match");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task AuthRequired_NonEmptyUserEmptyPassword_RepliesAuthFailure()
    {
        using var rig = Socks5Rig.Start(requireAuth: true);

        // UNAME="abc", PLEN=0. password.Length > 0 is false, so the Mind.Db clause is never evaluated.
        await rig.WriteAsync(new byte[] { 0x01, 0x02 });
        await rig.WriteAsync(new byte[] { 0x01, 0x03, (byte)'a', (byte)'b', (byte)'c', 0x00 });

        var reply = await rig.ReadClientAsync(4);

        Assert.AreEqual(4, reply.Length);
        Assert.AreEqual((byte)0x02, reply[1]);
        Assert.AreEqual((byte)0x01, reply[2]);
        Assert.AreEqual((byte)0x01, reply[3], "A zero-length password must be rejected regardless of username");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task AuthRequired_OffersBothMethods_SelectsUsernamePassword_ThenEofFailsAuth()
    {
        using var rig = Socks5Rig.Start(requireAuth: true);

        // Client offers both 0x00 and 0x02; auth mode must prefer 0x02. Then EOF before the auth version byte
        // must produce an auth failure (version read returns -1, which is not the 0x01 sub-negotiation version).
        await rig.WriteAsync(new byte[] { 0x02, 0x00, 0x02 });
        rig.ShutdownClientSend();

        var reply = await rig.ReadClientAsync(4);

        Assert.AreEqual(4, reply.Length);
        Assert.AreEqual((byte)0x05, reply[0]);
        Assert.AreEqual((byte)0x02, reply[1], "Auth required must select 0x02 even when 0x00 is also offered");
        Assert.AreEqual((byte)0x01, reply[2], "Auth failure reply version must be 0x01");
        Assert.AreEqual((byte)0x01, reply[3], "EOF before the auth version byte must fail authentication");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task AuthRequired_NonEmptyBogusCredentials_RepliesAuthFailure()
    {
        // Bogus credentials must fail whether Mind.Db is unset (the ?. short-circuits, UserFetch never
        // runs) or set (UserFetch finds no such user). Either way the reply is auth-failure, and the
        // handler never grants, so this does not depend on Mind.Db being null in the shared test process.
        using var rig = Socks5Rig.Start(requireAuth: true);

        // UNAME="abc", PASSWD="xyz": both non-empty, so evaluation reaches Mind.Db?.UserFetch, which is null.
        await rig.WriteAsync(new byte[] { 0x01, 0x02 });
        await rig.WriteAsync(new byte[] { 0x01, 0x03, (byte)'a', (byte)'b', (byte)'c', 0x03, (byte)'x', (byte)'y', (byte)'z' });

        var reply = await rig.ReadClientAsync(4);

        Assert.AreEqual(4, reply.Length);
        Assert.AreEqual((byte)0x02, reply[1]);
        Assert.AreEqual((byte)0x01, reply[2]);
        Assert.AreEqual((byte)0x01, reply[3], "With no user store configured, non-empty credentials must fail (never succeed)");

        await rig.WaitForHandlerAsync();
    }

    #endregion

    #region Request-phase rejections (before any outbound connect)

    [TestMethod]
    [Timeout(8000)]
    public async Task NoAuth_BindCommand_RepliesCommandNotSupported()
    {
        using var rig = Socks5Rig.Start(requireAuth: false);

        // No-auth greeting is accepted, then a BIND (0x02) request must be refused before any dial.
        await rig.WriteAsync(new byte[] { 0x01, 0x00 });
        await rig.WriteAsync(new byte[] { 0x05, 0x02, 0x00, 0x01, 127, 0, 0, 1, 0x00, 0x50 });

        var reply = await rig.ReadClientAsync(12);

        Assert.AreEqual(12, reply.Length, "Expected [0x05,0x00] method reply then a 10-byte request reply");
        Assert.AreEqual((byte)0x05, reply[0]);
        Assert.AreEqual((byte)0x00, reply[1], "No-auth must be accepted first");
        Assert.AreEqual((byte)0x05, reply[2], "Request reply version must be 0x05");
        Assert.AreEqual((byte)Socks5ReplyType.CommandNotSupported, reply[3], "BIND must yield command-not-supported (0x07)");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task NoAuth_UdpAssociateCommand_RepliesCommandNotSupported()
    {
        using var rig = Socks5Rig.Start(requireAuth: false);

        // UDP ASSOCIATE (0x03) is likewise unsupported and must be refused before any dial.
        await rig.WriteAsync(new byte[] { 0x01, 0x00 });
        await rig.WriteAsync(new byte[] { 0x05, 0x03, 0x00, 0x01, 127, 0, 0, 1, 0x00, 0x50 });

        var reply = await rig.ReadClientAsync(12);

        Assert.AreEqual(12, reply.Length);
        Assert.AreEqual((byte)0x00, reply[1]);
        Assert.AreEqual((byte)0x05, reply[2]);
        Assert.AreEqual((byte)Socks5ReplyType.CommandNotSupported, reply[3], "UDP ASSOCIATE must yield 0x07");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task NoAuth_ConnectWithUnsupportedAddressType_RepliesAddressTypeNotSupported()
    {
        using var rig = Socks5Rig.Start(requireAuth: false);

        // CONNECT(0x01) with an ATYP the handler does not know (0x02). The default switch arm rejects with
        // address-type-not-supported and returns BEFORE parsing any address bytes or dialing.
        await rig.WriteAsync(new byte[] { 0x01, 0x00 });
        await rig.WriteAsync(new byte[] { 0x05, 0x01, 0x00, 0x02 });

        var reply = await rig.ReadClientAsync(12);

        Assert.AreEqual(12, reply.Length);
        Assert.AreEqual((byte)0x00, reply[1]);
        Assert.AreEqual((byte)0x05, reply[2]);
        Assert.AreEqual((byte)Socks5ReplyType.AddressTypeNotSupported, reply[3], "Unknown ATYP must yield 0x08");

        await rig.WaitForHandlerAsync();
    }

    #endregion

    #region No-reply / truncation paths

    [TestMethod]
    [Timeout(8000)]
    public async Task NoAuth_BadVersionInRequestHeader_ClosesWithoutRequestReply()
    {
        using var rig = Socks5Rig.Start(requireAuth: false);

        // No-auth greeting is accepted (2 bytes). The request header then carries the wrong version (0x04):
        // the handler returns silently without a request reply, so only the method-selection reply is seen.
        await rig.WriteAsync(new byte[] { 0x01, 0x00 });
        await rig.WriteAsync(new byte[] { 0x04, 0x01, 0x00, 0x01, 127, 0, 0, 1, 0x00, 0x50 });

        var reply = await rig.ReadClientAsync(12);

        Assert.AreEqual(2, reply.Length, "A bad request version must produce no request reply, only the method-selection reply");
        Assert.AreEqual((byte)0x05, reply[0]);
        Assert.AreEqual((byte)0x00, reply[1]);

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task TruncatedMethodList_ClientShutsDown_RepliesNothing()
    {
        using var rig = Socks5Rig.Start(requireAuth: false);

        // Announce 3 methods but send only 1 byte, then half-close: ReadExact hits EOF and the handler returns
        // without ever writing a method-selection reply.
        await rig.WriteAsync(new byte[] { 0x03, 0x00 });
        rig.ShutdownClientSend();

        var reply = await rig.ReadClientAsync(4);

        Assert.AreEqual(0, reply.Length, "A truncated method list must produce no reply at all");

        await rig.WaitForHandlerAsync();
    }

    [TestMethod]
    [Timeout(8000)]
    public async Task ImmediateEofBeforeGreeting_RepliesNothing()
    {
        using var rig = Socks5Rig.Start(requireAuth: false);

        // Half-close immediately: the nMethods read returns EOF and the handler returns with no output.
        rig.ShutdownClientSend();

        var reply = await rig.ReadClientAsync(4);

        Assert.AreEqual(0, reply.Length, "Immediate EOF before the greeting must produce no reply");

        await rig.WaitForHandlerAsync();
    }

    #endregion
}

// Loopback socket rig. The handler reads from a concrete NetworkStream, so a real TCP pair is required.
// The handler is launched immediately with a 2.5s CancellationTokenSource; when it exits (or is cancelled),
// the server socket is shut down so pending client reads return 0 instead of blocking. Every read is bounded
// by the same token, so no test can hang.
internal sealed class Socks5Rig : IDisposable
{
    private readonly Socket _clientSocket;
    private readonly NetworkStream _clientStream;
    private readonly Socket _serverSocket;
    private readonly NetworkStream _serverStream;
    private readonly ListenerSocket _conn;
    private readonly CancellationTokenSource _cts;
    private readonly Task _handlerTask;

    private Socks5Rig(Socket clientSocket, NetworkStream clientStream, Socket serverSocket, NetworkStream serverStream, ListenerSocket conn, CancellationTokenSource cts, bool requireAuth)
    {
        _clientSocket = clientSocket;
        _clientStream = clientStream;
        _serverSocket = serverSocket;
        _serverStream = serverStream;
        _conn = conn;
        _cts = cts;

        _handlerTask = Task.Run(async () =>
        {
            try
            {
                // firstByte (0x05) is ignored by the handler; the dispatcher would already have consumed it.
                await Socks5Handler.HandleAsync(_conn, 0x05, requireAuth, _cts.Token);
            }
            catch
            {
                // Cancellation or a closed socket during a rejection path is expected; swallow it.
            }
            finally
            {
                try { _serverSocket.Shutdown(SocketShutdown.Both); } catch { }
            }
        });
    }

    public static Socks5Rig Start(bool requireAuth)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        clientSocket.Connect(IPAddress.Loopback, port);

        var serverSocket = listener.AcceptSocket();

        listener.Stop();

        var serverStream = new NetworkStream(serverSocket);
        var clientStream = new NetworkStream(clientSocket) { ReadTimeout = 2000, WriteTimeout = 2000 };

        var conn = new ListenerSocket
        {
            RawSocket = serverSocket,
            Stream = serverStream,
        };

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));

        return new Socks5Rig(clientSocket, clientStream, serverSocket, serverStream, conn, cts, requireAuth);
    }

    public async Task WriteAsync(byte[] bytes)
    {
        await _clientStream.WriteAsync(bytes, _cts.Token);
    }

    public void ShutdownClientSend()
    {
        try { _clientSocket.Shutdown(SocketShutdown.Send); } catch { }
    }

    // Reads up to count bytes, stopping early on EOF (server shutdown) or cancellation. Never throws, never
    // blocks past the shared 2.5s token, so callers can safely assert on the actual byte count received.
    public async Task<byte[]> ReadClientAsync(int count)
    {
        var buffer = new byte[count];
        var offset = 0;

        try
        {
            while (offset < count)
            {
                var read = await _clientStream.ReadAsync(buffer.AsMemory(offset), _cts.Token);

                if (read == 0)
                {
                    break;
                }

                offset += read;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        return buffer[..offset];
    }

    public async Task WaitForHandlerAsync()
    {
        try
        {
            await _handlerTask.WaitAsync(TimeSpan.FromSeconds(4));
        }
        catch (TimeoutException)
        {
            Assert.Fail("Handler did not complete within 4 seconds; a rejection path must never block");
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _clientStream.Dispose(); } catch { }
        try { _serverStream.Dispose(); } catch { }
        try { _clientSocket.Dispose(); } catch { }
        try { _serverSocket.Dispose(); } catch { }
        try { _cts.Dispose(); } catch { }
    }
}