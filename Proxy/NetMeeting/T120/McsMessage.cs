// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.T120;

/// <summary>
/// MCS (T.125 Multipoint Communication Service) message types and codec.
///
/// Connection phase: BER-encoded Connect-Initial (tag 101) / Connect-Response (tag 102).
/// Domain phase: PER-encoded domain PDUs (ErectDomain, AttachUser, ChannelJoin, SendData).
/// </summary>
internal static class McsConstants
{
    // ──────────────────────────────────────────────────────────
    //  BER APPLICATION tags for Connect PDUs
    // ──────────────────────────────────────────────────────────

    /// <summary>Connect-Initial: APPLICATION [101] — 0x7F 0x65</summary>
    public const byte CONNECT_INITIAL_TAG_1 = 0x7F;
    public const byte CONNECT_INITIAL_TAG_2 = 0x65;

    /// <summary>Connect-Response: APPLICATION [102] — 0x7F 0x66</summary>
    public const byte CONNECT_RESPONSE_TAG_1 = 0x7F;
    public const byte CONNECT_RESPONSE_TAG_2 = 0x66;

    // ──────────────────────────────────────────────────────────
    //  Domain PDU types (PER CHOICE indices)
    //  DomainMCSPDU ::= CHOICE { ... }
    // ──────────────────────────────────────────────────────────

    public const int DOMAIN_ERECT_DOMAIN_REQUEST = 1;
    public const int DOMAIN_ATTACH_USER_REQUEST = 10;
    public const int DOMAIN_ATTACH_USER_CONFIRM = 11;
    public const int DOMAIN_CHANNEL_JOIN_REQUEST = 14;
    public const int DOMAIN_CHANNEL_JOIN_CONFIRM = 15;
    public const int DOMAIN_DISCONNECT_PROVIDER_ULTIMATUM = 8;
    public const int DOMAIN_SEND_DATA_REQUEST = 25;
    public const int DOMAIN_SEND_DATA_INDICATION = 26;

    /// <summary>Number of root alternatives in DomainMCSPDU CHOICE.</summary>
    public const int DOMAIN_ROOT_COUNT = 28;

    // ──────────────────────────────────────────────────────────
    //  Result enum (used in AttachUserConfirm, ChannelJoinConfirm)
    // ──────────────────────────────────────────────────────────

    public const int RESULT_SUCCESSFUL = 0;
    public const int RESULT_DOMAIN_MERGED = 1;
    public const int RESULT_DOMAIN_NOT_HIERARCHICAL = 2;
    public const int RESULT_NO_SUCH_CHANNEL = 3;
    public const int RESULT_NO_SUCH_DOMAIN = 4;
    public const int RESULT_NO_SUCH_USER = 5;
    public const int RESULT_NOT_ADMITTED = 6;
    public const int RESULT_OTHER_USER_ID = 7;
    public const int RESULT_PARAMETERS_UNACCEPTABLE = 8;
    public const int RESULT_TOKEN_NOT_AVAILABLE = 9;
    public const int RESULT_TOKEN_NOT_POSSESSED = 10;
    public const int RESULT_TOO_MANY_CHANNELS = 11;
    public const int RESULT_TOO_MANY_TOKENS = 12;
    public const int RESULT_TOO_MANY_USERS = 13;
    public const int RESULT_UNSPECIFIED_FAILURE = 14;
    public const int RESULT_USER_REJECTED = 15;
    public const int RESULT_ROOT_COUNT = 16;

    // ──────────────────────────────────────────────────────────
    //  Data priority
    // ──────────────────────────────────────────────────────────

    public const int PRIORITY_TOP = 0;
    public const int PRIORITY_HIGH = 1;
    public const int PRIORITY_MEDIUM = 2;
    public const int PRIORITY_LOW = 3;
    public const int PRIORITY_ROOT_COUNT = 4;

    // ──────────────────────────────────────────────────────────
    //  Segmentation flags (2-bit field)
    // ──────────────────────────────────────────────────────────

    public const int SEGMENTATION_BEGIN = 0x80;
    public const int SEGMENTATION_END = 0x40;

    // ──────────────────────────────────────────────────────────
    //  Default domain parameters
    // ──────────────────────────────────────────────────────────

    public const int DEFAULT_MAX_CHANNEL_IDS = 34;
    public const int DEFAULT_MAX_USER_IDS = 2;
    public const int DEFAULT_MAX_TOKEN_IDS = 0;
    public const int DEFAULT_NUM_PRIORITIES = 1;
    public const int DEFAULT_MIN_THROUGHPUT = 0;
    public const int DEFAULT_MAX_HEIGHT = 1;
    public const int DEFAULT_MAX_MCS_PDU_SIZE = 65535;
    public const int DEFAULT_PROTOCOL_VERSION = 2;

    /// <summary>Return a friendly name for a domain PDU type.</summary>
    public static string DomainPduName(int index)
    {
        return index switch
        {
            DOMAIN_ERECT_DOMAIN_REQUEST => "ErectDomainRequest",
            DOMAIN_ATTACH_USER_REQUEST => "AttachUserRequest",
            DOMAIN_ATTACH_USER_CONFIRM => "AttachUserConfirm",
            DOMAIN_CHANNEL_JOIN_REQUEST => "ChannelJoinRequest",
            DOMAIN_CHANNEL_JOIN_CONFIRM => "ChannelJoinConfirm",
            DOMAIN_DISCONNECT_PROVIDER_ULTIMATUM => "DisconnectProviderUltimatum",
            DOMAIN_SEND_DATA_REQUEST => "SendDataRequest",
            DOMAIN_SEND_DATA_INDICATION => "SendDataIndication",
            _ => $"DomainPDU({index})"
        };
    }
}

// ──────────────────────────────────────────────────────────
//  MCS Connect-Initial / Connect-Response (BER-encoded)
// ──────────────────────────────────────────────────────────

/// <summary>
/// Domain parameters carried in Connect-Initial/Response.
/// </summary>
internal class McsDomainParameters
{
    public int MaxChannelIds { get; init; }
    public int MaxUserIds { get; init; }
    public int MaxTokenIds { get; init; }
    public int NumPriorities { get; init; }
    public int MinThroughput { get; init; }
    public int MaxHeight { get; init; }
    public int MaxMcsPduSize { get; init; }
    public int ProtocolVersion { get; init; }

    public static McsDomainParameters Default => new()
    {
        MaxChannelIds = McsConstants.DEFAULT_MAX_CHANNEL_IDS,
        MaxUserIds = McsConstants.DEFAULT_MAX_USER_IDS,
        MaxTokenIds = McsConstants.DEFAULT_MAX_TOKEN_IDS,
        NumPriorities = McsConstants.DEFAULT_NUM_PRIORITIES,
        MinThroughput = McsConstants.DEFAULT_MIN_THROUGHPUT,
        MaxHeight = McsConstants.DEFAULT_MAX_HEIGHT,
        MaxMcsPduSize = McsConstants.DEFAULT_MAX_MCS_PDU_SIZE,
        ProtocolVersion = McsConstants.DEFAULT_PROTOCOL_VERSION
    };
}

/// <summary>Parsed MCS Connect-Initial message.</summary>
internal class McsConnectInitial
{
    public McsDomainParameters CallingDomainParameters { get; init; }
    public McsDomainParameters CalledDomainParameters { get; init; }
    public McsDomainParameters MinimumDomainParameters { get; init; }
    public McsDomainParameters MaximumDomainParameters { get; init; }
    public byte[] UserData { get; init; }
}

/// <summary>Parsed MCS Connect-Response message.</summary>
internal class McsConnectResponse
{
    public int Result { get; init; }
    public int CalledConnectId { get; init; }
    public McsDomainParameters DomainParameters { get; init; }
    public byte[] UserData { get; init; }
}

// ──────────────────────────────────────────────────────────
//  MCS Domain PDU envelope
// ──────────────────────────────────────────────────────────

/// <summary>Parsed MCS domain PDU.</summary>
internal class McsDomainPdu
{
    /// <summary>DomainMCSPDU CHOICE index.</summary>
    public int Type { get; init; }

    // ErectDomainRequest
    public int SubHeight { get; init; }
    public int SubInterval { get; init; }

    // AttachUserConfirm
    public int Result { get; init; }
    public int Initiator { get; init; }

    // ChannelJoinRequest / ChannelJoinConfirm
    public int UserId { get; init; }
    public int ChannelId { get; init; }

    // SendDataRequest / SendDataIndication
    public int DataPriority { get; init; }
    public byte[] UserData { get; init; }

    public override string ToString() => McsConstants.DomainPduName(Type);
}

/// <summary>
/// BER codec for MCS Connect-Initial/Response and PER codec for domain PDUs.
/// </summary>
internal static class McsCodec
{
    // ──────────────────────────────────────────────────────────
    //  Connect-Initial (BER decode)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Detect if data starts with MCS Connect-Initial tag (0x7F 0x65).
    /// </summary>
    public static bool IsConnectInitial(byte[] data)
    {
        return data != null && data.Length >= 2 &&
               data[0] == McsConstants.CONNECT_INITIAL_TAG_1 &&
               data[1] == McsConstants.CONNECT_INITIAL_TAG_2;
    }

    /// <summary>
    /// Detect if data starts with MCS Connect-Response tag (0x7F 0x66).
    /// </summary>
    public static bool IsConnectResponse(byte[] data)
    {
        return data != null && data.Length >= 2 &&
               data[0] == McsConstants.CONNECT_RESPONSE_TAG_1 &&
               data[1] == McsConstants.CONNECT_RESPONSE_TAG_2;
    }

    /// <summary>
    /// Parse an MCS Connect-Initial from BER-encoded data.
    /// </summary>
    public static McsConnectInitial DecodeConnectInitial(byte[] data)
    {
        var offset = 0;

        // APPLICATION [101] tag
        if (data[offset++] != McsConstants.CONNECT_INITIAL_TAG_1 ||
            data[offset++] != McsConstants.CONNECT_INITIAL_TAG_2)
        {
            throw new ArgumentException("Not an MCS Connect-Initial");
        }

        var totalLen = ReadBerLength(data, ref offset);

        // callingDomainSelector OCTET STRING
        SkipBerOctetString(data, ref offset);

        // calledDomainSelector OCTET STRING
        SkipBerOctetString(data, ref offset);

        // upwardFlag BOOLEAN
        SkipBerBoolean(data, ref offset);

        // targetParameters DomainParameters
        var targetParams = ReadBerDomainParameters(data, ref offset);

        // minimumParameters DomainParameters
        var minParams = ReadBerDomainParameters(data, ref offset);

        // maximumParameters DomainParameters
        var maxParams = ReadBerDomainParameters(data, ref offset);

        // userData OCTET STRING
        var userData = ReadBerOctetString(data, ref offset);

        return new McsConnectInitial
        {
            CallingDomainParameters = targetParams,
            MinimumDomainParameters = minParams,
            MaximumDomainParameters = maxParams,
            UserData = userData
        };
    }

    /// <summary>
    /// Parse an MCS Connect-Response from BER-encoded data.
    /// </summary>
    public static McsConnectResponse DecodeConnectResponse(byte[] data)
    {
        var offset = 0;

        if (data[offset++] != McsConstants.CONNECT_RESPONSE_TAG_1 ||
            data[offset++] != McsConstants.CONNECT_RESPONSE_TAG_2)
        {
            throw new ArgumentException("Not an MCS Connect-Response");
        }

        var totalLen = ReadBerLength(data, ref offset);

        // result ENUMERATED
        var result = ReadBerEnumerated(data, ref offset);

        // calledConnectId INTEGER
        var connectId = ReadBerInteger(data, ref offset);

        // domainParameters DomainParameters
        var domainParams = ReadBerDomainParameters(data, ref offset);

        // userData OCTET STRING
        var userData = ReadBerOctetString(data, ref offset);

        return new McsConnectResponse
        {
            Result = result,
            CalledConnectId = connectId,
            DomainParameters = domainParams,
            UserData = userData
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Connect-Initial (BER encode)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Build an MCS Connect-Initial message.
    /// </summary>
    public static byte[] EncodeConnectInitial(McsConnectInitial ci)
    {
        var inner = new MemoryStream();

        // callingDomainSelector OCTET STRING (empty)
        WriteBerOctetString(inner, Array.Empty<byte>());

        // calledDomainSelector OCTET STRING (empty)
        WriteBerOctetString(inner, Array.Empty<byte>());

        // upwardFlag BOOLEAN TRUE
        WriteBerBoolean(inner, true);

        // targetParameters
        WriteBerDomainParameters(inner, ci.CallingDomainParameters ?? McsDomainParameters.Default);

        // minimumParameters
        WriteBerDomainParameters(inner, ci.MinimumDomainParameters ?? McsDomainParameters.Default);

        // maximumParameters
        WriteBerDomainParameters(inner, ci.MaximumDomainParameters ?? McsDomainParameters.Default);

        // userData
        WriteBerOctetString(inner, ci.UserData ?? Array.Empty<byte>());

        var innerBytes = inner.ToArray();

        var result = new MemoryStream();
        result.WriteByte(McsConstants.CONNECT_INITIAL_TAG_1);
        result.WriteByte(McsConstants.CONNECT_INITIAL_TAG_2);
        WriteBerLength(result, innerBytes.Length);
        result.Write(innerBytes);

        return result.ToArray();
    }

    /// <summary>
    /// Build an MCS Connect-Response message.
    /// </summary>
    public static byte[] EncodeConnectResponse(McsConnectResponse cr)
    {
        var inner = new MemoryStream();

        // result ENUMERATED
        WriteBerEnumerated(inner, cr.Result);

        // calledConnectId INTEGER
        WriteBerInteger(inner, cr.CalledConnectId);

        // domainParameters
        WriteBerDomainParameters(inner, cr.DomainParameters ?? McsDomainParameters.Default);

        // userData
        WriteBerOctetString(inner, cr.UserData ?? Array.Empty<byte>());

        var innerBytes = inner.ToArray();

        var result = new MemoryStream();
        result.WriteByte(McsConstants.CONNECT_RESPONSE_TAG_1);
        result.WriteByte(McsConstants.CONNECT_RESPONSE_TAG_2);
        WriteBerLength(result, innerBytes.Length);
        result.Write(innerBytes);

        return result.ToArray();
    }

    // ──────────────────────────────────────────────────────────
    //  Domain PDUs (PER decode)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a PER-encoded MCS domain PDU.
    /// </summary>
    public static McsDomainPdu DecodeDomainPdu(byte[] data)
    {
        var dec = new PerDecoder(data);

        // DomainMCSPDU CHOICE (28 root, NOT extensible)
        var type = dec.ReadChoiceIndex(McsConstants.DOMAIN_ROOT_COUNT);

        switch (type)
        {
            case McsConstants.DOMAIN_ERECT_DOMAIN_REQUEST:
            {
                var subHeight = (int)dec.ReadConstrainedWholeNumber(0, int.MaxValue);
                var subInterval = (int)dec.ReadConstrainedWholeNumber(0, int.MaxValue);

                return new McsDomainPdu
                {
                    Type = type,
                    SubHeight = subHeight,
                    SubInterval = subInterval
                };
            }

            case McsConstants.DOMAIN_ATTACH_USER_REQUEST:
            {
                return new McsDomainPdu { Type = type };
            }

            case McsConstants.DOMAIN_ATTACH_USER_CONFIRM:
            {
                var result = (int)dec.ReadEnumerated(McsConstants.RESULT_ROOT_COUNT);
                var hasInitiator = dec.ReadBit();
                var initiator = 0;

                if (hasInitiator)
                {
                    initiator = (int)dec.ReadConstrainedWholeNumber(1, 65535);
                }

                return new McsDomainPdu
                {
                    Type = type,
                    Result = result,
                    Initiator = initiator
                };
            }

            case McsConstants.DOMAIN_CHANNEL_JOIN_REQUEST:
            {
                var userId = (int)dec.ReadConstrainedWholeNumber(1, 65535);
                var channelId = (int)dec.ReadConstrainedWholeNumber(0, 65535);

                return new McsDomainPdu
                {
                    Type = type,
                    UserId = userId,
                    ChannelId = channelId
                };
            }

            case McsConstants.DOMAIN_CHANNEL_JOIN_CONFIRM:
            {
                var result = (int)dec.ReadEnumerated(McsConstants.RESULT_ROOT_COUNT);
                var userId = (int)dec.ReadConstrainedWholeNumber(1, 65535);
                var requested = (int)dec.ReadConstrainedWholeNumber(0, 65535);
                var hasAssigned = dec.ReadBit();
                var assigned = requested;

                if (hasAssigned)
                {
                    assigned = (int)dec.ReadConstrainedWholeNumber(0, 65535);
                }

                return new McsDomainPdu
                {
                    Type = type,
                    Result = result,
                    UserId = userId,
                    ChannelId = assigned
                };
            }

            case McsConstants.DOMAIN_SEND_DATA_REQUEST:
            case McsConstants.DOMAIN_SEND_DATA_INDICATION:
            {
                var initiator = (int)dec.ReadConstrainedWholeNumber(1, 65535);
                var channelId = (int)dec.ReadConstrainedWholeNumber(0, 65535);
                var priority = (int)dec.ReadEnumerated(McsConstants.PRIORITY_ROOT_COUNT);

                // Segmentation: 2-bit field
                dec.ReadBits(2);

                // userData OCTET STRING (unconstrained length)
                var userData = dec.ReadOctetString();

                return new McsDomainPdu
                {
                    Type = type,
                    Initiator = initiator,
                    ChannelId = channelId,
                    DataPriority = priority,
                    UserData = userData
                };
            }

            case McsConstants.DOMAIN_DISCONNECT_PROVIDER_ULTIMATUM:
            {
                var reason = (int)dec.ReadEnumerated(3); // 3 root alternatives
                return new McsDomainPdu { Type = type, Result = reason };
            }

            default:
            {
                return new McsDomainPdu { Type = type };
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Domain PDUs (PER encode)
    // ──────────────────────────────────────────────────────────

    public static byte[] EncodeErectDomainRequest(int subHeight = 0, int subInterval = 0)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_ERECT_DOMAIN_REQUEST, McsConstants.DOMAIN_ROOT_COUNT);
        enc.WriteConstrainedWholeNumber(subHeight, 0, int.MaxValue);
        enc.WriteConstrainedWholeNumber(subInterval, 0, int.MaxValue);
        return enc.ToArray();
    }

    public static byte[] EncodeAttachUserRequest()
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_ATTACH_USER_REQUEST, McsConstants.DOMAIN_ROOT_COUNT);
        return enc.ToArray();
    }

    public static byte[] EncodeAttachUserConfirm(int result, int initiator = 0)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_ATTACH_USER_CONFIRM, McsConstants.DOMAIN_ROOT_COUNT);
        enc.WriteEnumerated(result, McsConstants.RESULT_ROOT_COUNT);
        enc.WriteBit(initiator != 0); // optional initiator present
        if (initiator != 0)
        {
            enc.WriteConstrainedWholeNumber(initiator, 1, 65535);
        }
        return enc.ToArray();
    }

    public static byte[] EncodeChannelJoinRequest(int userId, int channelId)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_CHANNEL_JOIN_REQUEST, McsConstants.DOMAIN_ROOT_COUNT);
        enc.WriteConstrainedWholeNumber(userId, 1, 65535);
        enc.WriteConstrainedWholeNumber(channelId, 0, 65535);
        return enc.ToArray();
    }

    public static byte[] EncodeChannelJoinConfirm(int result, int userId, int channelId)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_CHANNEL_JOIN_CONFIRM, McsConstants.DOMAIN_ROOT_COUNT);
        enc.WriteEnumerated(result, McsConstants.RESULT_ROOT_COUNT);
        enc.WriteConstrainedWholeNumber(userId, 1, 65535);
        enc.WriteConstrainedWholeNumber(channelId, 0, 65535);
        enc.WriteBit(true); // assigned channel present
        enc.WriteConstrainedWholeNumber(channelId, 0, 65535);
        return enc.ToArray();
    }

    public static byte[] EncodeSendDataRequest(int initiator, int channelId,
        int priority, byte[] userData)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_SEND_DATA_REQUEST, McsConstants.DOMAIN_ROOT_COUNT);
        enc.WriteConstrainedWholeNumber(initiator, 1, 65535);
        enc.WriteConstrainedWholeNumber(channelId, 0, 65535);
        enc.WriteEnumerated(priority, McsConstants.PRIORITY_ROOT_COUNT);
        enc.WriteBits(0xC0 >> 6, 2); // BEGIN + END segmentation
        enc.WriteOctetString(userData);
        return enc.ToArray();
    }

    public static byte[] EncodeSendDataIndication(int initiator, int channelId,
        int priority, byte[] userData)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_SEND_DATA_INDICATION, McsConstants.DOMAIN_ROOT_COUNT);
        enc.WriteConstrainedWholeNumber(initiator, 1, 65535);
        enc.WriteConstrainedWholeNumber(channelId, 0, 65535);
        enc.WriteEnumerated(priority, McsConstants.PRIORITY_ROOT_COUNT);
        enc.WriteBits(0xC0 >> 6, 2); // BEGIN + END
        enc.WriteOctetString(userData);
        return enc.ToArray();
    }

    public static byte[] EncodeDisconnectProviderUltimatum(int reason)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_DISCONNECT_PROVIDER_ULTIMATUM,
            McsConstants.DOMAIN_ROOT_COUNT);
        enc.WriteEnumerated(reason, 3);
        return enc.ToArray();
    }

    // ──────────────────────────────────────────────────────────
    //  BER helpers
    // ──────────────────────────────────────────────────────────

    internal static int ReadBerLength(byte[] data, ref int offset)
    {
        var first = data[offset++];

        if ((first & 0x80) == 0)
        {
            return first; // Short form
        }

        var numBytes = first & 0x7F;
        var length = 0;

        for (var i = 0; i < numBytes; i++)
        {
            length = (length << 8) | data[offset++];
        }

        return length;
    }

    internal static void WriteBerLength(Stream stream, int length)
    {
        if (length < 128)
        {
            stream.WriteByte((byte)length);
        }
        else if (length < 256)
        {
            stream.WriteByte(0x81);
            stream.WriteByte((byte)length);
        }
        else
        {
            stream.WriteByte(0x82);
            stream.WriteByte((byte)(length >> 8));
            stream.WriteByte((byte)(length & 0xFF));
        }
    }

    private static byte[] ReadBerOctetString(byte[] data, ref int offset)
    {
        var tag = data[offset++];
        // Tag should be 0x04 (UNIVERSAL OCTET STRING)
        var length = ReadBerLength(data, ref offset);
        var result = new byte[length];
        Array.Copy(data, offset, result, 0, length);
        offset += length;
        return result;
    }

    private static void SkipBerOctetString(byte[] data, ref int offset)
    {
        var tag = data[offset++];
        var length = ReadBerLength(data, ref offset);
        offset += length;
    }

    private static void SkipBerBoolean(byte[] data, ref int offset)
    {
        var tag = data[offset++]; // 0x01
        var length = ReadBerLength(data, ref offset);
        offset += length;
    }

    private static int ReadBerEnumerated(byte[] data, ref int offset)
    {
        var tag = data[offset++]; // 0x0A
        var length = ReadBerLength(data, ref offset);
        var value = 0;
        for (var i = 0; i < length; i++)
        {
            value = (value << 8) | data[offset++];
        }
        return value;
    }

    private static int ReadBerInteger(byte[] data, ref int offset)
    {
        var tag = data[offset++]; // 0x02
        var length = ReadBerLength(data, ref offset);
        var value = 0;
        for (var i = 0; i < length; i++)
        {
            value = (value << 8) | data[offset++];
        }
        return value;
    }

    private static McsDomainParameters ReadBerDomainParameters(byte[] data, ref int offset)
    {
        var tag = data[offset++]; // 0x30 (SEQUENCE)
        var length = ReadBerLength(data, ref offset);
        var end = offset + length;

        var maxChannelIds = ReadBerInteger(data, ref offset);
        var maxUserIds = ReadBerInteger(data, ref offset);
        var maxTokenIds = ReadBerInteger(data, ref offset);
        var numPriorities = ReadBerInteger(data, ref offset);
        var minThroughput = ReadBerInteger(data, ref offset);
        var maxHeight = ReadBerInteger(data, ref offset);
        var maxMcsPduSize = ReadBerInteger(data, ref offset);
        var protocolVersion = ReadBerInteger(data, ref offset);

        offset = end; // Skip any trailing data

        return new McsDomainParameters
        {
            MaxChannelIds = maxChannelIds,
            MaxUserIds = maxUserIds,
            MaxTokenIds = maxTokenIds,
            NumPriorities = numPriorities,
            MinThroughput = minThroughput,
            MaxHeight = maxHeight,
            MaxMcsPduSize = maxMcsPduSize,
            ProtocolVersion = protocolVersion
        };
    }

    private static void WriteBerOctetString(Stream stream, byte[] value)
    {
        stream.WriteByte(0x04); // UNIVERSAL OCTET STRING
        WriteBerLength(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteBerBoolean(Stream stream, bool value)
    {
        stream.WriteByte(0x01); // BOOLEAN
        stream.WriteByte(0x01); // Length = 1
        stream.WriteByte(value ? (byte)0xFF : (byte)0x00);
    }

    private static void WriteBerEnumerated(Stream stream, int value)
    {
        stream.WriteByte(0x0A); // ENUMERATED
        var bytes = MinimalIntegerBytes(value);
        WriteBerLength(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteBerInteger(Stream stream, int value)
    {
        stream.WriteByte(0x02); // INTEGER
        var bytes = MinimalIntegerBytes(value);
        WriteBerLength(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteBerDomainParameters(Stream stream, McsDomainParameters p)
    {
        var inner = new MemoryStream();
        WriteBerInteger(inner, p.MaxChannelIds);
        WriteBerInteger(inner, p.MaxUserIds);
        WriteBerInteger(inner, p.MaxTokenIds);
        WriteBerInteger(inner, p.NumPriorities);
        WriteBerInteger(inner, p.MinThroughput);
        WriteBerInteger(inner, p.MaxHeight);
        WriteBerInteger(inner, p.MaxMcsPduSize);
        WriteBerInteger(inner, p.ProtocolVersion);

        var innerBytes = inner.ToArray();
        stream.WriteByte(0x30); // SEQUENCE
        WriteBerLength(stream, innerBytes.Length);
        stream.Write(innerBytes);
    }

    private static byte[] MinimalIntegerBytes(int value)
    {
        if (value >= 0 && value <= 127)
        {
            return new[] { (byte)value };
        }

        if (value >= -128 && value <= 127)
        {
            return new[] { (byte)value };
        }

        if (value >= -32768 && value <= 32767)
        {
            return new[] { (byte)(value >> 8), (byte)(value & 0xFF) };
        }

        if (value >= -8388608 && value <= 8388607)
        {
            return new[] { (byte)(value >> 16), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF) };
        }

        return new[]
        {
            (byte)(value >> 24), (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)
        };
    }
}
