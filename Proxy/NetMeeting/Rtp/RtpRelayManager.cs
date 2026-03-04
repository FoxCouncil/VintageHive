// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace VintageHive.Proxy.NetMeeting.Rtp;

/// <summary>
/// Manages RTP/RTCP relay pairs for an H.323 call.
/// Allocates even/odd port pairs, creates relay instances per logical channel,
/// and provides lifecycle management.
///
/// Each logical channel gets two relays: one for RTP (even port) and one for
/// RTCP (odd port = RTP port + 1).
/// </summary>
internal class RtpRelayManager : IDisposable
{
    private const string LOG_SRC = nameof(RtpRelayManager);

    private readonly IPAddress _localAddress;
    private readonly ConcurrentDictionary<int, RelayPair> _relays = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public RtpRelayManager(IPAddress localAddress)
    {
        _localAddress = localAddress;
    }

    /// <summary>Active relay pairs keyed by logical channel number.</summary>
    public IReadOnlyDictionary<int, RelayPair> ActiveRelays =>
        new Dictionary<int, RelayPair>(_relays);

    /// <summary>
    /// Create a relay pair for a logical channel.
    /// Allocates an even/odd port pair and creates RTP + RTCP relays.
    /// </summary>
    /// <param name="channelNumber">H.245 logical channel number.</param>
    /// <param name="sessionLabel">Label for logging (e.g. "audio", "video").</param>
    /// <returns>The allocated relay pair with local port information.</returns>
    public RelayPair CreateRelay(int channelNumber, string sessionLabel = "media")
    {
        var rtpPort = AllocateEvenPort();

        var rtpRelay = new RtpRelay(_localAddress, rtpPort, $"RTP:{sessionLabel}");
        var rtcpRelay = new RtpRelay(_localAddress, rtpPort + 1, $"RTCP:{sessionLabel}");

        var pair = new RelayPair
        {
            ChannelNumber = channelNumber,
            RtpRelay = rtpRelay,
            RtcpRelay = rtcpRelay,
            LocalRtpPort = rtpPort,
            LocalRtcpPort = rtpPort + 1
        };

        _relays[channelNumber] = pair;

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
            $"Created relay pair for channel {channelNumber} ({sessionLabel}): " +
            $"RTP={rtpPort}, RTCP={rtpPort + 1}", "");

        return pair;
    }

    /// <summary>
    /// Start a relay pair. Sets endpoints and begins forwarding.
    /// </summary>
    public void StartRelay(int channelNumber,
        IPEndPoint callerRtp, IPEndPoint callerRtcp,
        IPEndPoint calleeRtp, IPEndPoint calleeRtcp)
    {
        if (!_relays.TryGetValue(channelNumber, out var pair))
        {
            throw new InvalidOperationException(
                $"No relay pair for channel {channelNumber}");
        }

        pair.RtpRelay.SetEndpoints(callerRtp, calleeRtp);
        pair.RtcpRelay.SetEndpoints(callerRtcp, calleeRtcp);

        // Start relay loops in background
        pair.RtpTask = pair.RtpRelay.RunAsync(_cts.Token);
        pair.RtcpTask = pair.RtcpRelay.RunAsync(_cts.Token);

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
            $"Started relay for channel {channelNumber}: " +
            $"A[{callerRtp}] ↔ B[{calleeRtp}]", "");
    }

    /// <summary>
    /// Stop and remove a relay pair for a specific channel.
    /// </summary>
    public async Task StopRelayAsync(int channelNumber)
    {
        if (!_relays.TryRemove(channelNumber, out var pair))
        {
            return;
        }

        pair.RtpRelay.Stop();
        pair.RtcpRelay.Stop();

        // Wait for relay tasks to complete
        if (pair.RtpTask != null)
        {
            try { await pair.RtpTask; } catch { }
        }

        if (pair.RtcpTask != null)
        {
            try { await pair.RtcpTask; } catch { }
        }

        pair.RtpRelay.Dispose();
        pair.RtcpRelay.Dispose();

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
            $"Stopped relay for channel {channelNumber}. " +
            $"RTP: {pair.RtpRelay.TotalPackets} pkts, " +
            $"RTCP: {pair.RtcpRelay.TotalPackets} pkts", "");
    }

    /// <summary>
    /// Stop all active relays and clean up.
    /// </summary>
    public async Task StopAllAsync()
    {
        _cts.Cancel();

        foreach (var kvp in _relays)
        {
            var pair = kvp.Value;
            pair.RtpRelay.Stop();
            pair.RtcpRelay.Stop();
        }

        foreach (var kvp in _relays)
        {
            var pair = kvp.Value;

            if (pair.RtpTask != null)
            {
                try { await pair.RtpTask; } catch { }
            }

            if (pair.RtcpTask != null)
            {
                try { await pair.RtcpTask; } catch { }
            }

            pair.RtpRelay.Dispose();
            pair.RtcpRelay.Dispose();
        }

        _relays.Clear();
    }

    /// <summary>
    /// Get the local RTP endpoint for a channel (for rewriting H.245 OLC messages).
    /// </summary>
    public IPEndPoint GetLocalRtpEndpoint(int channelNumber)
    {
        if (_relays.TryGetValue(channelNumber, out var pair))
        {
            return new IPEndPoint(_localAddress, pair.LocalRtpPort);
        }

        return null;
    }

    /// <summary>
    /// Get the local RTCP endpoint for a channel.
    /// </summary>
    public IPEndPoint GetLocalRtcpEndpoint(int channelNumber)
    {
        if (_relays.TryGetValue(channelNumber, out var pair))
        {
            return new IPEndPoint(_localAddress, pair.LocalRtcpPort);
        }

        return null;
    }

    /// <summary>
    /// Get aggregate statistics for all relays.
    /// </summary>
    public RelayStatistics GetStatistics()
    {
        long totalRtpPackets = 0;
        long totalRtpBytes = 0;
        long totalRtcpPackets = 0;
        long totalRtcpBytes = 0;

        foreach (var kvp in _relays)
        {
            totalRtpPackets += kvp.Value.RtpRelay.TotalPackets;
            totalRtpBytes += kvp.Value.RtpRelay.TotalBytes;
            totalRtcpPackets += kvp.Value.RtcpRelay.TotalPackets;
            totalRtcpBytes += kvp.Value.RtcpRelay.TotalBytes;
        }

        return new RelayStatistics
        {
            ActiveChannels = _relays.Count,
            TotalRtpPackets = totalRtpPackets,
            TotalRtpBytes = totalRtpBytes,
            TotalRtcpPackets = totalRtcpPackets,
            TotalRtcpBytes = totalRtcpBytes
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cts.Cancel();

            foreach (var kvp in _relays)
            {
                kvp.Value.RtpRelay.Dispose();
                kvp.Value.RtcpRelay.Dispose();
            }

            _relays.Clear();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Allocate an even-numbered port from the dynamic range.
    /// Tries to bind a UDP socket to verify availability.
    /// </summary>
    internal static int AllocateEvenPort()
    {
        // Let the OS assign a port, then find the next even one
        // Strategy: bind to 0, get the assigned port, release it,
        // then use the nearest even port
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var assigned = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
        probe.Close();

        // Ensure even port (for RTP, RTCP gets the next odd port)
        var evenPort = assigned % 2 == 0 ? assigned : assigned + 1;

        // Verify both the even port and the next odd port are available
        try
        {
            using var testRtp = new UdpClient(new IPEndPoint(IPAddress.Any, evenPort));
            testRtp.Close();
        }
        catch (SocketException)
        {
            // Port taken — try next even port
            evenPort += 2;
        }

        try
        {
            using var testRtcp = new UdpClient(new IPEndPoint(IPAddress.Any, evenPort + 1));
            testRtcp.Close();
        }
        catch (SocketException)
        {
            // RTCP port taken — try next even port
            evenPort += 2;
        }

        return evenPort;
    }
}

/// <summary>
/// A pair of relays (RTP + RTCP) for one logical channel.
/// </summary>
internal class RelayPair
{
    public int ChannelNumber { get; init; }
    public RtpRelay RtpRelay { get; init; }
    public RtpRelay RtcpRelay { get; init; }
    public int LocalRtpPort { get; init; }
    public int LocalRtcpPort { get; init; }

    /// <summary>Background task running the RTP relay loop.</summary>
    public Task RtpTask { get; set; }

    /// <summary>Background task running the RTCP relay loop.</summary>
    public Task RtcpTask { get; set; }
}

/// <summary>
/// Aggregate relay statistics.
/// </summary>
internal class RelayStatistics
{
    public int ActiveChannels { get; init; }
    public long TotalRtpPackets { get; init; }
    public long TotalRtpBytes { get; init; }
    public long TotalRtcpPackets { get; init; }
    public long TotalRtcpBytes { get; init; }
}
