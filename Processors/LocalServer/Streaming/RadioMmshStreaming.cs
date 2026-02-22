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
        chunk[1] = chunkType;  // 'H'=0x48, 'D'=0x44
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

            // File Properties Object layout (offsets from object start):
            //  0-15: GUID, 16-23: Object Size, 24-39: File ID
            // 40-47: File Size (uint64)       → set to large value
            // 48-55: Creation Date (uint64)   → leave as-is
            // 56-63: Data Packets Count (uint64) → set non-zero
            // 64-71: Play Duration (uint64, 100ns units) → set to ~24 hours
            // 72-79: Send Duration (uint64)   → same
            // 80-87: Preroll (uint64, ms)      → set to 3000ms
            // 88-91: Flags (uint32)           → set broadcast=1, seekable=0
            // 92-95: Min Data Packet Size (uint32)
            // 96-99: Max Data Packet Size (uint32)
            // 100-103: Max Bitrate (uint32)

            // File Size: 2GB (large dummy value so WMP allocates buffers)
            BitConverter.GetBytes((long)0x7FFFFFFF).CopyTo(headerData, i + 40);

            // Data Packets Count: large number (WMP may use this to allocate)
            BitConverter.GetBytes((long)0xFFFF).CopyTo(headerData, i + 56);

            // Play Duration: ~24 hours in 100-nanosecond units
            BitConverter.GetBytes(24L * 3600 * 10_000_000).CopyTo(headerData, i + 64);

            // Send Duration: same
            BitConverter.GetBytes(24L * 3600 * 10_000_000).CopyTo(headerData, i + 72);

            // Preroll: 3000ms (3 seconds buffer before playback)
            BitConverter.GetBytes((long)3000).CopyTo(headerData, i + 80);

            // Flags: broadcast=1 (bit 0), seekable=0 (bit 1 cleared)
            BitConverter.GetBytes((uint)0x01).CopyTo(headerData, i + 88);

            // Ensure packet sizes are set
            if (BitConverter.ToInt32(headerData, i + 92) == 0)
                BitConverter.GetBytes(packetSize).CopyTo(headerData, i + 92);
            if (BitConverter.ToInt32(headerData, i + 96) == 0)
                BitConverter.GetBytes(packetSize).CopyTo(headerData, i + 96);

            // Max Bitrate: ensure non-zero (e.g., 128kbps)
            if (BitConverter.ToInt32(headerData, i + 100) == 0)
                BitConverter.GetBytes(128000).CopyTo(headerData, i + 100);

            Console.Error.WriteLine($"[MMSH] Patched ASF File Properties: fileSize=2GB, packets=65535, preroll=3000ms, broadcast=1");
            Console.Error.Flush();
            return;
        }
    }

    /// <summary>
    /// Create a WMP 9-specific $H chunk by cloning and patching the shared one
    /// with proper broadcast values (0 for size/count/duration).
    /// WMP 9 enforces ASF broadcast semantics more strictly than WMP 6.4.
    /// </summary>
    private static byte[] PatchHChunkForWmp9(byte[] hChunk)
    {
        var patched = (byte[])hChunk.Clone();

        // $H payload starts at offset 12 (4-byte framing + 8-byte MMS header)
        // Find File Properties Object by GUID in the ASF header within the payload
        for (int i = 12; i <= patched.Length - 104; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (patched[i + j] != AsfFilePropertiesGuid[j]) { match = false; break; }
            }
            if (!match) continue;

            // Override to proper broadcast values (0 = unknown/live)
            BitConverter.GetBytes((long)0).CopyTo(patched, i + 40);  // File Size = 0
            BitConverter.GetBytes((long)0).CopyTo(patched, i + 56);  // Data Packets Count = 0
            BitConverter.GetBytes((long)0).CopyTo(patched, i + 64);  // Play Duration = 0
            BitConverter.GetBytes((long)0).CopyTo(patched, i + 72);  // Send Duration = 0
            // Preroll, flags, packet sizes, bitrate: unchanged from base

            Console.Error.WriteLine("[MMSH] WMP9: patched $H with proper broadcast values (fileSize=0, packets=0, duration=0)");
            Console.Error.Flush();
            return patched;
        }

        return patched; // GUID not found, return clone as-is
    }

    /// <summary>
    /// Build an ASF Content Description Object with the given metadata strings.
    /// Empty/null strings produce a length of 0 with no payload bytes.
    /// </summary>
    private static byte[] BuildAsfContentDescription(
        string title, string author, string copyright, string description, string rating)
    {
        // Encode each string as UTF-16LE + null terminator (2 bytes)
        static byte[] EncodeField(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            var bytes = Encoding.Unicode.GetBytes(s);
            var result = new byte[bytes.Length + 2]; // +2 for null terminator
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            // last 2 bytes are already 0x00 0x00 from array init
            return result;
        }

        var titleBytes = EncodeField(title);
        var authorBytes = EncodeField(author);
        var copyrightBytes = EncodeField(copyright);
        var descBytes = EncodeField(description);
        var ratingBytes = EncodeField(rating);

        // Object layout: GUID(16) + ObjectSize(8) + 5×uint16 lengths + string data
        long objectSize = 16 + 8 + 10 + titleBytes.Length + authorBytes.Length +
                          copyrightBytes.Length + descBytes.Length + ratingBytes.Length;

        var obj = new byte[objectSize];
        int pos = 0;

        // GUID
        Buffer.BlockCopy(AsfContentDescriptionGuid, 0, obj, pos, 16); pos += 16;

        // Object Size
        BitConverter.GetBytes(objectSize).CopyTo(obj, pos); pos += 8;

        // String lengths (byte count including null terminator, or 0 if empty)
        BitConverter.GetBytes((ushort)titleBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)authorBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)copyrightBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)descBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)ratingBytes.Length).CopyTo(obj, pos); pos += 2;

        // String data
        Buffer.BlockCopy(titleBytes, 0, obj, pos, titleBytes.Length); pos += titleBytes.Length;
        Buffer.BlockCopy(authorBytes, 0, obj, pos, authorBytes.Length); pos += authorBytes.Length;
        Buffer.BlockCopy(copyrightBytes, 0, obj, pos, copyrightBytes.Length); pos += copyrightBytes.Length;
        Buffer.BlockCopy(descBytes, 0, obj, pos, descBytes.Length); pos += descBytes.Length;
        Buffer.BlockCopy(ratingBytes, 0, obj, pos, ratingBytes.Length);

        return obj;
    }

    /// <summary>
    /// Rebuild the $H chunk with an updated Content Description title.
    /// Finds and replaces the existing Content Description Object in the $H payload,
    /// preserving all other ASF objects.
    /// </summary>
    private static byte[] RebuildHChunkWithTitle(MmshLiveSession session, string newTitle)
    {
        var hChunk = session.HChunk;

        // $H payload starts at offset 12 (4-byte framing + 8-byte MMS header)
        // The payload is: ASF Header Object + Data Object header (50 bytes)
        // We need to find the Content Description Object within the ASF Header Object

        // Read the ASF Header Object size from bytes 16-23 of the payload (offset 28-35 in hChunk)
        long asfHeaderSize = BitConverter.ToInt64(hChunk, 12 + 16);
        var headerObj = new byte[asfHeaderSize];
        Buffer.BlockCopy(hChunk, 12, headerObj, 0, (int)asfHeaderSize);

        // Find existing Content Description Object by GUID
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

        if (cdOffset < 0) return hChunk; // No Content Description found, return original

        // Get size of existing Content Description Object
        long oldCdSize = BitConverter.ToInt64(headerObj, cdOffset + 16);

        // Build new Content Description with updated title
        var newCd = BuildAsfContentDescription(
            title: newTitle,
            author: $"VintageHive/{Mind.ApplicationVersion}",
            copyright: session.Station?.Country ?? "",
            description: session.Station?.Tags ?? "",
            rating: "");

        // Build new header: [before CD] + [new CD] + [after CD]
        int beforeCd = cdOffset;
        int afterCdStart = cdOffset + (int)oldCdSize;
        int afterCdLen = (int)asfHeaderSize - afterCdStart;

        long newAsfHeaderSize = beforeCd + newCd.Length + afterCdLen;
        var newHeaderObj = new byte[newAsfHeaderSize];
        Buffer.BlockCopy(headerObj, 0, newHeaderObj, 0, beforeCd);
        Buffer.BlockCopy(newCd, 0, newHeaderObj, beforeCd, newCd.Length);
        Buffer.BlockCopy(headerObj, afterCdStart, newHeaderObj, beforeCd + newCd.Length, afterCdLen);

        // Update ASF Header Object Size
        BitConverter.GetBytes(newAsfHeaderSize).CopyTo(newHeaderObj, 16);

        // Extract Data Object header (50 bytes at end of original $H payload)
        var dataObjHeader = new byte[50];
        Buffer.BlockCopy(hChunk, 12 + (int)asfHeaderSize, dataObjHeader, 0, 50);

        // Build new $H chunk
        var hPayload = new byte[newAsfHeaderSize + 50];
        Buffer.BlockCopy(newHeaderObj, 0, hPayload, 0, (int)newAsfHeaderSize);
        Buffer.BlockCopy(dataObjHeader, 0, hPayload, (int)newAsfHeaderSize, 50);

        return BuildMmshChunk(0x48, 0, 0, 0x0C, hPayload);
    }

    /// <summary>
    /// Find the byte offset of the Send Time field (4 bytes, uint32 ms) within
    /// a raw ASF data packet by parsing Error Correction + Payload Parsing Info.
    /// </summary>
    private static int FindAsfSendTimeOffset(byte[] asfPacket)
    {
        int pos = 0;

        // Error Correction
        byte ecFlags = asfPacket[pos++];
        if ((ecFlags & 0x80) != 0) // EC present
        {
            int ecLen = ecFlags & 0x0F;
            pos += ecLen;
        }

        // Length Type Flags
        byte ltf = asfPacket[pos++];
        pos++; // Property Flags (skip)

        // Variable-length fields based on type codes (00=absent, 01=BYTE, 10=WORD, 11=DWORD)
        static int TypeToSize(int t) => t switch { 1 => 1, 2 => 2, 3 => 4, _ => 0 };

        pos += TypeToSize((ltf >> 1) & 0x03); // Packet Length
        pos += TypeToSize((ltf >> 3) & 0x03); // Padding Length
        pos += TypeToSize((ltf >> 5) & 0x03); // Sequence

        return pos; // Send Time starts here (4 bytes, followed by 2-byte Duration)
    }

    // ===================================================================
    // FFmpeg process creation (duplicated for independence)
    // ===================================================================

    private static Process CreateWmaFfmpegProcess()
    {
        var process = new Process();
        process.StartInfo.FileName = GetFfmpegExecutablePath();
        // Microsoft ADPCM (format tag 0x0002) inside ASF container
        // - Natively supported on ALL Windows versions (no codec install needed)
        // - ~4:1 compression vs PCM → ~11 KB/s at 22050Hz mono
        // - block_size 1024 = reasonable ADPCM block alignment
        process.StartInfo.Arguments = "-probesize 32768 -analyzeduration 0 -i pipe:0 -fflags nobuffer -map_metadata -1 -c:a adpcm_ms -ar 22050 -ac 1 -block_size 1024 -f asf pipe:1";
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
    // Shared MMSH live sessions: one ffmpeg per station, multiple consumers
    // ===================================================================

    private static readonly ConcurrentDictionary<string, MmshLiveSession> _liveSessions = new();
    private static readonly ConcurrentDictionary<string, long> _clientLastSeq = new();
    private static readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    /// <summary>
    /// A live MMSH streaming session for one station.
    /// Owns: upstream HTTP connection, ffmpeg transcode process, circular buffer of pre-framed $D chunks.
    /// Multiple PLAY connections read from the same buffer with continuous LocationId/AFFlags.
    /// </summary>
    private class MmshLiveSession : IDisposable
    {
        public byte[] HChunk { get; }
        public int AsfPacketSize { get; }
        public RadioStationInfo Station { get; }
        private IcyMetadataStrippingStream _icyStream;

        /// <summary>Current track title from ICY metadata, or station info fallback.</summary>
        public string CurrentTrack =>
            _icyStream?.CurrentTrack ?? Station?.CurrentTrack;

        private const int RingCapacity = 300; // ~60s at ~5 pkt/s
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

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    int read = await ReadExactAsync(ffmpegOut, buf, 0, packetSize);
                    if (read == 0) break;
                    if (read < packetSize) Array.Clear(buf, read, packetSize - read);

                    var dChunk = BuildMmshChunk(0x44, locationId, 0, afFlags, buf);

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
                    afFlags = (byte)((afFlags + 1) % 255);

                    if (locationId % 500 == 0)
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
                // Wake any waiting consumers so they can exit
                lock (_lock) { _newDataTcs.TrySetResult(); }
            }

            Console.Error.WriteLine($"[MMSH-Prod] Ended ({_headSeq} total)");
            Console.Error.Flush();
        }

        public long LivePosition { get { lock (_lock) return _headSeq; } }

        /// <summary>
        /// Try to read a pre-framed $D chunk. Returns (null, seq) if not yet available.
        /// Automatically skips past stale positions that fell off the ring buffer.
        /// </summary>
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

        /// <summary>Wait until data is available at or after the given sequence.</summary>
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
            // Double-check after lock
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

            // Request ICY metadata so we can capture now-playing info
            httpClient.DefaultRequestHeaders.Add("Icy-MetaData", "1");

            var upstreamResponse = await httpClient.GetAsync(info.StreamUrl, HttpCompletionOption.ResponseHeadersRead);
            var rawUpstreamStream = await upstreamResponse.Content.ReadAsStreamAsync();

            // Wrap with ICY metadata stripper if the server provides metadata
            // icy-metaint can appear in either response headers or content headers
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
            _ = ffmpeg.StandardError.BaseStream.CopyToAsync(Stream.Null);
            _ = Task.Run(async () =>
            {
                try { await upstreamStream.CopyToAsync(ffmpeg.StandardInput.BaseStream); }
                catch { }
                try { ffmpeg.StandardInput.Close(); } catch { }
            });

            var ffmpegOut = ffmpeg.StandardOutput.BaseStream;

            // Read ASF Header Object
            var headerPrefix = new byte[24];
            if (await ReadExactAsync(ffmpegOut, headerPrefix, 0, 24) < 24)
                throw new InvalidOperationException("Failed to read ASF Header Object prefix");

            long asfHeaderSize = BitConverter.ToInt64(headerPrefix, 16);
            var headerObj = new byte[asfHeaderSize];
            Buffer.BlockCopy(headerPrefix, 0, headerObj, 0, 24);
            if (await ReadExactAsync(ffmpegOut, headerObj, 24, (int)asfHeaderSize - 24) < (int)asfHeaderSize - 24)
                throw new InvalidOperationException("Incomplete ASF Header Object");

            // Read first 50 bytes of Data Object
            var dataObjHeader = new byte[50];
            if (await ReadExactAsync(ffmpegOut, dataObjHeader, 0, 50) < 50)
                throw new InvalidOperationException("Failed to read ASF Data Object header");

            // Patch Data Object for broadcast
            BitConverter.GetBytes((long)0).CopyTo(dataObjHeader, 16);
            if (BitConverter.ToInt64(dataObjHeader, 40) == 0)
                BitConverter.GetBytes((long)0xFFFF).CopyTo(dataObjHeader, 40);
            dataObjHeader[48] = 0x01; dataObjHeader[49] = 0x01;

            int packetSize = GetAsfPacketSize(headerObj);
            PatchAsfHeaderForBroadcast(headerObj, packetSize);

            // Inject Content Description Object with station metadata
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

            // Update ASF Header Object size (bytes 16-23)
            BitConverter.GetBytes(finalHeaderSize).CopyTo(finalHeaderObj, 16);

            // Update number of header objects (bytes 24-27)
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
            // Parse Pragma values
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

            // WMP 6.4 response headers — separate Pragma lines (not comma-concatenated)
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
                // ═══ DESCRIBE ═══
                var resp = BuildWmp6Response(contentLength: session.HChunk.Length);

                Console.Error.WriteLine($"[MMSH-WMP6] DESCRIBE: $H={session.HChunk.Length}B");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.WriteAsync(session.HChunk);
                response.Handled = true;
            }
            else
            {
                // ═══ PLAY ═══
                request.ListenerSocket.IsKeepAlive = false;

                var resp = BuildWmp6Response();

                Console.Error.WriteLine($"[MMSH-WMP6] PLAY ctx={requestContext}");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));

                // On reconnect (ctx > 2), rebuild $H with current track title
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

                // Inline streaming loop — send chunks as-is from producer
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
    // ═══════════════════════════════════════════════════════════════════

    public static async Task HandleWmp9Stream(HttpRequest request, HttpResponse response, string stationId)
    {
        Console.Error.WriteLine($"[MMSH-WMP9] === {request.Method} /stream/wmp/{stationId}.asf ===");
        foreach (var h in request.Headers)
            Console.Error.WriteLine($"[MMSH-WMP9]   {h.Key}: {h.Value}");
        Console.Error.Flush();

        if (request.Method == "POST")
        {
            Console.Error.WriteLine("[MMSH-WMP9] POST log-line acknowledged");
            Console.Error.Flush();
            response.SetBodyString("", "text/plain");
            return;
        }

        try
        {
            // Parse Pragma values
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

            bool isVersion11 = pragmas.ContainsKey("version11-enabled");

            Console.Error.WriteLine($"[MMSH-WMP9] Mode={(isPlay ? "PLAY" : "DESCRIBE")} ctx={requestContext} v11={isVersion11}");
            Console.Error.Flush();

            var session = await GetOrCreateSessionAsync(stationId);
            var socket = request.ListenerSocket.Stream;
            var httpVer = request.Version;

            var features = "\"broadcast\"";

            // Echo back Supported features if client sent them (required for v11)
            var supportedHeader = "";
            if (request.Headers.TryGetValue("Supported", out var clientSupported))
                supportedHeader = $"Supported: {clientSupported}\r\n";

            if (!isPlay && isVersion11)
            {
                // ═══ v11 DESCRIBE ═══
                var wmp9HChunk = PatchHChunkForWmp9(session.HChunk);

                // Echo experiment pragmas from the request
                var experimentPragmas = new StringBuilder();
                foreach (var key in new[] { "packet-pair-experiment", "pipeline-experiment" })
                {
                    if (pragmas.TryGetValue(key, out var val))
                        experimentPragmas.Append($"Pragma: {key}={val}\r\n");
                }

                var resp =
                    $"{httpVer} 200 OK\r\n" +
                    "Server: Cougar/9.01.01.3814\r\n" +
                    "Content-Type: application/vnd.ms.wms-hdr.asfv1\r\n" +
                    $"Content-Length: {wmp9HChunk.Length}\r\n" +
                    $"Pragma: no-cache,client-id={clientId},features={features},timeout=60000,version11-enabled=1\r\n" +
                    supportedHeader +
                    experimentPragmas.ToString() +
                    "Cache-Control: no-cache\r\n" +
                    "\r\n";

                Console.Error.WriteLine($"[MMSH-WMP9] v11 DESCRIBE: $H={wmp9HChunk.Length}B");
                Console.Error.WriteLine($"[MMSH-WMP9] Response headers:\n{resp.TrimEnd()}");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.WriteAsync(wmp9HChunk);
                await socket.FlushAsync();

                response.Handled = true;
            }
            else if (!isPlay)
            {
                // ═══ Regular DESCRIBE (WMFSDK probe) ═══
                var wmp9HChunk = PatchHChunkForWmp9(session.HChunk);

                var resp =
                    $"{httpVer} 200 OK\r\n" +
                    "Server: Cougar/9.01.01.3814\r\n" +
                    "Content-Type: application/vnd.ms.wms-hdr.asfv1\r\n" +
                    $"Content-Length: {wmp9HChunk.Length}\r\n" +
                    $"Pragma: no-cache,client-id={clientId},features={features},timeout=60000\r\n" +
                    "Cache-Control: no-cache\r\n" +
                    "\r\n";

                Console.Error.WriteLine($"[MMSH-WMP9] DESCRIBE (probe): $H={wmp9HChunk.Length}B");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.WriteAsync(wmp9HChunk);
                response.Handled = true;
            }
            else
            {
                // ═══ PLAY ═══
                request.ListenerSocket.IsKeepAlive = false;

                var resp =
                    "HTTP/1.0 200 OK\r\n" +
                    "Server: Cougar/9.01.01.3814\r\n" +
                    "Content-Type: application/x-mms-framed\r\n" +
                    $"Pragma: no-cache,client-id={clientId},features={features},timeout=60000\r\n" +
                    "Pragma: xPlayStrm=1\r\n" +
                    "Pragma: xResetStrm=1\r\n" +
                    "Pragma: playlist-gen-id=1\r\n" +
                    "Pragma: packet-num=0\r\n" +
                    "Connection: close\r\n" +
                    "Cache-Control: no-cache\r\n" +
                    "\r\n";

                Console.Error.WriteLine($"[MMSH-WMP9] PLAY ctx={requestContext}");
                Console.Error.Flush();

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));

                if (requestContext <= 2)
                    await socket.WriteAsync(PatchHChunkForWmp9(session.HChunk));

                // Inline streaming loop — rebase locationId/afFlags/SendTime
                long catchUpTo = -1;
                long readPos;
                if (clientGuid != null && _clientLastSeq.TryGetValue(clientGuid, out var lastSeq))
                {
                    readPos = lastSeq + 1;
                    catchUpTo = session.LivePosition;
                }
                else
                {
                    readPos = Math.Max(0, session.LivePosition - 30);
                }

                Console.Error.WriteLine($"[MMSH-WMP9] Streaming from seq={readPos} live={session.LivePosition} guid={clientGuid ?? "N/A"}");
                Console.Error.Flush();

                uint sent = 0;
                uint clientLocationId = 0;
                byte clientAfFlags = 0;
                int sendTimeChunkOffset = -1;
                uint baseSendTime = 0;

                try
                {
                    while (session.IsAlive)
                    {
                        var (chunk, seq) = session.TryRead(readPos);
                        if (chunk != null)
                        {
                            var patched = (byte[])chunk.Clone();
                            BitConverter.GetBytes(clientLocationId).CopyTo(patched, 4);
                            patched[9] = clientAfFlags;

                            if (sendTimeChunkOffset < 0)
                            {
                                int asfOffset = FindAsfSendTimeOffset(patched.AsSpan(12).ToArray());
                                sendTimeChunkOffset = 12 + asfOffset;
                                baseSendTime = BitConverter.ToUInt32(patched, sendTimeChunkOffset);
                            }

                            uint origSendTime = BitConverter.ToUInt32(patched, sendTimeChunkOffset);
                            BitConverter.GetBytes(origSendTime - baseSendTime).CopyTo(patched, sendTimeChunkOffset);

                            await socket.WriteAsync(patched);

                            clientLocationId++;
                            clientAfFlags = (byte)((clientAfFlags + 1) % 255);

                            readPos = seq + 1;
                            sent++;

                            if (clientGuid != null)
                                _clientLastSeq[clientGuid] = seq;

                            if (catchUpTo > 0 && seq < catchUpTo)
                                await Task.Delay(50);
                            else if (catchUpTo > 0)
                            {
                                Console.Error.WriteLine($"[MMSH-WMP9] Caught up at seq={seq}");
                                Console.Error.Flush();
                                catchUpTo = -1;
                            }

                            if (sent % 500 == 0)
                            {
                                Console.Error.WriteLine($"[MMSH-WMP9] Sent {sent} (seq={seq})");
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
                    Console.Error.WriteLine($"[MMSH-WMP9] Client disconnected after {sent} packets (lastSeq={readPos - 1})");
                }
                catch (OperationCanceledException) { }

                Console.Error.Flush();
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
