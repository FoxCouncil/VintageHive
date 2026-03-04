// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;

namespace VintageHive.Proxy.NetMeeting.AppSharing;

/// <summary>
/// S20_CREATE packet — initiates an application sharing session.
///
/// Wire format (all LE):
///   Length(2) + Version/Type(2) + User(2) + Correlator(4) + CPCALLCAPS(204)
/// </summary>
internal class S20CreatePacket
{
    public ushort User { get; init; }
    public uint Correlator { get; init; }
    public byte[] Capabilities { get; init; }
}

/// <summary>
/// S20_JOIN packet — requests to join an existing sharing session.
///
/// Wire format: Length(2) + Version/Type(2) + User(2) + Correlator(4) + CPCALLCAPS(204)
/// </summary>
internal class S20JoinPacket
{
    public ushort User { get; init; }
    public uint Correlator { get; init; }
    public byte[] Capabilities { get; init; }
}

/// <summary>
/// S20_RESPOND packet — response to a JOIN request.
///
/// Wire format: Length(2) + Version/Type(2) + User(2) + Correlator(4) + Result(4)
/// </summary>
internal class S20RespondPacket
{
    public ushort User { get; init; }
    public uint Correlator { get; init; }
    public uint Result { get; init; }
}

/// <summary>
/// S20_DELETE packet — host removes a user from the session.
///
/// Wire format: Length(2) + Version/Type(2) + User(2) + Correlator(4)
/// </summary>
internal class S20DeletePacket
{
    public ushort User { get; init; }
    public uint Correlator { get; init; }
}

/// <summary>
/// S20_LEAVE packet — a user voluntarily leaves the session.
///
/// Wire format: Length(2) + Version/Type(2) + User(2) + Correlator(4)
/// </summary>
internal class S20LeavePacket
{
    public ushort User { get; init; }
    public uint Correlator { get; init; }
}

/// <summary>
/// S20_END packet — host terminates the entire sharing session.
///
/// Wire format: Length(2) + Version/Type(2) + User(2) + Correlator(4)
/// </summary>
internal class S20EndPacket
{
    public ushort User { get; init; }
    public uint Correlator { get; init; }
}

/// <summary>
/// S20_COLLISION packet — signals a session collision (two simultaneous CREATEs).
///
/// Wire format: Length(2) + Version/Type(2) + User(2) + Correlator(4)
/// </summary>
internal class S20CollisionPacket
{
    public ushort User { get; init; }
    public uint Correlator { get; init; }
}

/// <summary>
/// S20_DATA packet — wraps application sharing data payloads.
///
/// Wire format (all LE, NO leading length field):
///   Version/Type(2) + User(2) + Correlator(4) + AckID(1) + Stream(1) +
///   DataLength(2) + Datatype(1) + CompressionType(1) + CompressedLength(2) + Data(var)
/// </summary>
internal class S20DataPacket
{
    public ushort User { get; init; }
    public uint Correlator { get; init; }
    public byte AckId { get; init; }
    public byte Stream { get; init; }
    public byte Datatype { get; init; }
    public byte CompressionType { get; init; }
    public byte[] Data { get; init; }
}

/// <summary>
/// Decoded S20 packet envelope.
/// </summary>
internal class S20Message
{
    /// <summary>S20 Version/Type field.</summary>
    public ushort Type { get; init; }

    public S20CreatePacket Create { get; init; }
    public S20JoinPacket Join { get; init; }
    public S20RespondPacket Respond { get; init; }
    public S20DeletePacket Delete { get; init; }
    public S20LeavePacket Leave { get; init; }
    public S20EndPacket End { get; init; }
    public S20CollisionPacket Collision { get; init; }
    public S20DataPacket DataPacket { get; init; }

    /// <summary>Raw bytes for passthrough or diagnostics.</summary>
    public byte[] RawData { get; init; }

    public override string ToString() => AppSharingConstants.PacketTypeName(Type);
}

/// <summary>
/// CPCALLCAPS — combined capabilities structure exchanged during
/// S20_CREATE and S20_JOIN, always exactly 204 bytes.
///
/// Contains 7 capability sets concatenated in order:
///   General, Bitmap, Order, BitmapCache, Control, Activation, Pointer
///
/// Each set starts with: CapsType(2) + CapsLength(2) + set-specific data.
/// </summary>
internal class CpCallCaps
{
    /// <summary>Raw 204-byte capability block.</summary>
    public byte[] RawData { get; init; }

    /// <summary>Number of capability sets (should be 7).</summary>
    public ushort NumCapabilities { get; init; }

    /// <summary>
    /// Parse the CPCALLCAPS from a 204-byte buffer.
    /// </summary>
    public static CpCallCaps Decode(byte[] data, int offset = 0)
    {
        if (data == null || data.Length - offset < AppSharingConstants.CPCALLCAPS_SIZE)
        {
            throw new ArgumentException(
                $"CPCALLCAPS requires {AppSharingConstants.CPCALLCAPS_SIZE} bytes, " +
                $"got {data?.Length - offset ?? 0}");
        }

        var raw = new byte[AppSharingConstants.CPCALLCAPS_SIZE];
        Array.Copy(data, offset, raw, 0, AppSharingConstants.CPCALLCAPS_SIZE);

        // NumCapabilities is at offset 2 within the CPCALLCAPS structure
        // Layout: TotalLength(2) + NumCapabilities(2) + capsets...
        var numCaps = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2));

        return new CpCallCaps
        {
            RawData = raw,
            NumCapabilities = numCaps
        };
    }

    /// <summary>
    /// Encode a minimal CPCALLCAPS with default capability values.
    /// Returns exactly 204 bytes.
    /// </summary>
    public static byte[] EncodeDefault()
    {
        var data = new byte[AppSharingConstants.CPCALLCAPS_SIZE];

        // Total length of the caps block
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0),
            (ushort)AppSharingConstants.CPCALLCAPS_SIZE);

        // Number of capability sets
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2),
            (ushort)AppSharingConstants.CAPS_SET_COUNT);

        // Fill in the 7 capability set headers at their standard positions.
        // Each cap set: CapsType(2) + CapsLength(2) + data...
        // Sizes chosen to fit exactly in 204 bytes:
        //   Header(4) + General(24) + Bitmap(28) + Order(84) + BitmapCache(40)
        //   + Control(12) + Activation(4) + Pointer(8) = 204
        var pos = 4;

        // General caps (24 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos), AppSharingConstants.CAPS_GENERAL);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2), 24);
        pos += 24;

        // Bitmap caps (28 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos), AppSharingConstants.CAPS_BITMAP);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2), 28);
        pos += 28;

        // Order caps (84 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos), AppSharingConstants.CAPS_ORDER);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2), 84);
        pos += 84;

        // BitmapCache caps (40 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos), AppSharingConstants.CAPS_BMPCACHE);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2), 40);
        pos += 40;

        // Control caps (12 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos), AppSharingConstants.CAPS_CONTROL);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2), 12);
        pos += 12;

        // Activation caps (4 bytes — header only)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos), AppSharingConstants.CAPS_ACTIVATION);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2), 4);
        pos += 4;

        // Pointer caps (8 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos), AppSharingConstants.CAPS_POINTER);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2), 8);
        // pos += 8; // final position = 204

        return data;
    }

    /// <summary>
    /// Extract individual capability set data by type.
    /// Returns null if the set is not found.
    /// </summary>
    public byte[] GetCapabilitySet(ushort capsType)
    {
        if (RawData == null || RawData.Length < 4)
        {
            return null;
        }

        var pos = 4; // skip TotalLength + NumCapabilities
        var remaining = NumCapabilities;

        while (remaining > 0 && pos + 4 <= RawData.Length)
        {
            var setType = BinaryPrimitives.ReadUInt16LittleEndian(RawData.AsSpan(pos));
            var setLen = BinaryPrimitives.ReadUInt16LittleEndian(RawData.AsSpan(pos + 2));

            if (setLen < 4 || pos + setLen > RawData.Length)
            {
                break;
            }

            if (setType == capsType)
            {
                var setData = new byte[setLen];
                Array.Copy(RawData, pos, setData, 0, setLen);
                return setData;
            }

            pos += setLen;
            remaining--;
        }

        return null;
    }

    public override string ToString() => $"CPCALLCAPS({NumCapabilities} sets, {RawData?.Length ?? 0} bytes)";
}

/// <summary>
/// S20 Application Sharing binary codec — encode and decode all S20 packet types.
///
/// All multi-byte integer fields are little-endian per MS-MNPR.
/// Control packets (CREATE, JOIN, RESPOND, DELETE, LEAVE, END, COLLISION) have
/// a leading 2-byte length field; S20_DATA does NOT.
/// </summary>
internal static class S20Codec
{
    // ──────────────────────────────────────────────────────────
    //  Detection
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Peek at the S20 Version/Type field from raw packet data.
    /// For control packets, the type is at offset 2 (after the length field).
    /// For S20_DATA, the type is at offset 0 (no length prefix).
    /// Returns 0 if data is too short or not recognized.
    /// </summary>
    public static ushort PeekPacketType(byte[] data)
    {
        if (data == null || data.Length < 4)
        {
            return 0;
        }

        // Try offset 0 first — could be S20_DATA (no length prefix)
        var typeAt0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0));
        if (typeAt0 == AppSharingConstants.S20_DATA)
        {
            return typeAt0;
        }

        // Try offset 2 — control packet with length prefix
        var typeAt2 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        if (AppSharingConstants.IsS20Packet(typeAt2))
        {
            return typeAt2;
        }

        return 0;
    }

    /// <summary>
    /// Returns true if the data looks like a valid S20 packet.
    /// </summary>
    public static bool IsS20Packet(byte[] data)
    {
        return PeekPacketType(data) != 0;
    }

    // ──────────────────────────────────────────────────────────
    //  Encode — Control packets
    // ──────────────────────────────────────────────────────────

    /// <summary>Encode an S20_CREATE packet.</summary>
    public static byte[] EncodeCreate(S20CreatePacket pdu)
    {
        var caps = pdu.Capabilities ?? CpCallCaps.EncodeDefault();
        var packetLen = 2 + 2 + 4 + caps.Length; // type + user + correlator + caps
        var data = new byte[2 + packetLen]; // length prefix + body

        // Length field (excludes the length field itself)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), (ushort)packetLen);

        // Version/Type
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), AppSharingConstants.S20_CREATE);

        // User
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), pdu.User);

        // Correlator
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), pdu.Correlator);

        // CPCALLCAPS
        Array.Copy(caps, 0, data, 10, caps.Length);

        return data;
    }

    /// <summary>Encode an S20_JOIN packet.</summary>
    public static byte[] EncodeJoin(S20JoinPacket pdu)
    {
        var caps = pdu.Capabilities ?? CpCallCaps.EncodeDefault();
        var packetLen = 2 + 2 + 4 + caps.Length;
        var data = new byte[2 + packetLen];

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), (ushort)packetLen);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), AppSharingConstants.S20_JOIN);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), pdu.User);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), pdu.Correlator);
        Array.Copy(caps, 0, data, 10, caps.Length);

        return data;
    }

    /// <summary>Encode an S20_RESPOND packet.</summary>
    public static byte[] EncodeRespond(S20RespondPacket pdu)
    {
        // Length(2) + Type(2) + User(2) + Correlator(4) + Result(4)
        var data = new byte[14];

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 12); // body length
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), AppSharingConstants.S20_RESPOND);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), pdu.User);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), pdu.Correlator);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), pdu.Result);

        return data;
    }

    /// <summary>Encode an S20_DELETE packet.</summary>
    public static byte[] EncodeDelete(S20DeletePacket pdu)
    {
        return EncodeSimpleControl(AppSharingConstants.S20_DELETE, pdu.User, pdu.Correlator);
    }

    /// <summary>Encode an S20_LEAVE packet.</summary>
    public static byte[] EncodeLeave(S20LeavePacket pdu)
    {
        return EncodeSimpleControl(AppSharingConstants.S20_LEAVE, pdu.User, pdu.Correlator);
    }

    /// <summary>Encode an S20_END packet.</summary>
    public static byte[] EncodeEnd(S20EndPacket pdu)
    {
        return EncodeSimpleControl(AppSharingConstants.S20_END, pdu.User, pdu.Correlator);
    }

    /// <summary>Encode an S20_COLLISION packet.</summary>
    public static byte[] EncodeCollision(S20CollisionPacket pdu)
    {
        return EncodeSimpleControl(AppSharingConstants.S20_COLLISION, pdu.User, pdu.Correlator);
    }

    // ──────────────────────────────────────────────────────────
    //  Encode — S20_DATA
    // ──────────────────────────────────────────────────────────

    /// <summary>Encode an S20_DATA packet (no leading length field).</summary>
    public static byte[] EncodeData(S20DataPacket pdu)
    {
        var payload = pdu.Data ?? Array.Empty<byte>();
        var data = new byte[AppSharingConstants.S20_DATA_HEADER_SIZE + payload.Length];

        // Version/Type (no length prefix for DATA)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), AppSharingConstants.S20_DATA);

        // User
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), pdu.User);

        // Correlator
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), pdu.Correlator);

        // AckID
        data[8] = pdu.AckId;

        // Stream
        data[9] = pdu.Stream;

        // DataLength (total length of datatype + compression header + data)
        var dataLength = (ushort)(4 + payload.Length); // datatype(1) + compress(1) + compLen(2) + payload
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), dataLength);

        // Datatype
        data[12] = pdu.Datatype;

        // CompressionType
        data[13] = pdu.CompressionType;

        // CompressedLength (same as payload length when uncompressed)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)payload.Length);

        // Payload
        if (payload.Length > 0)
        {
            Array.Copy(payload, 0, data, AppSharingConstants.S20_DATA_HEADER_SIZE, payload.Length);
        }

        return data;
    }

    // ──────────────────────────────────────────────────────────
    //  Decode
    // ──────────────────────────────────────────────────────────

    /// <summary>Decode an S20 packet from raw bytes.</summary>
    public static S20Message Decode(byte[] data)
    {
        if (data == null || data.Length < 4)
        {
            throw new ArgumentException("S20 packet too short");
        }

        // Check if this is S20_DATA (type at offset 0, no length prefix)
        var typeAt0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0));
        if (typeAt0 == AppSharingConstants.S20_DATA)
        {
            return DecodeData(data);
        }

        // Control packet: Length(2) + Type(2) + ...
        if (data.Length < AppSharingConstants.MIN_CONTROL_PACKET)
        {
            throw new ArgumentException("S20 control packet too short");
        }

        var type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));

        return type switch
        {
            AppSharingConstants.S20_CREATE => DecodeCreate(data),
            AppSharingConstants.S20_JOIN => DecodeJoin(data),
            AppSharingConstants.S20_RESPOND => DecodeRespond(data),
            AppSharingConstants.S20_DELETE => DecodeSimpleControl(data, AppSharingConstants.S20_DELETE),
            AppSharingConstants.S20_LEAVE => DecodeSimpleControl(data, AppSharingConstants.S20_LEAVE),
            AppSharingConstants.S20_END => DecodeSimpleControl(data, AppSharingConstants.S20_END),
            AppSharingConstants.S20_COLLISION => DecodeSimpleControl(data, AppSharingConstants.S20_COLLISION),
            _ => new S20Message { Type = type, RawData = data }
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Internal helpers
    // ──────────────────────────────────────────────────────────

    private static byte[] EncodeSimpleControl(ushort type, ushort user, uint correlator)
    {
        // Length(2) + Type(2) + User(2) + Correlator(4) = 10 total
        var data = new byte[10];

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 8); // body length
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), user);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), correlator);

        return data;
    }

    private static S20Message DecodeCreate(byte[] data)
    {
        if (data.Length < 10)
        {
            throw new ArgumentException("S20_CREATE too short");
        }

        var user = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        var correlator = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6));

        byte[] caps = null;
        if (data.Length >= 10 + AppSharingConstants.CPCALLCAPS_SIZE)
        {
            caps = new byte[AppSharingConstants.CPCALLCAPS_SIZE];
            Array.Copy(data, 10, caps, 0, AppSharingConstants.CPCALLCAPS_SIZE);
        }

        return new S20Message
        {
            Type = AppSharingConstants.S20_CREATE,
            Create = new S20CreatePacket
            {
                User = user,
                Correlator = correlator,
                Capabilities = caps
            },
            RawData = data
        };
    }

    private static S20Message DecodeJoin(byte[] data)
    {
        if (data.Length < 10)
        {
            throw new ArgumentException("S20_JOIN too short");
        }

        var user = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        var correlator = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6));

        byte[] caps = null;
        if (data.Length >= 10 + AppSharingConstants.CPCALLCAPS_SIZE)
        {
            caps = new byte[AppSharingConstants.CPCALLCAPS_SIZE];
            Array.Copy(data, 10, caps, 0, AppSharingConstants.CPCALLCAPS_SIZE);
        }

        return new S20Message
        {
            Type = AppSharingConstants.S20_JOIN,
            Join = new S20JoinPacket
            {
                User = user,
                Correlator = correlator,
                Capabilities = caps
            },
            RawData = data
        };
    }

    private static S20Message DecodeRespond(byte[] data)
    {
        if (data.Length < 14)
        {
            throw new ArgumentException("S20_RESPOND too short");
        }

        return new S20Message
        {
            Type = AppSharingConstants.S20_RESPOND,
            Respond = new S20RespondPacket
            {
                User = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)),
                Correlator = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6)),
                Result = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10))
            },
            RawData = data
        };
    }

    private static S20Message DecodeSimpleControl(byte[] data, ushort expectedType)
    {
        if (data.Length < 10)
        {
            throw new ArgumentException($"{AppSharingConstants.PacketTypeName(expectedType)} too short");
        }

        var user = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        var correlator = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6));

        var msg = new S20Message { Type = expectedType, RawData = data };

        return expectedType switch
        {
            AppSharingConstants.S20_DELETE => new S20Message
            {
                Type = expectedType,
                Delete = new S20DeletePacket { User = user, Correlator = correlator },
                RawData = data
            },
            AppSharingConstants.S20_LEAVE => new S20Message
            {
                Type = expectedType,
                Leave = new S20LeavePacket { User = user, Correlator = correlator },
                RawData = data
            },
            AppSharingConstants.S20_END => new S20Message
            {
                Type = expectedType,
                End = new S20EndPacket { User = user, Correlator = correlator },
                RawData = data
            },
            AppSharingConstants.S20_COLLISION => new S20Message
            {
                Type = expectedType,
                Collision = new S20CollisionPacket { User = user, Correlator = correlator },
                RawData = data
            },
            _ => msg
        };
    }

    private static S20Message DecodeData(byte[] data)
    {
        if (data.Length < AppSharingConstants.S20_DATA_HEADER_SIZE)
        {
            throw new ArgumentException("S20_DATA too short");
        }

        var user = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        var correlator = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var ackId = data[8];
        var stream = data[9];
        // dataLength at offset 10
        var datatype = data[12];
        var compressionType = data[13];
        var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));

        byte[] payload;
        if (data.Length > AppSharingConstants.S20_DATA_HEADER_SIZE)
        {
            var payloadLen = Math.Min(compressedLength,
                data.Length - AppSharingConstants.S20_DATA_HEADER_SIZE);
            payload = new byte[payloadLen];
            Array.Copy(data, AppSharingConstants.S20_DATA_HEADER_SIZE, payload, 0, payloadLen);
        }
        else
        {
            payload = Array.Empty<byte>();
        }

        return new S20Message
        {
            Type = AppSharingConstants.S20_DATA,
            DataPacket = new S20DataPacket
            {
                User = user,
                Correlator = correlator,
                AckId = ackId,
                Stream = stream,
                Datatype = datatype,
                CompressionType = compressionType,
                Data = payload
            },
            RawData = data
        };
    }
}
