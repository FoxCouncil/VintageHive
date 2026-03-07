// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Pna;

namespace VintageHive.Processors.LocalServer.Streaming;

internal static class RadioPnaStreaming
{
    private const string LogSys = "PNA-STREAM";

    private static readonly ConcurrentDictionary<string, PnaLiveSession> _liveSessions = new();
    private static readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    // ===================================================================
    // FFmpeg process creation — decodes upstream to raw PCM for cook encoding
    // ===================================================================

    private const int CookSampleRate = 11025;
    private const int CookChannels = 2;
    private const int CookBitrate = 19000;

    private static Process CreateRaFfmpegProcess()
    {
        var cmdPath = GetFfmpegExecutablePath();
        var args = $"-probesize 32768 -analyzeduration 0 -i pipe:0 -fflags nobuffer -flush_packets 1 -c:a pcm_s16le -ar {CookSampleRate} -ac {CookChannels} -f s16le pipe:1";

        var process = new Process();

        process.StartInfo.FileName = cmdPath;
        process.StartInfo.Arguments = args;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        return process;
    }

    private static string GetFfmpegExecutablePath()
    {
        if (!Environment.Is64BitProcess)
        {
            throw new ApplicationException("Somehow, it's not x64? Everything VintageHive is 64bit. What?");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"libs\ffmpeg.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return @"libs\ffmpeg.osx.intel";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return @"libs\ffmpeg.amd64";
        }

        throw new Exception("Cannot determine operating system!");
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

        public RadioStationInfo Station { get; }

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

        public CookEncoder Encoder { get; }

        public PnaLiveSession(HttpClient httpClient, HttpResponseMessage upstreamResponse,
            Process ffmpeg, byte[] rmHeaders, Stream ffmpegOutput, RadioStationInfo stationInfo,
            CookEncoder encoder)
        {
            _httpClient = httpClient;
            _upstreamResponse = upstreamResponse;
            _ffmpeg = ffmpeg;
            RmHeaders = rmHeaders;
            Station = stationInfo;
            Encoder = encoder;

            _producerTask = Task.Run(() => ProduceLoop(ffmpegOutput));
        }

        private async Task ProduceLoop(Stream ffmpegOut)
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
                        Log.WriteLine(Log.LEVEL_INFO, LogSys,
                            $"Producer: read#{readCount} {bytesRead}B (total PCM={totalPcmBytes}, pkts={packetCount})");
                    }

                    // Feed raw PCM to cook encoder — it returns RM data packets
                    var packets = Encoder.EncodePcm(readBuf.AsSpan(0, bytesRead));

                    foreach (var packet in packets)
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

                        packetCount++;

                        Log.WriteLine(Log.LEVEL_INFO, LogSys,
                            $"Producer: superpacket #{packetCount} len={packet.Length} (after {readCount} reads)");
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

        public void AddClient(string stationId)
        {
            var count = Interlocked.Increment(ref _activeClients);
            _cleanupCts?.Cancel();
            _cleanupCts = null;
            Log.WriteLine(Log.LEVEL_INFO, LogSys,
                $"Station {stationId}: client connected ({count} active)");
        }

        public void RemoveClient(string stationId)
        {
            var count = Interlocked.Decrement(ref _activeClients);
            Log.WriteLine(Log.LEVEL_INFO, LogSys,
                $"Station {stationId}: client disconnected ({count} active)");
            if (count <= 0)
            {
                Log.WriteLine(Log.LEVEL_INFO, LogSys,
                    $"Station {stationId}: last client left, cleanup in 30s");
                ScheduleCleanup(stationId);
            }
        }

        private void ScheduleCleanup(string stationId)
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
                        _liveSessions.TryRemove(stationId, out _);
                        Dispose();
                        Log.WriteLine(Log.LEVEL_INFO, LogSys,
                            $"Session for {stationId} cleaned up (no clients for 30s)");
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
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

    internal static async Task<PnaLiveSession> GetOrCreateSessionAsync(string stationId)
    {
        if (_liveSessions.TryGetValue(stationId, out var session) && session.IsAlive)
        {
            return session;
        }

        await _sessionCreateLock.WaitAsync();
        try
        {
            if (_liveSessions.TryGetValue(stationId, out session) && session.IsAlive)
            {
                return session;
            }

            var info = await RadioStationResolver.ResolveStation(stationId);
            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Session creating: {info.Name} ({info.Codec})");

            var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                PlaintextStreamFilter = (filterContext, ct) =>
                    new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
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

            var ffmpeg = CreateRaFfmpegProcess();
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

            // Create cook encoder and build RM headers ourselves
            var encoder = new CookEncoder(CookSampleRate, CookChannels, CookBitrate);
            var rmHeaders = encoder.BuildRmHeaders(info.Name);

            Log.WriteLine(Log.LEVEL_INFO, LogSys,
                $"Session ready: cook encoder, RM headers={rmHeaders.Length}B station=\"{info.Name}\"");

            var newSession = new PnaLiveSession(httpClient, upstreamResponse, ffmpeg, rmHeaders, ffmpegOut, info, encoder);

            if (_liveSessions.TryGetValue(stationId, out var old))
            {
                old.Dispose();
            }
            _liveSessions[stationId] = newSession;

            return newSession;
        }
        finally
        {
            _sessionCreateLock.Release();
        }
    }

    // ===================================================================
    // HTTP streaming — /stream/real/{id}.ra
    // Serves cook codec (.ra5 header) over HTTP for RealPlayer G2+
    // ===================================================================

    public static async Task HandleRealStream(HttpRequest request, HttpResponse response, string stationId)
    {
        var traceId = request.ListenerSocket.TraceId.ToString();
        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"HTTP: /stream/real/{stationId}", traceId);

        PnaLiveSession session = null;

        try
        {
            session = await GetOrCreateSessionAsync(stationId);
            session.AddClient(stationId);

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

            // Send full RM file headers (.RMF + PROP + CONT + MDPR + DATA)
            var rmFileHeaders = session.Encoder.BuildRmFileHeaders(session.Station.Name);
            await socket.WriteAsync(rmFileHeaders);
            await socket.FlushAsync();

            Log.WriteLine(Log.LEVEL_INFO, LogSys,
                $"HTTP: sent RM file headers ({rmFileHeaders.Length} bytes)", traceId);

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
                        Log.WriteLine(Log.LEVEL_INFO, LogSys,
                            $"HTTP: packet #{sent} sent, len={chunk.Length}, seq={seq}", traceId);
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
            if (session != null)
            {
                session.RemoveClient(stationId);
            }
        }
    }

}
