// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Security;

namespace VintageHive.Network;

public abstract class Listener
{
    // Every listener that successfully BOUND registers here so the admin dashboard can report live per-service
    // activity. Previously an add-only ConcurrentBag populated in Start() before the bind was attempted, so a
    // listener whose port was already taken lingered in it forever and a re-Start added a duplicate entry.
    // Keyed by instance, so registering twice is idempotent.
    private static readonly ConcurrentDictionary<Listener, byte> Instances = new();

    private int _activeConnections;

    /// <summary>Connections this listener is handling right now.</summary>
    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    /// <summary>All listeners that have been started and are currently listening.</summary>
    public static IReadOnlyList<Listener> ActiveListeners => Instances.Keys.Where(l => l.IsListening).ToList();

    public bool IsSecure { get; }

    public SslContext SecurityContext { get; }

    public bool IsListening { get; internal set; }

    public IPAddress Address { get; private set; }

    public int Port { get; private set; }

    public SocketType SocketType { get; private set; }

    public ProtocolType ProtocolType { get; private set; }

    public Encoding Encoding { get; set; } = Encoding.UTF8;

    public Thread ProcessThread { get; private set; }

    const int HandshakeTimeoutMs = 15000;

    public Listener(IPAddress listenAddress, int port, SocketType type, ProtocolType protocol, bool secure = false)
    {
        Address = listenAddress;
        Port = port;
        SocketType = type;
        ProtocolType = protocol;
        IsSecure = secure;

        if (IsSecure)
        {
            SecurityContext = new SslContext();

            // Don't want to verify the client certs in this instance...
            SecurityContext.SetVerify(false);

            // We don't care about security, just access over SSL
            SecurityContext.SetCipherList("ALL:eNULL");
        }
    }

    public void Start()
    {
        var name = GetType().Name;

        if (IsSecure)
        {
            name += " [SSL]";
        }

        // Registration moved into Run, after the bind actually succeeds.
        ProcessThread = new Thread(new ThreadStart(Run))
        {
            // The " [SSL]" suffix was computed here and then thrown away, so a secure listener's thread was
            // indistinguishable from its plaintext twin in a debugger.
            Name = name
        };

        ProcessThread.Start();
    }

    /// <summary>
    /// Stops accepting new connections and unblocks the accept loop.
    /// </summary>
    /// <remarks>
    /// UdpListener had a Stop() from the start; the TCP base never did, so TCP services could only die with
    /// the process and no embedder or test could tear one down cleanly. Clearing IsListening alone is not
    /// enough - the loop is parked inside AcceptAsync and only wakes when the listening socket is closed,
    /// which raises ObjectDisposedException there. That is the exact non-SocketException path the accept loop
    /// now handles with a continue rather than falling through with a null connection.
    /// </remarks>
    public void Stop()
    {
        IsListening = false;

        Instances.TryRemove(this, out _);

        try
        {
            _listenSocket?.Close();
        }
        catch
        {
            // Already gone, or never bound.
        }
    }

    private Socket _listenSocket;

    // async void: any exception that escapes this method is unhandled and takes the PROCESS down rather than
    // one listener, so the whole body is wrapped. RunCore holds the real logic.
    private async void Run()
    {
        try
        {
            await RunCore();
        }
        catch (Exception ex)
        {
            IsListening = false;

            Instances.TryRemove(this, out _);

            Log.WriteException(GetType().Name, ex, "");
        }
    }

    private async Task RunCore()
    {
        if (IsListening)
        {
            throw new Exception("Starting a Listener while it's already listening!");
        }

        IsListening = true;

        using var socket = new Socket(SocketType, ProtocolType);

        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        var extraData = IsSecure ? "Secure " : "";

        try
        {
            socket.Bind(new IPEndPoint(Address, Port));
        }
        catch (SocketException ex)
        {
            Log.WriteLine(Log.LEVEL_ERROR, GetType().Name, $"Failed to bind {Address}:{Port} - {ex.Message}", "");

            IsListening = false;

            return;
        }

        socket.ReceiveTimeout = 100;

        socket.Listen();

        // Published only once the socket is fully configured and listening. Publishing it at construction
        // opened a window where a Stop() racing startup closed the socket before SetSocketOption/ReceiveTimeout
        // ran, and those threw ObjectDisposedException out of an async void - which takes the whole process
        // down rather than failing one listener.
        _listenSocket = socket;

        // Stop() may have been called during startup, before the socket existed to close.
        if (!IsListening)
        {
            return;
        }

        Instances[this] = 0;

        Log.WriteLine(Log.LEVEL_INFO, extraData.TrimEnd() + GetType().Name, $"Starting {extraData}{GetType().Name} Listener...{Address}:{Port}", "");

        while (IsListening)
        {
            Socket connection = null;

            try
            {
                connection = await socket.AcceptAsync();
            }
            catch (SocketException)
            {
                // Ignore
                continue;
            }
            catch (Exception ex)
            {
                Log.WriteException(GetType().Name, ex, "");

                // Without this the loop fell straight through to Task.Run with a null connection: the
                // increment and the NetworkStream constructor below both ran outside the try/finally, so
                // every iteration faulted silently AND leaked a count off the admin dashboard's active
                // connections, while the accept loop hot-spun. ObjectDisposedException after a shutdown
                // closes the socket is the ordinary way to get here.
                continue;
            }

            _ = Task.Run(async () =>
            {
                var reqBuffer = new byte[4096];

                NetworkStream networkStream = null;

                SslStream sslStream = null;

                ListenerSocket listenerSocket = null;

                // Cannot throw, and sits immediately before the try so the matching decrement in the finally
                // can never be skipped.
                Interlocked.Increment(ref _activeConnections);

                try
                {
                    // Inside the try: a peer that resets between accept and here makes this throw, and that
                    // used to escape the finally entirely.
                    networkStream = new NetworkStream(connection);

                    if (IsSecure)
                    {
                        sslStream = await PerformSecureHandshake(connection, networkStream, reqBuffer);

                        if (sslStream == null)
                        {
                            return;
                        }
                    }

                    listenerSocket = new ListenerSocket
                    {
                        IsSecure = IsSecure,
                        RawSocket = connection,
                        Stream = networkStream,
                        SecureStream = sslStream
                    };

                    var remoteAddress = listenerSocket.RemoteAddress;

                    Log.WriteLine(Log.LEVEL_DEBUG, GetType().Name, $"Opening connection to {remoteAddress}", listenerSocket.TraceId.ToString());

                    if (connection.Connected)
                    {
                        var connectionBuffer = await ProcessConnection(listenerSocket);

                        if (connectionBuffer != null)
                        {
                            if (IsSecure)
                            {
                                await sslStream.WriteAsync(connectionBuffer);
                            }
                            else
                            {
                                await connection.SendAsync(connectionBuffer, SocketFlags.None);
                            }
                        }
                    }

                    // Two session models coexist here. Most protocols are per-read: ProcessConnection returns a
                    // greeting and the loop below feeds each subsequent read to ProcessRequest. The others take
                    // the socket OVER inside ProcessConnection and drive the whole conversation themselves
                    // (OSCAR, MSN, YMSG, SOCKS, Gopher, Finger, MMS, PNA, ILS, H.323, T.120), so by the time it
                    // returns the session is finished. Those used to fall into this loop anyway and spin on a
                    // socket nobody was going to write to again, which is why individual servers grew their own
                    // "close it here, the base never does" workarounds. OwnsConnection makes the distinction
                    // explicit and gives a new service one thing to declare instead of a workaround to copy.
                    if (OwnsConnection)
                    {
                        await ProcessDisconnection(listenerSocket);

                        Log.WriteLine(Log.LEVEL_DEBUG, GetType().Name, $"Closing connection to {remoteAddress}", listenerSocket.TraceId.ToString());

                        return;
                    }

                    while (connection.Connected)
                    {
                        try
                        {
                            int read = IsSecure ? await sslStream.ReadAsync(reqBuffer) : await networkStream.ReadAsync(reqBuffer);

                            if (read <= 0)
                            {
                                break;
                            }

                            var resBuffer = await ProcessRequest(listenerSocket, reqBuffer, read).ConfigureAwait(false);

                            if (resBuffer != null)
                            {
                                if (IsSecure)
                                {
                                    await sslStream.WriteAsync(resBuffer);
                                }
                                else
                                {
                                    await connection.SendAsync(resBuffer, SocketFlags.None);
                                }
                            }

                            if (!listenerSocket.IsKeepAlive)
                            {
                                break;
                            }
                        }
                        catch (Exception ex) when (ex is SocketException || ex is IOException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Log.WriteException(GetType().Name, ex, listenerSocket.TraceId.ToString());
                        }
                    }

                    await ProcessDisconnection(listenerSocket);

                    Log.WriteLine(Log.LEVEL_DEBUG, GetType().Name, $"Closing connection to {remoteAddress}", listenerSocket.TraceId.ToString());
                }
                catch (Exception ex)
                {
                    // Setup failures (bad probe, failed handshake, cert error) used to vanish out of this async void with no log
                    Log.WriteException(GetType().Name, ex, listenerSocket?.TraceId.ToString() ?? "");
                }
                finally
                {
                    Interlocked.Decrement(ref _activeConnections);

                    // The single deterministic teardown: SSL_free (frees the SSL handle + both BIOs), the stream, and the socket
                    sslStream?.Dispose();

                    networkStream?.Dispose();

                    try { connection.Close(); } catch { }
                }
            });
        }

        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, "Stopping Listener...", "");

        IsListening = false;
    }

    /// <summary>
    /// Completes the TLS handshake for a secure listener, returning the negotiated stream or null to drop.
    /// </summary>
    /// <remarks>
    /// Virtual because the DEFAULT here is HTTP-shaped and has no business being the base class's idea of what
    /// every secure listener does: it sniffs a CONNECT verb, parses an HTTP request line, and mints a
    /// certificate for the Host it finds. That is forward-proxy MITM behaviour, correct only for the HTTPS
    /// proxy. Any future secure non-HTTP listener - IMAPS, POP3S, secure telnet - would silently have
    /// inherited it and mis-handshaked. Overriding this is the seam for those; the base keeps today's
    /// behaviour so nothing changes for the one listener that actually wants it.
    /// </remarks>
    protected virtual async Task<SslStream> PerformSecureHandshake(Socket connection, NetworkStream networkStream, byte[] scratch)
    {
        int read = await connection.ReceiveAsync(scratch, SocketFlags.None);

        if (read == 0)
        {
            return null;
        }

        var rawPacket = Encoding.ASCII.GetString(scratch, 0, read);

        // The Client is asking us to forward the connection.
        if (rawPacket.StartsWith("CONNECT"))
        {
            // We need to fake it...
            await connection.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.0 200 Connection Established\r\n\r\n"), SocketFlags.None);
        }

        var baseRequest = HttpRequest.Parse(scratch[..read], rawPacket, Encoding.ASCII);

        if (!baseRequest.IsValid)
        {
            return null;
        }

        var sslCertificate = CertificateAuthority.GetOrCreateDomainCertificate(baseRequest.Uri.Host);

        var stream = new SslStream(SecurityContext, networkStream);

        using var cert = X509Certificate.FromPEM(sslCertificate.Certificate);
        using var key = Rsa.FromPEMPrivateKey(sslCertificate.Key);

        stream.UseCertificate(cert);
        stream.UseRSAPrivateKey(key);

        // Bound the synchronous handshake reads so a silent half-open peer can't pin this thread forever
        networkStream.ReadTimeout = HandshakeTimeoutMs;

        stream.AuthenticateAsServer();

        networkStream.ReadTimeout = Timeout.Infinite;

        return stream;
    }

    /// <summary>
    /// Whether <see cref="ProcessConnection"/> drives the entire session itself rather than returning a
    /// greeting for the base per-read loop to follow up.
    /// </summary>
    /// <remarks>
    /// True for the socket-takeover servers. The base skips its read loop for those and tears the connection
    /// down as soon as ProcessConnection returns, because there is nothing left to read.
    /// </remarks>
    protected virtual bool OwnsConnection => false;

    public virtual Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        return Task.FromResult<byte[]>(null);
    }

    public virtual Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        return Task.FromResult<byte[]>(null);
    }

    public virtual Task ProcessDisconnection(ListenerSocket connection)
    {
        return Task.Delay(0);
    }
}
