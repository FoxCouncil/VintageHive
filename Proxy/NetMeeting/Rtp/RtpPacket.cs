// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;

namespace VintageHive.Proxy.NetMeeting.Rtp;

/// <summary>
/// Lightweight RTP header parser for statistics and logging.
/// Does NOT modify packet data — the relay forwards raw bytes.
///
/// RTP header (RFC 3550):
///  0                   1                   2                   3
///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |V=2|P|X|  CC   |M|     PT      |       sequence number         |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |                           timestamp                           |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |                             SSRC                              |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// </summary>
internal readonly struct RtpHeader
{
    /// <summary>RTP version (should be 2).</summary>
    public int Version { get; init; }

    /// <summary>Padding flag.</summary>
    public bool Padding { get; init; }

    /// <summary>Extension header present.</summary>
    public bool Extension { get; init; }

    /// <summary>CSRC count (0-15).</summary>
    public int CsrcCount { get; init; }

    /// <summary>Marker bit.</summary>
    public bool Marker { get; init; }

    /// <summary>Payload type (0-127).</summary>
    public int PayloadType { get; init; }

    /// <summary>Sequence number (0-65535).</summary>
    public int SequenceNumber { get; init; }

    /// <summary>Timestamp.</summary>
    public uint Timestamp { get; init; }

    /// <summary>Synchronization source identifier.</summary>
    public uint Ssrc { get; init; }

    /// <summary>Total header size including CSRC list.</summary>
    public int HeaderSize => RtpConstants.RTP_HEADER_MIN + (CsrcCount * 4);

    /// <summary>
    /// Try to parse an RTP header from raw packet data.
    /// Returns false if the data is too short or the version is wrong.
    /// </summary>
    public static bool TryParse(byte[] data, int length, out RtpHeader header)
    {
        header = default;

        if (length < RtpConstants.RTP_HEADER_MIN)
        {
            return false;
        }

        var version = (data[0] >> 6) & 0x03;
        if (version != RtpConstants.RTP_VERSION)
        {
            return false;
        }

        var padding = ((data[0] >> 5) & 0x01) != 0;
        var extension = ((data[0] >> 4) & 0x01) != 0;
        var csrcCount = data[0] & 0x0F;
        var marker = ((data[1] >> 7) & 0x01) != 0;
        var payloadType = data[1] & 0x7F;

        var seqNum = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        var timestamp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        var ssrc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));

        // Verify we have enough data for the CSRC list
        var expectedHeaderSize = RtpConstants.RTP_HEADER_MIN + (csrcCount * 4);
        if (length < expectedHeaderSize)
        {
            return false;
        }

        header = new RtpHeader
        {
            Version = version,
            Padding = padding,
            Extension = extension,
            CsrcCount = csrcCount,
            Marker = marker,
            PayloadType = payloadType,
            SequenceNumber = seqNum,
            Timestamp = timestamp,
            Ssrc = ssrc
        };

        return true;
    }

    /// <summary>
    /// Build a minimal RTP packet for testing.
    /// </summary>
    public static byte[] Build(int payloadType, int sequenceNumber, uint timestamp,
        uint ssrc, byte[] payload, bool marker = false)
    {
        var packet = new byte[RtpConstants.RTP_HEADER_MIN + payload.Length];

        // V=2, P=0, X=0, CC=0
        packet[0] = (byte)(RtpConstants.RTP_VERSION << 6);

        // M + PT
        packet[1] = (byte)((marker ? 0x80 : 0x00) | (payloadType & 0x7F));

        // Sequence number
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)sequenceNumber);

        // Timestamp
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), timestamp);

        // SSRC
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), ssrc);

        // Payload
        Array.Copy(payload, 0, packet, RtpConstants.RTP_HEADER_MIN, payload.Length);

        return packet;
    }
}

/// <summary>
/// Lightweight RTCP header parser for the first packet in a compound RTCP datagram.
///
/// RTCP header:
///  0                   1                   2                   3
///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |V=2|P|   RC    |      PT       |            length             |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |                         SSRC of sender                        |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// </summary>
internal readonly struct RtcpHeader
{
    /// <summary>RTCP version (should be 2).</summary>
    public int Version { get; init; }

    /// <summary>Padding flag.</summary>
    public bool Padding { get; init; }

    /// <summary>Reception report count (SR/RR) or subtype (other).</summary>
    public int Count { get; init; }

    /// <summary>Packet type (200=SR, 201=RR, 202=SDES, 203=BYE, 204=APP).</summary>
    public int PacketType { get; init; }

    /// <summary>Length in 32-bit words minus one (per RTCP spec).</summary>
    public int Length { get; init; }

    /// <summary>SSRC of the sender.</summary>
    public uint Ssrc { get; init; }

    /// <summary>
    /// Try to parse the first RTCP header from a compound packet.
    /// Returns false if the data is too short or version is wrong.
    /// </summary>
    public static bool TryParse(byte[] data, int length, out RtcpHeader header)
    {
        header = default;

        // Minimum RTCP packet: 4-byte header + 4-byte SSRC = 8 bytes
        if (length < 8)
        {
            return false;
        }

        var version = (data[0] >> 6) & 0x03;
        if (version != RtpConstants.RTP_VERSION)
        {
            return false;
        }

        var padding = ((data[0] >> 5) & 0x01) != 0;
        var count = data[0] & 0x1F;
        var packetType = data[1];
        var wordLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        var ssrc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));

        // Validate packet type is in RTCP range
        if (packetType < 200 || packetType > 204)
        {
            return false;
        }

        header = new RtcpHeader
        {
            Version = version,
            Padding = padding,
            Count = count,
            PacketType = packetType,
            Length = wordLength,
            Ssrc = ssrc
        };

        return true;
    }

    /// <summary>
    /// Build a minimal RTCP SR packet for testing.
    /// </summary>
    public static byte[] BuildSenderReport(uint ssrc, uint ntpHi, uint ntpLo,
        uint rtpTimestamp, uint packetCount, uint octetCount)
    {
        var packet = new byte[28]; // 4 header + 4 SSRC + 20 sender info

        // V=2, P=0, RC=0, PT=200
        packet[0] = (byte)(RtpConstants.RTP_VERSION << 6);
        packet[1] = (byte)RtpConstants.RTCP_SR;

        // Length in 32-bit words minus 1: (28/4) - 1 = 6
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), 6);

        // SSRC
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), ssrc);

        // NTP timestamp
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), ntpHi);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), ntpLo);

        // RTP timestamp
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(16), rtpTimestamp);

        // Packet count
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(20), packetCount);

        // Octet count
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(24), octetCount);

        return packet;
    }
}
