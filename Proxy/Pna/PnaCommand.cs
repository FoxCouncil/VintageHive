// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;

namespace VintageHive.Proxy.Pna;

internal static class PnaCommand
{
    // ===================================================================
    // PNA protocol constants (from xine-lib stream/pnm.c RE)
    // ===================================================================

    /// <summary>PNA server response chunk type tag: "PNA\0" (big-endian).</summary>
    public const uint PNA_TAG = 0x504E4100;

    // Sub-chunk IDs used in pnm_write_chunk format: [id(2,BE)][len(2,BE)][data]
    public const ushort CHUNK_CLIENT_CAPS      = 0x0003;
    public const ushort CHUNK_CLIENT_CHALLANGE = 0x0004;
    public const ushort CHUNK_BANDWIDTH        = 0x0005;
    public const ushort CHUNK_GUID             = 0x0013;
    public const ushort CHUNK_TIMESTAMP        = 0x0017;
    public const ushort CHUNK_TWENTYFOUR       = 0x0018;

    // 1-byte tag markers (different framing from 2-byte chunk IDs)
    public const byte TAG_CLIENT_STRING  = 0x63;
    public const byte TAG_PATH_REQUEST   = 0x52;

    // PNA stream data boundary marker
    public const byte PNA_STREAM_MARKER = 0x5A;

    // ===================================================================
    // RealMedia container FourCC tags (big-endian)
    // ===================================================================

    public const uint RM_RMF_TAG  = 0x2E524D46; // ".RMF"
    public const uint RM_PROP_TAG = 0x50524F50; // "PROP"
    public const uint RM_MDPR_TAG = 0x4D445052; // "MDPR"
    public const uint RM_CONT_TAG = 0x434F4E54; // "CONT"
    public const uint RM_DATA_TAG = 0x44415441; // "DATA"

    // ===================================================================
    // PNA_TAG server response builder
    // ===================================================================

    /// <summary>
    /// Build the PNA_TAG server response (12 bytes).
    ///
    /// Wire format (same structure as client hello preamble):
    ///   Bytes 0-3: PNA\0 magic (0x504E4100)
    ///   Byte  4:   Server PNA version (0x0A = PNA v10)
    ///   Bytes 5-7: Capability/padding fields
    ///   Bytes 8+:  Body opcodes parsed by pnm_get_chunk's PNA_TAG handler
    ///
    /// For xine-lib: bytes 0-3 → chunk_type = PNA_TAG, bytes 4-7 → chunk_size
    ///   (ignored for PNA_TAG; body is parsed dynamically via opcodes).
    /// For commercial clients (RA 3.0, RP Plus): byte 4 is the server version.
    ///   If byte 4 = 0x00, RA 3.0 reports "server too old" (Error 36).
    ///
    /// Body (4 bytes): initial(1) + opcode_pair(2) + trailing(1).
    ///   All zeros → no opcodes, no 'i' (challenge response not requested).
    /// </summary>
    /// <summary>
    /// Build the PNA_TAG server response (12 bytes, minimal — no 'i' opcode).
    ///
    /// Response: PNA\0(4) + echoed_fields(4) + body(4) = 12 bytes.
    ///
    /// For xine-lib: bytes 0-3 = chunk_type (PNA_TAG), bytes 4-7 = chunk_size (ignored).
    ///   Body: initial(0x00) + opcode_pair(0x00,0x00) → break + trailing(0x00).
    /// For commercial clients: bytes 0-3 = PNA\0, byte 4 = server version (must match client).
    /// </summary>
    public static byte[] BuildPnaTagResponse(byte[] clientHeaderFields)
    {
        var response = new byte[12];

        // Bytes 0-3: "PNA\0" tag
        BinaryPrimitives.WriteUInt32BigEndian(response, PNA_TAG);

        // Bytes 4-7: Echo client's version + protocol fields
        Buffer.BlockCopy(clientHeaderFields, 0, response, 4, 4);

        // Bytes 8-11: body (all zeros = no opcodes, no 'i')
        return response;
    }

    // ===================================================================
    // PNA stream packet builder (RM data → 0x5a-framed PNA)
    // ===================================================================

    /// <summary>
    /// Convert a raw RM data packet to a PNA 0x5a-framed stream packet.
    ///
    /// RM data packet: [version(2)][length(2)][stream_num(2)][timestamp(4)][reserved(1)][flags(1)][audio...]
    /// PNA stream header (8 bytes): [0x5a][fof1(2,BE)][fof2(2,BE)][seq(2,BE)][0x5a]
    /// PNA stream data (fof1-5 bytes): [index2(1)][timestamp(4)][reserved(1)][flags(1)][audio...]
    ///
    /// fof1 = fof2 = RM length field (bytes 2-3, which equals total RM packet size).
    /// Data = RM packet bytes 5 through end (skip version + length + stream_num_high).
    /// First data byte (index2) is overridden with an incrementing counter.
    ///
    /// On the client side, pnm_get_stream_chunk reconstructs the RM packet by
    /// prepending version(2) + length(2) + stream_num_high(1) and overwriting
    /// index2 with the calculated stream number.
    /// </summary>
    public static byte[] BuildPnaStreamPacket(byte[] rmPacket, ushort seq, byte index2)
    {
        if (rmPacket.Length < 6)
        {
            return null;
        }

        // fof1 = fof2 = RM packet length field (total packet size including 4-byte header)
        ushort rmLength = BinaryPrimitives.ReadUInt16BigEndian(rmPacket.AsSpan(2));

        int dataOffset = 5; // skip version(2) + length(2) + stream_num_high(1)
        int dataLen = rmPacket.Length - dataOffset;

        if (dataLen <= 0)
        {
            return null;
        }

        var packet = new byte[8 + dataLen];

        // 8-byte PNA stream header
        packet[0] = PNA_STREAM_MARKER;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), rmLength);  // fof1
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(3), rmLength);  // fof2
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(5), seq);       // first index
        packet[7] = PNA_STREAM_MARKER;

        // Data: RM bytes 5+ with index2 override
        Buffer.BlockCopy(rmPacket, dataOffset, packet, 8, dataLen);
        packet[8] = index2; // Override first data byte with incrementing index2

        return packet;
    }

    // ===================================================================
    // Challenge data for pnm_get_headers
    // ===================================================================

    /// <summary>
    /// Build the 72-byte challenge block sent after RM header chunks.
    ///
    /// pnm_get_headers reads chunks until chunk_type == 0, then reads 64 more
    /// bytes of challenge data. The "chunk_type == 0" happens when pnm_get_chunk
    /// reads 8 bytes (3+5) that don't match any known tag.
    ///
    /// Total: 8 bytes (fake preamble → unknown type → breaks loop) + 64 bytes.
    /// First byte must NOT be 0x72 (would trigger checksum framing path).
    /// </summary>
    public static byte[] BuildChallengeBlock()
    {
        // 72 bytes total: all zeros works fine.
        // Byte 0 = 0x00 (not 0x72, so client takes the 3+5 path).
        // pnm_get_chunk assembles 8-byte preamble → chunk_type = 0x00000000 (unknown) → returns.
        // pnm_get_headers sees chunk_type=0 → breaks header loop.
        // Then reads 64 more bytes → our remaining zeros.
        return new byte[72];
    }

    // ===================================================================
    // Client request parsing
    // ===================================================================

    /// <summary>
    /// Parsed result from a PNA client hello request.
    /// </summary>
    public class ClientRequest
    {
        public string ClientString { get; set; }
        public string PathRequest { get; set; }
        public byte[] Challenge { get; set; }
        public uint Bandwidth { get; set; }

        /// <summary>PNA protocol version from byte 4 of the client hello (0x08 = RA 3.0, 0x0A = RP Plus 6.x).</summary>
        public byte PnaVersion { get; set; }

        /// <summary>Raw bytes 4-7 from the client hello header (version + protocol fields).
        /// Echoed back in the server PNA_TAG response — commercial clients validate these.</summary>
        public byte[] PnaHeaderFields { get; set; }
    }

    /// <summary>
    /// Parse a PNA client hello request.
    ///
    /// Format:
    ///   [PNA header (11 bytes)]
    ///   [2-byte chunk ID sub-chunks: id(2,BE) + len(2,BE) + data]
    ///   [after_chunks (6 bytes)]
    ///   [CLIENT_STRING: 0x63 + len(2,BE) + string+null]
    ///   [PATH_REQUEST: 0x00 + 0x52 + len(2,BE) + path]
    ///   ['y','B']
    /// </summary>
    public static ClientRequest ParseClientRequest(byte[] data, int length)
    {
        var result = new ClientRequest();

        if (length < 11)
        {
            return result;
        }

        // Verify PNA header: first 3 bytes should be "PNA"
        if (data[0] != 0x50 || data[1] != 0x4E || data[2] != 0x41)
        {
            return result;
        }

        // Byte 4: PNA protocol version (0x08 = RA 3.0, 0x0A = RP Plus 6.x / xine-lib)
        result.PnaVersion = data[4];

        // Bytes 4-7: version + protocol fields — echoed in server PNA_TAG response.
        // Commercial clients validate these; values are version-specific.
        result.PnaHeaderFields = new byte[4];
        Buffer.BlockCopy(data, 4, result.PnaHeaderFields, 0, 4);

        // Scan for CLIENT_STRING tag (0x63) — 1-byte tag format.
        // Must be careful: 0x63 ('c') can appear in codec names and sub-chunk data.
        // Require strLen >= 10 to avoid false positives — all known client strings
        // are 30+ chars (e.g. "WinNT_5.1_6.0.6.99_plus32_SP60_en-US_686").
        for (int i = 11; i < length - 3; i++)
        {
            if (data[i] != TAG_CLIENT_STRING) continue;

            int strLen = (data[i + 1] << 8) | data[i + 2];
            if (strLen < 10 || strLen > 256 || i + 3 + strLen > length) continue;

            result.ClientString = Encoding.ASCII.GetString(data, i + 3, strLen).TrimEnd('\0');
            break;
        }

        // Scan for PATH_REQUEST: 0x00 + 0x52 + len(2,BE) + path
        for (int i = 11; i < length - 4; i++)
        {
            if (data[i] != 0x00 || data[i + 1] != TAG_PATH_REQUEST) continue;

            int pathLen = (data[i + 2] << 8) | data[i + 3];
            if (pathLen <= 0 || pathLen > 1024 || i + 4 + pathLen > length) continue;

            result.PathRequest = Encoding.ASCII.GetString(data, i + 4, pathLen).TrimEnd('\0');
            break;
        }

        // Scan for BANDWIDTH chunk (2-byte ID = 0x0005)
        for (int i = 11; i < length - 8; i++)
        {
            ushort chunkId = (ushort)((data[i] << 8) | data[i + 1]);
            if (chunkId != CHUNK_BANDWIDTH) continue;

            int chunkLen = (data[i + 2] << 8) | data[i + 3];
            if (chunkLen >= 4 && i + 4 + chunkLen <= length)
            {
                result.Bandwidth = BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(i + 4, 4));
            }
            break;
        }

        // Scan for CHALLENGE chunk (2-byte ID = 0x0004)
        for (int i = 11; i < length - 4; i++)
        {
            ushort chunkId = (ushort)((data[i] << 8) | data[i + 1]);
            if (chunkId != CHUNK_CLIENT_CHALLANGE) continue;

            int chunkLen = (data[i + 2] << 8) | data[i + 3];
            if (chunkLen > 0 && chunkLen <= 256 && i + 4 + chunkLen <= length)
            {
                result.Challenge = new byte[chunkLen];
                Buffer.BlockCopy(data, i + 4, result.Challenge, 0, chunkLen);
            }
            break;
        }

        return result;
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    public static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0) return false;
            totalRead += read;
        }
        return true;
    }

    public static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count)
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

    // ===================================================================
    // Hex dump for reverse-engineering / protocol logging
    // ===================================================================

    public static string HexDump(byte[] data, int offset, int length, int maxBytes = 256)
    {
        var sb = new StringBuilder();
        int end = Math.Min(offset + length, data.Length);
        int count = Math.Min(end - offset, maxBytes);

        for (int i = 0; i < count; i += 16)
        {
            sb.Append($"  {i:X4}: ");

            // Hex bytes
            for (int j = 0; j < 16; j++)
            {
                if (i + j < count)
                {
                    sb.Append($"{data[offset + i + j]:X2} ");
                }
                else
                {
                    sb.Append("   ");
                }
            }

            sb.Append(" | ");

            // ASCII
            for (int j = 0; j < 16 && i + j < count; j++)
            {
                byte b = data[offset + i + j];
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }

            sb.AppendLine();
        }

        if (count < end - offset)
        {
            sb.AppendLine($"  ... ({end - offset - count} more bytes)");
        }

        return sb.ToString();
    }

    public static string HexDump(byte[] data) => HexDump(data, 0, data.Length);

    // ===================================================================
    // RealMedia container chunk reading (from FFmpeg rm muxer output)
    // ===================================================================

    /// <summary>
    /// Read one RealMedia container chunk: [tag(4)] [size(4, BE)] [data(size-8)].
    /// Returns (tag, chunkBytes) where chunkBytes includes the full chunk (tag + size + data).
    /// Returns (0, null) on EOF.
    /// </summary>
    public static async Task<(uint tag, byte[] chunk)> ReadRmChunkAsync(Stream stream)
    {
        var header = new byte[8];
        if (await ReadExactAsync(stream, header, 0, 8) < 8)
        {
            return (0, null);
        }

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(header);
        uint size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));

        if (size < 8 || size > 0x100000) // sanity: max 1MB per chunk
        {
            return (tag, header);
        }

        var chunk = new byte[size];
        Buffer.BlockCopy(header, 0, chunk, 0, 8);

        int remaining = (int)size - 8;
        if (remaining > 0)
        {
            if (await ReadExactAsync(stream, chunk, 8, remaining) < remaining)
            {
                return (tag, chunk);
            }
        }

        return (tag, chunk);
    }

    /// <summary>
    /// Read one RealMedia data packet from the DATA section.
    /// Format: [version(2, BE)] [length(2, BE)] [rest(length-4)]
    /// Returns the complete packet bytes, or null on EOF.
    /// </summary>
    public static async Task<byte[]> ReadRmDataPacketAsync(Stream stream)
    {
        var header = new byte[4];
        if (await ReadExactAsync(stream, header, 0, 4) < 4)
        {
            return null;
        }

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(header);
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));

        if (length < 4)
        {
            return null;
        }

        var packet = new byte[length];
        Buffer.BlockCopy(header, 0, packet, 0, 4);

        int remaining = length - 4;
        if (remaining > 0)
        {
            if (await ReadExactAsync(stream, packet, 4, remaining) < remaining)
            {
                return null;
            }
        }

        return packet;
    }

    public static string FourCCToString(uint tag)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, tag);
        return Encoding.ASCII.GetString(bytes);
    }
}
