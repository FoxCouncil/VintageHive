// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors.LocalServer.Streaming;

internal static class RadioMmshStreaming
{
    // ===================================================================
    // ASF GUIDs
    // ===================================================================

    private static readonly byte[] AsfFilePropertiesGuid =
    {
        0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11,
        0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
    };

    private static readonly byte[] AsfContentDescriptionGuid =
    {
        0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
        0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
    };

    // ===================================================================
    // ASF utilities
    // ===================================================================

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead));
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    private static int GetAsfPacketSize(byte[] headerData)
    {
        for (int i = 0; i <= headerData.Length - 100; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerData[i + j] != AsfFilePropertiesGuid[j]) { match = false; break; }
            }
            if (match)
            {
                // Min Data Packet Size is at offset 92 from start of File Properties Object
                return BitConverter.ToInt32(headerData, i + 92);
            }
        }
        throw new InvalidOperationException("ASF File Properties Object not found in header");
    }

    /// <summary>
    /// Build an MMSH framed packet per MS-WMSP sections 2.2.3.1.1 + 2.2.3.1.2.
    /// Layout: [Framing Header 4 bytes] [MMS Data Packet: 8 byte header + payload]
    /// </summary>
    private static byte[] BuildMmshChunk(byte chunkType, uint locationId, byte incarnation, byte afFlags, byte[] payload)
    {
        int mmsPacketSize = payload.Length + 8; // MMS header (8) + payload
        var chunk = new byte[4 + mmsPacketSize];

        // Framing Header (4 bytes)
        chunk[0] = 0x24;       // Frame = 0x24, B-bit = 0
        chunk[1] = chunkType;  // 'H'=0x48, 'D'=0x44, 'M'=0x4D
        BitConverter.GetBytes((ushort)mmsPacketSize).CopyTo(chunk, 2); // PacketLength

        // MMS Data Packet header (8 bytes)
        BitConverter.GetBytes(locationId).CopyTo(chunk, 4);            // LocationId
        chunk[8] = incarnation;                                         // Incarnation
        chunk[9] = afFlags;                                             // AFFlags
        BitConverter.GetBytes((ushort)mmsPacketSize).CopyTo(chunk, 10); // PacketSize = total MMS packet size

        // Payload
        Buffer.BlockCopy(payload, 0, chunk, 12, payload.Length);
        return chunk;
    }

    /// <summary>
    /// Build an MMSH $M (Metadata) chunk with playlist/broadcast info.
    /// Matches Cougar/9.00.00.3372 format exactly:
    ///   header_line \0 name_len,name,type,value_len,value,... \r\n
    /// Entries are comma-separated with character-count prefixes on names.
    /// </summary>
    private static byte[] BuildMmshMetadataChunk(int playlistGenId)
    {
        var branding = $"VintageHive/{Mind.ApplicationVersion}";

        var header = $"playlist-gen-id={playlistGenId}, broadcast-id=1, features=\"broadcast\"";

        // Each entry: name_len,name,type,value_len,value
        // type 31 = string, type 3 = DWORD
        var entries = new (string name, int type, string value)[]
        {
            ("language", 31, ""),
            ("WMS_CONTENT_DESCRIPTION_SERVER_BRANDING_INFO", 31, branding),
            ("WMS_CONTENT_DESCRIPTION_PLAYLIST_ENTRY_START_OFFSET", 3, "5000"),
            ("WMS_CONTENT_DESCRIPTION_PLAYLIST_ENTRY_DURATION", 3, "0"),
            ("WMS_CONTENT_DESCRIPTION_COPIED_METADATA_FROM_PLAYLIST_FILE", 3, "1"),
            ("WMS_CONTENT_DESCRIPTION_PLAYLIST_ENTRY_URL", 31, "Push:*")
        };

        var formatted = entries.Select(e => $"{e.name.Length},{e.name},{e.type},{e.value.Length},{e.value}");
        var metadataText = header + "\0" + string.Join(",", formatted) + "\r\n";
        var payload = Encoding.ASCII.GetBytes(metadataText);

        return BuildMmshChunk(0x4D, 0, 0, 0x0C, payload);
    }

    /// <summary>
    /// Build an MMSH $C (Stream Change Notification) chunk.
    /// Real WMS sends this after $H to signal the stream is ready.
    /// </summary>
    private static byte[] BuildMmshStreamChangeChunk()
    {
        // $C is minimal: framing header (4 bytes) + LocationId (4 bytes)
        var chunk = new byte[8];
        chunk[0] = 0x24;       // Frame
        chunk[1] = 0x43;       // 'C'
        BitConverter.GetBytes((ushort)4).CopyTo(chunk, 2); // PacketLength = 4
        // LocationId = 0 (bytes 4-7 already zero from array init)
        return chunk;
    }

    /// <summary>
    /// Write data as an HTTP chunked transfer encoding chunk.
    /// Format: {hex-length}\r\n{data}\r\n
    /// </summary>
    private static async Task WriteHttpChunkAsync(Stream socket, byte[] data)
    {
        var header = Encoding.ASCII.GetBytes($"{data.Length:X}\r\n");
        await socket.WriteAsync(header);
        await socket.WriteAsync(data);
        await socket.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
        await socket.FlushAsync();
    }

    /// <summary>
    /// Write the terminal HTTP chunk (0\r\n\r\n) to signal end of chunked transfer.
    /// </summary>
    private static async Task WriteHttpChunkEndAsync(Stream socket)
    {
        await socket.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"));
        await socket.FlushAsync();
    }

    /// <summary>
    /// Patch ASF File Properties Object for live streaming compatibility with WMP 6.4.
    /// Sets broadcast flag, clears seekable, and fills in sensible non-zero values
    /// for fields that WMP 6.4 needs to allocate decoder buffers.
    /// </summary>
    private static void PatchAsfHeaderForBroadcast(byte[] headerData, int packetSize)
    {
        // Find File Properties Object by its GUID
        for (int i = 0; i <= headerData.Length - 104; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerData[i + j] != AsfFilePropertiesGuid[j]) { match = false; break; }
            }
            if (!match) continue;

            // File Size: 2GB (large dummy value so WMP allocates buffers)
            BitConverter.GetBytes((long)0x7FFFFFFF).CopyTo(headerData, i + 40);

            // Data Packets Count: large number
            BitConverter.GetBytes((long)0xFFFF).CopyTo(headerData, i + 56);

            // Play Duration: ~24 hours in 100-nanosecond units
            BitConverter.GetBytes(24L * 3600 * 10_000_000).CopyTo(headerData, i + 64);

            // Send Duration: same
            BitConverter.GetBytes(24L * 3600 * 10_000_000).CopyTo(headerData, i + 72);

            // Preroll: 3000ms
            BitConverter.GetBytes((uint)3000).CopyTo(headerData, i + 80);

            // Flags: broadcast=1, seekable=0
            BitConverter.GetBytes((uint)0x01).CopyTo(headerData, i + 88);

            // Ensure packet sizes are set
            if (BitConverter.ToInt32(headerData, i + 92) == 0)
                BitConverter.GetBytes(packetSize).CopyTo(headerData, i + 92);
            if (BitConverter.ToInt32(headerData, i + 96) == 0)
                BitConverter.GetBytes(packetSize).CopyTo(headerData, i + 96);

            // Max Bitrate: ensure non-zero
            if (BitConverter.ToInt32(headerData, i + 100) == 0)
                BitConverter.GetBytes(128000).CopyTo(headerData, i + 100);

            Console.Error.WriteLine($"[MMSH] Patched ASF File Properties: fileSize=2GB, packets=65535, preroll=3000ms, broadcast=1");
            Console.Error.Flush();
            return;
        }
    }

    /// <summary>
    /// Create a WMP 9-specific $H chunk by cloning and patching the shared one.
    /// Values match real Cougar/9.00.00.3372 capture:
    ///   File Size = header size (non-zero), Data Packets = 0xFFFFFFFF (unlimited),
    ///   Preroll = 5000ms, Flags = 0x09 (broadcast + bit 3).
    /// </summary>
    private static byte[] PatchHChunkForWmp9(byte[] hChunk)
    {
        var patched = (byte[])hChunk.Clone();

        for (int i = 12; i <= patched.Length - 104; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (patched[i + j] != AsfFilePropertiesGuid[j]) { match = false; break; }
            }
            if (!match) continue;

            // File Size: ASF payload size (exclude 12-byte MMSH framing + MMS header)
            // Real WMS uses the actual ASF content size, not the MMSH chunk size.
            BitConverter.GetBytes((long)(hChunk.Length - 12)).CopyTo(patched, i + 40);

            // Data Packets Count: 0xFFFFFFFF = unlimited/unknown (NOT zero!)
            // This is the critical value — zero tells WMP9 there's nothing to play.
            BitConverter.GetBytes((long)0xFFFFFFFF).CopyTo(patched, i + 56);

            // Play Duration: 0 (live broadcast)
            BitConverter.GetBytes((long)0).CopyTo(patched, i + 64);

            // Send Duration: 0 (live broadcast)
            BitConverter.GetBytes((long)0).CopyTo(patched, i + 72);

            // Preroll: 5000ms (matches real WMS, was 3000ms)
            BitConverter.GetBytes((long)5000).CopyTo(patched, i + 80);

            // Flags: 0x09 (broadcast=1 + bit 3, matching real WMS capture)
            BitConverter.GetBytes((uint)0x09).CopyTo(patched, i + 88);

            Console.Error.WriteLine($"[MMSH] WMP9: patched $H File Properties (fileSize={hChunk.Length}, packets=0xFFFFFFFF, preroll=5000, flags=0x09)");
            Console.Error.Flush();

            // Data Object: match real Cougar capture (Size=50, TotalDataPackets=0)
            long asfHeaderSize = BitConverter.ToInt64(patched, 12 + 16);
            int dataObjStart = 12 + (int)asfHeaderSize;
            if (dataObjStart + 50 <= patched.Length)
            {
                BitConverter.GetBytes((long)50).CopyTo(patched, dataObjStart + 16);   // Data Object Size = 50 bytes (header only)
                BitConverter.GetBytes((long)0).CopyTo(patched, dataObjStart + 40);    // TotalDataPackets = 0 (live broadcast)
                Console.Error.WriteLine("[MMSH] WMP9: Data Object Size=50, TotalDataPackets=0");
                Console.Error.Flush();
            }

            return patched;
        }

        return patched;
    }

    /// <summary>
    /// Build an ASF Content Description Object with the given metadata strings.
    /// </summary>
    private static byte[] BuildAsfContentDescription(
        string title, string author, string copyright, string description, string rating)
    {
        static byte[] EncodeField(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            var bytes = Encoding.Unicode.GetBytes(s);
            var result = new byte[bytes.Length + 2];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }

        var titleBytes = EncodeField(title);
        var authorBytes = EncodeField(author);
        var copyrightBytes = EncodeField(copyright);
        var descBytes = EncodeField(description);
        var ratingBytes = EncodeField(rating);

        long objectSize = 16 + 8 + 10 + titleBytes.Length + authorBytes.Length +
                          copyrightBytes.Length + descBytes.Length + ratingBytes.Length;

        var obj = new byte[objectSize];
        int pos = 0;

        Buffer.BlockCopy(AsfContentDescriptionGuid, 0, obj, pos, 16); pos += 16;
        BitConverter.GetBytes(objectSize).CopyTo(obj, pos); pos += 8;

        BitConverter.GetBytes((ushort)titleBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)authorBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)copyrightBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)descBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)ratingBytes.Length).CopyTo(obj, pos); pos += 2;

        Buffer.BlockCopy(titleBytes, 0, obj, pos, titleBytes.Length); pos += titleBytes.Length;
        Buffer.BlockCopy(authorBytes, 0, obj, pos, authorBytes.Length); pos += authorBytes.Length;
        Buffer.BlockCopy(copyrightBytes, 0, obj, pos, copyrightBytes.Length); pos += copyrightBytes.Length;
        Buffer.BlockCopy(descBytes, 0, obj, pos, descBytes.Length); pos += descBytes.Length;
        Buffer.BlockCopy(ratingBytes, 0, obj, pos, ratingBytes.Length);

        return obj;
    }

    /// <summary>
    /// Rebuild the $H chunk with an updated Content Description title.
    /// </summary>
    private static byte[] RebuildHChunkWithTitle(MmshLiveSession session, string newTitle)
    {
        var hChunk = session.HChunk;

        long asfHeaderSize = BitConverter.ToInt64(hChunk, 12 + 16);
        var headerObj = new byte[asfHeaderSize];
        Buffer.BlockCopy(hChunk, 12, headerObj, 0, (int)asfHeaderSize);

        int cdOffset = -1;
        for (int i = 0; i <= headerObj.Length - 24; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerObj[i + j] != AsfContentDescriptionGuid[j]) { match = false; break; }
            }
            if (match) { cdOffset = i; break; }
        }

        if (cdOffset < 0) return hChunk;

        long oldCdSize = BitConverter.ToInt64(headerObj, cdOffset + 16);

        var newCd = BuildAsfContentDescription(
            title: newTitle,
            author: $"VintageHive/{Mind.ApplicationVersion}",
            copyright: session.Station?.Country ?? "",
            description: session.Station?.Tags ?? "",
            rating: "");

        int beforeCd = cdOffset;
        int afterCdStart = cdOffset + (int)oldCdSize;
        int afterCdLen = (int)asfHeaderSize - afterCdStart;

        long newAsfHeaderSize = beforeCd + newCd.Length + afterCdLen;
        var newHeaderObj = new byte[newAsfHeaderSize];
        Buffer.BlockCopy(headerObj, 0, newHeaderObj, 0, beforeCd);
        Buffer.BlockCopy(newCd, 0, newHeaderObj, beforeCd, newCd.Length);
        Buffer.BlockCopy(headerObj, afterCdStart, newHeaderObj, beforeCd + newCd.Length, afterCdLen);

        BitConverter.GetBytes(newAsfHeaderSize).CopyTo(newHeaderObj, 16);

        var dataObjHeader = new byte[50];
        Buffer.BlockCopy(hChunk, 12 + (int)asfHeaderSize, dataObjHeader, 0, 50);

        var hPayload = new byte[newAsfHeaderSize + 50];
        Buffer.BlockCopy(newHeaderObj, 0, hPayload, 0, (int)newAsfHeaderSize);
        Buffer.BlockCopy(dataObjHeader, 0, hPayload, (int)newAsfHeaderSize, 50);

        return BuildMmshChunk(0x48, 0, 0, 0x0C, hPayload);
    }

    /// <summary>
    /// Safely locate the Send Time (4 bytes) and Duration (2 bytes) fields within
    /// a raw ASF data packet starting at <paramref name="offset"/> in the buffer.
    /// </summary>
    private static bool TryFindAsfSendTimeAndDurationOffsets(
        byte[] buffer, int offset, int length,
        out int sendTimeOffset,
        out int durationOffset)
    {
        sendTimeOffset = -1;
        durationOffset = -1;

        int pos = 0;
        if (length < 4) return false;

        byte ecFlags = buffer[offset + pos++];
        if ((ecFlags & 0x80) != 0)
        {
            int ecLen = ecFlags & 0x0F;
            if (length < pos + ecLen) return false;
            pos += ecLen;
        }

        if (length < pos + 2) return false;

        byte ltf = buffer[offset + pos++];
        pos++; // Property Flags

        static int TypeToSize(int t) => t switch { 1 => 1, 2 => 2, 3 => 4, _ => 0 };

        pos += TypeToSize((ltf >> 1) & 0x03); // Packet Length
        pos += TypeToSize((ltf >> 3) & 0x03); // Padding Length
        pos += TypeToSize((ltf >> 5) & 0x03); // Sequence

        if (length < pos + 6) return false;

        sendTimeOffset = pos;
        durationOffset = pos + 4;
        return true;
    }

    // ===================================================================
    // FFmpeg process creation
    // ===================================================================

    private static Process CreateWmaFfmpegProcess()
    {
        var process = new Process();
        process.StartInfo.FileName = GetFfmpegExecutablePath();
        // No -re flag: let ffmpeg run as fast as possible to fill the ring buffer.
        // Consumer pacing is handled by WaitForDataAsync when caught up to live position.
        process.StartInfo.Arguments = "-probesize 32768 -analyzeduration 0 -i pipe:0 -fflags nobuffer -flush_packets 1 -map_metadata -1 -c:a adpcm_ms -ar 22050 -ac 1 -block_size 1024 -f asf pipe:1";
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
    // Shared MMSH live sessions
    // ===================================================================

    private static readonly ConcurrentDictionary<string, MmshLiveSession> _liveSessions = new();
    private static readonly ConcurrentDictionary<string, long> _clientLastSeq = new();
    private static readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    private class MmshLiveSession : IDisposable
    {
        public byte[] HChunk { get; }
        public int AsfPacketSize { get; }
        public RadioStationInfo Station { get; }
        private IcyMetadataStrippingStream _icyStream;

        public string CurrentTrack =>
            _icyStream?.CurrentTrack ?? Station?.CurrentTrack;

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

        public bool IsAlive => !_producerTask.IsCompleted && !_disposed;

        public MmshLiveSession(HttpClient httpClient, HttpResponseMessage upstreamResponse,
            Process ffmpeg, byte[] hChunk, int packetSize, Stream ffmpegOutput,
            RadioStationInfo stationInfo, IcyMetadataStrippingStream icyStream = null)
        {
            _httpClient = httpClient;
            _upstreamResponse = upstreamResponse;
            _ffmpeg = ffmpeg;
            HChunk = hChunk;
            AsfPacketSize = packetSize;
            Station = stationInfo;
            _icyStream = icyStream;
            _producerTask = Task.Run(() => ProduceLoop(ffmpegOutput, packetSize));
        }

        private async Task ProduceLoop(Stream ffmpegOut, int packetSize)
        {
            uint locationId = 0;
            byte afFlags = 0;
            var buf = new byte[packetSize];
            var lastPacketTime = Stopwatch.GetTimestamp();

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    int read = await ReadExactAsync(ffmpegOut, buf, 0, packetSize);
                    if (read == 0) break;
                    if (read < packetSize) Array.Clear(buf, read, packetSize - read);

                    var now = Stopwatch.GetTimestamp();
                    var gapMs = (now - lastPacketTime) * 1000.0 / Stopwatch.Frequency;
                    lastPacketTime = now;

                    // Incarnation=1 for $D packets (matches real Windows Media Server behavior)
                    var dChunk = BuildMmshChunk(0x44, locationId, 1, afFlags, buf);

                    TaskCompletionSource oldTcs;
                    lock (_lock)
                    {
                        _ring[_headSeq % RingCapacity] = dChunk;
                        _headSeq++;
                        oldTcs = _newDataTcs;
                        _newDataTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    oldTcs.TrySetResult();

                    locationId++;
                    afFlags = (byte)(afFlags + 1);

                    if (locationId <= 100 || gapMs > 500)
                    {
                        Console.Error.WriteLine($"[MMSH-Prod] pkt={locationId} gap={gapMs:F0}ms");
                        Console.Error.Flush();
                    }
                    else if (locationId % 500 == 0)
                    {
                        Console.Error.WriteLine($"[MMSH-Prod] {locationId} packets");
                        Console.Error.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MMSH-Prod] Error: {ex.Message}");
            }
            finally
            {
                lock (_lock) { _newDataTcs.TrySetResult(); }
            }

            Console.Error.WriteLine($"[MMSH-Prod] Ended ({_headSeq} total)");
            Console.Error.Flush();
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

    private static async Task<MmshLiveSession> GetOrCreateSessionAsync(string stationId)
    {
        if (_liveSessions.TryGetValue(stationId, out var session) && session.IsAlive)
            return session;

        await _sessionCreateLock.WaitAsync();
        try
        {
            if (_liveSessions.TryGetValue(stationId, out session) && session.IsAlive)
                return session;

            var info = await RadioStationResolver.ResolveStation(stationId);
            Console.Error.WriteLine($"[MMSH-Session] Creating: {info.Name} ({info.Codec})");
            Console.Error.Flush();

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
                upstreamResponse.Content.Headers.TryGetValues("icy-metaint", out metaintValues);

            if (metaintValues != null)
            {
                var metaintStr = metaintValues.FirstOrDefault();
                if (int.TryParse(metaintStr, out int metaInterval) && metaInterval > 0)
                {
                    icyStream = new IcyMetadataStrippingStream(rawUpstreamStream, metaInterval);
                    upstreamStream = icyStream;
                    Console.Error.WriteLine($"[MMSH-Session] ICY metadata: interval={metaInterval}");
                    Console.Error.Flush();
                }
            }

            var ffmpeg = CreateWmaFfmpegProcess();
            ffmpeg.Start();
            _ = Task.Run(async () =>
            {
                using var reader = ffmpeg.StandardError;
                while (await reader.ReadLineAsync() is { } line)
                {
                    Console.Error.WriteLine($"[FFMPEG] {line}");
                    Console.Error.Flush();
                }
            });
            _ = Task.Run(async () =>
            {
                try { await upstreamStream.CopyToAsync(ffmpeg.StandardInput.BaseStream); }
                catch { }
                try { ffmpeg.StandardInput.Close(); } catch { }
            });

            var ffmpegOut = ffmpeg.StandardOutput.BaseStream;

            var headerPrefix = new byte[24];
            if (await ReadExactAsync(ffmpegOut, headerPrefix, 0, 24) < 24)
                throw new InvalidOperationException("Failed to read ASF Header Object prefix");

            long asfHeaderSize = BitConverter.ToInt64(headerPrefix, 16);
            var headerObj = new byte[asfHeaderSize];
            Buffer.BlockCopy(headerPrefix, 0, headerObj, 0, 24);
            if (await ReadExactAsync(ffmpegOut, headerObj, 24, (int)asfHeaderSize - 24) < (int)asfHeaderSize - 24)
                throw new InvalidOperationException("Incomplete ASF Header Object");

            var dataObjHeader = new byte[50];
            if (await ReadExactAsync(ffmpegOut, dataObjHeader, 0, 50) < 50)
                throw new InvalidOperationException("Failed to read ASF Data Object header");

            BitConverter.GetBytes((long)0).CopyTo(dataObjHeader, 16);
            if (BitConverter.ToInt64(dataObjHeader, 40) == 0)
                BitConverter.GetBytes((long)0xFFFF).CopyTo(dataObjHeader, 40);
            dataObjHeader[48] = 0x01; dataObjHeader[49] = 0x01;

            int packetSize = GetAsfPacketSize(headerObj);
            PatchAsfHeaderForBroadcast(headerObj, packetSize);

            var contentDesc = BuildAsfContentDescription(
                title: info.CurrentTrack ?? info.Name,
                author: $"VintageHive/{Mind.ApplicationVersion}",
                copyright: info.Country ?? "",
                description: info.Tags ?? "",
                rating: "");

            var finalHeaderSize = asfHeaderSize + contentDesc.Length;
            var finalHeaderObj = new byte[finalHeaderSize];
            Buffer.BlockCopy(headerObj, 0, finalHeaderObj, 0, (int)asfHeaderSize);
            Buffer.BlockCopy(contentDesc, 0, finalHeaderObj, (int)asfHeaderSize, contentDesc.Length);

            BitConverter.GetBytes(finalHeaderSize).CopyTo(finalHeaderObj, 16);

            uint numObjects = BitConverter.ToUInt32(finalHeaderObj, 24);
            BitConverter.GetBytes(numObjects + 1).CopyTo(finalHeaderObj, 24);

            Console.Error.WriteLine($"[MMSH-Session] ASF header: {finalHeaderSize}B (with Content Description +{contentDesc.Length}B)");
            Console.Error.Flush();

            var hPayload = new byte[finalHeaderSize + 50];
            Buffer.BlockCopy(finalHeaderObj, 0, hPayload, 0, (int)finalHeaderSize);
            Buffer.BlockCopy(dataObjHeader, 0, hPayload, (int)finalHeaderSize, 50);
            var hChunk = BuildMmshChunk(0x48, 0, 0, 0x0C, hPayload);

            Console.Error.WriteLine($"[MMSH-Session] ASF: header={finalHeaderSize}B packetSize={packetSize} $H={hChunk.Length}B");
            Console.Error.Flush();

            var newSession = new MmshLiveSession(httpClient, upstreamResponse, ffmpeg, hChunk, packetSize, ffmpegOut, info, icyStream);

            if (_liveSessions.TryGetValue(stationId, out var old))
                old.Dispose();
            _liveSessions[stationId] = newSession;

            return newSession;
        }
        finally
        {
            _sessionCreateLock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // WMP 6.4 (NSPlayer/4.x) — MMSH handler
    // ═══════════════════════════════════════════════════════════════════

    public static async Task HandleWmp6Stream(HttpRequest request, HttpResponse response, string stationId)
    {
        Console.Error.WriteLine($"[MMSH-WMP6] === {request.Method} /stream/wmp/{stationId}.asf ===");
        foreach (var h in request.Headers)
            Console.Error.WriteLine($"[MMSH-WMP6]   {h.Key}: {h.Value}");
        Console.Error.Flush();

        if (request.Method == "POST")
        {
            Console.Error.WriteLine("[MMSH-WMP6] POST log-line acknowledged");
            Console.Error.Flush();
            response.SetBodyString("", "text/plain");
            return;
        }

        try
        {
            var pragmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (request.Headers.TryGetValue("Pragma", out var pragmaRaw))
            {
                foreach (var item in pragmaRaw.Split(','))
                {
                    var trimmed = item.Trim();
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                        pragmas[trimmed[..eqIdx].Trim()] = trimmed[(eqIdx + 1)..].Trim();
                    else if (!string.IsNullOrEmpty(trimmed))
                        pragmas[trimmed] = "";
                }
            }

            bool isPlay = pragmas.ContainsKey("xPlayStrm") && pragmas["xPlayStrm"] == "1";
            var clientId = (uint)(Math.Abs(stationId.GetHashCode()) % 100000000);
            pragmas.TryGetValue("xClientGUID", out var clientGuid);

            int requestContext = 0;
            if (pragmas.TryGetValue("request-context", out var ctxStr))
                int.TryParse(ctxStr, out requestContext);

            Console.Error.WriteLine($"[MMSH-WMP6] Mode={(isPlay ? "PLAY" : "DESCRIBE")} ctx={requestContext}");
            Console.Error.Flush();

            var session = await GetOrCreateSessionAsync(stationId);
            var socket = request.ListenerSocket.Stream;
            var httpVer = request.Version;

            string BuildWmp6Response(int? contentLength = null) =>
                $"{httpVer} 200 OK\r\n" +
                "Server: Cougar/4.1\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                (contentLength.HasValue ? $"Content-Length: {contentLength.Value}\r\n" : "") +
                "Pragma: no-cache\r\n" +
                $"Pragma: client-id={clientId}\r\n" +
                "Pragma: features=\"broadcast\"\r\n" +
                "Pragma: timeout=60000\r\n" +
                "Cache-Control: no-cache\r\n" +
                "\r\n";

            if (!isPlay)
            {
                var resp = BuildWmp6Response(contentLength: session.HChunk.Length);

                Console.Error.WriteLine($"[MMSH-WMP6] DESCRIBE: $H={session.HChunk.Length}B");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.WriteAsync(session.HChunk);
                response.Handled = true;
            }
            else
            {
                request.ListenerSocket.IsKeepAlive = false;

                var resp = BuildWmp6Response();

                Console.Error.WriteLine($"[MMSH-WMP6] PLAY ctx={requestContext}");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));

                byte[] hChunk;
                if (requestContext > 2)
                {
                    var currentTrack = session.CurrentTrack ?? session.Station?.Name ?? "Unknown";
                    hChunk = RebuildHChunkWithTitle(session, currentTrack);
                    Console.Error.WriteLine($"[MMSH-WMP6] Reconnect: rebuilt $H with title=\"{currentTrack}\"");
                    Console.Error.Flush();
                }
                else
                {
                    hChunk = session.HChunk;
                }

                await socket.WriteAsync(hChunk);

                long readPos = Math.Max(0, session.LivePosition - 5);
                uint sent = 0;

                Console.Error.WriteLine($"[MMSH-WMP6] Streaming from seq={readPos} live={session.LivePosition}");
                Console.Error.Flush();

                try
                {
                    while (session.IsAlive)
                    {
                        var (chunk, seq) = session.TryRead(readPos);
                        if (chunk != null)
                        {
                            await socket.WriteAsync(chunk);
                            readPos = seq + 1;
                            sent++;

                            if (sent % 500 == 0)
                            {
                                Console.Error.WriteLine($"[MMSH-WMP6] Sent {sent} (seq={seq})");
                                Console.Error.Flush();
                            }
                        }
                        else
                        {
                            await session.WaitForDataAsync(readPos);
                        }
                    }
                }
                catch (IOException)
                {
                    Console.Error.WriteLine($"[MMSH-WMP6] Client disconnected after {sent} packets");
                }
                catch (OperationCanceledException) { }

                Console.Error.Flush();
                response.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MMSH-WMP6] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Console.Error.Flush();
            response.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // WMP 9+ (NSPlayer/9.x+) — MMSH handler
    // Replicates Cougar/9.00.00.3372 (Windows Media Services 9.0)
    // Protocol flow captured via Wireshark from real WMS server.
    // ═══════════════════════════════════════════════════════════════════

    private static Dictionary<string, string> ParseMmshPragmas(HttpRequest request)
    {
        var pragmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Headers.TryGetValue("Pragma", out var pragmaRaw))
        {
            foreach (var item in pragmaRaw.Split(','))
            {
                var trimmed = item.Trim();
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                    pragmas[trimmed[..eqIdx].Trim()] = trimmed[(eqIdx + 1)..].Trim();
                else if (!string.IsNullOrEmpty(trimmed))
                    pragmas[trimmed] = "";
            }
        }
        return pragmas;
    }

    /// <summary>
    /// Build a Cougar/9.00.00.3372-style response for non-streaming requests (POST, DESCRIBE).
    /// </summary>
    private static string BuildCougarSimpleResponse(int statusCode, string statusPhrase, string clientId, string dateStr, bool includeContentLength = true)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {statusPhrase}\r\n");
        sb.Append("Server: Cougar/9.00.00.3372\r\n");
        if (statusCode == 204)
            sb.Append("Content-Length: 0\r\n");
        sb.Append($"Date: {dateStr}\r\n");
        sb.Append($"Pragma: no-cache, client-id={clientId}, features=\"broadcast\", timeout=60000\r\n");
        sb.Append("Cache-Control: no-cache\r\n");
        sb.Append("Supported: com.microsoft.wm.srvppair, com.microsoft.wm.sswitch, com.microsoft.wm.predstrm, com.microsoft.wm.fastcache\r\n");
        if (statusCode != 204)
            sb.Append("Content-Length: 0\r\n");
        sb.Append("\r\n");
        return sb.ToString();
    }

    public static async Task HandleWmp9Stream(HttpRequest request, HttpResponse response, string stationId)
    {
        Console.Error.WriteLine($"[MMSH-WMP9] === {request.Method} /stream/wmp/{stationId}.asf ===");
        foreach (var h in request.Headers)
            Console.Error.WriteLine($"[MMSH-WMP9]   {h.Key}: {h.Value}");
        Console.Error.Flush();

        var pragmas = ParseMmshPragmas(request);
        var socket = request.ListenerSocket.Stream;
        var dateStr = DateTime.UtcNow.ToString("R");

        // Use client's own client-id if provided, otherwise generate one
        var clientId = pragmas.TryGetValue("client-id", out var cidStr) ? cidStr
            : ((uint)(Math.Abs(stationId.GetHashCode()) % 100000000)).ToString();

        // ═══ POST handling (matches Cougar/9.00.00.3372) ═══
        if (request.Method == "POST")
        {
            bool isStop = pragmas.ContainsKey("xStopStrm") && pragmas["xStopStrm"] == "1";
            bool isLogStats = request.Headers.TryGetValue("Content-Type", out var ct) &&
                              ct.Contains("x-wms-LogStats");

            if (isLogStats && !isStop)
            {
                // LogStats POST → 204 No Content (real Cougar sends this during active stream)
                Console.Error.WriteLine("[MMSH-WMP9] POST LogStats → 204 No Content");
                Console.Error.Flush();
                var resp = BuildCougarSimpleResponse(204, "No Content", clientId, dateStr);
                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.FlushAsync();
            }
            else
            {
                // xStopStrm or other POST → 200 OK Content-Length: 0
                Console.Error.WriteLine($"[MMSH-WMP9] POST {(isStop ? "xStopStrm" : "other")} → 200 OK");
                Console.Error.Flush();
                var resp = BuildCougarSimpleResponse(200, "OK", clientId, dateStr);
                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.FlushAsync();
            }

            response.Handled = true;
            return;
        }

        // ═══ GET handling ═══
        try
        {
            bool isPlay = pragmas.ContainsKey("xPlayStrm") && pragmas["xPlayStrm"] == "1";
            bool isPlayNext = pragmas.ContainsKey("xPlayNextEntry") && pragmas["xPlayNextEntry"] == "1";

            var session = await GetOrCreateSessionAsync(stationId);

            if (isPlay || isPlayNext)
            {
                // ═══════════════════════════════════════════════════════════
                // PLAY (xPlayStrm=1 or xPlayNextEntry=1)
                // Matches Cougar/9.00.00.3372 chunked streaming response.
                // Chunk sequence: $M → $H → $C → $M → $H → $D $D $D ...
                // ═══════════════════════════════════════════════════════════
                request.ListenerSocket.IsKeepAlive = false;

                const int InitialBufferPackets = 25;
                while (session.IsAlive && session.LivePosition < InitialBufferPackets)
                    await session.WaitForDataAsync(session.LivePosition);

                long readPos = Math.Max(0, session.LivePosition - InitialBufferPackets);
                var wmp9HChunk = PatchHChunkForWmp9(session.HChunk);

                int playlistGenId = 1;
                if (pragmas.TryGetValue("playlist-gen-id", out var genIdStr))
                    int.TryParse(genIdStr, out playlistGenId);

                var lastModified = DateTime.UtcNow.AddSeconds(-17).ToString("R");

                // HTTP/1.0 (through proxy) can't use chunked encoding — write MMSH
                // frames directly. HTTP/1.1 (direct) can use chunked encoding.
                var httpVer = request.Version ?? "HTTP/1.0";
                bool useChunked = httpVer.Contains("1.1");

                // Response headers — match Cougar/9.00.00.3372 capture
                var resp =
                    $"{httpVer} 200 OK\r\n" +
                    "Content-Type: application/x-mms-framed\r\n" +
                    "Server: Cougar/9.00.00.3372\r\n" +
                    $"Date: {dateStr}\r\n" +
                    $"Pragma: no-cache, client-id={clientId}, features=\"broadcast\", timeout=60000, AccelBW=3500000, AccelDuration=10000, Speed=1.000\r\n" +
                    "Cache-Control: no-cache, max-age=0, x-wms-stream-type=\"broadcast\", user-public, must-revalidate, x-wms-proxy-split\r\n" +
                    $"Last-Modified: {lastModified}\r\n" +
                    (useChunked ? "Transfer-Encoding: chunked\r\n" : "") +
                    "Supported: com.microsoft.wm.srvppair, com.microsoft.wm.sswitch, com.microsoft.wm.predstrm, com.microsoft.wm.fastcache\r\n" +
                    "\r\n";

                Console.Error.WriteLine($"[MMSH-WMP9] PLAY {(isPlayNext ? "xPlayNextEntry" : "xPlayStrm")} genId={playlistGenId} {httpVer} chunked={useChunked}");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));

                // $M → $H → $C → $M → $H (matches real Cougar sequence from PDML capture)
                // Real server increments playlist-gen-id by 1 in the second $M.
                var mChunk1 = BuildMmshMetadataChunk(playlistGenId);
                var mChunk2 = BuildMmshMetadataChunk(playlistGenId + 1);

                if (useChunked) await WriteHttpChunkAsync(socket, mChunk1);
                else await socket.WriteAsync(mChunk1);

                if (useChunked) await WriteHttpChunkAsync(socket, wmp9HChunk);
                else await socket.WriteAsync(wmp9HChunk);

                var cChunk = BuildMmshStreamChangeChunk();
                if (useChunked) await WriteHttpChunkAsync(socket, cChunk);
                else await socket.WriteAsync(cChunk);

                // Second $M + $H after stream change notification
                if (useChunked) await WriteHttpChunkAsync(socket, mChunk2);
                else await socket.WriteAsync(mChunk2);

                if (useChunked) await WriteHttpChunkAsync(socket, wmp9HChunk);
                else await socket.WriteAsync(wmp9HChunk);

                await socket.FlushAsync();

                Console.Error.WriteLine($"[MMSH-WMP9] Sent $M({mChunk1.Length}B) $H({wmp9HChunk.Length}B) $C(8B) $M({mChunk2.Length}B) $H — streaming from seq={readPos} live={session.LivePosition}");
                Console.Error.Flush();

                // Stream $D data packets
                uint sent = 0;
                uint clientLocationId = 0;
                byte clientAfFlags = 0;
                uint? sendTimeBase = null;

                try
                {
                    while (session.IsAlive)
                    {
                        var (chunk, seq) = session.TryRead(readPos);
                        if (chunk != null)
                        {
                            var outChunk = (byte[])chunk.Clone();
                            BitConverter.GetBytes(clientLocationId).CopyTo(outChunk, 4);
                            outChunk[9] = clientAfFlags;

                            if (TryFindAsfSendTimeAndDurationOffsets(
                                    outChunk, 12, session.AsfPacketSize,
                                    out int stOffset, out _))
                            {
                                uint sendTime = BitConverter.ToUInt32(outChunk, 12 + stOffset);
                                sendTimeBase ??= sendTime;
                                uint rebased = sendTime - sendTimeBase.Value;
                                BitConverter.GetBytes(rebased).CopyTo(outChunk, 12 + stOffset);
                            }

                            if (useChunked) await WriteHttpChunkAsync(socket, outChunk);
                            else await socket.WriteAsync(outChunk);

                            readPos = seq + 1;
                            sent++;
                            clientLocationId++;
                            clientAfFlags = (byte)(clientAfFlags + 1);

                            if (sent <= 50 || sent % 500 == 0)
                            {
                                Console.Error.WriteLine($"[MMSH-WMP9] $D pkt={sent} seq={seq}");
                                Console.Error.Flush();
                            }
                        }
                        else
                        {
                            await session.WaitForDataAsync(readPos);
                        }
                    }
                }
                catch (IOException)
                {
                    Console.Error.WriteLine($"[MMSH-WMP9] Client disconnected after {sent} packets");
                }
                catch (OperationCanceledException) { }

                Console.Error.Flush();
                response.Handled = true;
            }
            else
            {
                // ═══════════════════════════════════════════════════════════
                // DESCRIBE (no xPlayStrm/xPlayNextEntry — ASF header probe)
                // Must echo version11-enabled and experiment pragmas back
                // so WMP9 proceeds to the PLAY request.
                // ═══════════════════════════════════════════════════════════
                var wmp9HChunk = PatchHChunkForWmp9(session.HChunk);

                bool isVersion11 = pragmas.ContainsKey("version11-enabled");

                // Build extra Pragma lines that WMP9 expects echoed back
                var extraPragmas = new StringBuilder();
                if (isVersion11)
                    extraPragmas.Append("Pragma: version11-enabled=1\r\n");
                foreach (var key in new[] { "packet-pair-experiment", "pipeline-experiment" })
                {
                    if (pragmas.TryGetValue(key, out var val))
                        extraPragmas.Append($"Pragma: {key}={val}\r\n");
                }

                var descHttpVer = request.Version ?? "HTTP/1.0";
                var resp =
                    $"{descHttpVer} 200 OK\r\n" +
                    "Content-Type: application/vnd.ms.wms-hdr.asfv1\r\n" +
                    "Server: Cougar/9.00.00.3372\r\n" +
                    $"Date: {dateStr}\r\n" +
                    $"Content-Length: {wmp9HChunk.Length}\r\n" +
                    $"Pragma: no-cache, client-id={clientId}, features=\"broadcast\", timeout=60000\r\n" +
                    extraPragmas.ToString() +
                    "Cache-Control: no-cache\r\n" +
                    "Supported: com.microsoft.wm.srvppair, com.microsoft.wm.sswitch, com.microsoft.wm.predstrm, com.microsoft.wm.fastcache\r\n" +
                    "\r\n";

                Console.Error.WriteLine($"[MMSH-WMP9] DESCRIBE: $H={wmp9HChunk.Length}B v11={isVersion11}");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.WriteAsync(wmp9HChunk);
                await socket.FlushAsync();
                response.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MMSH-WMP9] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Console.Error.Flush();
            response.Handled = true;
        }
    }
}
