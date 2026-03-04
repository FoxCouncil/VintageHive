// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net.Sockets;

namespace VintageHive.Network;

/// <summary>
/// Abstract base class for UDP datagram services.
/// Counterpart to <see cref="Listener"/> (TCP). Each received datagram is dispatched
/// to <see cref="ProcessDatagram"/> which returns an optional response.
/// Used by H.225.0 RAS (UDP 1719) and other stateless/request-response protocols.
/// </summary>
public abstract class UdpListener
{
    private readonly IPAddress _address;
    private readonly int _port;
    private readonly ManualResetEventSlim _startedEvent = new(false);
    private Thread _thread;
    private volatile bool _running;
    private UdpClient _udp;

    protected UdpListener(IPAddress address, int port)
    {
        _address = address;
        _port = port;
    }

    /// <summary>Configured listen address.</summary>
    public IPAddress Address => _address;

    /// <summary>Configured listen port (may be 0 for ephemeral).</summary>
    public int Port => _port;

    /// <summary>Actual bound port after start (resolves ephemeral port 0).</summary>
    public int BoundPort { get; private set; }

    /// <summary>Whether the listener loop is running.</summary>
    public bool IsListening => _running;

    /// <summary>
    /// Start listening on a background thread.
    /// Call <see cref="WaitForStart"/> to block until the socket is bound and receiving.
    /// </summary>
    public void Start()
    {
        if (_running)
        {
            throw new InvalidOperationException($"{GetType().Name} is already listening");
        }

        _running = true;

        _thread = new Thread(Run)
        {
            Name = GetType().Name
        };

        _thread.Start();
    }

    /// <summary>Stop the listener and close the socket.</summary>
    public void Stop()
    {
        _running = false;
        _udp?.Close();
    }

    /// <summary>
    /// Block until the listener is bound and accepting datagrams.
    /// Returns false if the timeout expires before the listener starts.
    /// </summary>
    public bool WaitForStart(int timeoutMs = 5000)
    {
        return _startedEvent.Wait(timeoutMs);
    }

    /// <summary>
    /// Process an incoming UDP datagram.
    /// Return a byte array to send back to the sender, or null for no response.
    /// </summary>
    public abstract Task<byte[]> ProcessDatagram(IPEndPoint remoteEndPoint, byte[] data, int length);

    /// <summary>
    /// Send an unsolicited datagram to a specific endpoint.
    /// Used for server-initiated messages (e.g., InfoRequest, keep-alive).
    /// </summary>
    protected async Task SendToAsync(byte[] data, IPEndPoint remoteEndPoint)
    {
        if (_udp != null && _running)
        {
            await _udp.SendAsync(data, data.Length, remoteEndPoint);
        }
    }

    private async void Run()
    {
        _udp = new UdpClient(new IPEndPoint(_address, _port));

        BoundPort = ((IPEndPoint)_udp.Client.LocalEndPoint).Port;
        _startedEvent.Set();

        var logSource = GetType().Name;

        Log.WriteLine(Log.LEVEL_INFO, logSource,
            $"Starting {logSource}...{_address}:{BoundPort}", "");

        while (_running)
        {
            UdpReceiveResult result;

            try
            {
                result = await _udp.ReceiveAsync();
            }
            catch (SocketException) when (!_running)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.WriteException(logSource, ex, "");
                continue;
            }

            try
            {
                var response = await ProcessDatagram(
                    result.RemoteEndPoint, result.Buffer, result.Buffer.Length);

                if (response != null && _running)
                {
                    await _udp.SendAsync(response, response.Length, result.RemoteEndPoint);
                }
            }
            catch (Exception ex)
            {
                Log.WriteException(logSource, ex, "");
            }
        }

        Log.WriteLine(Log.LEVEL_INFO, logSource,
            $"Stopping {logSource}...", "");
    }
}
