// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;
using VintageHive.Processors.LocalServer.Streaming;

namespace VintageHive.Proxy.Mms;

internal class MmsSession
{
    private const string LogSys = "MMS";
    private const int PING_INTERVAL_MS = 30000;

    private readonly ListenerSocket _connection;
    private readonly NetworkStream _stream;
    private readonly string _traceId;

    private ushort _seq;
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();

    private enum SessionState { Init, Ready, Streaming }
    private SessionState _state = SessionState.Init;

    // Stream context
    private string _stationId;
    private RadioMmshStreaming.MmshLiveSession _liveSession;
    private uint _openFileId;
    private byte _incarnation;
    private uint _readBlockIncarnation;
    private uint _playIncarnation;
    private byte[] _mmsAsfHeader; // ASF header for MMS (includes Script Stream for Now Playing)
    private string _pendingScriptCommand; // Track title to send as script command after next data packet

    public MmsSession(ListenerSocket connection)
    {
        _connection = connection;
        _stream = connection.Stream;
        _traceId = connection.TraceId.ToString();
        _stopwatch.Start();
    }

    public async Task RunAsync()
    {
        using var cts = new CancellationTokenSource();

        try
        {
            Log.WriteLine(Log.LEVEL_INFO, LogSys, "New MMS connection", _traceId);

            while (_connection.IsConnected && !cts.Token.IsCancellationRequested)
            {
                if (_state == SessionState.Streaming)
                {
                    await StreamingLoop(cts.Token);
                }
                else
                {
                    await HandleCommandPhase(cts.Token);
                }
            }
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
            if (_liveSession != null && _stationId != null)
            {
                _liveSession.RemoveClient(_stationId);
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSys, "MMS session ended", _traceId);
        }
    }

    private async Task HandleCommandPhase(CancellationToken ct)
    {
        var (isCommand, data) = await MmsCommand.ReadMessageAsync(_stream, ct);

        if (data == null)
        {
            _connection.RawSocket.Close();
            return;
        }

        if (!isCommand)
        {
            Log.WriteLine(Log.LEVEL_VERBOSE, LogSys, "Ignoring data packet in command phase", _traceId);
            return;
        }

        var (mid, fields) = MmsCommand.ParseMmsMessage(data);

        Log.WriteLine(Log.LEVEL_VERBOSE, LogSys, $"Rx MID=0x{mid:X8} ({fields.Length}B fields)", _traceId);

        switch (mid)
        {
            case MmsCommand.MID_Connect:
            await HandleConnect(fields);
            break;

            case MmsCommand.MID_FunnelInfo:
            await HandleFunnelInfo(fields);
            break;

            case MmsCommand.MID_ConnectFunnel:
            await HandleConnectFunnel(fields);
            break;

            case MmsCommand.MID_OpenFile:
            await HandleOpenFile(fields);
            break;

            case MmsCommand.MID_ReadBlock:
            await HandleReadBlock(fields);
            break;

            case MmsCommand.MID_StreamSwitch:
            await HandleStreamSwitch(fields);
            break;

            case MmsCommand.MID_StartPlaying:
            await HandleStartPlaying(fields);
            break;

            case MmsCommand.MID_StopPlaying:
            await HandleStopPlaying(fields);
            break;

            case MmsCommand.MID_CloseFile:
            HandleCloseFile();
            break;

            case MmsCommand.MID_Pong:
            break;

            case MmsCommand.MID_Logging:
            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "Rx Logging", _traceId);
            break;

            case MmsCommand.MID_CancelReadBlock:
            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "Rx CancelReadBlock", _traceId);
            break;

            default:
            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"Rx unknown MID=0x{mid:X8}", _traceId);
            break;
        }
    }

    // ===================================================================
    // Command handlers
    // ===================================================================

    private async Task HandleConnect(byte[] fields)
    {
        var subscriberName = fields.Length > 12
            ? MmsCommand.ExtractUnicodeString(fields, 12)
            : "unknown";

        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Connect: {subscriberName}", _traceId);

        var responseFields = MmsCommand.BuildConnectedEXFields();
        await SendCommand(MmsCommand.MID_ConnectedEX, responseFields);
    }

    private async Task HandleFunnelInfo(byte[] fields)
    {
        Log.WriteLine(Log.LEVEL_VERBOSE, LogSys, "Rx FunnelInfo", _traceId);

        var clientId = (uint)(Math.Abs(_traceId.GetHashCode()) % 100000000);
        var responseFields = MmsCommand.BuildReportFunnelInfoFields(clientId);
        await SendCommand(MmsCommand.MID_ReportFunnelInfo, responseFields);
    }

    private async Task HandleConnectFunnel(byte[] fields)
    {
        Log.WriteLine(Log.LEVEL_VERBOSE, LogSys, "Rx ConnectFunnel", _traceId);

        var responseFields = MmsCommand.BuildConnectedFunnelFields();
        await SendCommand(MmsCommand.MID_ConnectedFunnel, responseFields);
    }

    private async Task HandleOpenFile(byte[] fields)
    {
        var openFilePlayIncarnation = fields.Length >= 4 ? BitConverter.ToUInt32(fields, 0) : 0u;
        var fileName = fields.Length > 16
            ? MmsCommand.ExtractUnicodeString(fields, 16)
            : "";

        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"OpenFile: {fileName}", _traceId);

        _stationId = ParseStationIdFromPath(fileName);
        if (string.IsNullOrEmpty(_stationId))
        {
            Log.WriteLine(Log.LEVEL_ERROR, LogSys, $"Cannot parse station ID from: {fileName}", _traceId);
            _connection.RawSocket.Close();
            return;
        }

        _liveSession = await RadioMmshStreaming.GetOrCreateSessionAsync(_stationId);
        _liveSession.AddClient(_stationId);

        _openFileId = 1;
        _incarnation = 1;

        // Get raw ASF header and patch File Properties for MMS live broadcast.
        // Keep Script Stream Properties — WMP9 uses script commands on stream 2 for Now Playing display.
        _mmsAsfHeader = PatchAsfHeaderForMms((byte[])_liveSession.RawAsfHeader.Clone());
        int headerSize = _mmsAsfHeader.Length;
        int packetSize = _liveSession.AsfPacketSize;
        int bitRate = _liveSession.Station?.Bitrate > 0 ? _liveSession.Station.Bitrate * 1000 : 128000;

        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"ASF header={headerSize}B pktSize={packetSize} bitRate={bitRate}", _traceId);

        var responseFields = MmsCommand.BuildReportOpenFileFields(openFilePlayIncarnation, _openFileId, headerSize, packetSize, bitRate);
        await SendCommand(MmsCommand.MID_ReportOpenFile, responseFields);
    }

    private async Task HandleReadBlock(byte[] fields)
    {
        _readBlockIncarnation = fields.Length >= 44 ? BitConverter.ToUInt32(fields, 40) : _incarnation;
        var playSequence = fields.Length >= 48 ? BitConverter.ToUInt32(fields, 44) : 0u;

        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"ReadBlock incarnation=0x{_readBlockIncarnation:X2} seq={playSequence}", _traceId);

        var responseFields = MmsCommand.BuildReportReadBlockFields(_readBlockIncarnation, playSequence);
        await SendCommand(MmsCommand.MID_ReportReadBlock, responseFields);

        await SendAsfHeader(_mmsAsfHeader, (byte)_readBlockIncarnation);

        _incarnation = (byte)_readBlockIncarnation;

        _state = SessionState.Ready;
        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "State → READY", _traceId);
    }

    private async Task HandleStreamSwitch(byte[] fields)
    {
        Log.WriteLine(Log.LEVEL_VERBOSE, LogSys, "Rx StreamSwitch", _traceId);

        var responseFields = MmsCommand.BuildReportStreamSwitchFields();
        await SendCommand(MmsCommand.MID_ReportStreamSwitch, responseFields);
    }

    private async Task HandleStartPlaying(byte[] fields)
    {
        _playIncarnation = fields.Length >= 32 ? BitConverter.ToUInt32(fields, 28) : 1u;

        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"StartPlaying incarnation=0x{_playIncarnation:X2}", _traceId);

        var responseFields = MmsCommand.BuildStartedPlayingFields(_playIncarnation, _openFileId);
        await SendCommand(MmsCommand.MID_StartedPlaying, responseFields);

        _incarnation = (byte)_playIncarnation;

        _state = SessionState.Streaming;
        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "State → STREAMING", _traceId);

        // Send initial script command with current track title for WMP9 Now Playing display
        if (_liveSession != null)
        {
            var currentTrack = _liveSession.CurrentTrack;
            if (!string.IsNullOrEmpty(currentTrack))
            {
                _pendingScriptCommand = currentTrack;
            }
        }
    }

    private async Task HandleStopPlaying(byte[] fields)
    {
        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "StopPlaying", _traceId);

        var stopPlayInc = fields.Length >= 8 ? BitConverter.ToUInt32(fields, 4) : _playIncarnation;
        var endFields = MmsCommand.BuildEndOfStreamFields(0, stopPlayInc);
        await SendCommand(MmsCommand.MID_EndOfStream, endFields);

        _state = SessionState.Ready;
        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "State → READY (stopped)", _traceId);
    }

    private void HandleCloseFile()
    {
        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "CloseFile", _traceId);

        if (_liveSession != null && _stationId != null)
        {
            _liveSession.RemoveClient(_stationId);
            _liveSession = null;
            _stationId = null;
        }

        _connection.RawSocket.Close();
    }

    // ===================================================================
    // Streaming loop
    // ===================================================================

    private async Task StreamingLoop(CancellationToken ct)
    {
        long readPos = Math.Max(0, _liveSession.LivePosition - 20);
        uint locationId = 0;
        byte afFlags = 0;
        uint sent = 0;
        uint lastSendTime = 0; // ASF presentation time from most recent audio packet
        var lastKnownTrack = _liveSession.CurrentTrack;
        var lastPingTime = _stopwatch.ElapsedMilliseconds;
        uint packetsSinceLastText = 0;
        const uint TextResendInterval = 50; // re-send TEXT every N audio packets
        byte scriptObjectNumber = 0; // media object number for TEXT stream — must increment per script command

        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"Streaming start seq={readPos} live={_liveSession.LivePosition}", _traceId);

        try
        {
            while (_state == SessionState.Streaming && _liveSession.IsAlive && !ct.IsCancellationRequested)
            {
                // Check for incoming client messages (non-blocking)
                if (_stream.DataAvailable)
                {
                    var (isCommand, data) = await MmsCommand.ReadMessageAsync(_stream, ct);
                    if (data == null) break;

                    if (isCommand)
                    {
                        var (mid, fields) = MmsCommand.ParseMmsMessage(data);

                        switch (mid)
                        {
                            case MmsCommand.MID_Pong:
                            break;
                            case MmsCommand.MID_Logging:
                            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "Rx Logging (streaming)", _traceId);
                            break;
                            case MmsCommand.MID_StopPlaying:
                            await HandleStopPlaying(fields);
                            return;
                            case MmsCommand.MID_CloseFile:
                            HandleCloseFile();
                            return;
                            case MmsCommand.MID_StartPlaying:
                            var newPlayInc = fields.Length >= 32 ? BitConverter.ToUInt32(fields, 28) : _playIncarnation;
                            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"Rx StartPlaying (streaming) incarnation=0x{newPlayInc:X2}", _traceId);
                            _playIncarnation = newPlayInc;
                            _incarnation = (byte)newPlayInc;
                            var spFields = MmsCommand.BuildStartedPlayingFields(_playIncarnation, _openFileId);
                            await SendCommand(MmsCommand.MID_StartedPlaying, spFields);
                            break;
                            case MmsCommand.MID_StreamSwitch:
                            await HandleStreamSwitch(fields);
                            break;
                            case MmsCommand.MID_ReadBlock:
                            await HandleReadBlock(fields);
                            break;
                            default:
                            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"Rx MID=0x{mid:X8} (streaming)", _traceId);
                            break;
                        }
                    }
                }

                // Send keep-alive ping
                var now = _stopwatch.ElapsedMilliseconds;
                if (now - lastPingTime >= PING_INTERVAL_MS)
                {
                    var pingFields = MmsCommand.BuildPingFields((uint)(now / 1000));
                    await SendCommand(MmsCommand.MID_Ping, pingFields);
                    lastPingTime = now;
                }

                // Read and send data packets
                var (chunk, seq) = _liveSession.TryRead(readPos);
                if (chunk != null)
                {
                    if (chunk.Length > 12)
                    {
                        var asfPacket = new byte[chunk.Length - 12];
                        Buffer.BlockCopy(chunk, 12, asfPacket, 0, asfPacket.Length);

                        // Track SendTime from audio data packets for script command timing
                        if (RadioMmshStreaming.TryFindAsfSendTimeAndDurationOffsets(
                                asfPacket, 0, asfPacket.Length,
                                out int stOffset, out _))
                        {
                            lastSendTime = BitConverter.ToUInt32(asfPacket, stOffset);
                        }

                        var dataPacket = MmsCommand.BuildDataPacket(locationId, _incarnation, afFlags, asfPacket);
                        await _stream.WriteAsync(dataPacket, ct);

                        locationId++;
                        afFlags = (byte)(afFlags + 1);
                        packetsSinceLastText++;

                        // Periodic TEXT re-send: keep Now Playing fresh even without track changes
                        if (_pendingScriptCommand == null && packetsSinceLastText >= TextResendInterval)
                        {
                            var currentTrack = _liveSession.CurrentTrack;
                            if (!string.IsNullOrEmpty(currentTrack))
                                _pendingScriptCommand = currentTrack;
                        }

                        // Inject TEXT script command after audio packet
                        if (_pendingScriptCommand != null && lastSendTime > 0)
                        {
                            var scriptTitle = $"{_pendingScriptCommand} [#{scriptObjectNumber} t={lastSendTime}]";
                            _pendingScriptCommand = null;
                            packetsSinceLastText = 0;

                            var scriptPacket = RadioMmshStreaming.BuildScriptCommandPacket(
                                scriptTitle, lastSendTime, mediaObjectNumber: scriptObjectNumber++);

                            var scriptDataPacket = MmsCommand.BuildDataPacket(locationId, _incarnation, afFlags, scriptPacket);
                            await _stream.WriteAsync(scriptDataPacket, ct);

                            locationId++;
                            afFlags = (byte)(afFlags + 1);

                            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"MMS: TEXT \"{scriptTitle}\" (sendTime={lastSendTime})", _traceId);
                        }
                    }

                    readPos = seq + 1;
                    sent++;

                    if (sent == 1 || sent % 500 == 0)
                    {
                        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"Streaming: {sent} pkts sent", _traceId);
                    }
                }
                else
                {
                    // Wait for new data or track change
                    var trackTask = _liveSession.TrackChangeTask;
                    var dataTask = _liveSession.WaitForDataAsync(readPos);
                    await Task.WhenAny(dataTask, trackTask);

                    if (trackTask.IsCompleted && _liveSession.TryGetTrackUpdate(lastKnownTrack, out var newTrack))
                    {
                        lastKnownTrack = newTrack;
                        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"MMS: track changed to \"{newTrack}\"", _traceId);

                        // TEXT only — StreamChange causes WMP9 to disconnect and FFmpeg to error.
                        // Same approach as MMSH: compact TEXT inline between audio packets.
                        _pendingScriptCommand = newTrack;
                    }
                }
            }
        }
        catch (IOException)
        {
            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"Client disconnected during streaming ({sent} pkts)", _traceId);
        }

        if (_state == SessionState.Streaming)
        {
            var endFields = MmsCommand.BuildEndOfStreamFields(0, _playIncarnation);
            try { await SendCommand(MmsCommand.MID_EndOfStream, endFields); } catch { }
            _state = SessionState.Ready;
        }
    }

    // ===================================================================
    // ASF header delivery as Data packets
    // ===================================================================

    private async Task SendAsfHeader(byte[] rawAsfHeader, byte incarnation)
    {
        int chunkSize = Math.Min(_liveSession?.AsfPacketSize ?? 4096, rawAsfHeader.Length);
        if (chunkSize <= 0) chunkSize = rawAsfHeader.Length;

        int totalChunks = (rawAsfHeader.Length + chunkSize - 1) / chunkSize;
        uint chunkIndex = 0;

        for (int offset = 0; offset < rawAsfHeader.Length; offset += chunkSize)
        {
            int remaining = rawAsfHeader.Length - offset;
            int thisChunkSize = Math.Min(chunkSize, remaining);

            var chunkData = new byte[thisChunkSize];
            Buffer.BlockCopy(rawAsfHeader, offset, chunkData, 0, thisChunkSize);

            // AFFlags: 0x04=first, 0x08=last, 0x0C=single (first+last), 0x00=middle
            byte headerAfFlags;
            if (totalChunks == 1)
                headerAfFlags = 0x0C;
            else if (offset == 0)
                headerAfFlags = 0x04;
            else if (offset + thisChunkSize >= rawAsfHeader.Length)
                headerAfFlags = 0x08;
            else
                headerAfFlags = 0x00;

            var dataPacket = MmsCommand.BuildDataPacket(chunkIndex, incarnation, headerAfFlags, chunkData);
            await _stream.WriteAsync(dataPacket);

            chunkIndex++;
        }

        Log.WriteLine(Log.LEVEL_VERBOSE, LogSys, $"Sent ASF header: {rawAsfHeader.Length}B in {totalChunks} chunks", _traceId);
    }

    // ===================================================================
    // Send helper
    // ===================================================================

    private async Task SendCommand(uint mid, byte[] commandFields)
    {
        double timeSent = _stopwatch.Elapsed.TotalSeconds;
        var packet = MmsCommand.BuildTcpMessage(mid, _seq++, timeSent, commandFields);
        await _stream.WriteAsync(packet);
        await _stream.FlushAsync();

        Log.WriteLine(Log.LEVEL_VERBOSE, LogSys, $"Tx MID=0x{mid:X8} seq={_seq - 1} ({packet.Length}B)", _traceId);
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    /// <summary>
    /// Patch ASF File Properties Object for MMS live broadcast.
    /// </summary>
    private static byte[] PatchAsfHeaderForMms(byte[] rawAsfHeader)
    {
        ReadOnlySpan<byte> filePropsGuid = new byte[] {
            0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11,
            0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
        };

        if (rawAsfHeader.Length < 30) return rawAsfHeader;

        long headerObjSize = BitConverter.ToInt64(rawAsfHeader, 16);

        for (int i = 30; i + 104 <= rawAsfHeader.Length && i + 104 <= (int)headerObjSize; i++)
        {
            if (!rawAsfHeader.AsSpan(i, 16).SequenceEqual(filePropsGuid))
                continue;

            BitConverter.GetBytes((long)rawAsfHeader.Length).CopyTo(rawAsfHeader, i + 40);
            BitConverter.GetBytes((long)0xFFFFFFFF).CopyTo(rawAsfHeader, i + 56);
            BitConverter.GetBytes((long)0).CopyTo(rawAsfHeader, i + 64);
            BitConverter.GetBytes((long)0).CopyTo(rawAsfHeader, i + 72);
            BitConverter.GetBytes((long)1000).CopyTo(rawAsfHeader, i + 80);
            BitConverter.GetBytes((uint)0x09).CopyTo(rawAsfHeader, i + 88);
            break;
        }

        int dataObjStart = (int)headerObjSize;
        if (dataObjStart + 50 <= rawAsfHeader.Length)
        {
            BitConverter.GetBytes((long)50).CopyTo(rawAsfHeader, dataObjStart + 16);
            BitConverter.GetBytes((long)0).CopyTo(rawAsfHeader, dataObjStart + 40);
        }

        return rawAsfHeader;
    }

    private static string ParseStationIdFromPath(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');

        const string prefix = "stream/wmp/";
        int idx = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = path[(idx + prefix.Length)..];
            int dotIdx = rest.LastIndexOf('.');
            return dotIdx > 0 ? rest[..dotIdx] : rest;
        }

        var lastSlash = path.LastIndexOf('/');
        var filename = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        var dot = filename.LastIndexOf('.');
        return dot > 0 ? filename[..dot] : filename;
    }
}
