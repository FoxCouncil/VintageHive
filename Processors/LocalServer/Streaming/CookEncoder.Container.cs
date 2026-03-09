// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
// Cook encoder — RealMedia container construction and GENR interleaving

using System.Buffers.Binary;
using System.Text;

namespace VintageHive.Processors.LocalServer.Streaming;

internal partial class CookEncoder
{
    // ===================================================================
    // GENR interleave group builder
    // ===================================================================

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

    // ===================================================================
    // .ra5 header and extradata
    // ===================================================================

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

    // ===================================================================
    // Full RM container builders
    // ===================================================================

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
    // RM container chunk writers
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
}
