using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using VintageHive.Proxy.Security;

namespace VintageHive.Proxy;

public abstract class Listener
{
    public bool IsSecure { get; }

    public SslContext SecurityContext { get; }

    public bool IsListening { get; internal set; }

    public IPAddress Address { get; private set; }

    public int Port { get; private set; }

    public SocketType SocketType { get; private set; }

    public ProtocolType ProtocolType { get; private set; }

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

            // Setup Security Certificate Store
            SecurityContext.SetCertificateChain("Certs/dialnine.com.crt");
            SecurityContext.SetPrivateKeyFile("Certs/dialnine.com.key");
        }
    }

    public void Start()
    {
        ProcessThread = new Thread(new ThreadStart(Run));
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

        socket.Listen();

        Console.WriteLine($"Starting Listener...{Address}:{Port}");

        while (IsListening)
        {
            Socket connection;

            try
            {
                connection = await socket.AcceptAsync();
            }
            catch (SocketException)
            {
                // Ignore
                break;
            }

            _ = Task.Run(async () =>
            {
                var reqBuffer = new byte[4096];

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

                    var networkStream = new NetworkStream(connection);

                    sslStream = new SslStream(SecurityContext, networkStream);

                    sslStream.AuthenticateAsServer();
                }

                var listenerSocket = new ListenerSocket
                {
                    IsSecure = IsSecure,
                    RawSocket = connection,
                    SecureStream = sslStream
                };

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

                while (true)
                {
                    try
                    {
                        if (!connection.Connected)
                        {
                            break;
                        }

                        int read = IsSecure ? await sslStream.ReadAsync(reqBuffer) : await connection.ReceiveAsync(reqBuffer, SocketFlags.None);

                        if (read <= 0)
                        {
                            break;
                        }

                        var resBuffer = await ProcessRequest(listenerSocket, reqBuffer, read);

                        if (IsSecure)
                        {
                            await sslStream.WriteAsync(resBuffer);

                            sslStream.Dispose();
                        }
                        else
                        {
                            if (resBuffer == null)
                            {
                                connection.Disconnect(false);
                            }
                            else
                            {
                                await connection.SendAsync(resBuffer, SocketFlags.None);
                            }
                        }

                        connection.Close();
                    }
                    catch (SocketException sex) { /* Ignore */ Console.WriteLine(sex.Message); }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Connection Exception: \n\n" + ex.Message);
                    }
                }
            });
        }

        Console.WriteLine("Stopping Listener...");

        IsListening = false;
    }

    internal virtual Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        return Task.FromResult<byte[]>(null);
    }

    internal virtual Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        return Task.FromResult<byte[]>(null);
    }
}
