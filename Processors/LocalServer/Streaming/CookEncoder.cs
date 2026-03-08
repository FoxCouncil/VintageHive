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

    private readonly float[] _window;        // [N] sine window — matches DLL cook_init_mdct_tables
    private readonly float[] _mdctTmp;       // [2*N] workspace for prewindow folding + FFT scratch
    private readonly float[] _coeffs;        // [MaxFrameSize] MDCT output coefficients
    private readonly float[] _preRotCos;     // [N/2] cos((i+0.25)*π/N)
    private readonly float[] _preRotSin;     // [N/2] sin((i+0.25)*π/N)
    private readonly float[] _postRotTwiddle; // [N/2+1] cos(i*π/N)*sqrt(2/N)
    private readonly int[] _bitRevTable;     // [N/2] bit-reversal permutation for FFT postrotate
    private readonly float[] _fftTwiddles;   // [N/2] complex twiddle factors for FFT stages (DLL-style bit-reversed order)
    private readonly int _fftSize;           // N/2 = number of complex FFT points
    private readonly int _fftLogN;           // log2(N/2)

    // 3-frame overlap buffer per channel: [frame_k-2, frame_k-1, frame_k]
    // After shift+fill, MDCT processes [0..2N-1] = [frame_k-2, frame_k-1]
    private readonly float[][] _overlapBuf;  // [channel][3*frameSize]
    private readonly float[][] _lpcOutput;   // [channel][2*frameSize] — work buffer after LPC

    // DC removal state per channel (1st-order IIR highpass)
    private readonly float[] _dcState;

    // Pre-emphasis biquad filter state per channel (3 sections × 4 state vars = 12 floats)
    private readonly float[][] _preemphasisState;
    private readonly int _preemphCoeffIdx;

    // Subband gain state per channel
    private readonly int _subbandSize; // = frameSize / 8
    private readonly float[][] _gainHistory;        // [channel][4]
    private readonly int[][] _prevGainInfo;         // [channel][17] — [0]=count, [1..8]=subband, [9..16]=gain
    private readonly int[][] _currGainInfo;         // [channel][17]
    private readonly float[][] _windowFilterState;  // [channel][8] — 2 biquad sections × 4 state vars
    private readonly float[] _gainStepRatios;       // [11] per-sample gain interpolation step ratios

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

    // Raw cook frame collector for testing (written before interleaving)
    internal List<byte[]> RawFrames { get; } = new();

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

        // Initialize DSP — MDCT tables matching DLL cook_init_mdct_tables
        int N = _frameSize;
        int halfN = N / 2;
        _window = new float[N];
        _mdctTmp = new float[N * 2];
        _coeffs = new float[MaxFrameSize];
        _preRotCos = new float[halfN];
        _preRotSin = new float[halfN];
        _postRotTwiddle = new float[halfN + 1];
        _bitRevTable = new int[halfN];
        _fftTwiddles = new float[halfN];

        // 3N overlap buffer and 2N LPC work buffer per channel
        _overlapBuf = new float[_channels][];
        _lpcOutput = new float[_channels][];
        for (int ch = 0; ch < _channels; ch++)
        {
            _overlapBuf[ch] = new float[_frameSize * 3];
            _lpcOutput[ch] = new float[_frameSize * 2];
        }

        // DC removal state
        _dcState = new float[_channels];

        // Pre-emphasis filter state (3 biquad sections × 4 state vars per channel)
        _preemphasisState = new float[_channels][];
        for (int ch = 0; ch < _channels; ch++)
        {
            _preemphasisState[ch] = new float[12];
        }
        _preemphCoeffIdx = (1024 / _frameSize) * _totalSubbands - 28;

        // Subband gain state
        _subbandSize = _frameSize / 8;
        _gainHistory = new float[_channels][];
        _prevGainInfo = new int[_channels][];
        _currGainInfo = new int[_channels][];
        _windowFilterState = new float[_channels][];
        for (int ch = 0; ch < _channels; ch++)
        {
            _gainHistory[ch] = new float[] { 1.0f, 1.0f, 1.0f, 1.0f };
            _prevGainInfo[ch] = new int[17];
            _currGainInfo[ch] = new int[17];
            _windowFilterState[ch] = new int[8].Select(_ => 0f).ToArray();
        }

        // Gain interpolation step ratios: pow(base[i], 1.0/subbandSize) for each base
        _gainStepRatios = new float[CookData.GainPowBase.Length];
        for (int i = 0; i < CookData.GainPowBase.Length; i++)
        {
            _gainStepRatios[i] = (float)Math.Pow(CookData.GainPowBase[i], 1.0 / _subbandSize);
        }

        // Build N-point sine window: sin((i+0.5)*π/(2N))
        // Matches DLL: fsin(((i - (-0.5)) * π) / (2 * mdct_size))
        for (int i = 0; i < N; i++)
        {
            _window[i] = (float)Math.Sin((i + 0.5) * Math.PI / (2.0 * N));
        }

        // Pre-rotation twiddles: cos/sin((i+0.25)*π/N) for i=0..N/2-1
        // DLL: ((i - (-0.25)) * π) / mdct_size
        for (int i = 0; i < halfN; i++)
        {
            double angle = (i + 0.25) * Math.PI / N;
            _preRotCos[i] = (float)Math.Cos(angle);
            _preRotSin[i] = (float)Math.Sin(angle);
        }

        // Post-rotation twiddles: cos(i*π/N)*sqrt(2/N) for i=0..N/2
        // DLL: fcos((i * π) / mdct_size) * sqrt(2.0 / mdct_size)
        double normScale = Math.Sqrt(2.0 / N);
        for (int i = 0; i <= halfN; i++)
        {
            _postRotTwiddle[i] = (float)(Math.Cos(i * Math.PI / N) * normScale);
        }

        // FFT size and log2 for split-radix DIF FFT
        _fftSize = halfN; // N/2 complex points
        _fftLogN = 0;
        for (int tmp = _fftSize; tmp > 1; tmp >>= 1) { _fftLogN++; }

        // Bit-reversal table for N/2 point FFT
        {
            for (int i = 0; i < _fftSize; i++)
            {
                int rev = 0;
                int val = i;
                for (int b = 0; b < _fftLogN; b++)
                {
                    rev = (rev << 1) | (val & 1);
                    val >>= 1;
                }
                _bitRevTable[i] = rev;
            }
        }

        // FFT twiddle factors — DLL style (cook_init_fft_tables):
        // twiddle[i] = (cos, sin)(bit_rev[2*i] * 2π / fft_size)
        // Bit-reversed order with positive angles, used by DIF FFT.
        for (int i = 0; i < _fftSize / 2; i++)
        {
            double angle = _bitRevTable[2 * i] * 2.0 * Math.PI / _fftSize;
            _fftTwiddles[i * 2] = (float)Math.Cos(angle);
            _fftTwiddles[i * 2 + 1] = (float)Math.Sin(angle);
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
                RawFrames.Add(cookFrame);
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

    /// <summary>
    /// Same as EncodeWavFile but also returns the encoder instance for accessing RawFrames.
    /// </summary>
    public static (byte[] rmData, CookEncoder encoder) EncodeWavFileWithEncoder(string inputPath)
    {
        int sampleRate = 11025;
        int channels = 2;
        int bitrate = 19000;
        var encoder = new CookEncoder(sampleRate, channels, bitrate);

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

        var allPackets = encoder.EncodePcm(pcm);

        uint numPackets = (uint)allPackets.Count;
        uint totalDataSize = 0;
        foreach (var pkt in allPackets)
        {
            totalDataSize += (uint)pkt.Length;
        }
        uint durationMs = (uint)(durationSeconds * 1000);

        string title = Path.GetFileNameWithoutExtension(inputPath);
        var headers = encoder.BuildRmFileHeaders(title, numPackets, totalDataSize, durationMs);

        using var ms = new MemoryStream();
        ms.Write(headers, 0, headers.Length);
        foreach (var pkt in allPackets)
        {
            ms.Write(pkt, 0, pkt.Length);
        }

        Console.WriteLine($"Encoded {numPackets} RM packets, {allPackets.Sum(p => p.Length)} bytes data");
        return (ms.ToArray(), encoder);
    }

    // ===================================================================
    // Frame encoding
    // ===================================================================

    private byte[] EncodeOneFrame()
    {
        // Convert s16le PCM to float samples per channel with DC removal
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

                // DC removal highpass: y[n] = (x[n] - dcOffset) - dcState;
                //                      dcState += alpha * y[n]
                float dcRemoved = ((float)s - CookData.DcOffset) - _dcState[ch];
                _dcState[ch] -= dcRemoved * CookData.DcFilterAlpha;
                samples[ch][i] = dcRemoved;
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
        int N = _frameSize;
        int ch = channelIndex;

        // ---------------------------------------------------------------
        // Step 1: Shift overlap buffer — move [N..3N-1] to [0..2N-1]
        // ---------------------------------------------------------------
        Array.Copy(_overlapBuf[ch], N, _overlapBuf[ch], 0, N * 2);

        // ---------------------------------------------------------------
        // Step 2: Write new PCM samples to [2N..3N-1]
        // (DC removal already applied in EncodeOneFrame)
        // ---------------------------------------------------------------
        Array.Copy(pcmSamples, 0, _overlapBuf[ch], N * 2, N);

        // ---------------------------------------------------------------
        // Step 3: Apply pre-emphasis to the new samples [2N..3N-1]
        // ---------------------------------------------------------------
        if (_preemphCoeffIdx >= 0 && _preemphCoeffIdx < 23)
        {
            ApplyFilterCoeffs(_overlapBuf[ch], N * 2,
                _preemphasisState[ch], 0,
                CookData.PreemphasisCoeffs, _preemphCoeffIdx * 15,
                3, N);
        }

        // ---------------------------------------------------------------
        // Step 4: Save current gain info → previous (DLL does this BEFORE computing new gains)
        // ---------------------------------------------------------------
        Array.Copy(_currGainInfo[ch], 0, _prevGainInfo[ch], 0, 17);

        // ---------------------------------------------------------------
        // Step 5: Compute subband gains (peak detection + quantization)
        // ---------------------------------------------------------------
        ComputeSubbandGains(ch);

        // ---------------------------------------------------------------
        // Step 6: Copy 2N samples to LPC work buffer, apply LPC if gain events exist
        // (DLL: cook_apply_lpc_encoder copies, then conditionally calls cook_lpc_filter)
        // ---------------------------------------------------------------
        Array.Copy(_overlapBuf[ch], 0, _lpcOutput[ch], 0, N * 2);
        if (_prevGainInfo[ch][0] != 0 || _currGainInfo[ch][0] != 0)
        {
            if (_frameCounter == 24 && ch == 0)
            {
                Console.Error.WriteLine($"LPC frame_ctr={_frameCounter} ch{ch}: prev_count={_prevGainInfo[ch][0]} curr_count={_currGainInfo[ch][0]}");
                for (int gi = 0; gi < _currGainInfo[ch][0]; gi++)
                {
                    Console.Error.WriteLine($"  curr event {gi}: sub={_currGainInfo[ch][gi + 1]} gain={_currGainInfo[ch][gi + 9]}");
                }
                // Dump energy per time-domain gain subband BEFORE LPC
                for (int gs = 0; gs < 8; gs++)
                {
                    float e1 = 0, e2 = 0;
                    for (int s = 0; s < _subbandSize; s++)
                    {
                        e1 += _lpcOutput[ch][gs * _subbandSize + s] * _lpcOutput[ch][gs * _subbandSize + s];
                        e2 += _lpcOutput[ch][N + gs * _subbandSize + s] * _lpcOutput[ch][N + gs * _subbandSize + s];
                    }
                    Console.Error.WriteLine($"  pre-LPC sub{gs}: half1_nrg={e1:E3} half2_nrg={e2:E3}");
                }
            }
            LpcFilter(_lpcOutput[ch], _prevGainInfo[ch], _currGainInfo[ch], N, -1);
            if (_frameCounter == 24 && ch == 0)
            {
                // Dump energy per time-domain gain subband AFTER LPC
                for (int gs = 0; gs < 8; gs++)
                {
                    float e1 = 0, e2 = 0;
                    for (int s = 0; s < _subbandSize; s++)
                    {
                        e1 += _lpcOutput[ch][gs * _subbandSize + s] * _lpcOutput[ch][gs * _subbandSize + s];
                        e2 += _lpcOutput[ch][N + gs * _subbandSize + s] * _lpcOutput[ch][N + gs * _subbandSize + s];
                    }
                    Console.Error.WriteLine($"  post-LPC sub{gs}: half1_nrg={e1:E3} half2_nrg={e2:E3}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Step 7: Forward MDCT on 2N work buffer
        // ---------------------------------------------------------------
        ApplyMdct(_lpcOutput[ch]);

        // Diagnostic: dump MDCT coefficients and energy for first diverging frame
        if (_frameCounter == 13)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Frame {_frameCounter} ch{ch} MDCT coeffs (first 40):");
            for (int d = 0; d < 40; d++)
            {
                sb.Append($" {_coeffs[d]:E4}");
                if ((d & 7) == 7) { sb.AppendLine(); }
            }
            sb.AppendLine($"Overlap[0..7]: {_overlapBuf[ch][0]:E4} {_overlapBuf[ch][1]:E4} {_overlapBuf[ch][2]:E4} {_overlapBuf[ch][3]:E4} {_overlapBuf[ch][4]:E4} {_overlapBuf[ch][5]:E4} {_overlapBuf[ch][6]:E4} {_overlapBuf[ch][7]:E4}");
            sb.AppendLine($"Overlap[{_frameSize*2}..+7]: {_overlapBuf[ch][_frameSize*2]:E4} {_overlapBuf[ch][_frameSize*2+1]:E4} {_overlapBuf[ch][_frameSize*2+2]:E4} {_overlapBuf[ch][_frameSize*2+3]:E4}");
            sb.AppendLine($"LPC[0..7]: {_lpcOutput[ch][0]:E4} {_lpcOutput[ch][1]:E4} {_lpcOutput[ch][2]:E4} {_lpcOutput[ch][3]:E4} {_lpcOutput[ch][4]:E4} {_lpcOutput[ch][5]:E4} {_lpcOutput[ch][6]:E4} {_lpcOutput[ch][7]:E4}");
            Console.Error.Write(sb.ToString());
        }

        _frameCounter++;

        // ---------------------------------------------------------------
        // Step 8-10: Encode bitstream
        // ---------------------------------------------------------------
        int totalBands = _totalSubbands;
        int frameBits = _frameBitsPerChannel;

        // Calculate qindex (spectral envelope) per subband
        // Use double accumulation to match x87 FPU behavior: the DLL keeps
        // the running sum in an 80-bit register across all 20 iterations,
        // only truncating to float at the end. Our SSE2 float would truncate
        // after every addition, accumulating 20 rounding errors.
        var qindex = new sbyte[CookData.MaxSubbands * 2];
        for (int i = 0; i < totalBands; i++)
        {
            double nrg = 0;
            for (int j = 0; j < BandSize; j++)
            {
                double c = _coeffs[i * BandSize + j];
                nrg += c * c;
            }
            qindex[i] = CalcQindex((float)nrg);
        }

        // Clamp qi[0] to fit in 6-bit unsigned field (qi+6 must be 0..63)
        if (qindex[0] < -6) { qindex[0] = -6; }
        if (qindex[0] > 57) { qindex[0] = 57; }

        // Backward pass: ensure forward deltas never exceed +11
        for (int i = totalBands - 2; i >= 0; i--)
        {
            if (qindex[i] < qindex[i + 1] - 11)
            {
                qindex[i] = (sbyte)(qindex[i + 1] - 11);
            }
        }
        if (qindex[0] > 57) { qindex[0] = 57; }

        // Write bitstream
        var bw = new CookBitWriter(frameBits / 8);

        // Step 8: Encode temporal gain events
        EncodeTemporalGain(bw, _currGainInfo[ch]);

        // Step 9: Spectral envelope — first qindex as 6 bits, rest as Huffman-coded deltas
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
        byte vectorBits = 5;
        int bitsAfterEnvelope = (int)bw.BitsWritten;
        int bitsLeft = frameBits - bitsAfterEnvelope - vectorBits;
        int bitsAvail = bitsLeft;

        var category = new byte[CookData.MaxSubbands * 2];
        var catIndex = new byte[127];
        BitAlloc(_frameSize, bitsLeft, vectorBits, totalBands, qindex, category, catIndex);

        // Diagnostic: show qi/cat for key frames
        if ((_frameCounter >= 24 && _frameCounter <= 30 && ch == 0) ||
            (_frameCounter == 101 && ch == 0))
        {
            int audioFrame = _frameCounter / 2;
            var sb = new System.Text.StringBuilder();
            sb.Append($"Frame {audioFrame} (ctr={_frameCounter}) ch{ch}: qi=[");
            for (int i = 0; i < totalBands; i++) { sb.Append($"{qindex[i]},"); }
            sb.Append($"] cat=[");
            for (int i = 0; i < totalBands; i++) { sb.Append($"{category[i]},"); }
            sb.Append($"] bits={bitsLeft} env={bitsAfterEnvelope}");
            Console.Error.WriteLine(sb.ToString());
        }

        // Step 10: Quantize and pack VQ codewords using real step-size quantization
        var packedBands = new PackedCoeffs[totalBands];
        ushort totalVqBits = 0;
        for (int i = 0; i < totalBands; i++)
        {
            packedBands[i] = new PackedCoeffs();
            float gain = _hpowTab[Math.Clamp(64 + qindex[i], 0, 127)];
            totalVqBits += packedBands[i].Pack(_coeffs, i * BandSize, BandSize, category[i], gain);
        }

        // Correction: reduce categories that overshoot the actual VQ budget
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
            float gain = _hpowTab[Math.Clamp(64 + qindex[idx], 0, 127)];
            int newCat = Math.Min(pband.Cat + 1, CookData.NumCategories - 1);
            totalVqBits -= pband.NBits;
            pband.Pack(_coeffs, idx * BandSize, BandSize, newCat, gain);
            totalVqBits += pband.NBits;
            bandsCorrected++;
        }

        // Write vector_bits correction count
        bw.Write((uint)bandsCorrected, vectorBits);

        // Write VQ codewords + signs
        ushort remaining = vqBudget;
        for (int i = 0; i < totalBands; i++)
        {
            packedBands[i].Write(bw, remaining);
            remaining = (ushort)Math.Max(0, remaining - packedBands[i].NBits);
            if (remaining == 0)
            {
                break;
            }
        }

        // Pad to frame boundary
        bw.PadToEnd();

        // XOR scramble per-channel
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

    /// <summary>
    /// Forward MDCT matching DLL cook_mdct_forward pipeline:
    /// prewindow → fft_prerotate → fft_compute → fft_postrotate.
    /// </summary>
    private void ApplyMdct(float[] input2N)
    {
        int N = _frameSize;
        int halfN = N / 2;

        // =============================================================
        // Step 1: Prewindow — fold 2N input into N using window table.
        // Matches DLL cook_mdct_prewindow exactly.
        // Q1=[0..N/2-1], Q2=[N/2..N-1], Q3=[N..3N/2-1], Q4=[3N/2..2N-1]
        // =============================================================
        // All MDCT stages use double intermediates to match x87 FPU behavior
        // (DLL's 32-bit x86 code loads float → 80-bit register, computes in
        // double precision per MSVC default, stores back to float).
        for (int j = 0; j < halfN; j++)
        {
            // First half: Q2 forward + Q1 reversed
            _mdctTmp[j] = (float)((double)input2N[halfN + j] * _window[halfN + j]
                        + (double)input2N[halfN - 1 - j] * _window[halfN - 1 - j]);

            // Second half (reversed): Q3 reversed * win - Q4 forward * win
            _mdctTmp[N - 1 - j] = (float)((double)_window[halfN + j] * input2N[halfN + N - 1 - j]
                                 - (double)_window[halfN - 1 - j] * input2N[halfN + N + j]);
        }

        // =============================================================
        // Step 2: FFT prerotate — N real → N/2 interleaved complex.
        // Pairs samples from opposite ends with twiddle factors.
        // Matches DLL cook_fft_prerotate.
        // =============================================================
        // Use _mdctTmp[N..2N-1] as FFT workspace (N floats = N/2 complex)
        int fftOff = N; // offset into _mdctTmp for FFT data
        for (int i = 0; i < halfN; i++)
        {
            double xHead = _mdctTmp[i * 2];
            double xTail = _mdctTmp[N - 1 - i * 2];
            double cosV = _preRotCos[i];
            double sinV = _preRotSin[i];
            _mdctTmp[fftOff + i * 2] = (float)(sinV * xTail + cosV * xHead);
            _mdctTmp[fftOff + i * 2 + 1] = (float)(cosV * xTail - sinV * xHead);
        }

        // =============================================================
        // Step 3: Split-radix DIF FFT on N/2 complex points.
        // Matches DLL cook_fft_compute exactly: radix-4 first pass,
        // then radix-2 stages with precomputed twiddles, then final
        // radix-2 pass. Output is in bit-reversed order.
        // =============================================================
        {
            int fftN = _fftSize; // N/2 complex points

            // Phase 1: Radix-4 butterfly (no twiddles)
            // 4 pointers at data[0], data[N/4], data[N/2], data[3N/4] (complex)
            int ip0 = fftOff;
            int ip1 = fftOff + (fftN >> 1);
            int ip2 = fftOff + fftN;
            int ip3 = fftOff + (fftN >> 1) + fftN;

            // All butterfly arithmetic uses double intermediates to match
            // x87 FPU behavior (DLL loads float → 80-bit register, computes
            // in double precision per MSVC default, stores back to float).
            for (int i = 0; i < (fftN >> 2); i++)
            {
                double cr = _mdctTmp[ip2];
                double ar = _mdctTmp[ip0];
                double ci = _mdctTmp[ip2 + 1];
                double di = _mdctTmp[ip3 + 1];
                double dr = _mdctTmp[ip3];
                double ai = _mdctTmp[ip0 + 1];
                double br = _mdctTmp[ip1];
                double sacim = ci + ai;
                double bi = _mdctTmp[ip1 + 1];
                double sbdre = dr + br;
                double sbdim = di + bi;
                double sacre = cr + ar;

                _mdctTmp[ip0] = (float)(sacre + sbdre);
                _mdctTmp[ip0 + 1] = (float)(sbdim + sacim);

                ai -= ci;
                bi -= di;
                br -= dr;
                ar -= cr;

                _mdctTmp[ip1] = (float)(sacre - sbdre);
                _mdctTmp[ip1 + 1] = (float)(sacim - sbdim);
                _mdctTmp[ip2] = (float)(bi + ar);
                _mdctTmp[ip2 + 1] = (float)(ai - br);
                _mdctTmp[ip3] = (float)(ar - bi);
                _mdctTmp[ip3 + 1] = (float)(br + ai);

                ip0 += 2;
                ip1 += 2;
                ip2 += 2;
                ip3 += 2;
            }

            // Phase 2: Radix-2 stages with precomputed twiddles
            int numGroups = 4;
            int sz = fftN >> 3;

            for (int stage = 0; stage < _fftLogN - 3; stage++)
            {
                ip2 = fftOff;
                ip3 = fftOff;
                int twIdx = 0;

                for (int g = 0; g < numGroups; g++)
                {
                    ip3 = ip2 + sz * 2;
                    double wcr = _fftTwiddles[twIdx];
                    double wci = _fftTwiddles[twIdx + 1];

                    for (int bf = 0; bf < sz; bf++)
                    {
                        int savP3im = ip3 + 1;
                        double a_re = _mdctTmp[ip2];
                        double t_re = wci * _mdctTmp[ip3 + 1] + wcr * _mdctTmp[ip3];
                        double a_im = _mdctTmp[ip2 + 1];
                        double t_im = wci * _mdctTmp[ip3] - wcr * _mdctTmp[ip3 + 1];

                        _mdctTmp[ip2] = (float)(t_re + a_re);
                        _mdctTmp[ip3] = (float)(a_re - t_re);
                        _mdctTmp[ip2 + 1] = (float)(a_im - t_im);
                        ip2 += 2;
                        ip3 += 2;
                        _mdctTmp[savP3im] = (float)(t_im + a_im);
                    }

                    ip2 += sz * 2;
                    twIdx += 2;
                }

                sz >>= 1;
                numGroups <<= 1;
            }

            // Phase 3: Final radix-2 pass with twiddles
            int tw3 = 0;
            ip2 = fftOff + 2;

            for (int i = 0; i < (fftN >> 1); i++)
            {
                double wcr = _fftTwiddles[tw3];
                double wci = _fftTwiddles[tw3 + 1];
                tw3 += 2;

                double a_re = _mdctTmp[ip2 - 2];
                double a_im = _mdctTmp[ip2 - 1];
                double t_re = wci * _mdctTmp[ip2 + 1] + wcr * _mdctTmp[ip2];
                double t_im = wci * _mdctTmp[ip2] - wcr * _mdctTmp[ip2 + 1];

                _mdctTmp[ip2 - 2] = (float)(t_re + a_re);
                _mdctTmp[ip2 - 1] = (float)(a_im - t_im);
                _mdctTmp[ip2] = (float)(a_re - t_re);
                _mdctTmp[ip2 + 1] = (float)(t_im + a_im);

                ip2 += 4;
            }
        }

        // =============================================================
        // Step 4: FFT postrotate — extract N real MDCT coefficients.
        // DIF FFT outputs in bit-reversed order, so we unscramble via
        // _bitRevTable, matching DLL cook_fft_postrotate exactly.
        // =============================================================
        for (int i = 0; i < halfN; i++)
        {
            int bri = _bitRevTable[i];
            double re = _mdctTmp[fftOff + bri * 2];
            double im = _mdctTmp[fftOff + bri * 2 + 1];
            double cosV = _postRotTwiddle[i];
            double sinV = _postRotTwiddle[halfN - i];

            _coeffs[i * 2] = (float)(sinV * im + cosV * re);
            _coeffs[N - 1 - i * 2] = (float)(sinV * re - cosV * im);
        }
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

    /// <summary>
    /// Compute power index (qindex) for a subband's energy.
    /// Matches DLL cook_compute_power_index exactly (verified from binary at 633f5040-633f5050):
    ///   633f5040 = 0.0  (initial sum, no epsilon)
    ///   633f5044 = 0.05 (scale factor — multiply, not add!)
    ///   633f5048 = 1.0  (threshold for conditional)
    ///   633f504c = sqrt(2) (multiplier when scaled > 1.0)
    ///   633f5050 = sqrt(0.5) (default multiplier when scaled &lt;= 1.0)
    /// </summary>
    private static sbyte CalcQindex(float nrg)
    {
        // DLL: sum starts at 0.0 (no epsilon), nrg is already the sum of coeff^2
        // Then: scaled = sum * 0.05
        float scaled = nrg * 0.05f;

        // DLL: default multiplier = sqrt(0.5); if (1.0 < scaled) multiplier = sqrt(2)
        float val;
        if (scaled > 1.0f)
        {
            val = scaled * 1.4142135f; // sqrt(2) — boost large values
        }
        else
        {
            val = scaled * 0.70710678f; // sqrt(0.5) — shrink small values
        }

        // Handle zero/denorm: for silence, nrg=0 → val=0 → exponent is -127, clamp to -31
        if (val <= 0f)
        {
            return -31;
        }

        // DLL: extract IEEE 754 exponent — fast floor(log2)
        int bits = BitConverter.SingleToInt32Bits(val);
        int exponent = (bits >> 23) - 127;

        // DLL: power_value - (power_value >> 31) — ceiling for negative values
        exponent -= (exponent >> 31);

        return (sbyte)Math.Clamp(exponent, -31, 63);
    }

    // ===================================================================
    // Cascaded biquad IIR filter (Direct Form II Transposed)
    // Matches cokr3260.dll cook_apply_filter_coeffs exactly.
    // Each section: y[n] = b0*x + b1*x1 + b2*x2 - a1*y1 - a2*y2
    // ===================================================================

    private static void ApplyFilterCoeffs(float[] signal, int sigOffset,
        float[] filterState, int stateOffset,
        float[] filterCoeffs, int coeffOffset,
        int numSections, int numSamples)
    {
        // Use double intermediates to match x87 FPU behavior: the DLL keeps
        // all intermediate values in 80-bit FPU registers. The IIR feedback
        // loop is especially sensitive to precision accumulation.
        for (int section = 0; section < numSections; section++)
        {
            int co = coeffOffset + section * 5;
            int so = stateOffset + section * 4;
            double b0 = filterCoeffs[co];
            double b1 = filterCoeffs[co + 1];
            double b2 = filterCoeffs[co + 2];
            double a1 = filterCoeffs[co + 3];
            double a2 = filterCoeffs[co + 4];

            for (int i = 0; i < numSamples; i++)
            {
                double x = signal[sigOffset + i];
                double x1 = filterState[so];
                double x2 = filterState[so + 1];
                double y1 = filterState[so + 2];
                double y2 = filterState[so + 3];

                filterState[so + 1] = (float)x1;
                double y = (b2 * x2 + x * b0 + x1 * b1) - a1 * y1 - y2 * a2;
                filterState[so] = (float)x;
                filterState[so + 3] = (float)y1;
                filterState[so + 2] = (float)y;
                signal[sigOffset + i] = (float)y;
            }
        }
    }

    // ===================================================================
    // Subband gain computation (cook_compute_subband_gains)
    // Detects transients by comparing peak amplitudes across 8 subbands
    // between windowed previous frame and current frame data.
    // ===================================================================

    private void ComputeSubbandGains(int ch)
    {
        int N = _frameSize;
        int srcOffset = N + _subbandSize;

        // Copy overlap region for windowed peak measurement
        var windowedBuf = new float[N];
        Array.Copy(_overlapBuf[ch], srcOffset, windowedBuf, 0, N);

        // Apply 2-section biquad lowpass window to the copy
        ApplyFilterCoeffs(windowedBuf, 0, _windowFilterState[ch], 0,
            CookData.WindowFilterCoeffs, 0, 2, N);

        // Save current gain history
        float savedGain0 = _gainHistory[ch][0];
        float savedGain1 = _gainHistory[ch][1];
        float savedGain2 = _gainHistory[ch][2];
        float savedGain3 = _gainHistory[ch][3];

        // Find peak amplitudes per subband (8 subbands)
        var currPeaks = new float[10]; // extra room for quantize
        var prevPeaks = new float[10];
        int overlapIdx = srcOffset;
        int windowIdx = 0;

        for (int sub = 0; sub < 8; sub++)
        {
            currPeaks[sub] = 1.0f;
            prevPeaks[sub] = 1.0f;

            for (int s = 0; s < _subbandSize; s++)
            {
                float ov = _overlapBuf[ch][overlapIdx++];
                float wi = windowedBuf[windowIdx++];

                float absOv = MathF.Abs(ov);
                if (currPeaks[sub] < absOv) { currPeaks[sub] = absOv; }
                float absWi = MathF.Abs(wi);
                if (prevPeaks[sub] < absWi) { prevPeaks[sub] = absWi; }
            }
        }

        // Update gain history — stores peaks from subbands 6 & 7
        _gainHistory[ch][0] = currPeaks[6];
        _gainHistory[ch][1] = currPeaks[7];
        _gainHistory[ch][2] = prevPeaks[6];
        _gainHistory[ch][3] = prevPeaks[7];

        // Build 10-element peak arrays for QuantizeGains:
        // [savedGain0, savedGain1, currPeaks[0..7]] and [savedGain2, savedGain3, prevPeaks[0..7]]
        var currSet = new float[10];
        currSet[0] = savedGain0;
        currSet[1] = savedGain1;
        Array.Copy(currPeaks, 0, currSet, 2, 8);

        var prevSet = new float[10];
        prevSet[0] = savedGain2;
        prevSet[1] = savedGain3;
        Array.Copy(prevPeaks, 0, prevSet, 2, 8);

        QuantizeGains(currSet, prevSet, _currGainInfo[ch]);
    }

    // ===================================================================
    // Gain quantization (cook_quantize_gains)
    // Detects gain transitions by comparing log2 peaks across subbands.
    // Output: gainInfo[0]=count, [1..8]=subband indices, [9..16]=gain values
    // ===================================================================

    private static void QuantizeGains(float[] currPeaks, float[] prevPeaks, int[] outGainInfo)
    {
        // Compute log2 of peak ratios (IEEE 754 exponent extraction)
        // DLL normalizes each peak by peaks[8] (= subband 6 peak)
        var currLog = new int[10];
        var prevLog = new int[10];

        for (int i = 0; i < 10; i++)
        {
            float ratio = currPeaks[i] / Math.Max(currPeaks[8], 1e-30f);
            int bits = BitConverter.SingleToInt32Bits(ratio);
            int exp = ((bits >> 23) & 0xFF) - 127;
            exp = exp - (exp >> 31);
            currLog[i] = Math.Clamp(exp, -7, 4);
        }

        for (int i = 0; i < 10; i++)
        {
            float ratio = prevPeaks[i] / Math.Max(prevPeaks[8], 1e-30f);
            int bits = BitConverter.SingleToInt32Bits(ratio);
            int exp = ((bits >> 23) & 0xFF) - 127;
            exp = exp - (exp >> 31);
            prevLog[i] = exp; // DLL does NOT clamp prevLog
        }

        outGainInfo[0] = 0;

        for (int i = 0; i < 8; i++)
        {
            int prev = currLog[i];
            int curr = currLog[i + 1];
            int next = currLog[i + 2];

            // Condition 1: ascending current pattern OR sharp-drop pattern,
            // with ascending previous pattern (DLL: cook_quantize_gains)
            if (((prev < curr && curr < next) ||
                 (curr - next < -1 && prev < next)) &&
                prevLog[i] < prevLog[i + 2] &&
                prevLog[i + 1] <= prevLog[i + 2])
            {
                int count = outGainInfo[0];
                int gainVal = (curr + prev) / 2;
                if (count == 0 || outGainInfo[count + 8] != gainVal)
                {
                    outGainInfo[0] = count + 1;
                }
                outGainInfo[outGainInfo[0]] = i;
                outGainInfo[outGainInfo[0] + 8] = gainVal;
            }

            if (outGainInfo[0] >= 8)
            {
                break;
            }

            // Condition 2: descending current pattern with descending previous pattern
            if (curr < prev && next <= curr &&
                prevLog[i + 2] < prevLog[i] &&
                prevLog[i + 2] <= prevLog[i + 1])
            {
                int count = outGainInfo[0];
                int gainVal = (curr + prev) / 2;
                if (count == 0 || outGainInfo[count + 8] != gainVal)
                {
                    outGainInfo[0] = count + 1;
                }
                outGainInfo[outGainInfo[0]] = i;
                outGainInfo[outGainInfo[0] + 8] = gainVal;
            }

            if (outGainInfo[0] >= 8)
            {
                break;
            }
        }

        // Trim trailing zero-gain events
        while (outGainInfo[0] > 0 && outGainInfo[outGainInfo[0] + 8] == 0)
        {
            outGainInfo[0]--;
        }
    }

    // ===================================================================
    // LPC gain filter (cook_lpc_filter)
    // Applies per-subband gain compensation. Direction: -1 for encoder
    // (analysis), +1 for decoder (synthesis).
    // ===================================================================

    private void LpcFilter(float[] buffer, int[] prevGainInfo, int[] currGainInfo,
        int samplesPerChannel, int direction)
    {
        // Build gain schedule for current frame (second half of MDCT window)
        var gainSchedule = new int[9];
        int count = currGainInfo[0];
        int scanIdx = count;
        for (int sub = 7; sub >= 0; sub--)
        {
            if (count > 0 && sub == currGainInfo[scanIdx])
            {
                gainSchedule[sub] = currGainInfo[scanIdx + 8] * direction;
                scanIdx--;
                count--;
            }
            else
            {
                gainSchedule[sub] = (sub < 7) ? gainSchedule[sub + 1] : 0;
            }
        }

        // Apply gains to second half (current frame's coefficients)
        // DLL: cook_apply_subband_gain(buf+spc, gs[sub], gs[sub+1]) where gs[8]=0
        int offset = samplesPerChannel;
        for (int sub = 0; sub < 8; sub++)
        {
            ApplySubbandGain(buffer, offset, gainSchedule[sub], gainSchedule[sub + 1]);
            offset += _subbandSize;
        }

        // Build gain schedule for previous frame (first half)
        var prevSchedule = new int[9];
        count = prevGainInfo[0];
        scanIdx = count;
        for (int sub = 7; sub >= 0; sub--)
        {
            if (count > 0 && sub == prevGainInfo[scanIdx])
            {
                prevSchedule[sub] = prevGainInfo[scanIdx + 8] * direction;
                scanIdx--;
                count--;
            }
            else
            {
                prevSchedule[sub] = (sub < 7) ? prevSchedule[sub + 1] : 0;
            }
        }

        // Apply gains to first half with offset from prevSchedule[0]
        // DLL reuses the same array, so gain_schedule[0] IS prevSchedule[0] at this point
        offset = 0;
        for (int sub = 0; sub < 8; sub++)
        {
            int prevG = prevSchedule[0] + prevSchedule[sub];
            int currG = prevSchedule[0] + prevSchedule[sub + 1];
            ApplySubbandGain(buffer, offset, prevG, currG);
            offset += _subbandSize;
        }
    }

    // ===================================================================
    // Apply gain to one subband (cook_apply_subband_gain)
    // If prev == curr: constant gain. Otherwise: geometric interpolation.
    // ===================================================================

    private void ApplySubbandGain(float[] buffer, int offset, int prevGainIdx, int currGainIdx)
    {
        // gain = 2^gainIndex
        float gain = MathF.Pow(2.0f, prevGainIdx);

        if (prevGainIdx == currGainIdx)
        {
            for (int i = 0; i < _subbandSize; i++)
            {
                buffer[offset + i] *= gain;
            }
        }
        else
        {
            // Geometric ramp from prev to curr gain
            int delta = currGainIdx - prevGainIdx;
            int stepIdx = delta + 11; // center of 23-entry GainPowBase table
            float stepRatio = (stepIdx >= 0 && stepIdx < _gainStepRatios.Length)
                ? _gainStepRatios[stepIdx]
                : MathF.Pow(2.0f, (float)delta / _subbandSize);

            for (int i = 0; i < _subbandSize; i++)
            {
                buffer[offset + i] *= gain;
                gain *= stepRatio;
            }
        }
    }

    // ===================================================================
    // Temporal gain encoding (cook_encode_envelope for gain events)
    // Writes gain event count as unary code, then per-event data.
    // ===================================================================

    private static void EncodeTemporalGain(CookBitWriter bw, int[] gainInfo)
    {
        int count = gainInfo[0];
        if (count < 0 || count > 8) { count = 0; }

        // Write gain count as unary code (matching DLL's cook_encode_envelope)
        bw.Write((uint)CookData.GainFirstCode[count], CookData.GainFirstBits[count]);

        // Write per-event: [3-bit subband_index] then [1-bit or 5-bit gain_value]
        for (int i = 0; i < count; i++)
        {
            // 3-bit subband index (gainInfo[1..8])
            int subIdx = gainInfo[i + 1];
            bw.Write((uint)(subIdx & 7), 3);

            // Gain value (gainInfo[9..16]): -1 = no change, else (val+7)|0x10
            int gainVal = gainInfo[i + 9];
            if (gainVal == -1)
            {
                bw.Write(0, 1);
            }
            else
            {
                bw.Write((uint)((gainVal + 7) | 0x10), 5);
            }
        }
    }

    // ===================================================================
    // Packed VQ coefficients — uses real encoder's linear step-size quantization
    // index = clamp(round(|coeff| / (stepSize * gain) + offset), 0, maxIndex)
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

        public ushort Pack(float[] allCoeffs, int coeffOffset, int bandSize, int cat, float gain)
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
            int maxIdx = CookData.VqMaxIndex[cat];
            float stepSize = CookData.VqStepSize[cat];
            float offset = CookData.VqQuantOffset[cat];
            float invStepGain = (gain > 1e-30f) ? 1.0f / (stepSize * gain) : 0f;
            Num = CookData.NumVqGroups[cat];

            for (int groupNo = 0; groupNo < Num; groupNo++)
            {
                int groupStart = coeffOffset + groupNo * groupSize;
                int cw = 0;
                Span<int> cvals = stackalloc int[5];
                Span<int> sarr = stackalloc int[5];

                for (int k = 0; k < groupSize; k++)
                {
                    int ci = groupStart + k;
                    float el = ci < allCoeffs.Length ? allCoeffs[ci] : 0;

                    // Real encoder quantization: index = round(|coeff| / (step * gain) + offset)
                    int curVal = (int)MathF.Round(MathF.Abs(el) * invStepGain + offset);
                    curVal = Math.Clamp(curVal, 0, maxIdx);

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

                // Collect sign bits for non-zero coefficients (forward order)
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
