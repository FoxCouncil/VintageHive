// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using System.Text;

namespace VintageHive.Proxy.NetMeeting.Omnet;

/// <summary>
/// Sequence stamp for tracking ordering of workset operations.
/// Ordering: compare genNumber first, then nodeId as tiebreaker.
/// </summary>
internal class OmnetSeqStamp
{
    public uint GenNumber { get; init; }
    public ushort NodeId { get; init; }

    public override string ToString() => $"Seq({GenNumber},{NodeId})";
}

/// <summary>
/// Object identifier within a workset: sequence number + creator node.
/// </summary>
internal class OmnetObjectId
{
    public uint Sequence { get; init; }
    public ushort Creator { get; init; }

    public override string ToString() => $"Obj({Sequence},{Creator})";
}

/// <summary>
/// WSGROUP_INFO structure stored in ObManControl workset #0.
/// Describes a registered workset group (e.g., whiteboard, file transfer).
///
/// Per MS-MNPR section 2.2.5.22:
///   0x00: length (4 bytes, excludes itself)
///   0x04: idStamp "OMWI" (4 bytes)
///   0x08: channelID (2 bytes)
///   0x0A: creator (2 bytes)
///   0x0C: wsGroupID (1 byte)
///   0x0D: pad1 (1 byte)
///   0x0E: pad2 (2 bytes)
///   0x10: functionProfile (16 bytes, null-terminated ASCII)
///   0x20: wsGroupName (32 bytes, null-terminated ASCII)
/// </summary>
internal class WsGroupInfo
{
    public ushort ChannelId { get; init; }
    public ushort Creator { get; init; }
    public byte WsGroupId { get; init; }
    public string FunctionProfile { get; init; }
    public string WsGroupName { get; init; }

    /// <summary>
    /// Encode this WSGROUP_INFO to bytes (including the 4-byte length prefix).
    /// </summary>
    public byte[] Encode()
    {
        var data = new byte[4 + OmnetConstants.WSGROUP_INFO_SIZE]; // 64 total

        // Length field (excludes itself)
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), (uint)OmnetConstants.WSGROUP_INFO_SIZE);

        // idStamp = "OMWI"
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), OmnetConstants.WSGROUP_INFO_STAMP);

        // channelID
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), ChannelId);

        // creator
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), Creator);

        // wsGroupID
        data[12] = WsGroupId;
        // pad1, pad2 already zero

        // functionProfile (null-terminated, max 16 bytes including null)
        if (FunctionProfile != null)
        {
            var fpBytes = Encoding.ASCII.GetBytes(FunctionProfile);
            var fpLen = Math.Min(fpBytes.Length, 15);
            Array.Copy(fpBytes, 0, data, 16, fpLen);
        }

        // wsGroupName (null-terminated, max 32 bytes including null)
        if (WsGroupName != null)
        {
            var nameBytes = Encoding.ASCII.GetBytes(WsGroupName);
            var nameLen = Math.Min(nameBytes.Length, 31);
            Array.Copy(nameBytes, 0, data, 32, nameLen);
        }

        return data;
    }

    /// <summary>
    /// Decode a WSGROUP_INFO from bytes (including the 4-byte length prefix).
    /// </summary>
    public static WsGroupInfo Decode(byte[] data)
    {
        if (data == null || data.Length < 4 + OmnetConstants.WSGROUP_INFO_SIZE)
        {
            throw new ArgumentException("WSGROUP_INFO too short");
        }

        var stamp = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (stamp != OmnetConstants.WSGROUP_INFO_STAMP)
        {
            throw new InvalidDataException(
                $"Invalid WSGROUP_INFO stamp: 0x{stamp:X8} (expected 0x{OmnetConstants.WSGROUP_INFO_STAMP:X8})");
        }

        return new WsGroupInfo
        {
            ChannelId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)),
            Creator = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10)),
            WsGroupId = data[12],
            FunctionProfile = ReadNullTerminatedAscii(data, 16, 16),
            WsGroupName = ReadNullTerminatedAscii(data, 32, 32)
        };
    }

    private static string ReadNullTerminatedAscii(byte[] data, int offset, int maxLen)
    {
        var end = offset;
        var limit = Math.Min(offset + maxLen, data.Length);

        while (end < limit && data[end] != 0)
        {
            end++;
        }

        if (end == offset)
        {
            return string.Empty;
        }

        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    public override string ToString() =>
        $"WsGroup({WsGroupId}, ch={ChannelId}, profile={FunctionProfile}, name={WsGroupName})";
}

/// <summary>
/// Parsed OMNET message envelope.
/// All OMNET messages share a 4-byte prefix: Sender(2) + MessageType(2).
/// Additional fields depend on the message category.
/// </summary>
internal class OmnetMessage
{
    /// <summary>MCS user ID of the sender.</summary>
    public ushort Sender { get; init; }

    /// <summary>OMNET message type.</summary>
    public ushort MessageType { get; init; }

    // ── Joiner fields (HELLO, WELCOME) ───────────────────

    /// <summary>Compression capabilities bitfield.</summary>
    public uint CompressionCaps { get; init; }

    // ── Lock fields ──────────────────────────────────────

    public byte WsGroupId { get; init; }
    public byte WorksetId { get; init; }

    /// <summary>Correlator for LOCK_GRANT/DENY/NOTIFY, reserved for others.</summary>
    public ushort LockCorrelator { get; init; }

    // ── Workset group send fields ────────────────────────

    public ushort Correlator { get; init; }
    public OmnetObjectId ObjectId { get; init; }
    public uint MaxObjIdSeqUsed { get; init; }

    // ── Operation fields ─────────────────────────────────

    public byte Position { get; init; }
    public byte Flags { get; init; }
    public OmnetSeqStamp SeqStamp { get; init; }

    // ── Object data fields (ADD, REPLACE, UPDATE, CATCHUP) ──

    public uint TotalSize { get; init; }
    public uint UpdateSize { get; init; }
    public byte[] Data { get; init; }

    // ── OBJECT_CATCHUP additional stamps ─────────────────

    public OmnetSeqStamp PositionStamp { get; init; }
    public OmnetSeqStamp ReplaceStamp { get; init; }
    public OmnetSeqStamp UpdateStamp { get; init; }

    public override string ToString() =>
        $"OMNET {OmnetConstants.MessageName(MessageType)} from {Sender}";
}

/// <summary>
/// OMNET message codec — parses and builds all 20 OMNET message types.
/// All fields are little-endian per MS-MNPR.
/// </summary>
internal static class OmnetCodec
{
    // ──────────────────────────────────────────────────────────
    //  Detection
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Check if data looks like an OMNET message by examining the
    /// message type field at offset 2.
    /// </summary>
    public static bool IsOmnetMessage(byte[] data)
    {
        if (data == null || data.Length < 4)
        {
            return false;
        }

        var msgType = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));

        return msgType switch
        {
            OmnetConstants.MSG_HELLO or
            OmnetConstants.MSG_WELCOME or
            (>= OmnetConstants.MSG_LOCK_REQ and <= OmnetConstants.MSG_LOCK_NOTIFY) or
            (>= OmnetConstants.MSG_WSGROUP_SEND_REQ and <= OmnetConstants.MSG_WSGROUP_SEND_DENY) or
            OmnetConstants.MSG_WORKSET_CLEAR or
            OmnetConstants.MSG_WORKSET_NEW or
            OmnetConstants.MSG_WORKSET_CATCHUP or
            (>= OmnetConstants.MSG_OBJECT_ADD and <= OmnetConstants.MSG_OBJECT_MOVE) or
            OmnetConstants.MSG_MORE_DATA => true,
            _ => false
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Decode
    // ──────────────────────────────────────────────────────────

    /// <summary>Decode an OMNET message from raw bytes.</summary>
    public static OmnetMessage Decode(byte[] data)
    {
        if (data == null || data.Length < 4)
        {
            throw new ArgumentException("OMNET message too short");
        }

        var sender = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0));
        var msgType = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));

        return msgType switch
        {
            OmnetConstants.MSG_HELLO or
            OmnetConstants.MSG_WELCOME => DecodeJoiner(data, sender, msgType),

            OmnetConstants.MSG_LOCK_REQ or
            OmnetConstants.MSG_LOCK_GRANT or
            OmnetConstants.MSG_LOCK_DENY or
            OmnetConstants.MSG_UNLOCK or
            OmnetConstants.MSG_LOCK_NOTIFY => DecodeLock(data, sender, msgType),

            OmnetConstants.MSG_WSGROUP_SEND_REQ or
            OmnetConstants.MSG_WSGROUP_SEND_MIDWAY or
            OmnetConstants.MSG_WSGROUP_SEND_COMPLETE or
            OmnetConstants.MSG_WSGROUP_SEND_DENY => DecodeWsGroupSend(data, sender, msgType),

            OmnetConstants.MSG_WORKSET_CLEAR => DecodeWorksetClear(data, sender),
            OmnetConstants.MSG_WORKSET_NEW or
            OmnetConstants.MSG_WORKSET_CATCHUP => DecodeWorksetNewOrCatchup(data, sender, msgType),

            OmnetConstants.MSG_OBJECT_ADD => DecodeObjectAdd(data, sender),
            OmnetConstants.MSG_OBJECT_CATCHUP => DecodeObjectCatchup(data, sender),
            OmnetConstants.MSG_OBJECT_REPLACE or
            OmnetConstants.MSG_OBJECT_UPDATE => DecodeObjectReplaceOrUpdate(data, sender, msgType),
            OmnetConstants.MSG_OBJECT_DELETE or
            OmnetConstants.MSG_OBJECT_MOVE => DecodeOperationHeaderOnly(data, sender, msgType),

            OmnetConstants.MSG_MORE_DATA => DecodeMoreData(data, sender),

            _ => throw new NotSupportedException($"Unknown OMNET message type: 0x{msgType:X4}")
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Encode
    // ──────────────────────────────────────────────────────────

    /// <summary>Encode an OMNET HELLO message.</summary>
    public static byte[] EncodeHello(ushort sender, uint compressionCaps)
    {
        return EncodeJoiner(sender, OmnetConstants.MSG_HELLO, compressionCaps);
    }

    /// <summary>Encode an OMNET WELCOME message.</summary>
    public static byte[] EncodeWelcome(ushort sender, uint compressionCaps)
    {
        return EncodeJoiner(sender, OmnetConstants.MSG_WELCOME, compressionCaps);
    }

    /// <summary>Encode an OMNET lock message.</summary>
    public static byte[] EncodeLock(ushort sender, ushort msgType, byte wsGroupId,
        byte worksetId, ushort correlator = 0)
    {
        var data = new byte[OmnetConstants.LOCK_SIZE];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), sender);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), msgType);
        data[4] = wsGroupId;
        data[5] = worksetId;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), correlator);
        // Bytes 8-11 reserved (zeros)
        return data;
    }

    /// <summary>Encode an OMNET workset group send message.</summary>
    public static byte[] EncodeWsGroupSend(ushort sender, ushort msgType,
        byte wsGroupId, ushort correlator, OmnetObjectId objectId = null, uint maxObjIdSeqUsed = 0)
    {
        var data = new byte[OmnetConstants.WSGROUP_SEND_SIZE];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), sender);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), msgType);
        data[4] = wsGroupId;
        // data[5] = pad1
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), correlator);

        if (objectId != null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), objectId.Sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), objectId.Creator);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), maxObjIdSeqUsed);
        return data;
    }

    /// <summary>Encode an OMNET OBJECT_ADD message with object data.</summary>
    public static byte[] EncodeObjectAdd(ushort sender, byte wsGroupId, byte worksetId,
        byte position, OmnetSeqStamp seqStamp, OmnetObjectId objectId,
        byte[] objectData, uint totalSize = 0, uint updateSize = 0)
    {
        var dataLen = objectData?.Length ?? 0;
        var packet = new byte[OmnetConstants.OBJECT_ADD_HEADER_SIZE + dataLen];

        WriteOperationHeader(packet, sender, OmnetConstants.MSG_OBJECT_ADD,
            wsGroupId, worksetId, position, 0, seqStamp, objectId);

        var total = totalSize > 0 ? totalSize : (uint)dataLen;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(24), total);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28), updateSize);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(32), (uint)dataLen);

        if (objectData != null)
        {
            Array.Copy(objectData, 0, packet, 36, dataLen);
        }

        return packet;
    }

    /// <summary>Encode an OMNET OBJECT_DELETE message.</summary>
    public static byte[] EncodeObjectDelete(ushort sender, byte wsGroupId, byte worksetId,
        OmnetSeqStamp seqStamp, OmnetObjectId objectId)
    {
        var packet = new byte[OmnetConstants.OPERATION_HEADER_SIZE];

        WriteOperationHeader(packet, sender, OmnetConstants.MSG_OBJECT_DELETE,
            wsGroupId, worksetId, 0, 0, seqStamp, objectId);

        return packet;
    }

    /// <summary>Encode an OMNET OBJECT_REPLACE or OBJECT_UPDATE message.</summary>
    public static byte[] EncodeObjectReplaceOrUpdate(ushort sender, ushort msgType,
        byte wsGroupId, byte worksetId, OmnetSeqStamp seqStamp, OmnetObjectId objectId,
        byte[] objectData, uint totalSize = 0)
    {
        var dataLen = objectData?.Length ?? 0;
        var packet = new byte[OmnetConstants.OBJECT_REPLACE_HEADER_SIZE + dataLen];

        WriteOperationHeader(packet, sender, msgType,
            wsGroupId, worksetId, 0, 0, seqStamp, objectId);

        var total = totalSize > 0 ? totalSize : (uint)dataLen;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(24), total);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28), (uint)dataLen);

        if (objectData != null)
        {
            Array.Copy(objectData, 0, packet, 32, dataLen);
        }

        return packet;
    }

    /// <summary>Encode an OMNET OBJECT_MOVE message.</summary>
    public static byte[] EncodeObjectMove(ushort sender, byte wsGroupId, byte worksetId,
        byte position, OmnetSeqStamp seqStamp, OmnetObjectId objectId)
    {
        var packet = new byte[OmnetConstants.OPERATION_HEADER_SIZE];

        WriteOperationHeader(packet, sender, OmnetConstants.MSG_OBJECT_MOVE,
            wsGroupId, worksetId, position, 0, seqStamp, objectId);

        return packet;
    }

    /// <summary>Encode an OMNET WORKSET_NEW message.</summary>
    public static byte[] EncodeWorksetNew(ushort sender, byte wsGroupId, byte worksetId,
        OmnetSeqStamp seqStamp, OmnetObjectId objectId, byte position = 0, byte flags = 0)
    {
        var packet = new byte[OmnetConstants.OPERATION_HEADER_SIZE];

        WriteOperationHeader(packet, sender, OmnetConstants.MSG_WORKSET_NEW,
            wsGroupId, worksetId, position, flags, seqStamp, objectId);

        return packet;
    }

    /// <summary>Encode an OMNET WORKSET_CLEAR message.</summary>
    public static byte[] EncodeWorksetClear(ushort sender, byte wsGroupId, byte worksetId,
        OmnetSeqStamp clearStamp)
    {
        var packet = new byte[OmnetConstants.WORKSET_CLEAR_SIZE];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0), sender);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), OmnetConstants.MSG_WORKSET_CLEAR);
        packet[4] = wsGroupId;
        packet[5] = worksetId;
        // position=0, flags=0
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), clearStamp.GenNumber);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), clearStamp.NodeId);
        // pad at 14-15
        return packet;
    }

    /// <summary>Encode an OMNET MORE_DATA continuation message.</summary>
    public static byte[] EncodeMoreData(ushort sender, byte[] objectData)
    {
        var dataLen = objectData?.Length ?? 0;
        var packet = new byte[OmnetConstants.MORE_DATA_HEADER_SIZE + dataLen];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0), sender);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), OmnetConstants.MSG_MORE_DATA);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)dataLen);

        if (objectData != null)
        {
            Array.Copy(objectData, 0, packet, 8, dataLen);
        }

        return packet;
    }

    // ──────────────────────────────────────────────────────────
    //  Internal decode helpers
    // ──────────────────────────────────────────────────────────

    private static OmnetMessage DecodeJoiner(byte[] data, ushort sender, ushort msgType)
    {
        if (data.Length < OmnetConstants.JOINER_SIZE)
        {
            throw new ArgumentException($"Joiner message too short: {data.Length}");
        }

        return new OmnetMessage
        {
            Sender = sender,
            MessageType = msgType,
            CompressionCaps = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8))
        };
    }

    private static OmnetMessage DecodeLock(byte[] data, ushort sender, ushort msgType)
    {
        if (data.Length < OmnetConstants.LOCK_SIZE)
        {
            throw new ArgumentException($"Lock message too short: {data.Length}");
        }

        return new OmnetMessage
        {
            Sender = sender,
            MessageType = msgType,
            WsGroupId = data[4],
            WorksetId = data[5],
            LockCorrelator = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6))
        };
    }

    private static OmnetMessage DecodeWsGroupSend(byte[] data, ushort sender, ushort msgType)
    {
        if (data.Length < OmnetConstants.WSGROUP_SEND_SIZE)
        {
            throw new ArgumentException($"WsGroupSend message too short: {data.Length}");
        }

        return new OmnetMessage
        {
            Sender = sender,
            MessageType = msgType,
            WsGroupId = data[4],
            Correlator = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)),
            ObjectId = new OmnetObjectId
            {
                Sequence = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)),
                Creator = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12))
            },
            MaxObjIdSeqUsed = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16))
        };
    }

    private static OmnetMessage DecodeWorksetClear(byte[] data, ushort sender)
    {
        if (data.Length < OmnetConstants.WORKSET_CLEAR_SIZE)
        {
            throw new ArgumentException($"WORKSET_CLEAR too short: {data.Length}");
        }

        return new OmnetMessage
        {
            Sender = sender,
            MessageType = OmnetConstants.MSG_WORKSET_CLEAR,
            WsGroupId = data[4],
            WorksetId = data[5],
            SeqStamp = new OmnetSeqStamp
            {
                GenNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)),
                NodeId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12))
            }
        };
    }

    private static OmnetMessage DecodeWorksetNewOrCatchup(byte[] data, ushort sender, ushort msgType)
    {
        if (data.Length < OmnetConstants.OPERATION_HEADER_SIZE)
        {
            throw new ArgumentException($"{OmnetConstants.MessageName(msgType)} too short: {data.Length}");
        }

        return DecodeOperationHeader(data, sender, msgType);
    }

    private static OmnetMessage DecodeObjectAdd(byte[] data, ushort sender)
    {
        if (data.Length < OmnetConstants.OBJECT_ADD_HEADER_SIZE)
        {
            throw new ArgumentException($"OBJECT_ADD too short: {data.Length}");
        }

        var msg = DecodeOperationHeader(data, sender, OmnetConstants.MSG_OBJECT_ADD);
        var totalSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24));
        var updateSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28));
        var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32));

        byte[] objectData = null;
        if (dataLength > 0 && data.Length >= OmnetConstants.OBJECT_ADD_HEADER_SIZE + (int)dataLength)
        {
            objectData = new byte[dataLength];
            Array.Copy(data, OmnetConstants.OBJECT_ADD_HEADER_SIZE, objectData, 0, (int)dataLength);
        }

        return new OmnetMessage
        {
            Sender = msg.Sender,
            MessageType = msg.MessageType,
            WsGroupId = msg.WsGroupId,
            WorksetId = msg.WorksetId,
            Position = msg.Position,
            Flags = msg.Flags,
            SeqStamp = msg.SeqStamp,
            ObjectId = msg.ObjectId,
            TotalSize = totalSize,
            UpdateSize = updateSize,
            Data = objectData
        };
    }

    private static OmnetMessage DecodeObjectCatchup(byte[] data, ushort sender)
    {
        if (data.Length < OmnetConstants.OBJECT_CATCHUP_HEADER_SIZE)
        {
            throw new ArgumentException($"OBJECT_CATCHUP too short: {data.Length}");
        }

        var msg = DecodeOperationHeader(data, sender, OmnetConstants.MSG_OBJECT_CATCHUP);
        var totalSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24));
        var updateSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28));

        var posStamp = new OmnetSeqStamp
        {
            GenNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32)),
            NodeId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(36))
        };
        var replStamp = new OmnetSeqStamp
        {
            GenNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(40)),
            NodeId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(44))
        };
        var updStamp = new OmnetSeqStamp
        {
            GenNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(48)),
            NodeId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(52))
        };

        var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(56));
        byte[] objectData = null;
        if (dataLength > 0 && data.Length >= OmnetConstants.OBJECT_CATCHUP_HEADER_SIZE + (int)dataLength)
        {
            objectData = new byte[dataLength];
            Array.Copy(data, OmnetConstants.OBJECT_CATCHUP_HEADER_SIZE, objectData, 0, (int)dataLength);
        }

        return new OmnetMessage
        {
            Sender = msg.Sender,
            MessageType = msg.MessageType,
            WsGroupId = msg.WsGroupId,
            WorksetId = msg.WorksetId,
            Position = msg.Position,
            Flags = msg.Flags,
            SeqStamp = msg.SeqStamp,
            ObjectId = msg.ObjectId,
            TotalSize = totalSize,
            UpdateSize = updateSize,
            PositionStamp = posStamp,
            ReplaceStamp = replStamp,
            UpdateStamp = updStamp,
            Data = objectData
        };
    }

    private static OmnetMessage DecodeObjectReplaceOrUpdate(byte[] data, ushort sender, ushort msgType)
    {
        if (data.Length < OmnetConstants.OBJECT_REPLACE_HEADER_SIZE)
        {
            throw new ArgumentException($"{OmnetConstants.MessageName(msgType)} too short: {data.Length}");
        }

        var msg = DecodeOperationHeader(data, sender, msgType);
        var totalSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24));
        var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28));

        byte[] objectData = null;
        if (dataLength > 0 && data.Length >= OmnetConstants.OBJECT_REPLACE_HEADER_SIZE + (int)dataLength)
        {
            objectData = new byte[dataLength];
            Array.Copy(data, OmnetConstants.OBJECT_REPLACE_HEADER_SIZE, objectData, 0, (int)dataLength);
        }

        return new OmnetMessage
        {
            Sender = msg.Sender,
            MessageType = msg.MessageType,
            WsGroupId = msg.WsGroupId,
            WorksetId = msg.WorksetId,
            Position = msg.Position,
            Flags = msg.Flags,
            SeqStamp = msg.SeqStamp,
            ObjectId = msg.ObjectId,
            TotalSize = totalSize,
            Data = objectData
        };
    }

    private static OmnetMessage DecodeOperationHeaderOnly(byte[] data, ushort sender, ushort msgType)
    {
        if (data.Length < OmnetConstants.OPERATION_HEADER_SIZE)
        {
            throw new ArgumentException($"{OmnetConstants.MessageName(msgType)} too short: {data.Length}");
        }

        return DecodeOperationHeader(data, sender, msgType);
    }

    private static OmnetMessage DecodeMoreData(byte[] data, ushort sender)
    {
        if (data.Length < OmnetConstants.MORE_DATA_HEADER_SIZE)
        {
            throw new ArgumentException($"MORE_DATA too short: {data.Length}");
        }

        var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        byte[] objectData = null;
        if (dataLength > 0 && data.Length >= OmnetConstants.MORE_DATA_HEADER_SIZE + (int)dataLength)
        {
            objectData = new byte[dataLength];
            Array.Copy(data, OmnetConstants.MORE_DATA_HEADER_SIZE, objectData, 0, (int)dataLength);
        }

        return new OmnetMessage
        {
            Sender = sender,
            MessageType = OmnetConstants.MSG_MORE_DATA,
            Data = objectData
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Internal encode/decode shared helpers
    // ──────────────────────────────────────────────────────────

    private static byte[] EncodeJoiner(ushort sender, ushort msgType, uint compressionCaps)
    {
        var data = new byte[OmnetConstants.JOINER_SIZE];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), sender);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), msgType);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), OmnetConstants.CAPS_LENGTH);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), compressionCaps);
        return data;
    }

    private static OmnetMessage DecodeOperationHeader(byte[] data, ushort sender, ushort msgType)
    {
        return new OmnetMessage
        {
            Sender = sender,
            MessageType = msgType,
            WsGroupId = data[4],
            WorksetId = data[5],
            Position = data[6],
            Flags = data[7],
            SeqStamp = new OmnetSeqStamp
            {
                GenNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)),
                NodeId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12))
            },
            ObjectId = new OmnetObjectId
            {
                Sequence = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)),
                Creator = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20))
            }
        };
    }

    private static void WriteOperationHeader(byte[] packet, ushort sender, ushort msgType,
        byte wsGroupId, byte worksetId, byte position, byte flags,
        OmnetSeqStamp seqStamp, OmnetObjectId objectId)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0), sender);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), msgType);
        packet[4] = wsGroupId;
        packet[5] = worksetId;
        packet[6] = position;
        packet[7] = flags;

        if (seqStamp != null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), seqStamp.GenNumber);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), seqStamp.NodeId);
        }

        if (objectId != null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), objectId.Sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), objectId.Creator);
        }
    }
}
