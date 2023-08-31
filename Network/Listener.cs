// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Security;

namespace VintageHive.Network;

public abstract class Listener
{
    public bool IsSecure { get; }

    public SslContext SecurityContext { get; }

    public bool IsListening { get; internal set; }

    public IPAddress Address { get; private set; }

    public int Port { get; private set; }

    public SocketType SocketType { get; private set; }

    public ProtocolType ProtocolType { get; private set; }

    public Encoding Encoding { get; set; } = Encoding.UTF8;

    public Thread ProcessThread { get; private set; }

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

        socket.Bind(new IPEndPoint(Address, Port));

        socket.ReceiveTimeout = 100;

        socket.Listen();

        var extraData = IsSecure ? "Secure " : "";

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
            catch
            {
                Debugger.Break();
            }

            _ = Task.Run(async () =>
            {
                var reqBuffer = new byte[4096];

                var networkStream = new NetworkStream(connection);

                SslStream sslStream = null;

                if (IsSecure)
                {
                    int read = await connection.ReceiveAsync(reqBuffer, SocketFlags.None);

                    if (read == 0)
                    {
                        // Bail!
                        connection.Close();

                        return;
                    }

                    var rawPacket = Encoding.ASCII.GetString(reqBuffer, 0, read);

                    // The Client is asking us to forward the connection.
                    if (rawPacket.StartsWith("CONNECT"))
                    {
                        // We need to fake it...
                        await connection.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.0 200 Connection Established\r\n\r\n"), SocketFlags.None);
                    }

                    var baseRequest = HttpRequest.Parse(rawPacket, Encoding.ASCII);

                    var sslCertificate = CertificateAuthority.GetOrCreateDomainCertificate(baseRequest.Uri.Host);

                    sslStream = new SslStream(SecurityContext, networkStream);

                    using var cert = X509Certificate.FromPEM(sslCertificate.Certificate);
                    using var key = Rsa.FromPEMPrivateKey(sslCertificate.Key);

                    sslStream.UseCertificate(cert);
                    sslStream.UseRSAPrivateKey(key);

                    sslStream.AuthenticateAsServer();
                }

                var listenerSocket = new ListenerSocket
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
                        if (!connection.Connected)
                        {
                            break;
                        }

                        int read = IsSecure ? await sslStream.ReadAsync(reqBuffer) : await networkStream.ReadAsync(reqBuffer);

                        if (read <= 0)
                        {
                            break;
                        }

                        var resBuffer = await ProcessRequest(listenerSocket, reqBuffer, read).ConfigureAwait(false);

                        if (IsSecure)
                        {
                            if (resBuffer != null)
                            {
                                await sslStream.WriteAsync(resBuffer);
                            }
                            else if (!listenerSocket.IsKeepAlive)
                            {
                                sslStream.Dispose();
                            }
                        }
                        else
                        {
                            if (resBuffer != null)
                            {
                                await connection.SendAsync(resBuffer, SocketFlags.None);
                            }
                            else if (!listenerSocket.IsKeepAlive)
                            {
                                if (connection.Connected)
                                {
                                    connection.Disconnect(false);
                                }
                            }
                        }

                        if (!listenerSocket.IsKeepAlive)
                        {
                            connection.Close();
                        }
                    }
                    catch (Exception ex) when (ex is SocketException || ex is IOException)
                    {
                        /* Ignore */
                        // Display.WriteLog("Socket Exception: \n\n" + sex.Message);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteException(GetType().Name, ex, listenerSocket.TraceId.ToString());
                    }
                }

                Log.WriteLine(Log.LEVEL_DEBUG, GetType().Name, $"Closing connection to {remoteAddress}", listenerSocket.TraceId.ToString());
            });
        }

        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, "Stopping Listener...", "");

        IsListening = false;
    }

    public virtual Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        return Task.FromResult<byte[]>(null);
    }

    public virtual Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        return Task.FromResult<byte[]>(null);
    }
}
