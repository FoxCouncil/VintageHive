// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using VintageHive.Network;
using VintageHive.Processors.LocalServer.Streaming;

namespace VintageHive.Proxy.Pna;

internal class PnaSession
{
    private const string LogSys = "PNA";

    private readonly ListenerSocket _connection;
    private readonly NetworkStream _stream;
    private readonly string _traceId;

    // Stream context
    private string _sessionKey;
    private RadioPnaStreaming.PnaLiveSession _liveSession;

    public PnaSession(ListenerSocket connection)
    {
        _connection = connection;
        _stream = connection.Stream;
        _traceId = connection.TraceId.ToString();
    }

    public async Task RunAsync()
    {
        try
        {
            Log.WriteLine(Log.LEVEL_INFO, LogSys, "New PNA connection", _traceId);

            // Step 1: Read client hello
            var buffer = new byte[4096];
            int read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length));

            if (read == 0)
            {
                Log.WriteLine(Log.LEVEL_INFO, LogSys, "Client sent 0 bytes — closing", _traceId);
                return;
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Rx client hello ({read} bytes)", _traceId);
            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"Client hello:\n{PnaCommand.HexDump(buffer, 0, read)}", _traceId);

            // Step 2: Parse client request
            var request = PnaCommand.ParseClientRequest(buffer, read);

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Client: \"{request.ClientString}\" Path: \"{request.PathRequest}\" BW: {request.Bandwidth} PNA v{request.PnaVersion:X2}", _traceId);

            // Step 3: Extract station ID from path
            string stationId = null;
            if (!string.IsNullOrEmpty(request.PathRequest))
            {
                stationId = ParseStationIdFromPath(request.PathRequest);
                Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Station ID: {stationId}", _traceId);
            }

            if (string.IsNullOrEmpty(stationId))
            {
                Log.WriteLine(Log.LEVEL_ERROR, LogSys, "No station ID in request — closing", _traceId);
                return;
            }

            // Step 3.5: Select codec profile based on client bandwidth
            var profile = RealCodecProfile.SelectForBandwidth(request.Bandwidth);
            _sessionKey = $"{stationId}:{profile.Key}";

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Selected profile: {profile} (bandwidth={request.Bandwidth} bytes/sec)", _traceId);

            // Step 4: Create live session (starts FFmpeg transcoding)
            try
            {
                _liveSession = await RadioPnaStreaming.GetOrCreateSessionAsync(stationId, profile);
                _liveSession.AddClient(_sessionKey);
                Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Live session ready: \"{_liveSession.Station.Name}\"", _traceId);
            }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_ERROR, LogSys, $"Failed to create live session: {ex.Message}", _traceId);
                return;
            }

            // Step 5: Send PNA_TAG server response (12 bytes, minimal)
            var pnaTag = PnaCommand.BuildPnaTagResponse(request.PnaHeaderFields);
            await _stream.WriteAsync(pnaTag);
            await _stream.FlushAsync();

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Sent PNA_TAG ({pnaTag.Length} bytes): {BitConverter.ToString(pnaTag)}", _traceId);

            // Step 6: Send raw RM container headers (PROP + CONT + MDPR + DATA)
            var rmHeaders = _liveSession.RmHeaders;
            if (rmHeaders != null && rmHeaders.Length > 0)
            {
                await _stream.WriteAsync(rmHeaders);
                await _stream.FlushAsync();

                Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Sent RM headers ({rmHeaders.Length} bytes)", _traceId);
            }

            // Step 7: Stream 0x5a-framed PNA audio packets (no challenge for now)
            await StreamingLoop();
        }
        catch (IOException)
        {
            Log.WriteLine(Log.LEVEL_INFO, LogSys, "Client disconnected (IO)", _traceId);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_ERROR, LogSys, $"Session error: {ex.Message}", _traceId);
        }
        finally
        {
            if (_liveSession != null && _sessionKey != null)
            {
                _liveSession.RemoveClient(_sessionKey);
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSys, "PNA session ended", _traceId);
        }
    }

    // ===================================================================
    // Streaming loop — send 0x5a-framed PNA audio packets
    // ===================================================================

    private async Task StreamingLoop()
    {
        long readPos = Math.Max(0, _liveSession.LivePosition - 5);
        uint sent = 0;
        ushort seq = 0;
        byte index2 = 0x10; // Second index, counts from 0x10 (per xine-lib comment)
        string lastKnownTrack = _liveSession.CurrentTrack;

        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Streaming start seq={readPos} live={_liveSession.LivePosition}", _traceId);

        while (_liveSession.IsAlive && _connection.IsConnected)
        {
            var (rmPacket, ringSeq) = _liveSession.TryRead(readPos);

            if (rmPacket != null)
            {
                var pnaPacket = PnaCommand.BuildPnaStreamPacket(rmPacket, seq, index2);

                if (pnaPacket != null)
                {
                    await _stream.WriteAsync(pnaPacket);
                    seq++;
                    index2++;
                }

                readPos = ringSeq + 1;
                sent++;

                if (sent == 1 || sent % 500 == 0)
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"Streaming: {sent} pkts sent, seq={seq}", _traceId);
                }

                // Flush periodically to keep latency low
                if (sent % 10 == 0)
                {
                    await _stream.FlushAsync();
                }
            }
            else
            {
                // Flush before waiting for new data
                await _stream.FlushAsync();

                // Wait for either new audio data or a track change
                var dataTask = _liveSession.WaitForDataAsync(readPos);
                var trackTask = _liveSession.TrackChangeTask;
                await Task.WhenAny(dataTask, trackTask);

                // Check for track change
                if (_liveSession.TryGetTrackUpdate(lastKnownTrack, out var newTrack))
                {
                    lastKnownTrack = newTrack;
                    Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Now playing: \"{newTrack}\"", _traceId);
                }
            }
        }

        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Streaming ended ({sent} pkts)", _traceId);
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private static string ParseStationIdFromPath(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');

        const string prefix = "stream/real/";
        int idx = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = path[(idx + prefix.Length)..];
            int dotIdx = rest.LastIndexOf('.');
            return dotIdx > 0 ? rest[..dotIdx] : rest;
        }

        // Fallback: try to extract filename without extension
        var lastSlash = path.LastIndexOf('/');
        var filename = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        var dot = filename.LastIndexOf('.');
        return dot > 0 ? filename[..dot] : filename;
    }
}
