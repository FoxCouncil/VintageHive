// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Diagnostics;

using VintageHive.Proxy.Http;
using VintageHive.Proxy.Pna;

namespace VintageHive.Processors.LocalServer.Streaming;

internal static class RadioPnaStreaming
{
    private const string LogSys = "PNA-STREAM";

    private static readonly ConcurrentDictionary<string, PnaLiveSession> _liveSessions = new();
    private static readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    // ===================================================================
    // FFmpeg process creation
    // ===================================================================

    /// <summary>
    /// Create FFmpeg process that decodes upstream audio to raw PCM for CookEncoder.
    /// </summary>
    private static Process CreateCookFfmpegProcess(RealCodecProfile profile)
    {
        var cmdPath = FfmpegUtils.GetExecutablePath();
        var args = $"-probesize 32768 -analyzeduration 0 -i pipe:0 -fflags nobuffer -flush_packets 1 -c:a pcm_s16le -ar {profile.SampleRate} -ac {profile.Channels} -f s16le pipe:1";

        var process = new Process();

        process.StartInfo.FileName = cmdPath;
        process.StartInfo.Arguments = args;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        return process;
    }

    /// <summary>
    /// Create FFmpeg process that encodes upstream audio directly to RM container
    /// with the ra_144 (14.4kbps lpcJ) codec. FFmpeg handles everything —
    /// we just read RM chunks from stdout.
    /// </summary>
    private static Process CreateRa144FfmpegProcess()
    {
        var cmdPath = FfmpegUtils.GetExecutablePath();
        var args = "-probesize 32768 -analyzeduration 0 -i pipe:0 -fflags nobuffer -flush_packets 1 -c:a ra_144 -f rm pipe:1";

        var process = new Process();

        process.StartInfo.FileName = cmdPath;
        process.StartInfo.Arguments = args;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        return process;
    }

    // ===================================================================
    // PNA Live Session — ring buffer with RM data packets
    // ===================================================================

    internal class PnaLiveSession : IDisposable
    {
        /// <summary>
        /// Raw RealMedia header bytes (PROP + CONT + MDPR + DATA header).
        /// Used by PNA clients after PNA_TAG handshake.
        /// </summary>
        public byte[] RmHeaders { get; }

        /// <summary>
        /// Full RM file headers (.RMF + PROP + CONT + MDPR + DATA header).
        /// Used by HTTP clients. Rebuilt with current track title on access.
        /// </summary>
        public byte[] RmFileHeaders { get; }

        public RadioStationInfo Station { get; }
        public RealCodecProfile Profile { get; }

        private const int RingCapacity = 300;
        private readonly byte[][] _ring = new byte[RingCapacity][];
        private long _headSeq;
        private readonly object _lock = new();
        private TaskCompletionSource _newDataTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly HttpClient _httpClient;
        private readonly HttpResponseMessage _upstreamResponse;
        private readonly Process _ffmpeg;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        private int _activeClients;
        private CancellationTokenSource _cleanupCts;

        public bool IsAlive => !_producerTask.IsCompleted && !_disposed;

        /// <summary>CookEncoder instance (null for ra_144 pipeline).</summary>
        public CookEncoder Encoder { get; }

        // Track metadata from ICY stream
        private readonly IcyMetadataStrippingStream _icyStream;

        /// <summary>Current track title from upstream ICY metadata, or station default.</summary>
        public string CurrentTrack => _icyStream?.CurrentTrack ?? Station?.CurrentTrack;

        // Track change notification (mirrors MmshLiveSession pattern)
        private readonly object _trackLock = new();
        private TaskCompletionSource _trackChangeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _lastNotifiedTrack;
        private long _lastTrackChangeTimestamp;

        public Task TrackChangeTask { get { lock (_trackLock) return _trackChangeTcs.Task; } }

        public bool TryGetTrackUpdate(string lastKnown, out string newTrack)
        {
            newTrack = CurrentTrack;
            return newTrack != null && newTrack != lastKnown;
        }

        /// <summary>Cook pipeline: CookEncoder encodes PCM from FFmpeg.</summary>
        public PnaLiveSession(HttpClient httpClient, HttpResponseMessage upstreamResponse, Process ffmpeg, byte[] rmHeaders, byte[] rmFileHeaders, Stream ffmpegOutput, RadioStationInfo stationInfo, RealCodecProfile profile, CookEncoder encoder, IcyMetadataStrippingStream icyStream = null)
        {
            _httpClient = httpClient;
            _upstreamResponse = upstreamResponse;
            _ffmpeg = ffmpeg;
            RmHeaders = rmHeaders;
            RmFileHeaders = rmFileHeaders;
            Station = stationInfo;
            Profile = profile;
            Encoder = encoder;
            _icyStream = icyStream;
            _lastNotifiedTrack = stationInfo?.CurrentTrack;

            if (_icyStream != null)
            {
                _icyStream.TrackChanged += OnTrackChanged;
            }

            _producerTask = Task.Run(() => ProduceCookLoop(ffmpegOutput));
        }

        /// <summary>Ra144 pipeline: FFmpeg produces complete RM data packets.</summary>
        public PnaLiveSession(HttpClient httpClient, HttpResponseMessage upstreamResponse, Process ffmpeg, byte[] rmHeaders, byte[] rmFileHeaders, Stream ffmpegOutput, RadioStationInfo stationInfo, RealCodecProfile profile, IcyMetadataStrippingStream icyStream = null)
        {
            _httpClient = httpClient;
            _upstreamResponse = upstreamResponse;
            _ffmpeg = ffmpeg;
            RmHeaders = rmHeaders;
            RmFileHeaders = rmFileHeaders;
            Station = stationInfo;
            Profile = profile;
            Encoder = null;
            _icyStream = icyStream;
            _lastNotifiedTrack = stationInfo?.CurrentTrack;

            if (_icyStream != null)
            {
                _icyStream.TrackChanged += OnTrackChanged;
            }

            _producerTask = Task.Run(() => ProduceRa144Loop(ffmpegOutput));
        }

        private void OnTrackChanged(string newTrack)
        {
            var now = Stopwatch.GetTimestamp();
            lock (_trackLock)
            {
                // Debounce: skip if less than 5 seconds since last change
                var elapsedMs = (now - _lastTrackChangeTimestamp) * 1000.0 / Stopwatch.Frequency;
                if (_lastTrackChangeTimestamp != 0 && elapsedMs < 5000)
                {
                    return;
                }

                _lastTrackChangeTimestamp = now;
                _lastNotifiedTrack = newTrack;

                var oldTcs = _trackChangeTcs;
                _trackChangeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                oldTcs.TrySetResult();
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Track changed: \"{newTrack}\"");
        }

        /// <summary>Cook pipeline producer: reads raw PCM from FFmpeg, encodes via CookEncoder.</summary>
        private async Task ProduceCookLoop(Stream ffmpegOut)
        {
            uint packetCount = 0;
            uint readCount = 0;
            long totalPcmBytes = 0;
            var readBuf = new byte[4096];

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    int bytesRead = await ffmpegOut.ReadAsync(readBuf, _cts.Token);
                    if (bytesRead == 0)
                    {
                        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Producer: FFmpeg EOF after {readCount} reads, {totalPcmBytes} PCM bytes");
                        break;
                    }

                    readCount++;
                    totalPcmBytes += bytesRead;

                    if (readCount <= 20 || readCount % 200 == 0)
                    {
                        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Producer: read#{readCount} {bytesRead}B (total PCM={totalPcmBytes}, pkts={packetCount})");
                    }

                    // Feed raw PCM to cook encoder — it returns RM data packets
                    var packets = Encoder.EncodePcm(readBuf.AsSpan(0, bytesRead));

                    foreach (var packet in packets)
                    {
                        PushPacket(packet);
                        packetCount++;

                        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Producer: superpacket #{packetCount} len={packet.Length} (after {readCount} reads)");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_ERROR, LogSys, $"Producer error: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                lock (_lock) { _newDataTcs.TrySetResult(); }
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Producer ended ({packetCount} total packets, {readCount} reads)");
        }

        /// <summary>Ra144 pipeline producer: reads complete RM data packets from FFmpeg stdout.</summary>
        private async Task ProduceRa144Loop(Stream ffmpegOut)
        {
            uint packetCount = 0;

            try
            {
                // FFmpeg produces RM container: skip header chunks, then read data packets
                // Read and discard RM header chunks (.RMF, PROP, CONT, MDPR, DATA header)
                // until we reach the DATA chunk, then read data packets
                while (!_cts.IsCancellationRequested)
                {
                    var (tag, chunk) = await PnaCommand.ReadRmChunkAsync(ffmpegOut);
                    if (tag == 0 || chunk == null)
                    {
                        Log.WriteLine(Log.LEVEL_ERROR, LogSys, "Ra144 producer: failed to read RM header chunks");
                        return;
                    }

                    Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Ra144 producer: header chunk {PnaCommand.FourCCToString(tag)} ({chunk.Length} bytes)");

                    if (tag == PnaCommand.RM_DATA_TAG)
                    {
                        break; // DATA chunk found — data packets follow
                    }
                }

                // Read RM data packets from the DATA section
                while (!_cts.IsCancellationRequested)
                {
                    var packet = await PnaCommand.ReadRmDataPacketAsync(ffmpegOut);
                    if (packet == null)
                    {
                        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Ra144 producer: FFmpeg EOF after {packetCount} packets");
                        break;
                    }

                    PushPacket(packet);
                    packetCount++;

                    if (packetCount <= 20 || packetCount % 200 == 0)
                    {
                        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Ra144 producer: packet #{packetCount} len={packet.Length}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_ERROR, LogSys, $"Ra144 producer error: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                lock (_lock) { _newDataTcs.TrySetResult(); }
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Ra144 producer ended ({packetCount} total packets)");
        }

        /// <summary>Push an RM data packet into the ring buffer and wake consumers.</summary>
        private void PushPacket(byte[] packet)
        {
            TaskCompletionSource oldTcs;
            lock (_lock)
            {
                _ring[_headSeq % RingCapacity] = packet;
                _headSeq++;
                oldTcs = _newDataTcs;
                _newDataTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            oldTcs.TrySetResult();
        }

        public long LivePosition { get { lock (_lock) return _headSeq; } }

        public (byte[] chunk, long seq) TryRead(long requestedSeq)
        {
            lock (_lock)
            {
                long oldest = Math.Max(0, _headSeq - RingCapacity);
                long seq = Math.Max(requestedSeq, oldest);
                if (seq >= _headSeq) return (null, seq);
                return (_ring[seq % RingCapacity], seq);
            }
        }

        public async Task WaitForDataAsync(long afterSeq)
        {
            Task waitTask;
            lock (_lock)
            {
                if (_headSeq > afterSeq) return;
                waitTask = _newDataTcs.Task;
            }
            await waitTask.WaitAsync(_cts.Token);
        }

        public void AddClient(string sessionKey)
        {
            var count = Interlocked.Increment(ref _activeClients);
            _cleanupCts?.Cancel();
            _cleanupCts = null;
            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session {sessionKey}: client connected ({count} active)");
        }

        public void RemoveClient(string sessionKey)
        {
            var count = Interlocked.Decrement(ref _activeClients);
            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session {sessionKey}: client disconnected ({count} active)");
            if (count <= 0)
            {
                Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session {sessionKey}: last client left, cleanup in 30s");
                ScheduleCleanup(sessionKey);
            }
        }

        private void ScheduleCleanup(string sessionKey)
        {
            var cts = new CancellationTokenSource();
            _cleanupCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
                    if (_activeClients <= 0)
                    {
                        _liveSessions.TryRemove(sessionKey, out _);
                        Dispose();
                        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session {sessionKey} cleaned up (no clients for 30s)");
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_icyStream != null)
            {
                _icyStream.TrackChanged -= OnTrackChanged;
            }

            _cts.Cancel();
            try { _ffmpeg.Kill(); } catch { }
            try { _ffmpeg.Dispose(); } catch { }
            try { _upstreamResponse.Dispose(); } catch { }
            try { _httpClient.Dispose(); } catch { }
            _cts.Dispose();
        }
    }

    // ===================================================================
    // Session factory
    // ===================================================================

    internal static async Task<PnaLiveSession> GetOrCreateSessionAsync(string stationId, RealCodecProfile profile = null)
    {
        profile ??= RealCodecProfile.CookStereo11k;
        var sessionKey = $"{stationId}:{profile.Key}";

        if (_liveSessions.TryGetValue(sessionKey, out var session) && session.IsAlive)
        {
            return session;
        }

        await _sessionCreateLock.WaitAsync();
        try
        {
            if (_liveSessions.TryGetValue(sessionKey, out session) && session.IsAlive)
            {
                return session;
            }

            var info = await RadioStationResolver.ResolveStation(stationId);
            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session creating: {info.Name} ({info.Codec}) profile={profile}");

            var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
            });
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            httpClient.DefaultRequestHeaders.Add("Icy-MetaData", "1");

            var upstreamResponse = await httpClient.GetAsync(info.StreamUrl, HttpCompletionOption.ResponseHeadersRead);
            var rawUpstreamStream = await upstreamResponse.Content.ReadAsStreamAsync();

            IcyMetadataStrippingStream icyStream = null;
            Stream upstreamStream = rawUpstreamStream;

            IEnumerable<string> metaintValues = null;
            if (!upstreamResponse.Headers.TryGetValues("icy-metaint", out metaintValues))
            {
                upstreamResponse.Content.Headers.TryGetValues("icy-metaint", out metaintValues);
            }

            if (metaintValues != null)
            {
                var metaintStr = metaintValues.FirstOrDefault();
                if (int.TryParse(metaintStr, out int metaInterval) && metaInterval > 0)
                {
                    icyStream = new IcyMetadataStrippingStream(rawUpstreamStream, metaInterval);
                    upstreamStream = icyStream;
                    Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session: ICY metadata interval={metaInterval}");
                }
            }

            PnaLiveSession newSession;

            if (profile.CodecType == RealCodecType.Ra144)
            {
                newSession = await CreateRa144Session(httpClient, upstreamResponse, upstreamStream, info, profile, icyStream);
            }
            else
            {
                newSession = await CreateCookSession(httpClient, upstreamResponse, upstreamStream, info, profile, icyStream);
            }

            if (_liveSessions.TryGetValue(sessionKey, out var old))
            {
                old.Dispose();
            }
            _liveSessions[sessionKey] = newSession;

            return newSession;
        }
        finally
        {
            _sessionCreateLock.Release();
        }
    }

    private static async Task<PnaLiveSession> CreateCookSession(HttpClient httpClient, HttpResponseMessage upstreamResponse, Stream upstreamStream, RadioStationInfo info, RealCodecProfile profile, IcyMetadataStrippingStream icyStream)
    {
        var ffmpeg = CreateCookFfmpegProcess(profile);
        ffmpeg.Start();
        _ = Task.Run(async () =>
        {
            using var reader = ffmpeg.StandardError;
            while (await reader.ReadLineAsync() is { } line)
            {
                Log.WriteLine(Log.LEVEL_VERBOSE, "FFMPEG-RA", line);
            }
        });
        _ = Task.Run(async () =>
        {
            try { await upstreamStream.CopyToAsync(ffmpeg.StandardInput.BaseStream); }
            catch { }
            try { ffmpeg.StandardInput.Close(); } catch { }
        });

        var ffmpegOut = ffmpeg.StandardOutput.BaseStream;

        var encoder = new CookEncoder(profile.SampleRate, profile.Channels, profile.Bitrate);

        // Build title with current track if available
        var title = BuildStreamTitle(info, icyStream?.CurrentTrack);
        var rmHeaders = encoder.BuildRmHeaders(title);
        var rmFileHeaders = encoder.BuildRmFileHeaders(title);

        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session ready: cook {profile}, RM headers={rmHeaders.Length}B station=\"{info.Name}\"");

        return new PnaLiveSession(httpClient, upstreamResponse, ffmpeg, rmHeaders, rmFileHeaders, ffmpegOut, info, profile, encoder, icyStream);
    }

    private static async Task<PnaLiveSession> CreateRa144Session(HttpClient httpClient, HttpResponseMessage upstreamResponse, Stream upstreamStream, RadioStationInfo info, RealCodecProfile profile, IcyMetadataStrippingStream icyStream)
    {
        var ffmpeg = CreateRa144FfmpegProcess();
        ffmpeg.Start();
        _ = Task.Run(async () =>
        {
            using var reader = ffmpeg.StandardError;
            while (await reader.ReadLineAsync() is { } line)
            {
                Log.WriteLine(Log.LEVEL_VERBOSE, "FFMPEG-RA144", line);
            }
        });
        _ = Task.Run(async () =>
        {
            try { await upstreamStream.CopyToAsync(ffmpeg.StandardInput.BaseStream); }
            catch { }
            try { ffmpeg.StandardInput.Close(); } catch { }
        });

        var ffmpegOut = ffmpeg.StandardOutput.BaseStream;

        // Read RM header chunks from FFmpeg's RM muxer output
        // Collect PROP+CONT+MDPR+DATA for PNA, and .RMF+PROP+CONT+MDPR+DATA for HTTP
        using var pnaHeaderMs = new MemoryStream();
        using var httpHeaderMs = new MemoryStream();

        while (true)
        {
            var (tag, chunk) = await PnaCommand.ReadRmChunkAsync(ffmpegOut);
            if (tag == 0 || chunk == null)
            {
                throw new InvalidOperationException("FFmpeg ra_144: failed to read RM header chunks");
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Ra144: header chunk {PnaCommand.FourCCToString(tag)} ({chunk.Length} bytes)");

            // HTTP gets everything; PNA skips .RMF
            httpHeaderMs.Write(chunk, 0, chunk.Length);
            if (tag != PnaCommand.RM_RMF_TAG)
            {
                pnaHeaderMs.Write(chunk, 0, chunk.Length);
            }

            if (tag == PnaCommand.RM_DATA_TAG)
            {
                break;
            }
        }

        var rmHeaders = pnaHeaderMs.ToArray();
        var rmFileHeaders = httpHeaderMs.ToArray();

        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session ready: ra_144, PNA headers={rmHeaders.Length}B HTTP headers={rmFileHeaders.Length}B station=\"{info.Name}\"");

        return new PnaLiveSession(httpClient, upstreamResponse, ffmpeg, rmHeaders, rmFileHeaders, ffmpegOut, info, profile, icyStream);
    }

    // ===================================================================
    // HTTP streaming — /stream/real/{id}.ra
    // Serves RealAudio over HTTP for RealPlayer G2+
    // ===================================================================

    public static async Task HandleRealStream(HttpRequest request, HttpResponse response, string stationId)
    {
        var traceId = request.ListenerSocket.TraceId.ToString();
        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"HTTP: /stream/real/{stationId}", traceId);

        PnaLiveSession session = null;
        string sessionKey = null;

        try
        {
            // HTTP path defaults to cook stereo 11kHz (proven compatible)
            var profile = RealCodecProfile.CookStereo11k;
            session = await GetOrCreateSessionAsync(stationId, profile);
            sessionKey = $"{stationId}:{profile.Key}";
            session.AddClient(sessionKey);

            // Mark handled immediately — we're writing directly to the socket.
            response.Handled = true;

            var socket = request.ListenerSocket.Stream;

            // Send HTTP response headers — serve as RM container
            var httpHeaders =
                $"{request.Version} 200 OK\r\n" +
                "Content-Type: application/vnd.rn-realmedia\r\n" +
                "Pragma: no-cache\r\n" +
                "Cache-Control: no-cache\r\n" +
                "\r\n";
            await socket.WriteAsync(Encoding.ASCII.GetBytes(httpHeaders));

            // Build RM file headers with current track title
            byte[] rmFileHeaders;
            if (session.Encoder != null)
            {
                var title = BuildStreamTitle(session.Station, session.CurrentTrack);
                rmFileHeaders = session.Encoder.BuildRmFileHeaders(title);
            }
            else
            {
                // Ra144: use stored headers from FFmpeg
                rmFileHeaders = session.RmFileHeaders;
            }

            await socket.WriteAsync(rmFileHeaders);
            await socket.FlushAsync();

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"HTTP: sent RM file headers ({rmFileHeaders.Length} bytes)", traceId);

            // Stream full RM data packets (with 12-byte headers intact)
            long readPos = Math.Max(0, session.LivePosition - 5);
            uint sent = 0;

            while (session.IsAlive)
            {
                var (chunk, seq) = session.TryRead(readPos);
                if (chunk != null)
                {
                    await socket.WriteAsync(chunk);

                    readPos = seq + 1;
                    sent++;

                    if (sent <= 15 || sent % 50 == 0)
                    {
                        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"HTTP: packet #{sent} sent, len={chunk.Length}, seq={seq}", traceId);
                    }

                    // Flush aggressively during initial buffering (first 2 groups),
                    // then every 10 packets (one group) after that
                    if (sent <= 20 || sent % 10 == 0)
                    {
                        await socket.FlushAsync();
                    }
                }
                else
                {
                    await socket.FlushAsync();
                    await session.WaitForDataAsync(readPos);
                }
            }
        }
        catch (IOException)
        {
            Log.WriteLine(Log.LEVEL_INFO, LogSys, "HTTP: client disconnected", traceId);
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_ERROR, LogSys, $"HTTP: error: {ex.Message}", traceId);
            if (!response.Handled)
            {
                response.SetNotFound();
            }
        }
        finally
        {
            if (session != null && sessionKey != null)
            {
                session.RemoveClient(sessionKey);
            }
        }
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    /// <summary>
    /// Build a stream title combining station name and current track.
    /// Used for CONT chunk in RM headers.
    /// </summary>
    private static string BuildStreamTitle(RadioStationInfo info, string currentTrack)
    {
        if (!string.IsNullOrEmpty(currentTrack))
        {
            return $"{info.Name} - {currentTrack}";
        }

        return info.Name;
    }
}
