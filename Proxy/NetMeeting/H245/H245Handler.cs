// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;

namespace VintageHive.Proxy.NetMeeting.H245;

/// <summary>
/// H.245 control channel handler — accepts a TCP connection from one side
/// of an H.323 call, opens an outbound TCP to the other side, then proxies
/// TPKT-framed H.245 messages bidirectionally.
///
/// During proxying, OLC (OpenLogicalChannel) and OLC-Ack messages are
/// inspected so the handler can track which logical channels are open
/// and extract RTP/RTCP transport addresses. This information is exposed
/// for future RTP relay integration.
/// </summary>
internal class H245Handler : IDisposable
{
    private const string LOG_SRC = nameof(H245Handler);

    private readonly TcpListener _listener;
    private readonly IPAddress _localAddress;
    private bool _disposed;

    /// <summary>Port the handler is listening on (resolved after Start).</summary>
    public int Port { get; private set; }

    /// <summary>Logical channels opened during the H.245 session (keyed by channel number).</summary>
    public Dictionary<int, LogicalChannelInfo> LogicalChannels { get; } = new();

    /// <summary>Whether master/slave determination has completed.</summary>
    public bool MasterSlaveResolved { get; private set; }

    /// <summary>Local side's role after MSD: true = master.</summary>
    public bool IsMaster { get; private set; }

    /// <summary>Number of H.245 messages proxied.</summary>
    public int MessageCount { get; private set; }

    /// <summary>Callback invoked when a logical channel reaches the Open state (OLC-Ack received).</summary>
    public Action<int, LogicalChannelInfo> OnChannelOpened { get; set; }

    /// <summary>Callback invoked when a logical channel is closed (on CLC receipt).</summary>
    public Action<int> OnChannelClosed { get; set; }

    public H245Handler(IPAddress localAddress, int port)
    {
        _localAddress = localAddress;
        _listener = new TcpListener(localAddress, port);
        Port = port;
    }

    /// <summary>
    /// Run the H.245 proxy: accept one inbound connection, connect outbound
    /// to the remote H.245 address, then proxy messages until the channel closes.
    /// </summary>
    public async Task RunAsync(IPEndPoint remoteH245Address, CancellationToken ct = default)
    {
        _listener.Start(1);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        try
        {
            // Accept inbound H.245 connection
            using var inboundClient = await AcceptWithCancellation(ct);
            var inboundStream = inboundClient.GetStream();

            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
                $"H.245 inbound connected from {inboundClient.Client.RemoteEndPoint}", "");

            // Connect outbound to the other side's H.245 address
            using var outboundClient = new TcpClient();
            await outboundClient.ConnectAsync(remoteH245Address.Address, remoteH245Address.Port);
            var outboundStream = outboundClient.GetStream();

            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
                $"H.245 outbound connected to {remoteH245Address}", "");

            // Proxy bidirectionally
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var inToOut = ProxyDirection(inboundStream, outboundStream, "in→out", cts);
            var outToIn = ProxyDirection(outboundStream, inboundStream, "out→in", cts);

            await Task.WhenAny(inToOut, outToIn);

            cts.Cancel();

            try
            {
                await Task.WhenAll(inToOut, outToIn);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }

            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
                $"H.245 session ended, {MessageCount} messages proxied", "");
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task ProxyDirection(NetworkStream source, NetworkStream dest,
        string direction, CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var payload = await TpktFrame.ReadAsync(source);
                if (payload == null)
                {
                    break;
                }

                // Inspect the message (best-effort — parse failures don't block forwarding)
                try
                {
                    var msg = H245Codec.Decode(payload);
                    InspectMessage(msg, direction);

                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                        $"H.245 {direction}: {msg}", "");
                }
                catch
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                        $"H.245 {direction}: (unparsed, {payload.Length} bytes)", "");
                }

                // Forward raw TPKT frame to other side
                await TpktFrame.WriteAsync(dest, payload);
                MessageCount++;
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
    }

    private void InspectMessage(H245Message msg, string direction)
    {
        if (msg.MasterSlaveDeterminationAck != null)
        {
            MasterSlaveResolved = true;
            // The ack tells us the receiver's role — if we received "master"
            // decision, it means the remote thinks WE are master
            IsMaster = msg.MasterSlaveDeterminationAck.Decision == H245Constants.MSD_DECISION_MASTER;
        }

        if (msg.OpenLogicalChannel != null)
        {
            var olc = msg.OpenLogicalChannel;
            LogicalChannels[olc.ForwardLogicalChannelNumber] = new LogicalChannelInfo
            {
                ChannelNumber = olc.ForwardLogicalChannelNumber,
                SessionId = olc.SessionId,
                DataType = olc.DataType,
                SenderMediaChannel = olc.MediaChannel,
                SenderMediaControlChannel = olc.MediaControlChannel,
                State = LogicalChannelState.Opening
            };
        }

        if (msg.OpenLogicalChannelAck != null)
        {
            var ack = msg.OpenLogicalChannelAck;
            if (LogicalChannels.TryGetValue(ack.ForwardLogicalChannelNumber, out var ch))
            {
                ch.ReceiverMediaChannel = ack.MediaChannel;
                ch.ReceiverMediaControlChannel = ack.MediaControlChannel;
                ch.State = LogicalChannelState.Open;
                OnChannelOpened?.Invoke(ack.ForwardLogicalChannelNumber, ch);
            }
        }

        if (msg.OpenLogicalChannelReject != null)
        {
            if (LogicalChannels.TryGetValue(msg.OpenLogicalChannelReject.ForwardLogicalChannelNumber, out var ch))
            {
                ch.State = LogicalChannelState.Rejected;
            }
        }

        if (msg.CloseLogicalChannel != null)
        {
            var lcn = msg.CloseLogicalChannel.ForwardLogicalChannelNumber;
            if (LogicalChannels.TryGetValue(lcn, out var ch))
            {
                ch.State = LogicalChannelState.Closed;
            }
            else
            {
                // Channel may have been opened before handler started tracking
                LogicalChannels[lcn] = new LogicalChannelInfo
                {
                    ChannelNumber = lcn,
                    State = LogicalChannelState.Closed
                };
            }
            OnChannelClosed?.Invoke(lcn);
        }

        if (msg.CloseLogicalChannelAck != null)
        {
            if (LogicalChannels.TryGetValue(msg.CloseLogicalChannelAck.ForwardLogicalChannelNumber, out var ch))
            {
                ch.State = LogicalChannelState.Closed;
            }
            // OnChannelClosed already fired on the CLC — CLC-Ack is just confirmation
        }
    }

    private async Task<TcpClient> AcceptWithCancellation(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<TcpClient>();

        using var reg = ct.Register(() => tcs.TrySetCanceled());

        var acceptTask = _listener.AcceptTcpClientAsync();
        var completed = await Task.WhenAny(acceptTask, tcs.Task);

        if (completed == tcs.Task)
        {
            throw new OperationCanceledException(ct);
        }

        return await acceptTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _listener.Stop();
        }
    }
}

/// <summary>State of a logical channel.</summary>
internal enum LogicalChannelState
{
    Opening,
    Open,
    Rejected,
    Closed
}

/// <summary>
/// Tracks an H.245 logical channel opened during the call.
/// Contains RTP/RTCP transport addresses from both sides.
/// </summary>
internal class LogicalChannelInfo
{
    /// <summary>Logical channel number (1..65535).</summary>
    public int ChannelNumber { get; init; }

    /// <summary>Session ID (1=audio, 2=video, 3=data).</summary>
    public int SessionId { get; init; }

    /// <summary>DataType CHOICE index (audio=3, video=2, etc.).</summary>
    public int DataType { get; init; }

    /// <summary>Current channel state.</summary>
    public LogicalChannelState State { get; set; }

    /// <summary>Media (RTP) address from the OLC sender.</summary>
    public IPEndPoint SenderMediaChannel { get; set; }

    /// <summary>Media control (RTCP) address from the OLC sender.</summary>
    public IPEndPoint SenderMediaControlChannel { get; set; }

    /// <summary>Media (RTP) address from the OLC-Ack responder.</summary>
    public IPEndPoint ReceiverMediaChannel { get; set; }

    /// <summary>Media control (RTCP) address from the OLC-Ack responder.</summary>
    public IPEndPoint ReceiverMediaControlChannel { get; set; }
}
