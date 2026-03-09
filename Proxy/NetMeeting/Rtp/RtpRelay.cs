// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;

namespace VintageHive.Proxy.NetMeeting.Rtp;

/// <summary>
/// Bidirectional UDP relay for one RTP or RTCP stream between two endpoints.
/// Binds a single local UDP socket, learns remote endpoint addresses from
/// the first received packet from each side, and forwards packets between them.
///
/// Pure pass-through — no header modification.
/// </summary>
internal class RtpRelay : IDisposable
{
    private const string LOG_SRC = nameof(RtpRelay);

    private readonly UdpClient _socket;
    private readonly string _label;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>Local port this relay is bound to.</summary>
    public int LocalPort { get; }

    /// <summary>Endpoint A's address (caller side).</summary>
    public IPEndPoint EndpointA { get; private set; }

    /// <summary>Endpoint B's address (callee side).</summary>
    public IPEndPoint EndpointB { get; private set; }

    /// <summary>Packets forwarded from A to B.</summary>
    public long PacketsAtoB { get; private set; }

    /// <summary>Packets forwarded from B to A.</summary>
    public long PacketsBtoA { get; private set; }

    /// <summary>Bytes forwarded from A to B.</summary>
    public long BytesAtoB { get; private set; }

    /// <summary>Bytes forwarded from B to A.</summary>
    public long BytesBtoA { get; private set; }

    /// <summary>Total packets forwarded in both directions.</summary>
    public long TotalPackets => PacketsAtoB + PacketsBtoA;

    /// <summary>Total bytes forwarded in both directions.</summary>
    public long TotalBytes => BytesAtoB + BytesBtoA;

    /// <summary>When the relay was started.</summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>When the relay was stopped.</summary>
    public DateTime? StoppedAt { get; private set; }

    /// <summary>
    /// Create a relay bound to a specific local port.
    /// </summary>
    /// <param name="localAddress">Local address to bind to.</param>
    /// <param name="localPort">Local UDP port to bind to.</param>
    /// <param name="label">Label for logging (e.g. "RTP:audio" or "RTCP:video").</param>
    public RtpRelay(IPAddress localAddress, int localPort, string label = "RTP")
    {
        _label = label;
        LocalPort = localPort;
        _socket = new UdpClient(new IPEndPoint(localAddress, localPort));
    }

    /// <summary>
    /// Set the expected endpoint addresses. Packets from these addresses will be
    /// forwarded to the other side. Unknown senders are ignored.
    /// </summary>
    public void SetEndpoints(IPEndPoint endpointA, IPEndPoint endpointB)
    {
        EndpointA = endpointA;
        EndpointB = endpointB;
    }

    /// <summary>
    /// Run the relay loop until cancelled or stopped.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        _running = true;
        StartedAt = DateTime.UtcNow;

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{_label} relay started on port {LocalPort}: {EndpointA} ↔ {EndpointB}", "");

        try
        {
            while (_running && !ct.IsCancellationRequested)
            {
                UdpReceiveResult result;

                try
                {
                    result = await _socket.ReceiveAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException) when (!_running)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                var sender = result.RemoteEndPoint;
                var data = result.Buffer;

                // Determine direction and forward
                if (IsFromEndpoint(sender, EndpointA))
                {
                    // A → B
                    if (EndpointB != null)
                    {
                        try
                        {
                            await _socket.SendAsync(data, data.Length, EndpointB);
                            PacketsAtoB++;
                            BytesAtoB += data.Length;
                        }
                        catch (SocketException ex)
                        {
                            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"{_label} send to B failed: {ex.Message}", "");
                        }
                    }
                }
                else if (IsFromEndpoint(sender, EndpointB))
                {
                    // B → A
                    if (EndpointA != null)
                    {
                        try
                        {
                            await _socket.SendAsync(data, data.Length, EndpointA);
                            PacketsBtoA++;
                            BytesBtoA += data.Length;
                        }
                        catch (SocketException ex)
                        {
                            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"{_label} send to A failed: {ex.Message}", "");
                        }
                    }
                }
                else
                {
                    // Learn endpoint address if one side hasn't been seen yet
                    if (EndpointA == null)
                    {
                        EndpointA = sender;
                        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"{_label} learned endpoint A: {sender}", "");
                    }
                    else if (EndpointB == null)
                    {
                        EndpointB = sender;
                        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"{_label} learned endpoint B: {sender}", "");
                    }
                    else
                    {
                        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"{_label} ignoring packet from unknown {sender}", "");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.WriteException(LOG_SRC, ex, "");
        }
        finally
        {
            _running = false;
            StoppedAt = DateTime.UtcNow;

            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{_label} relay stopped. A→B: {PacketsAtoB} pkts/{BytesAtoB} bytes, B→A: {PacketsBtoA} pkts/{BytesBtoA} bytes", "");
        }
    }

    /// <summary>Stop the relay.</summary>
    public void Stop()
    {
        _running = false;
        _socket.Close();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _running = false;
            _socket.Dispose();
        }
    }

    /// <summary>
    /// Check if a received packet's sender matches an expected endpoint.
    /// Compares IP address and port.
    /// </summary>
    private static bool IsFromEndpoint(IPEndPoint sender, IPEndPoint expected)
    {
        if (expected == null || sender == null)
        {
            return false;
        }

        return sender.Address.Equals(expected.Address) && sender.Port == expected.Port;
    }
}
