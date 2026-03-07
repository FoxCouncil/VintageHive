// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
// Cook codec encoder — ported from NihAV (Kostya Shishkov, March 2023)

using System.Buffers.Binary;

namespace VintageHive.Processors.LocalServer.Streaming;

internal class CookEncoder
{
    private const int MaxFrameSize = 1024;
    private const int BandSize = CookData.BandSize; // 20

    // ===================================================================
    // Configuration (set during construction)
    // ===================================================================

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _frameSize;    // samples per channel per frame (256/512/1024)
    private readonly int _flavourIndex;
    private readonly CookData.FlavourInfo _flavour;
    private readonly CookData.BitrateParams _brInfo;
    private readonly int _totalSubbands;
    private readonly int _frameBitsPerChannel; // frame_bits / channels for mono-per-channel encoding
    private readonly int _codedFrameSize;      // sub_packet_h * sub_packet_size bytes
    private readonly int _subPacketSize;       // bytes per individual cook frame (frame_bits/8)
    private readonly int _subPacketH;          // frames per superpacket
    private readonly int _framesPerBlock;
    private readonly int _factor;

    // ===================================================================
    // DSP state
    // ===================================================================

    private readonly float[] _window;
    private readonly float[] _mdctTmp;
    private readonly float[] _coeffs;
    private readonly float[][] _delay; // [channel][frameSize]

    // Power/half-power lookup tables
    private readonly float[] _powTab = new float[128];
    private readonly float[] _hpowTab = new float[128];

    // ===================================================================
    // PCM buffering
    // ===================================================================

    private readonly byte[] _pcmBuffer;
    private int _pcmBufferPos;
    private readonly int _pcmBytesPerFrame; // frameSize * channels * 2 (s16le)
    private uint _timestamp; // milliseconds
    private int _frameCounter; // total frames encoded, for diagnostics
    private int _rmPacketIndex; // tracks position within interleave group (0..sub_packet_h-1)

    // GENR interleave buffering: accumulate framesPerGroup frames, then emit
    // sub_packet_h RM packets with interleaved frame ordering.
    // framesPerGroup = sub_packet_h * (coded_frame_size / sub_packet_size)
    private readonly int _framesPerPacket;  // cook frames per RM data packet (coded_frame_size / sub_packet_size)
    private readonly int _framesPerGroup;   // total frames per GENR interleave group
    private readonly List<byte[]> _interleaveBuffer = new();

    // ===================================================================
    // Construction
    // ===================================================================

    public CookEncoder(int sampleRate, int channels, int bitrate)
    {
        _sampleRate = sampleRate;
        _channels = channels;

        _frameSize = sampleRate switch
        {
            8000 or 11025 => 256,
            22050 => 512,
            _ => 1024,
        };

        // Find matching flavour
        _flavourIndex = -1;
        for (int i = CookData.CookFlavours.Length - 1; i >= 0; i--)
        {
            var f = CookData.CookFlavours[i];
            if (f.SampleRate == (uint)sampleRate && f.Channels == channels)
            {
                if (bitrate <= 0 || bitrate <= f.Bitrate * 1000)
                {
                    _flavourIndex = i;
                    if (bitrate > 0 && bitrate == f.Bitrate * 1000)
                    {
                        break;
                    }
                }
            }
        }

        if (_flavourIndex < 0)
        {
            throw new ArgumentException($"No cook flavour for {sampleRate}Hz {channels}ch {bitrate}bps");
        }

        _flavour = CookData.CookFlavours[_flavourIndex];
        _brInfo = CookData.BitrateParamsTable[_flavour.BrIds[0]];
        _totalSubbands = _brInfo.MaxSubbands;
        _frameBitsPerChannel = _brInfo.FrameBits / _brInfo.Channels;
        _subPacketSize = _brInfo.FrameBits / 8;
        _framesPerBlock = _flavour.FramesPerBlock;
        _factor = _flavour.Factor;
        _subPacketH = _framesPerBlock;

        // Verify that per-channel bits divide evenly
        if (_brInfo.FrameBits % _brInfo.Channels != 0)
        {
            throw new ArgumentException(
                $"FrameBits ({_brInfo.FrameBits}) not evenly divisible by Channels ({_brInfo.Channels})");
        }

        // coded_frame_size = sub_packet_h * sub_packet_size (one superpacket worth of frames)
        _codedFrameSize = _subPacketH * _subPacketSize;

        // GENR interleave group: the deinterleaver collects sub_packet_h RM packets,
        // each containing coded_frame_size/sub_packet_size cook frames.
        _framesPerPacket = _codedFrameSize / _subPacketSize;
        _framesPerGroup = _subPacketH * _framesPerPacket;

        // Initialize DSP
        _window = new float[_frameSize * 2];
        _mdctTmp = new float[_frameSize * 2];
        _coeffs = new float[MaxFrameSize];
        _delay = new float[_channels][];
        for (int ch = 0; ch < _channels; ch++)
        {
            _delay[ch] = new float[_frameSize];
        }

        // Build sine window with sqrt(2/N) normalization.
        // The real cook encoder (cook3260.dll) uses sin(θ) in the window and sqrt(2/N) in
        // the MDCT post-twiddle. Since our MDCT is a raw sum, we fold sqrt(2/N) into the
        // window to get equivalent coefficient magnitude.
        float fSamples = _frameSize;
        float factor = MathF.PI / (2.0f * fSamples);
        float windowScale = MathF.Sqrt(2.0f / fSamples);
        for (int k = 0; k < _frameSize * 2; k++)
        {
            _window[k] = MathF.Sin(factor * (k + 0.5f)) * windowScale;
        }

        // Build power tables
        for (int i = 0; i < 128; i++)
        {
            _powTab[i] = MathF.Pow(2.0f, i - 64.0f);
            _hpowTab[i] = MathF.Pow(2.0f, (i - 64.0f) * 0.5f);
        }

        // PCM buffering: accumulate one frame worth of PCM
        _pcmBytesPerFrame = _frameSize * _channels * 2;
        _pcmBuffer = new byte[_pcmBytesPerFrame];
        _pcmBufferPos = 0;
    }

    // ===================================================================
    // Public API
    // ===================================================================

    /// <summary>
    /// Feed raw PCM data (s16le, interleaved). Returns complete RM data packets
    /// when enough data has been accumulated, or empty list if more data needed.
    /// Each returned byte[] is one RM data packet (12-byte header + coded_frame_size payload).
    /// Packets are emitted in groups of sub_packet_h after framesPerGroup cook frames
    /// have been encoded, with GENR interleaving applied.
    /// </summary>
    public List<byte[]> EncodePcm(ReadOnlySpan<byte> pcmData)
    {
        var packets = new List<byte[]>();
        int offset = 0;

        while (offset < pcmData.Length)
        {
            int needed = _pcmBytesPerFrame - _pcmBufferPos;
            int available = pcmData.Length - offset;
            int toCopy = Math.Min(needed, available);

            pcmData.Slice(offset, toCopy).CopyTo(_pcmBuffer.AsSpan(_pcmBufferPos));
            _pcmBufferPos += toCopy;
            offset += toCopy;

            if (_pcmBufferPos >= _pcmBytesPerFrame)
            {
                // We have one full frame of PCM — encode it
                var cookFrame = EncodeOneFrame();
                if (cookFrame.Length != _subPacketSize)
                {
                    throw new InvalidOperationException(
                        $"Cook frame size mismatch: got {cookFrame.Length}, expected {_subPacketSize}");
                }
                _interleaveBuffer.Add(cookFrame);
                _pcmBufferPos = 0;

                // When we have a full GENR interleave group, emit sub_packet_h RM packets
                if (_interleaveBuffer.Count >= _framesPerGroup)
                {
                    var groupPackets = BuildInterleaveGroup();
                    packets.AddRange(groupPackets);
                    _interleaveBuffer.Clear();
                }
            }
        }

        return packets;
    }

    /// <summary>
    /// Build sub_packet_h RM data packets from a full GENR interleave group.
    /// The GENR deinterleaver in FFmpeg fills its buffer using:
    ///   slot = sps * (h*x + ((h+1)/2)*(y&amp;1) + (y>>1))
    /// where y = RM packet index (0..h-1), x = frame position within packet (0..framesPerPacket-1).
    /// We must place temporal frame f at (y, x) such that slot f = h*x + ((h+1)/2)*(y&amp;1) + (y>>1).
    /// </summary>
    private List<byte[]> BuildInterleaveGroup()
    {
        int h = _subPacketH;
        int halfH = (h + 1) / 2; // = 5 for h=10

        var rmPackets = new List<byte[]>(h);

        for (int y = 0; y < h; y++)
        {
            // Build the payload for RM packet y: framesPerPacket cook frames
            var payload = new byte[_codedFrameSize];

            for (int x = 0; x < _framesPerPacket; x++)
            {
                // Which temporal frame goes at position x in packet y?
                int temporalFrame = _framesPerPacket * x + halfH * (y & 1) + (y >> 1);
                Buffer.BlockCopy(_interleaveBuffer[temporalFrame], 0,
                    payload, x * _subPacketSize, _subPacketSize);
            }

            var rmPacket = BuildRmDataPacket(payload);
            rmPackets.Add(rmPacket);
        }

        return rmPackets;
    }

    /// <summary>Build a standalone .ra4 header for HTTP streaming.</summary>
    public byte[] BuildRa4Header()
    {
        // We build a .ra version 4 header:
        // .ra\xFD (4) + version(2) + .ra4 signature(4) + data_size(4) +
        // version2(2) + header_size(4) + flavor(2) + coded_frame_size(4) +
        // ... (matches FFmpeg rmdec.c version 4 format)
        // Actually, the jazz1.rm uses version 5. Let's use version 5 for compat.
        return BuildRa5Header();
    }

    /// <summary>Build .ra version 5 header matching jazz1.rm format.</summary>
    public byte[] BuildRa5Header()
    {
        var extradata = GetExtradata();
        using var ms = new MemoryStream();
        using var bw = new BigEndianWriter(ms);

        // .ra magic
        bw.WriteBytes(new byte[] { 0x2E, 0x72, 0x61, 0xFD });
        bw.WriteU16(5); // version

        // Version 5 fields
        bw.WriteU16(0); // unused
        bw.WriteBytes(new byte[] { 0x2E, 0x72, 0x61, 0x35 }); // ".ra5"
        bw.WriteU32(0x7FFFFFFF); // data_size (streaming)
        bw.WriteU16(5); // version2

        // header_size: everything from after this field to end of extradata
        int headerSizePos = (int)ms.Position;
        bw.WriteU32(0); // placeholder

        int headerStart = (int)ms.Position;

        bw.WriteU16((ushort)_flavourIndex); // flavor
        bw.WriteU32((uint)_codedFrameSize); // coded_frame_size

        // Three u32 fields from jazz1.rm — appear to be related to codec byte sizes
        // These match the jazz1.rm reference file for flavor 9; may need adjustment for other flavors
        const uint Ra5DataField1 = 0x00012688; // jazz1.rm offset 0x1E
        const uint Ra5DataField2 = 0x00024970; // jazz1.rm offset 0x22
        const uint Ra5DataField3 = 0x00024970; // jazz1.rm offset 0x26
        bw.WriteU32(Ra5DataField1);
        bw.WriteU32(Ra5DataField2);
        bw.WriteU32(Ra5DataField3);

        bw.WriteU16((ushort)_subPacketH);    // sub_packet_h
        bw.WriteU16((ushort)(_subPacketH * _subPacketSize)); // frame_size (total bytes per superpacket)
        bw.WriteU16((ushort)_subPacketSize); // sub_packet_size
        bw.WriteU16(0); // skip

        // Version 5 extra fields
        bw.WriteU16(0); // v5_skip1
        bw.WriteU16((ushort)_sampleRate); // sample_rate (duplicate)
        bw.WriteU16(0); // v5_skip3

        bw.WriteU16((ushort)_sampleRate); // sample_rate
        bw.WriteU32(16); // sample_size (bits)
        bw.WriteU16((ushort)_channels); // channels

        // Interleaver: "genr" as LE u32 (per FFmpeg rmdec: avio_rl32)
        bw.WriteBytes(new byte[] { 0x67, 0x65, 0x6E, 0x72 }); // "genr"
        // Codec: "cook" as raw bytes
        bw.WriteBytes(new byte[] { 0x63, 0x6F, 0x6F, 0x6B }); // "cook"

        // After codec_id: two skip bytes, one skip byte (v5), then codecdata_length
        bw.WriteU16(0x0107); // skip
        bw.WriteU8(0); // skip
        bw.WriteU8(0); // v5 skip
        bw.WriteU32((uint)extradata.Length); // codecdata_length
        bw.WriteBytes(extradata);

        // Patch header_size — per jazz1.rm, this counts from the version2 field
        // (includes version2(2) + header_size_field(4) + content)
        int headerEnd = (int)ms.Position;
        int headerSize = headerEnd - headerStart + 6;
        ms.Position = headerSizePos;
        bw.WriteU32((uint)headerSize);

        return ms.ToArray();
    }

    /// <summary>
    /// Build complete RM container headers (PROP+CONT+MDPR+DATA) for PNA streaming.
    /// </summary>
    public byte[] BuildRmHeaders(string stationName)
    {
        var ra5Header = BuildRa5Header();
        using var ms = new MemoryStream();

        // PROP chunk (50 bytes)
        WritePropChunk(ms, 0);

        // CONT chunk (station name as title)
        WriteContChunk(ms, stationName);

        // MDPR chunk (stream 0, audio)
        WriteMdprChunk(ms, ra5Header, stationName);

        // DATA header (18 bytes)
        WriteDataHeader(ms);

        return ms.ToArray();
    }

    /// <summary>
    /// Build full RM file headers (.RMF+PROP+CONT+MDPR+DATA) for HTTP streaming.
    /// This produces an RM container that RealPlayer can parse over HTTP.
    /// For static files, pass numPackets and totalDataSize so the DATA chunk and PROP
    /// contain correct counts (FFmpeg's RM demuxer needs these to find data packets).
    /// </summary>
    public byte[] BuildRmFileHeaders(string stationName, uint numPackets = 0, uint totalDataSize = 0, uint durationMs = 0x7FFFFFFF)
    {
        var ra5Header = BuildRa5Header();
        var titleBytes = Encoding.ASCII.GetBytes(stationName ?? "VintageHive Radio");

        // Pre-calculate chunk sizes to determine data_offset
        int rmfSize = 18;
        int propSize = 50;
        int contSize = 8 + 2 + 2 + titleBytes.Length + 2 + 2 + 2; // tag+size+version+title_len+title+author+copyright+comment

        var streamNameBytes = Encoding.ASCII.GetBytes("Audio Stream");
        var mimeBytes = Encoding.ASCII.GetBytes("audio/x-pn-realaudio");
        int mdprSize = 8 + 2 + 2 + 4 * 7 + 1 + streamNameBytes.Length + 1 + mimeBytes.Length + 4 + ra5Header.Length;

        uint dataOffset = (uint)(rmfSize + propSize + contSize + mdprSize);

        using var ms = new MemoryStream();

        // .RMF chunk (18 bytes)
        WriteRmfChunk(ms);

        // PROP chunk (50 bytes) with correct data_offset
        WritePropChunk(ms, dataOffset, numPackets, durationMs);

        // CONT chunk
        WriteContChunk(ms, stationName);

        // MDPR chunk
        WriteMdprChunk(ms, ra5Header, stationName);

        // DATA header (18 bytes) — for files, includes actual packet count and data size
        WriteDataHeader(ms, numPackets, totalDataSize);

        return ms.ToArray();
    }

    /// <summary>Get cook codec extradata for container headers.</summary>
    public byte[] GetExtradata()
    {
        using var ms = new MemoryStream();
        using var bw = new BigEndianWriter(ms);

        if (_channels <= 2)
        {
            int chMode;
            if (_brInfo.Channels == 1)
            {
                chMode = 1; // mono
            }
            else if (_brInfo.JsBits == 0)
            {
                chMode = 2; // independent stereo
            }
            else
            {
                chMode = 3; // joint stereo
            }

            bw.WriteU32((uint)((1 << 24) | chMode));
            bw.WriteU16((ushort)(_frameSize * _brInfo.Channels));
            bw.WriteU16(_brInfo.MaxSubbands);

            if (chMode == 3)
            {
                bw.WriteU32(0); // delay
                bw.WriteU16(_brInfo.JsStart);
                bw.WriteU16(_brInfo.JsBits);
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Generate a static .rm test file from a sine wave.
    /// Used to validate cook encoding with FFmpeg offline.
    /// </summary>
    public static byte[] GenerateTestRmFile(float durationSeconds = 5.0f)
    {
        int sampleRate = 11025;
        int channels = 2;
        int bitrate = 19000;
        var encoder = new CookEncoder(sampleRate, channels, bitrate);

        // Generate sine wave PCM (440 Hz)
        int totalSamples = (int)(sampleRate * durationSeconds);
        var pcm = new byte[totalSamples * channels * 2];
        for (int i = 0; i < totalSamples; i++)
        {
            short sample = (short)(Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 4000);
            for (int ch = 0; ch < channels; ch++)
            {
                int idx = (i * channels + ch) * 2;
                pcm[idx] = (byte)(sample & 0xFF);
                pcm[idx + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }

        // Encode all PCM — returns RM data packets (12-byte header + payload each)
        var allPackets = encoder.EncodePcm(pcm);

        // Compute file metadata for DATA chunk and PROP chunk
        uint numPackets = (uint)allPackets.Count;
        uint totalDataSize = 0;
        foreach (var pkt in allPackets)
        {
            totalDataSize += (uint)pkt.Length;
        }
        uint durationMs = (uint)(durationSeconds * 1000);

        // Build complete RM file with correct packet counts
        string title = "Cook Test - 440Hz Sine";
        var headers = encoder.BuildRmFileHeaders(title, numPackets, totalDataSize, durationMs);

        using var ms = new MemoryStream();
        ms.Write(headers, 0, headers.Length);
        foreach (var pkt in allPackets)
        {
            ms.Write(pkt, 0, pkt.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Encode a WAV file to a cook .rm file. Uses FFmpeg to resample/convert
    /// the input to 11025Hz stereo s16le PCM before encoding.
    /// </summary>
    public static byte[] EncodeWavFile(string inputPath)
    {
        int sampleRate = 11025;
        int channels = 2;
        int bitrate = 19000;
        var encoder = new CookEncoder(sampleRate, channels, bitrate);

        // Use FFmpeg to convert input to raw s16le PCM at our target format
        var ffmpegPath = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows) ? @"libs\ffmpeg.exe" :
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.OSX) ? "libs/ffmpeg.osx.intel" : "libs/ffmpeg.amd64";
        var ffmpegArgs = $"-i \"{inputPath}\" -ar {sampleRate} -ac {channels} -c:a pcm_s16le -f s16le pipe:1";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = ffmpegArgs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        using var stdout = process.StandardOutput.BaseStream;

        // Read all PCM data from stdout
        using var pcmStream = new MemoryStream();
        stdout.CopyTo(pcmStream);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"FFmpeg failed (exit {process.ExitCode}): {stderr}");
        }

        var pcm = pcmStream.ToArray();
        int totalSamplesPerChannel = pcm.Length / (channels * 2);
        float durationSeconds = (float)totalSamplesPerChannel / sampleRate;

        Console.WriteLine($"Input: {totalSamplesPerChannel} samples/ch, {durationSeconds:F1}s at {sampleRate}Hz {channels}ch");

        // Encode all PCM
        var allPackets = encoder.EncodePcm(pcm);

        // Compute file metadata
        uint numPackets = (uint)allPackets.Count;
        uint totalDataSize = 0;
        foreach (var pkt in allPackets)
        {
            totalDataSize += (uint)pkt.Length;
        }
        uint durationMs = (uint)(durationSeconds * 1000);

        // Build complete RM file
        string title = Path.GetFileNameWithoutExtension(inputPath);
        var headers = encoder.BuildRmFileHeaders(title, numPackets, totalDataSize, durationMs);

        using var ms = new MemoryStream();
        ms.Write(headers, 0, headers.Length);
        foreach (var pkt in allPackets)
        {
            ms.Write(pkt, 0, pkt.Length);
        }

        Console.WriteLine($"Encoded {numPackets} RM packets, {allPackets.Sum(p => p.Length)} bytes data");
        return ms.ToArray();
    }

    // ===================================================================
    // Frame encoding
    // ===================================================================

    private byte[] EncodeOneFrame()
    {
        // Convert s16le PCM to float samples per channel
        var samples = new float[_channels][];
        for (int ch = 0; ch < _channels; ch++)
        {
            samples[ch] = new float[_frameSize];
        }

        for (int i = 0; i < _frameSize; i++)
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                int idx = (i * _channels + ch) * 2;
                short s = (short)((_pcmBuffer[idx + 1] << 8) | _pcmBuffer[idx]);
                samples[ch][i] = s;
            }
        }

        // Encode each channel independently
        using var output = new MemoryStream();
        for (int ch = 0; ch < _channels; ch++)
        {
            var channelFrame = EncodeChannel(samples[ch], ch);
            output.Write(channelFrame, 0, channelFrame.Length);
        }

        return output.ToArray();
    }

    private byte[] EncodeChannel(float[] pcmSamples, int channelIndex)
    {
        // MDCT transform with 50% overlap
        ApplyMdct(_delay[channelIndex], pcmSamples);

        int totalBands = _totalSubbands;
        int frameBits = _frameBitsPerChannel;

        // Calculate qindex (spectral envelope) per subband
        var qindex = new sbyte[CookData.MaxSubbands * 2];
        for (int i = 0; i < totalBands; i++)
        {
            float nrg = 0;
            for (int j = 0; j < BandSize; j++)
            {
                float c = _coeffs[i * BandSize + j];
                nrg += c * c;
            }
            qindex[i] = CalcQindex(nrg);
        }
        // Clamp qi[0] to fit in 6-bit unsigned field (qi+6 must be 0..63)
        // Real encoder (cook_categorize_subbands) clamps to [-6, 57]
        if (qindex[0] < -6)
        {
            qindex[0] = -6;
        }
        if (qindex[0] > 57)
        {
            qindex[0] = 57;
        }

        // Backward pass: ensure forward deltas never exceed +11
        // Real encoder: qi[i] >= qi[i+1] - 11
        for (int i = totalBands - 2; i >= 0; i--)
        {
            if (qindex[i] < qindex[i + 1] - 11)
            {
                qindex[i] = (sbyte)(qindex[i + 1] - 11);
            }
        }

        // Re-clamp qi[0] after backward pass (it may have been raised)
        if (qindex[0] > 57)
        {
            qindex[0] = 57;
        }

        // Diagnostic: log first few frames to check MDCT output
        _frameCounter++;
        if (_frameCounter <= 5 || _frameCounter % 500 == 0)
        {
            float maxCoeff = 0, sumCoeffSq = 0;
            for (int i = 0; i < totalBands * BandSize; i++)
            {
                float abs = MathF.Abs(_coeffs[i]);
                if (abs > maxCoeff) { maxCoeff = abs; }
                sumCoeffSq += _coeffs[i] * _coeffs[i];
            }
            float pcmMax = 0;
            for (int i = 0; i < pcmSamples.Length; i++)
            {
                float abs = MathF.Abs(pcmSamples[i]);
                if (abs > pcmMax) { pcmMax = abs; }
            }
            var qiStr = string.Join(",", qindex.Take(totalBands).Select(q => q.ToString()));
            Log.WriteLine(Log.LEVEL_INFO, "COOK-ENC",
                $"Frame#{_frameCounter} ch{channelIndex}: pcmMax={pcmMax:F4} maxCoeff={maxCoeff:F2} rmsCoeff={MathF.Sqrt(sumCoeffSq / (totalBands * BandSize)):F2} qi=[{qiStr}]");
        }

        // Write bitstream
        var bw = new CookBitWriter(frameBits / 8);

        // Gain: 0 = no temporal gain
        bw.Write(0, 1);

        // Envelope: first qindex as 6 bits, rest as Huffman-coded deltas
        sbyte lastQ = qindex[0];
        bw.Write((uint)(qindex[0] + 6), 6);
        for (int i = 1; i < totalBands; i++)
        {
            int cbIdx = Math.Min(i - 1, 12);
            int diff = Math.Clamp(qindex[i] - lastQ, -12, 11);
            qindex[i] = (sbyte)(lastQ + diff);
            lastQ = qindex[i];

            int symIdx = diff + 12; // 0..23
            bw.Write(CookData.QuantCodes[cbIdx][symIdx], CookData.QuantBits[cbIdx][symIdx]);
        }

        // Bit allocation
        // FFmpeg's decoder reads vectorBits (num_vectors) BEFORE calling categorize().
        // So categorize sees bits_left = total - gain - envelope - vectorBits.
        // We must use the same value so categories match the decoder exactly.
        byte vectorBits = 5;
        int bitsAfterEnvelope = (int)bw.BitsWritten;
        int bitsLeft = frameBits - bitsAfterEnvelope - vectorBits; // matches decoder's categorize
        int bitsAvail = bitsLeft;                                   // VQ budget = remaining after vectorBits

        var category = new byte[CookData.MaxSubbands * 2];
        var catIndex = new byte[127];
        BitAlloc(_frameSize, bitsLeft, vectorBits, totalBands, qindex, category, catIndex);

        // Quantize and pack VQ codewords for each band
        var packedBands = new PackedCoeffs[totalBands];
        ushort totalVqBits = 0;
        for (int i = 0; i < totalBands; i++)
        {
            packedBands[i] = new PackedCoeffs();
            // Scale coefficients by hpow_tab
            var bandCoeffs = new float[BandSize];
            float scale = _hpowTab[64 - qindex[i]];
            for (int j = 0; j < BandSize; j++)
            {
                bandCoeffs[j] = _coeffs[i * BandSize + j] * scale;
            }
            totalVqBits += packedBands[i].Pack(bandCoeffs, category[i]);
        }

        // Diagnostic: log bit budget on first few frames
        if (_frameCounter <= 5 || _frameCounter % 500 == 0)
        {
            var catStr = string.Join(",", category.Take(totalBands).Select(c => c.ToString()));
            Log.WriteLine(Log.LEVEL_INFO, "COOK-ENC",
                $"Frame#{_frameCounter} ch{channelIndex}: frameBits={frameBits} envBits={bitsAfterEnvelope} bitsAvail={bitsAvail} totalVqBits={totalVqBits} cats=[{catStr}]");
        }

        // Correction: reduce categories that overshoot the actual VQ budget.
        // catIndex now has (1<<vectorBits)-1 entries matching FFmpeg's categorize output.
        ushort vqBudget = (ushort)bitsAvail;
        int bandsCorrected = 0;
        int maxCorrBands = (1 << vectorBits) - 1;
        for (int ci = 0; ci < maxCorrBands; ci++)
        {
            if (totalVqBits <= vqBudget)
            {
                break;
            }
            int idx = catIndex[ci];
            var pband = packedBands[idx];
            var bandCoeffs = new float[BandSize];
            float scale = _hpowTab[64 - qindex[idx]];
            for (int j = 0; j < BandSize; j++)
            {
                bandCoeffs[j] = _coeffs[idx * BandSize + j] * scale;
            }
            int newCat = Math.Min(pband.Cat + 1, CookData.NumCategories - 1);
            totalVqBits -= pband.NBits;
            pband.Pack(bandCoeffs, newCat);
            totalVqBits += pband.NBits;
            bandsCorrected++;
        }

        // Write vector_bits correction count
        bw.Write((uint)bandsCorrected, vectorBits);

        // Diagnostic: log post-correction state
        if (_frameCounter <= 5 || _frameCounter % 500 == 0)
        {
            var corrCatStr = string.Join(",", Enumerable.Range(0, totalBands).Select(i => packedBands[i].Cat.ToString()));
            Log.WriteLine(Log.LEVEL_INFO, "COOK-ENC",
                $"Frame#{_frameCounter} ch{channelIndex}: CORRECTED totalVqBits={totalVqBits} vqBudget={vqBudget} corr={bandsCorrected} cats=[{corrCatStr}]");
        }

        // Write VQ codewords + signs
        ushort remaining = vqBudget;
        int bitsActuallyWritten = 0;
        for (int i = 0; i < totalBands; i++)
        {
            int beforeBits = (int)bw.BitsWritten;
            packedBands[i].Write(bw, remaining);
            int wrote = (int)bw.BitsWritten - beforeBits;
            bitsActuallyWritten += wrote;
            remaining = (ushort)Math.Max(0, remaining - packedBands[i].NBits);
            if (remaining == 0)
            {
                if (_frameCounter <= 5 || _frameCounter % 500 == 0)
                {
                    Log.WriteLine(Log.LEVEL_INFO, "COOK-ENC",
                        $"Frame#{_frameCounter} ch{channelIndex}: TRUNCATED at band {i}/{totalBands}, wrote {bitsActuallyWritten}/{totalVqBits} VQ bits");
                }
                break;
            }
        }

        // Pad to frame boundary
        bw.PadToEnd();

        // Diagnostic: dump raw bitstream for first frame
        if (_frameCounter <= 2)
        {
            var rawBytes = bw.ToArray();
            var hexStr = string.Join(" ", rawBytes.Select(b => b.ToString("X2")));
            Log.WriteLine(Log.LEVEL_INFO, "COOK-ENC",
                $"Frame#{_frameCounter} ch{channelIndex}: RAW bitstream ({rawBytes.Length} bytes): {hexStr}");

            // Also dump first few MDCT coefficients
            var coeffStr = string.Join(", ", Enumerable.Range(0, Math.Min(40, totalBands * BandSize))
                .Select(i => _coeffs[i].ToString("F1")));
            Log.WriteLine(Log.LEVEL_INFO, "COOK-ENC",
                $"Frame#{_frameCounter} ch{channelIndex}: MDCT coeffs[0..39]: {coeffStr}");
        }

        // XOR scramble per-channel. The decoder's decode_bytes calls
        // decode_bytes independently for each channel with a 32-bit
        // aligned buffer, so each channel uses key[j&3] from offset 0.
        var channelBytes = bw.ToArray();
        for (int i = 0; i < channelBytes.Length; i++)
        {
            channelBytes[i] ^= CookData.XorKey[i & 3];
        }
        return channelBytes;
    }

    // ===================================================================
    // MDCT with 50% overlap
    // ===================================================================

    private void ApplyMdct(float[] delay, float[] src)
    {
        int N = _frameSize; // number of output coefficients
        int twoN = N * 2;   // number of input samples

        // Build windowed input: [delay, src] * window
        for (int n = 0; n < N; n++)
        {
            _mdctTmp[n] = delay[n] * _window[n];
            _mdctTmp[N + n] = src[n] * _window[N + n];
        }

        // MDCT: X[k] = sum_{n=0}^{2N-1} input[n] * cos(π/(2M) * (2n + 1 + N) * (2k + 1))
        // Standard Type-IV DCT forward MDCT. FFmpeg's cook decoder IMDCT uses the
        // complementary kernel cos(π/N * (m+0.5-N/2)(k+0.5)) with Cook's overlap-add
        // (subtraction-based), which together give perfect reconstruction.
        float piOver2M = MathF.PI / (2.0f * twoN);
        for (int k = 0; k < N; k++)
        {
            float sum = 0;
            float factor = piOver2M * (2 * k + 1);
            for (int n = 0; n < twoN; n++)
            {
                sum += _mdctTmp[n] * MathF.Cos(factor * (2 * n + 1 + N));
            }
            _coeffs[k] = sum;
        }

        // Save current frame as delay for next frame
        Array.Copy(src, 0, delay, 0, N);
    }

    // ===================================================================
    // Bit allocation (categorize) — from NihAV bitalloc()
    // ===================================================================

    private static void BitAlloc(int samples, int bits, byte vectorBits, int totalSubbands,
        sbyte[] qindex, byte[] category, byte[] catIndex)
    {
        int availBits = bits > samples
            ? samples + ((bits - samples) * 5) / 8
            : bits;

        // Binary search for bias
        int bias = -32;
        for (int i = 0; i < 6; i++)
        {
            int sum = 0;
            for (int j = 0; j < totalSubbands; j++)
            {
                int idx = ClipCat(((32 >> i) + bias - qindex[j]) / 2);
                sum += CookData.ExpBitsTab[idx];
            }
            if (sum >= availBits - 32)
            {
                bias += 32 >> i;
            }
        }

        // Compute initial categories
        var expIndex1 = new int[CookData.MaxSubbands * 2];
        var expIndex2 = new int[CookData.MaxSubbands * 2];
        int totalExpBits = 0;
        for (int i = 0; i < totalSubbands; i++)
        {
            int idx = ClipCat((bias - qindex[i]) / 2);
            totalExpBits += CookData.ExpBitsTab[idx];
            expIndex1[i] = idx;
            expIndex2[i] = idx;
        }

        // Iterative refinement
        int tbias1 = totalExpBits;
        int tbias2 = totalExpBits;
        var tcat = new int[256];
        int tcatIdx1 = 128;
        int tcatIdx2 = 128;

        for (int iter = 1; iter < (1 << vectorBits); iter++)
        {
            if (tbias1 + tbias2 > availBits * 2)
            {
                // Need to reduce bits: bump a category up (fewer bits)
                int max = -999999;
                int bestIdx = totalSubbands + 1;
                for (int j = 0; j < totalSubbands; j++)
                {
                    if (expIndex1[j] >= CookData.NumCategories - 1)
                    {
                        continue;
                    }
                    int t = -2 * expIndex1[j] - qindex[j] + bias;
                    if (t >= max)
                    {
                        max = t;
                        bestIdx = j;
                    }
                }
                if (bestIdx >= totalSubbands)
                {
                    break;
                }
                tcat[tcatIdx1] = bestIdx;
                tcatIdx1++;
                tbias1 -= CookData.ExpBitsTab[expIndex1[bestIdx]] - CookData.ExpBitsTab[expIndex1[bestIdx] + 1];
                expIndex1[bestIdx]++;
            }
            else
            {
                // Need more bits: bump a category down (more bits)
                int min = 999999;
                int bestIdx = totalSubbands + 1;
                for (int j = 0; j < totalSubbands; j++)
                {
                    if (expIndex2[j] == 0)
                    {
                        continue;
                    }
                    int t = -2 * expIndex2[j] - qindex[j] + bias;
                    if (t < min)
                    {
                        min = t;
                        bestIdx = j;
                    }
                }
                if (bestIdx >= totalSubbands)
                {
                    break;
                }
                tcatIdx2--;
                tcat[tcatIdx2] = bestIdx;
                tbias2 -= CookData.ExpBitsTab[expIndex2[bestIdx]] - CookData.ExpBitsTab[expIndex2[bestIdx] - 1];
                expIndex2[bestIdx]--;
            }
        }

        // Output categories from expIndex2
        for (int i = 0; i < totalSubbands; i++)
        {
            category[i] = (byte)expIndex2[i];
        }

        // Output adjustment sequence — exactly numvector_size-1 entries, matching FFmpeg's categorize.
        // FFmpeg reads from tmp_categorize_array starting at tmp_categorize_array2_idx,
        // outputting numvector_size-1 entries. Entries beyond the valid range default to 0 (band 0).
        int numEntries = (1 << vectorBits) - 1;
        for (int i = 0; i < catIndex.Length; i++)
        {
            catIndex[i] = 0;
        }
        int ci = 0;
        for (int i = tcatIdx2; ci < numEntries; i++, ci++)
        {
            if (i < tcatIdx1)
            {
                catIndex[ci] = (byte)tcat[i];
            }
            // else: stays 0 (band 0), matching FFmpeg's zero-initialized array
        }
    }

    private static int ClipCat(int val)
    {
        return Math.Clamp(val, 0, CookData.NumCategories - 1);
    }

    private static sbyte CalcQindex(float nrg)
    {
        float nrg0 = nrg * 0.05f;
        if (nrg0 <= 1.0f)
        {
            nrg0 *= MathF.Sqrt(0.5f);
        }
        else
        {
            nrg0 *= MathF.Sqrt(2.0f);
        }
        if (nrg0 < 1e-10f)
        {
            return -31;
        }
        return (sbyte)Math.Clamp(MathF.Log2(nrg0), -31.0f, 63.0f);
    }

    // ===================================================================
    // VQ coefficient mapping
    // ===================================================================

    private static int MapCoef(float val, float[] centroids)
    {
        if (val < centroids[1] * 0.5f)
        {
            return 0;
        }

        int len = centroids.Length;
        if (val < centroids[len - 1])
        {
            for (int i = 1; i < len - 1; i++)
            {
                if (val <= (centroids[i] + centroids[i + 1]) * 0.5f)
                {
                    return i;
                }
            }
        }
        return len - 1;
    }

    // ===================================================================
    // Packed VQ coefficients
    // ===================================================================

    private class PackedCoeffs
    {
        public ushort[] Cw = new ushort[10];
        public byte[] Bits = new byte[10];
        public ushort[] Signs = new ushort[10];
        public byte[] Nnz = new byte[10];
        public int Num;
        public int Cat;
        public ushort NBits;

        public ushort Pack(float[] coeffs, int cat)
        {
            Cat = cat;
            NBits = 0;

            if (cat >= CookData.NumVqGroups.Length)
            {
                Num = 0;
                return 0;
            }

            int groupSize = CookData.VqGroupSize[cat];
            int multiplier = CookData.VqMult[cat];
            var vqCodes = CookData.VqCodes[cat];
            var vqBits = CookData.VqBits[cat];
            var centroids = CookData.QuantCentroidTab[cat];
            Num = CookData.NumVqGroups[cat];

            for (int groupNo = 0; groupNo < Num; groupNo++)
            {
                int groupStart = groupNo * groupSize;
                int cw = 0;
                Span<int> cvals = stackalloc int[5];
                Span<int> sarr = stackalloc int[5];

                for (int k = 0; k < groupSize; k++)
                {
                    int coeffIdx = groupStart + k;
                    float el = coeffIdx < coeffs.Length ? coeffs[coeffIdx] : 0;
                    int curVal = Math.Min(MapCoef(MathF.Abs(el), centroids), multiplier - 1);
                    cvals[k] = curVal;
                    sarr[k] = el < 0 ? 1 : 0;
                    cw = cw * multiplier + curVal;
                }

                // Clamp codeword if out of range or has zero-length code
                while (cw >= vqBits.Length || vqBits[cw] == 0)
                {
                    int maxPos = 0;
                    int maxVal = cvals[0];
                    for (int k = 1; k < groupSize; k++)
                    {
                        if (cvals[k] > maxVal)
                        {
                            maxVal = cvals[k];
                            maxPos = k;
                        }
                    }
                    if (cvals[maxPos] <= 0)
                    {
                        break;
                    }
                    cvals[maxPos]--;
                    cw = 0;
                    for (int k = 0; k < groupSize; k++)
                    {
                        cw = cw * multiplier + cvals[k];
                    }
                }

                // Collect sign bits for non-zero coefficients.
                // The decoder reads signs in forward order (j = 0 to vd-1),
                // so we emit them in the same forward order.
                ushort signs = 0;
                byte nnz = 0;
                for (int k = 0; k < groupSize; k++)
                {
                    if (cvals[k] != 0)
                    {
                        signs = (ushort)((signs << 1) | sarr[k]);
                        nnz++;
                    }
                }

                if (cw < vqCodes.Length && cw < vqBits.Length && vqBits[cw] > 0)
                {
                    Cw[groupNo] = vqCodes[cw];
                    Bits[groupNo] = vqBits[cw];
                }
                else
                {
                    // Fallback: zero codeword
                    Cw[groupNo] = vqCodes[0];
                    Bits[groupNo] = vqBits[0];
                    signs = 0;
                    nnz = 0;
                }
                Signs[groupNo] = signs;
                Nnz[groupNo] = nnz;
                NBits += (ushort)(Bits[groupNo] + nnz);
            }

            return NBits;
        }

        public void Write(CookBitWriter bw, ushort bitsLeft)
        {
            for (int i = 0; i < Num; i++)
            {
                ushort curBits = (ushort)(Bits[i] + Nnz[i]);
                if (curBits > bitsLeft)
                {
                    break;
                }
                bitsLeft -= curBits;
                bw.Write(Cw[i], Bits[i]);
                if (Nnz[i] > 0)
                {
                    bw.Write(Signs[i], Nnz[i]);
                }
            }
        }
    }

    // ===================================================================
    // RM data packet builder
    // ===================================================================

    private byte[] BuildRmDataPacket(byte[] cookPayload)
    {
        int packetLen = 12 + cookPayload.Length;
        var packet = new byte[packetLen];

        // version = 0
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0), 0);
        // length (total packet including header)
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)packetLen);
        // stream_number = 0
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 0);
        // timestamp (ms) — all packets in a group share the same timestamp (per jazz1.rm)
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(6), _timestamp);
        // reserved = 0
        packet[10] = 0;
        // flags: keyframe (0x02) only on first packet of each interleave group,
        // regular (0x00) for the rest. The GENR deinterleaver resets its counter
        // on keyframe, so marking every packet as keyframe prevents accumulation.
        packet[11] = (_rmPacketIndex == 0) ? (byte)0x02 : (byte)0x00;
        // payload
        Buffer.BlockCopy(cookPayload, 0, packet, 12, cookPayload.Length);

        // Advance interleave group index
        _rmPacketIndex = (_rmPacketIndex + 1) % _subPacketH;

        // Advance timestamp: each RM packet covers framesPerPacket cook frames.
        float frameDurationMs = _frameSize * 1000.0f / _sampleRate;
        _timestamp += (uint)(frameDurationMs * _framesPerPacket);

        return packet;
    }

    // ===================================================================
    // RM container chunk builders
    // ===================================================================

    private void WriteRmfChunk(MemoryStream ms)
    {
        using var bw = new BigEndianWriter(ms, leaveOpen: true);
        bw.WriteU32(0x2E524D46); // ".RMF"
        bw.WriteU32(18);         // chunk size
        bw.WriteU16(0);          // version
        bw.WriteU32(0);          // file_version
        bw.WriteU32(5);          // num_headers (.RMF + PROP + CONT + MDPR + DATA)
    }

    private void WritePropChunk(MemoryStream ms, uint dataOffset, uint numPackets = 0, uint durationMs = 0x7FFFFFFF)
    {
        using var bw = new BigEndianWriter(ms, leaveOpen: true);

        uint prerollMs = (uint)MathF.Round(_frameSize * _subPacketH * 1000.0f / _sampleRate);
        bool isFile = numPackets > 0;
        ushort flags = isFile ? (ushort)0x0003 : (ushort)0x000B; // file: SAVE|PERFECT_PLAY, stream: +LIVE_BROADCAST

        bw.WriteU32(0x50524F50); // "PROP"
        bw.WriteU32(50);         // chunk size = 50 bytes
        bw.WriteU16(0);          // version
        bw.WriteU32((uint)(_flavour.Bitrate * 1000)); // max_bit_rate
        bw.WriteU32((uint)(_flavour.Bitrate * 1000)); // avg_bit_rate
        bw.WriteU32((uint)_codedFrameSize);            // max_packet_size
        bw.WriteU32((uint)_codedFrameSize);            // avg_packet_size
        bw.WriteU32(numPackets);  // num_packets
        bw.WriteU32(durationMs);  // duration_ms
        bw.WriteU32(prerollMs);   // preroll_ms
        bw.WriteU32(0);           // index_offset
        bw.WriteU32(dataOffset);  // data_offset
        bw.WriteU16(1);           // num_streams
        bw.WriteU16(flags);       // flags
    }

    private void WriteContChunk(MemoryStream ms, string title)
    {
        using var bw = new BigEndianWriter(ms, leaveOpen: true);
        var titleBytes = Encoding.ASCII.GetBytes(title ?? "VintageHive Radio");
        // tag(4) + size(4) + version(2) + title_len(2) + title + author_len(2) + copyright_len(2) + comment_len(2)
        int chunkSize = 8 + 2 + 2 + titleBytes.Length + 2 + 2 + 2;

        bw.WriteU32(0x434F4E54); // "CONT"
        bw.WriteU32((uint)chunkSize);
        bw.WriteU16(0);          // version
        bw.WriteU16((ushort)titleBytes.Length);
        bw.WriteBytes(titleBytes);
        bw.WriteU16(0); // author
        bw.WriteU16(0); // copyright
        bw.WriteU16(0); // comment
    }

    private void WriteMdprChunk(MemoryStream ms, byte[] ra5Header, string stationName)
    {
        using var bw = new BigEndianWriter(ms, leaveOpen: true);

        // Stream description strings
        var streamNameBytes = Encoding.ASCII.GetBytes("Audio Stream");
        var mimeBytes = Encoding.ASCII.GetBytes("audio/x-pn-realaudio");

        int chunkSize = 8   // tag + size
            + 2             // version
            + 2             // stream_number
            + 4             // max_bit_rate
            + 4             // avg_bit_rate
            + 4             // max_packet_size
            + 4             // avg_packet_size
            + 4             // start_time
            + 4             // preroll
            + 4             // duration
            + 1 + streamNameBytes.Length  // stream_name_size + string
            + 1 + mimeBytes.Length        // mime_type_size + string
            + 4 + ra5Header.Length;       // type_specific_len + data

        bw.WriteU32(0x4D445052); // "MDPR"
        bw.WriteU32((uint)chunkSize);
        bw.WriteU16(0);          // version
        bw.WriteU16(0);          // stream_number
        bw.WriteU32((uint)(_flavour.Bitrate * 1000)); // max_bit_rate
        bw.WriteU32((uint)(_flavour.Bitrate * 1000)); // avg_bit_rate
        bw.WriteU32((uint)_codedFrameSize);            // max_packet_size (payload, matches jazz1.rm)
        bw.WriteU32((uint)_codedFrameSize);            // avg_packet_size
        bw.WriteU32(0);          // start_time
        bw.WriteU32(0);          // preroll
        bw.WriteU32(0x7FFFFFFF); // duration

        bw.WriteU8((byte)streamNameBytes.Length);
        bw.WriteBytes(streamNameBytes);
        bw.WriteU8((byte)mimeBytes.Length);
        bw.WriteBytes(mimeBytes);

        bw.WriteU32((uint)ra5Header.Length);
        bw.WriteBytes(ra5Header);
    }

    private void WriteDataHeader(MemoryStream ms, uint numPackets = 0, uint dataSize = 0)
    {
        using var bw = new BigEndianWriter(ms, leaveOpen: true);

        uint chunkSize = 18 + dataSize; // 18-byte header + data payload

        bw.WriteU32(0x44415441); // "DATA"
        bw.WriteU32(chunkSize);  // chunk size
        bw.WriteU16(0);          // version
        bw.WriteU32(numPackets); // num_packets
        bw.WriteU32(0);          // next_data_header
    }

    // ===================================================================
    // Big-endian bitstream writer
    // ===================================================================

    internal class CookBitWriter
    {
        private readonly byte[] _buffer;
        private int _bitPos;
        private readonly int _totalBits;

        public int BitsWritten => _bitPos;

        public CookBitWriter(int byteSize)
        {
            _buffer = new byte[byteSize];
            _bitPos = 0;
            _totalBits = byteSize * 8;
        }

        public void Write(uint value, int numBits)
        {
            if (numBits <= 0 || _bitPos + numBits > _totalBits)
            {
                return;
            }

            for (int i = numBits - 1; i >= 0; i--)
            {
                int byteIdx = _bitPos / 8;
                int bitIdx = 7 - (_bitPos % 8);
                if (((value >> i) & 1) != 0)
                {
                    _buffer[byteIdx] |= (byte)(1 << bitIdx);
                }
                _bitPos++;
            }
        }

        public void PadToEnd()
        {
            // Remaining bits are already zero
            _bitPos = _totalBits;
        }

        public byte[] ToArray()
        {
            return (byte[])_buffer.Clone();
        }
    }

    // ===================================================================
    // Big-endian binary writer helper
    // ===================================================================

    internal class BigEndianWriter : IDisposable
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;

        public BigEndianWriter(Stream stream, bool leaveOpen = false)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
        }

        public void WriteU8(byte value)
        {
            _stream.WriteByte(value);
        }

        public void WriteU16(ushort value)
        {
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, value);
            _stream.Write(buf);
        }

        public void WriteU32(uint value)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, value);
            _stream.Write(buf);
        }

        public void WriteBytes(byte[] data)
        {
            _stream.Write(data, 0, data.Length);
        }

        public void Dispose()
        {
            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }
    }
}
