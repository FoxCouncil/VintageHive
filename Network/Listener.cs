// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Security;

namespace VintageHive.Network;

public abstract class Listener
{
    // Every started listener registers here so the admin dashboard can report live per-service activity.
    private static readonly ConcurrentBag<Listener> Instances = new();

    private int _activeConnections;

    /// <summary>Connections this listener is handling right now.</summary>
    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    /// <summary>All listeners that have been started and are currently listening.</summary>
    public static IReadOnlyList<Listener> ActiveListeners => Instances.Where(l => l.IsListening).ToList();

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
        Instances.Add(this);

        var name = GetType().Name;

        if (IsSecure)
        {
            name += " [SSL]";
        }

        ProcessThread = new Thread(new ThreadStart(Run))
        {
            Name = GetType().Name
        };

        ProcessThread.Start();
    }

    private async void Run()
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
            }

            _ = Task.Run(async () =>
            {
                Interlocked.Increment(ref _activeConnections);

                var reqBuffer = new byte[4096];

                var networkStream = new NetworkStream(connection);

                SslStream sslStream = null;

                ListenerSocket listenerSocket = null;

                try
                {
                    if (IsSecure)
                    {
                        int read = await connection.ReceiveAsync(reqBuffer, SocketFlags.None);

                        if (read == 0)
                        {
                            return;
                        }

                        var rawPacket = Encoding.ASCII.GetString(reqBuffer, 0, read);

                        // The Client is asking us to forward the connection.
                        if (rawPacket.StartsWith("CONNECT"))
                        {
                            // We need to fake it...
                            await connection.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.0 200 Connection Established\r\n\r\n"), SocketFlags.None);
                        }

                        var baseRequest = HttpRequest.Parse(reqBuffer[..read], rawPacket, Encoding.ASCII);

                        if (!baseRequest.IsValid)
                        {
                            return;
                        }

                        var sslCertificate = CertificateAuthority.GetOrCreateDomainCertificate(baseRequest.Uri.Host);

                        sslStream = new SslStream(SecurityContext, networkStream);

                        using var cert = X509Certificate.FromPEM(sslCertificate.Certificate);
                        using var key = Rsa.FromPEMPrivateKey(sslCertificate.Key);

                        sslStream.UseCertificate(cert);
                        sslStream.UseRSAPrivateKey(key);

                        // Bound the synchronous handshake reads so a silent half-open peer can't pin this thread forever
                        networkStream.ReadTimeout = HandshakeTimeoutMs;

                        sslStream.AuthenticateAsServer();

                        networkStream.ReadTimeout = Timeout.Infinite;
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

                    networkStream.Dispose();

                    try { connection.Close(); } catch { }
                }
            });
        }

        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, "Stopping Listener...", "");

        IsListening = false;
    }

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
