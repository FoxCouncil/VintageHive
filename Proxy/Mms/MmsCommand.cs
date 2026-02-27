// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Mms;

internal static class MmsCommand
{
    // ===================================================================
    // Constants
    // ===================================================================

    public const uint SESSION_ID = 0xB00BFACE;
    public const uint SEAL = 0x20534D4D; // "MMS " little-endian

    public const string FUNNEL_NAME = "Funnel Of The Gods";

    // TcpMessageHeader size (fixed: rep+version+sessionId+messageLength+seal+chunkCount+seq+MBZ+timeSent)
    public const int TCP_HEADER_SIZE = 32;

    // ===================================================================
    // MID Constants — Server → Client (0x0004xxxx)
    // ===================================================================

    public const uint MID_ConnectedEX = 0x00040001;
    public const uint MID_ConnectedFunnel = 0x00040002;
    public const uint MID_DisconnectedFunnel = 0x00040003;
    public const uint MID_StartedPlaying = 0x00040005;
    public const uint MID_ReportOpenFile = 0x00040006;
    public const uint MID_ReportReadBlock = 0x00040011;
    public const uint MID_ReportFunnelInfo = 0x00040015;
    public const uint MID_Ping = 0x0004001B;
    public const uint MID_EndOfStream = 0x0004001E;
    public const uint MID_ReportStreamSwitch = 0x00040021;

    // ===================================================================
    // MID Constants — Client → Server (0x0003xxxx)
    // ===================================================================

    public const uint MID_Connect = 0x00030001;
    public const uint MID_ConnectFunnel = 0x00030002;
    public const uint MID_OpenFile = 0x00030005;
    public const uint MID_StartPlaying = 0x00030007;
    public const uint MID_StopPlaying = 0x00030009;
    public const uint MID_CloseFile = 0x0003000D;
    public const uint MID_ReadBlock = 0x00030015;
    public const uint MID_FunnelInfo = 0x00030018;
    public const uint MID_Pong = 0x0003001B;
    public const uint MID_CancelReadBlock = 0x00030025;
    public const uint MID_Logging = 0x00030032;
    public const uint MID_StreamSwitch = 0x00030033;

    // ===================================================================
    // Building messages
    // ===================================================================

    /// <summary>
    /// Build a complete TcpMessageHeader + MMS message body.
    /// The commandFields array is the MMS-specific payload (after chunkLen + MID).
    /// </summary>
    public static byte[] BuildTcpMessage(uint mid, ushort seq, double timeSent, byte[] commandFields)
    {
        // MMS message = chunkLen(4) + MID(4) + commandFields, padded to 8-byte boundary
        int mmsBodyLen = 8 + commandFields.Length;
        int paddedMmsLen = (mmsBodyLen + 7) & ~7;

        // messageLength = size of everything after the 16-byte TcpPreHeader (seal included in pre-header)
        // Must use padded length so client reads the exact number of bytes we wrote
        int messageLength = paddedMmsLen + 16;

        int totalPacketSize = TCP_HEADER_SIZE + paddedMmsLen;
        // Per MS-MMSP 2.2.3: "total size of the TcpMessageHeader packet in multiples of 8 bytes"
        int chunkCount = totalPacketSize / 8;

        var packet = new byte[totalPacketSize];

        // TcpMessageHeader (32 bytes)
        packet[0] = 0x01;                                                    // rep
        packet[1] = 0x00;                                                    // version
        packet[2] = 0x00;                                                    // versionMinor
        packet[3] = 0x00;                                                    // padding
        BitConverter.GetBytes(SESSION_ID).CopyTo(packet, 4);                 // sessionId
        BitConverter.GetBytes(messageLength).CopyTo(packet, 8);              // messageLength
        BitConverter.GetBytes(SEAL).CopyTo(packet, 12);                      // seal
        BitConverter.GetBytes(chunkCount).CopyTo(packet, 16);                // chunkCount
        BitConverter.GetBytes(seq).CopyTo(packet, 20);                       // seq
        // MBZ = 0 at 22-23 (already zero)
        BitConverter.GetBytes(timeSent).CopyTo(packet, 24);                  // timeSent (DOUBLE)

        // MMS message body (at offset 32)
        int mmsChunkLen = paddedMmsLen / 8;
        BitConverter.GetBytes(mmsChunkLen).CopyTo(packet, 32);               // chunkLen
        BitConverter.GetBytes(mid).CopyTo(packet, 36);                       // MID

        if (commandFields.Length > 0)
        {
            Buffer.BlockCopy(commandFields, 0, packet, 40, commandFields.Length);
        }

        return packet;
    }

    /// <summary>
    /// Build a Data packet (8-byte header + ASF payload). No TcpMessageHeader.
    /// </summary>
    public static byte[] BuildDataPacket(uint locationId, byte incarnation, byte afFlags, byte[] asfPayload)
    {
        int totalPacketSize = 8 + asfPayload.Length;
        var packet = new byte[totalPacketSize];

        BitConverter.GetBytes(locationId).CopyTo(packet, 0);                      // LocationId
        packet[4] = incarnation;                                                    // playIncarnation
        packet[5] = afFlags;                                                        // AFFlags
        BitConverter.GetBytes((ushort)totalPacketSize).CopyTo(packet, 6);          // PacketSize (total Data packet size per spec)

        Buffer.BlockCopy(asfPayload, 0, packet, 8, asfPayload.Length);

        return packet;
    }

    /// <summary>
    /// Parse a received MMS message body (after TcpMessageHeader) into MID and command fields.
    /// </summary>
    public static (uint mid, byte[] fields) ParseMmsMessage(byte[] messageBody)
    {
        if (messageBody.Length < 8)
        {
            return (0, Array.Empty<byte>());
        }

        // chunkLen at offset 0 (skip)
        uint mid = BitConverter.ToUInt32(messageBody, 4);
        byte[] fields = messageBody.Length > 8
            ? messageBody[8..]
            : Array.Empty<byte>();

        return (mid, fields);
    }

    /// <summary>
    /// Read a complete message from the network stream.
    /// Handles both TcpMessageHeader (command) packets and Data packets.
    /// Returns (isCommand, data) where data is the full message body for commands
    /// or the full data packet (8-byte header + payload) for data packets.
    /// Returns (false, null) on disconnect.
    /// </summary>
    public static async Task<(bool isCommand, byte[] data)> ReadMessageAsync(NetworkStream stream, CancellationToken ct)
    {
        // Read first 8 bytes — enough to check sessionId at offset 4
        // TcpMessageHeader: [rep(1) version(1) versionMinor(1) padding(1)] [sessionId(4) = 0xB00BFACE]
        // Data packet:      [LocationId(4)] [incarnation(1) AFFlags(1) PacketSize(2)]
        var peek = new byte[8];
        if (!await ReadExactAsync(stream, peek, 0, 8, ct))
        {
            return (false, null);
        }

        uint sessionId = BitConverter.ToUInt32(peek, 4);

        if (sessionId == SESSION_ID)
        {
            // TcpMessageHeader — read remaining 32 bytes to complete the 40-byte header
            var header = new byte[TCP_HEADER_SIZE];
            Buffer.BlockCopy(peek, 0, header, 0, 8);
            if (!await ReadExactAsync(stream, header, 8, TCP_HEADER_SIZE - 8, ct))
            {
                return (false, null);
            }

            int messageLength = BitConverter.ToInt32(header, 8);
            int mmsBodyLen = messageLength - 16;
            if (mmsBodyLen <= 0)
            {
                return (true, Array.Empty<byte>());
            }

            // Pad to 8-byte boundary for reading
            int paddedLen = (mmsBodyLen + 7) & ~7;
            var mmsBody = new byte[paddedLen];
            if (!await ReadExactAsync(stream, mmsBody, 0, paddedLen, ct))
            {
                return (false, null);
            }

            return (true, mmsBody);
        }
        else
        {
            // Data packet — first 8 bytes already contain full data header
            // LocationId(4) + incarnation(1) + AFFlags(1) + PacketSize(2)
            ushort packetSize = BitConverter.ToUInt16(peek, 6);
            var payload = new byte[packetSize];
            if (packetSize > 0 && !await ReadExactAsync(stream, payload, 0, packetSize, ct))
            {
                return (false, null);
            }

            var fullPacket = new byte[8 + packetSize];
            Buffer.BlockCopy(peek, 0, fullPacket, 0, 8);
            Buffer.BlockCopy(payload, 0, fullPacket, 8, packetSize);

            return (false, fullPacket);
        }
    }

    /// <summary>
    /// Extract a Unicode string from MMS command fields at the given byte offset.
    /// MMS strings are null-terminated UTF-16LE.
    /// </summary>
    public static string ExtractUnicodeString(byte[] fields, int offset)
    {
        if (offset >= fields.Length)
        {
            return string.Empty;
        }

        int end = offset;
        while (end + 1 < fields.Length)
        {
            if (fields[end] == 0 && fields[end + 1] == 0)
                break;
            end += 2;
        }

        return Encoding.Unicode.GetString(fields, offset, end - offset);
    }

    /// <summary>
    /// Encode a string as null-terminated UTF-16LE for MMS command fields.
    /// </summary>
    public static byte[] EncodeUnicodeString(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var result = new byte[bytes.Length + 2]; // +2 for null terminator
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    // ===================================================================
    // Helper: build common server response command fields
    // ===================================================================

    /// <summary>
    /// Build ConnectedEX (0x00040001) command fields per MS-MMSP 2.2.4.2.
    /// </summary>
    public static byte[] BuildConnectedEXFields()
    {
        // ServerVersionInfo must be "major.minor" format per spec ABNF
        // Version 9+ causes WMP9 to abandon MMS (tries RTSP instead).
        // Use 4.1 so WMP9 stays on MMS. StreamChange will be tested with this version.
        const string serverVersionStr = "4.1.0.3928";
        var serverVersionBytes = EncodeUnicodeString(serverVersionStr);
        int cbServerVersionInfo = serverVersionStr.Length + 1; // characters including null

        // Fixed fields: hr(4) + playIncarnation(4) + MacToViewerProtRev(4) + ViewerToMacProtRev(4)
        // + blockGroupPlayTime(8) + blockGroupBlocks(4) + nMaxOpenFiles(4) + nBlockMaxBytes(4)
        // + maxBitRate(4) + cbServerVersionInfo(4) + cbVersionInfo(4) + cbVersionUrl(4) + cbAuthenPackage(4)
        // = 56 bytes, then variable strings
        int fixedSize = 56;
        var fields = new byte[fixedSize + serverVersionBytes.Length];
        int pos = 0;

        BitConverter.GetBytes((uint)0x00000000).CopyTo(fields, pos); pos += 4;  // hr = S_OK
        BitConverter.GetBytes(0xF0F0F0EFu).CopyTo(fields, pos); pos += 4;       // playIncarnation (no packet-pair)
        BitConverter.GetBytes(0x0004000Bu).CopyTo(fields, pos); pos += 4;        // MacToViewerProtocolRevision
        BitConverter.GetBytes(0x0003001Cu).CopyTo(fields, pos); pos += 4;        // ViewerToMacProtocolRevision
        BitConverter.GetBytes(1.0).CopyTo(fields, pos); pos += 8;               // blockGroupPlayTime (DOUBLE)
        BitConverter.GetBytes(0x00000001u).CopyTo(fields, pos); pos += 4;        // blockGroupBlocks
        BitConverter.GetBytes(0x00000001u).CopyTo(fields, pos); pos += 4;        // nMaxOpenFiles
        BitConverter.GetBytes(0x00008000u).CopyTo(fields, pos); pos += 4;        // nBlockMaxBytes
        BitConverter.GetBytes(0x00989680u).CopyTo(fields, pos); pos += 4;        // maxBitRate (10 Mbps)
        BitConverter.GetBytes((uint)cbServerVersionInfo).CopyTo(fields, pos); pos += 4; // cbServerVersionInfo
        BitConverter.GetBytes((uint)0).CopyTo(fields, pos); pos += 4;            // cbVersionInfo (no upgrade)
        BitConverter.GetBytes((uint)0).CopyTo(fields, pos); pos += 4;            // cbVersionUrl (no upgrade)
        BitConverter.GetBytes((uint)0).CopyTo(fields, pos); pos += 4;            // cbAuthenPackage (no auth)

        Buffer.BlockCopy(serverVersionBytes, 0, fields, pos, serverVersionBytes.Length);

        return fields;
    }

    /// <summary>
    /// Build ReportFunnelInfo (0x00040015) command fields per MS-MMSP 2.2.4.7.
    /// </summary>
    public static byte[] BuildReportFunnelInfoFields(uint clientId)
    {
        // hr(4) + playIncarnation(4) + transportMask(4) + nBlockFragments(4) + fragmentBytes(4)
        // + nCubs(4) + failedCubs(4) + nDisks(4) + decluster(4) + cubddDatagramSize(4) = 40 bytes
        var fields = new byte[40];
        int pos = 0;

        BitConverter.GetBytes((uint)0x00000000).CopyTo(fields, pos); pos += 4;  // hr = S_OK
        BitConverter.GetBytes(0xF0F0F0EFu).CopyTo(fields, pos); pos += 4;       // playIncarnation (no packet-pair)
        BitConverter.GetBytes(0x00000008u).CopyTo(fields, pos); pos += 4;        // transportMask (MUST be 0x00000008)
        BitConverter.GetBytes(0x00000001u).CopyTo(fields, pos); pos += 4;        // nBlockFragments (MUST be 0x00000001)
        BitConverter.GetBytes(0x00010000u).CopyTo(fields, pos); pos += 4;        // fragmentBytes (MUST be 0x00010000)
        BitConverter.GetBytes(clientId).CopyTo(fields, pos); pos += 4;           // nCubs = unique client ID
        BitConverter.GetBytes((uint)0x00000000).CopyTo(fields, pos); pos += 4;   // failedCubs (MUST be 0x00000000)
        BitConverter.GetBytes(0x00000001u).CopyTo(fields, pos); pos += 4;        // nDisks (MUST be 0x00000001)
        BitConverter.GetBytes((uint)0x00000000).CopyTo(fields, pos); pos += 4;   // decluster (MUST be 0x00000000)
        BitConverter.GetBytes((uint)0x00000000).CopyTo(fields, pos);             // cubddDatagramSize (MUST be 0x00000000)
        return fields;
    }

    /// <summary>
    /// Build ConnectedFunnel (0x00040002) command fields per MS-MMSP 2.2.4.3.
    /// </summary>
    public static byte[] BuildConnectedFunnelFields()
    {
        // hr(4) + playIncarnation(4) + packetPayloadSize(4) + funnelName(variable)
        var nameBytes = EncodeUnicodeString(FUNNEL_NAME);
        var fields = new byte[12 + nameBytes.Length];
        int pos = 0;

        BitConverter.GetBytes((uint)0x00000000).CopyTo(fields, pos); pos += 4;  // hr = S_OK
        BitConverter.GetBytes((uint)0x00000000).CopyTo(fields, pos); pos += 4;  // playIncarnation (SHOULD be 0)
        BitConverter.GetBytes((uint)0x00000000).CopyTo(fields, pos); pos += 4;  // packetPayloadSize (SHOULD be 0)
        Buffer.BlockCopy(nameBytes, 0, fields, pos, nameBytes.Length);
        return fields;
    }

    /// <summary>
    /// Build ReportOpenFile (0x00040006) command fields per MS-MMSP 2.2.4.7.
    /// </summary>
    public static byte[] BuildReportOpenFileFields(uint playIncarnation, uint openFileId, int fileHeaderSize, int packetSize, int bitRate)
    {
        // hr(4) + playIncarnation(4) + openFileId(4) + padding(4) + fileName(4)
        // + fileAttributes(4) + fileDuration(8) + fileBlocks(4) + unused1(16)
        // + filePacketSize(4) + filePacketCount(8) + fileBitRate(4) + fileHeaderSize(4)
        // + unused2(36) = 108 bytes
        var fields = new byte[108];
        int pos = 0;

        BitConverter.GetBytes((uint)0).CopyTo(fields, pos); pos += 4;                 // hr = S_OK
        BitConverter.GetBytes(playIncarnation).CopyTo(fields, pos); pos += 4;          // playIncarnation (echo client)
        BitConverter.GetBytes(openFileId).CopyTo(fields, pos); pos += 4;               // openFileId
        // padding(4) = 0, fileName(4) = 0 — already zero
        pos += 8;
        // fileAttributes: BROADCAST (0x02000000) | PLAYLIST (0x40000000)
        BitConverter.GetBytes(0x42000000u).CopyTo(fields, pos); pos += 4;              // fileAttributes
        BitConverter.GetBytes(0.0).CopyTo(fields, pos); pos += 8;                     // fileDuration = 0 (live)
        BitConverter.GetBytes((uint)0).CopyTo(fields, pos); pos += 4;                 // fileBlocks = 0 (live)
        // unused1(16) — already zero
        pos += 16;
        BitConverter.GetBytes((uint)packetSize).CopyTo(fields, pos); pos += 4;         // filePacketSize
        BitConverter.GetBytes(0UL).CopyTo(fields, pos); pos += 8;                     // filePacketCount = 0 (unknown/live)
        BitConverter.GetBytes((uint)bitRate).CopyTo(fields, pos); pos += 4;            // fileBitRate
        BitConverter.GetBytes((uint)fileHeaderSize).CopyTo(fields, pos); pos += 4;     // fileHeaderSize
        // unused2(36) — already zero

        return fields;
    }

    /// <summary>
    /// Build ReportReadBlock (0x00040011) command fields per MS-MMSP 2.2.4.8.
    /// </summary>
    public static byte[] BuildReportReadBlockFields(uint playIncarnation, uint playSequence)
    {
        // hr(4) + playIncarnation(4) + playSequence(4) = 12 bytes
        var fields = new byte[12];
        int pos = 0;

        BitConverter.GetBytes((uint)0).CopyTo(fields, pos); pos += 4;             // hr = S_OK
        BitConverter.GetBytes(playIncarnation).CopyTo(fields, pos); pos += 4;      // playIncarnation (echo client)
        BitConverter.GetBytes(playSequence).CopyTo(fields, pos);                   // playSequence (echo client)
        return fields;
    }

    /// <summary>
    /// Build ReportStreamSwitch (0x00040021) command fields.
    /// </summary>
    public static byte[] BuildReportStreamSwitchFields()
    {
        // hr (4) = 0 (success)
        var fields = new byte[4];
        BitConverter.GetBytes((uint)0).CopyTo(fields, 0);
        return fields;
    }

    /// <summary>
    /// Build StartedPlaying (0x00040005) command fields per MS-MMSP 2.2.4.5.
    /// </summary>
    public static byte[] BuildStartedPlayingFields(uint playIncarnation, uint tigerFileId)
    {
        // hr(4) + playIncarnation(4) + tigerFileId(4) + unused1(4) + unused2(12) = 28 bytes
        var fields = new byte[28];
        int pos = 0;

        BitConverter.GetBytes((uint)0).CopyTo(fields, pos); pos += 4;             // hr = S_OK
        BitConverter.GetBytes(playIncarnation).CopyTo(fields, pos); pos += 4;      // playIncarnation (echo client)
        BitConverter.GetBytes(tigerFileId).CopyTo(fields, pos); pos += 4;          // tigerFileId (openFileId)
        BitConverter.GetBytes((uint)0).CopyTo(fields, pos);                        // unused1 = 0
        // unused2(12) — already zero
        return fields;
    }

    /// <summary>
    /// Build Ping (0x0004001B) command fields.
    /// </summary>
    public static byte[] BuildPingFields(uint dwDuration)
    {
        // dwDuration (4)
        var fields = new byte[4];
        BitConverter.GetBytes(dwDuration).CopyTo(fields, 0);
        return fields;
    }

    /// <summary>
    /// Build EndOfStream (0x0004001E) command fields.
    /// </summary>
    public static byte[] BuildEndOfStreamFields(uint hr, uint playIncarnation)
    {
        // hr (4) + playIncarnation (4)
        var fields = new byte[8];
        BitConverter.GetBytes(hr).CopyTo(fields, 0);
        BitConverter.GetBytes(playIncarnation).CopyTo(fields, 4);
        return fields;
    }

    // ===================================================================
    // Private helpers
    // ===================================================================

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
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
}
