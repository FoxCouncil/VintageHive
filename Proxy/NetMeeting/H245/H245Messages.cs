// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.H225;

namespace VintageHive.Proxy.NetMeeting.H245;

/// <summary>
/// Parsed H.245 MultimediaSystemControlMessage envelope.
/// </summary>
internal class H245Message
{
    /// <summary>Top-level CHOICE: request(0), response(1), command(2), indication(3).</summary>
    public int TopLevel { get; init; }

    /// <summary>Sub-CHOICE index within request/response/command/indication.</summary>
    public int SubIndex { get; init; }

    // ── Request types ──
    public MasterSlaveDetermination MasterSlaveDetermination { get; init; }
    public TerminalCapabilitySet TerminalCapabilitySet { get; init; }
    public OpenLogicalChannel OpenLogicalChannel { get; init; }
    public CloseLogicalChannel CloseLogicalChannel { get; init; }
    public RoundTripDelayRequest RoundTripDelayRequest { get; init; }

    // ── Response types ──
    public MasterSlaveDeterminationAck MasterSlaveDeterminationAck { get; init; }
    public MasterSlaveDeterminationReject MasterSlaveDeterminationReject { get; init; }
    public TerminalCapabilitySetAck TerminalCapabilitySetAck { get; init; }
    public TerminalCapabilitySetReject TerminalCapabilitySetReject { get; init; }
    public OpenLogicalChannelAck OpenLogicalChannelAck { get; init; }
    public OpenLogicalChannelReject OpenLogicalChannelReject { get; init; }
    public CloseLogicalChannelAck CloseLogicalChannelAck { get; init; }

    /// <summary>
    /// Raw payload bytes for messages we don't deeply decode
    /// (commands, indications, non-standard, etc.).
    /// </summary>
    public byte[] RawPayload { get; init; }

    public override string ToString()
    {
        return H245Constants.MessageName(TopLevel, SubIndex);
    }
}

// ──────────────────────────────────────────────────────────
//  Request data types
// ──────────────────────────────────────────────────────────

internal class MasterSlaveDetermination
{
    /// <summary>Terminal type value (0..255). Higher = more likely master.</summary>
    public int TerminalType { get; init; }

    /// <summary>Random 24-bit number for tie-breaking (0..16777215).</summary>
    public int StatusDeterminationNumber { get; init; }
}

internal class TerminalCapabilitySet
{
    /// <summary>Sequence number (0..255) for matching request to ack.</summary>
    public int SequenceNumber { get; init; }

    /// <summary>Protocol identifier OID.</summary>
    public int[] ProtocolIdentifier { get; init; }

    /// <summary>Raw capability table entries (not deeply parsed, passed through).</summary>
    public byte[] RawCapabilityTable { get; init; }

    /// <summary>Raw capability descriptors (not deeply parsed, passed through).</summary>
    public byte[] RawCapabilityDescriptors { get; init; }
}

internal class OpenLogicalChannel
{
    /// <summary>Logical channel number (1..65535).</summary>
    public int ForwardLogicalChannelNumber { get; init; }

    /// <summary>DataType CHOICE index (nullData=1, videoData=2, audioData=3, etc.).</summary>
    public int DataType { get; init; }

    /// <summary>Session ID from H2250LogicalChannelParameters (1=audio, 2=video).</summary>
    public int SessionId { get; init; }

    /// <summary>RTP media channel transport address (where to send media).</summary>
    public IPEndPoint MediaChannel { get; init; }

    /// <summary>RTCP media control channel transport address.</summary>
    public IPEndPoint MediaControlChannel { get; init; }
}

internal class CloseLogicalChannel
{
    /// <summary>Logical channel number being closed.</summary>
    public int ForwardLogicalChannelNumber { get; init; }

    /// <summary>Source of close: 0 = user, 1 = lcse.</summary>
    public int Source { get; init; }
}

internal class RoundTripDelayRequest
{
    /// <summary>Sequence number (0..255) for matching request to response.</summary>
    public int SequenceNumber { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Response data types
// ──────────────────────────────────────────────────────────

internal class MasterSlaveDeterminationAck
{
    /// <summary>Decision: 0 = master, 1 = slave.</summary>
    public int Decision { get; init; }
}

internal class MasterSlaveDeterminationReject
{
    /// <summary>Cause: 0 = identicalNumbers.</summary>
    public int Cause { get; init; }
}

internal class TerminalCapabilitySetAck
{
    /// <summary>Echoed sequence number.</summary>
    public int SequenceNumber { get; init; }
}

internal class TerminalCapabilitySetReject
{
    /// <summary>Echoed sequence number.</summary>
    public int SequenceNumber { get; init; }

    /// <summary>Cause CHOICE index.</summary>
    public int Cause { get; init; }
}

internal class OpenLogicalChannelAck
{
    /// <summary>Echoed logical channel number.</summary>
    public int ForwardLogicalChannelNumber { get; init; }

    /// <summary>RTP media channel (from H2250LogicalChannelAckParameters).</summary>
    public IPEndPoint MediaChannel { get; init; }

    /// <summary>RTCP media control channel (from H2250LogicalChannelAckParameters).</summary>
    public IPEndPoint MediaControlChannel { get; init; }

    /// <summary>Session ID (from H2250LogicalChannelAckParameters).</summary>
    public int? SessionId { get; init; }
}

internal class OpenLogicalChannelReject
{
    /// <summary>Echoed logical channel number.</summary>
    public int ForwardLogicalChannelNumber { get; init; }

    /// <summary>Cause CHOICE index.</summary>
    public int Cause { get; init; }
}

internal class CloseLogicalChannelAck
{
    /// <summary>Echoed logical channel number.</summary>
    public int ForwardLogicalChannelNumber { get; init; }
}

/// <summary>
/// PER codec for H.245 MultimediaSystemControlMessage.
/// Decodes/encodes TPKT-framed H.245 messages used during call control.
///
/// MultimediaSystemControlMessage ::= CHOICE {
///     request    RequestMessage,     -- 0
///     response   ResponseMessage,    -- 1
///     command    CommandMessage,      -- 2
///     indication IndicationMessage   -- 3
/// }
/// </summary>
internal static class H245Codec
{
    // ──────────────────────────────────────────────────────────
    //  Decode
    // ──────────────────────────────────────────────────────────

    public static H245Message Decode(byte[] data)
    {
        var dec = new PerDecoder(data);

        // Top-level CHOICE (4 root, NOT extensible)
        var topLevel = dec.ReadChoiceIndex(H245Constants.MSG_ROOT_COUNT);

        return topLevel switch
        {
            H245Constants.MSG_REQUEST => DecodeRequest(dec),
            H245Constants.MSG_RESPONSE => DecodeResponse(dec),
            _ => new H245Message { TopLevel = topLevel, SubIndex = -1, RawPayload = data }
        };
    }

    private static H245Message DecodeRequest(PerDecoder dec)
    {
        // RequestMessage CHOICE (11 root, extensible)
        var subIndex = dec.ReadChoiceIndex(H245Constants.REQ_ROOT_COUNT, extensible: true);

        switch (subIndex)
        {
            case H245Constants.REQ_MASTER_SLAVE_DETERMINATION:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_REQUEST,
                    SubIndex = subIndex,
                    MasterSlaveDetermination = DecodeMasterSlaveDetermination(dec)
                };
            }

            case H245Constants.REQ_TERMINAL_CAPABILITY_SET:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_REQUEST,
                    SubIndex = subIndex,
                    TerminalCapabilitySet = DecodeTerminalCapabilitySet(dec)
                };
            }

            case H245Constants.REQ_OPEN_LOGICAL_CHANNEL:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_REQUEST,
                    SubIndex = subIndex,
                    OpenLogicalChannel = DecodeOpenLogicalChannel(dec)
                };
            }

            case H245Constants.REQ_CLOSE_LOGICAL_CHANNEL:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_REQUEST,
                    SubIndex = subIndex,
                    CloseLogicalChannel = DecodeCloseLogicalChannel(dec)
                };
            }

            case H245Constants.REQ_ROUND_TRIP_DELAY_REQUEST:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_REQUEST,
                    SubIndex = subIndex,
                    RoundTripDelayRequest = DecodeRoundTripDelayRequest(dec)
                };
            }

            default:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_REQUEST,
                    SubIndex = subIndex
                };
            }
        }
    }

    private static H245Message DecodeResponse(PerDecoder dec)
    {
        // ResponseMessage CHOICE (15 root, extensible)
        var subIndex = dec.ReadChoiceIndex(H245Constants.RSP_ROOT_COUNT, extensible: true);

        switch (subIndex)
        {
            case H245Constants.RSP_MASTER_SLAVE_DETERMINATION_ACK:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_RESPONSE,
                    SubIndex = subIndex,
                    MasterSlaveDeterminationAck = DecodeMasterSlaveDeterminationAck(dec)
                };
            }

            case H245Constants.RSP_MASTER_SLAVE_DETERMINATION_REJECT:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_RESPONSE,
                    SubIndex = subIndex,
                    MasterSlaveDeterminationReject = DecodeMasterSlaveDeterminationReject(dec)
                };
            }

            case H245Constants.RSP_TERMINAL_CAPABILITY_SET_ACK:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_RESPONSE,
                    SubIndex = subIndex,
                    TerminalCapabilitySetAck = DecodeTerminalCapabilitySetAck(dec)
                };
            }

            case H245Constants.RSP_TERMINAL_CAPABILITY_SET_REJECT:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_RESPONSE,
                    SubIndex = subIndex,
                    TerminalCapabilitySetReject = DecodeTerminalCapabilitySetReject(dec)
                };
            }

            case H245Constants.RSP_OPEN_LOGICAL_CHANNEL_ACK:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_RESPONSE,
                    SubIndex = subIndex,
                    OpenLogicalChannelAck = DecodeOpenLogicalChannelAck(dec)
                };
            }

            case H245Constants.RSP_OPEN_LOGICAL_CHANNEL_REJECT:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_RESPONSE,
                    SubIndex = subIndex,
                    OpenLogicalChannelReject = DecodeOpenLogicalChannelReject(dec)
                };
            }

            case H245Constants.RSP_CLOSE_LOGICAL_CHANNEL_ACK:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_RESPONSE,
                    SubIndex = subIndex,
                    CloseLogicalChannelAck = DecodeCloseLogicalChannelAck(dec)
                };
            }

            default:
            {
                return new H245Message
                {
                    TopLevel = H245Constants.MSG_RESPONSE,
                    SubIndex = subIndex
                };
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Request decoders
    // ──────────────────────────────────────────────────────────

    private static MasterSlaveDetermination DecodeMasterSlaveDetermination(PerDecoder dec)
    {
        // MasterSlaveDetermination ::= SEQUENCE { ..., terminalType, statusDeterminationNumber }
        var hasExt = dec.ReadExtensionBit();

        var terminalType = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.TERMINAL_TYPE_MIN, H245Constants.TERMINAL_TYPE_MAX);
        var statusNum = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.STATUS_DETERMINATION_MIN, H245Constants.STATUS_DETERMINATION_MAX);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new MasterSlaveDetermination
        {
            TerminalType = terminalType,
            StatusDeterminationNumber = statusNum
        };
    }

    private static TerminalCapabilitySet DecodeTerminalCapabilitySet(PerDecoder dec)
    {
        // TerminalCapabilitySet ::= SEQUENCE {
        //   sequenceNumber INTEGER (0..255),
        //   protocolIdentifier OBJECT IDENTIFIER,
        //   multiplexCapability MultiplexCapability OPTIONAL,    -- [0]
        //   capabilityTable SET OF CapabilityTableEntry OPTIONAL, -- [1]
        //   capabilityDescriptors SET OF CapabilityDescriptor OPTIONAL, -- [2]
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(3);

        var seqNum = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.TCS_SEQ_MIN, H245Constants.TCS_SEQ_MAX);
        var protocolId = dec.ReadObjectIdentifier();

        // MultiplexCapability OPTIONAL — skip if present
        if (opts[0])
        {
            SkipMultiplexCapability(dec);
        }

        // We capture remaining data as raw bytes for pass-through
        // (capability tables are deeply nested and not needed for proxying)
        byte[] rawCapTable = null;
        byte[] rawCapDescriptors = null;

        // For now, skip capability table and descriptors
        if (opts[1])
        {
            rawCapTable = SkipAndCaptureSetOf(dec);
        }

        if (opts[2])
        {
            rawCapDescriptors = SkipAndCaptureSetOf(dec);
        }

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new TerminalCapabilitySet
        {
            SequenceNumber = seqNum,
            ProtocolIdentifier = protocolId,
            RawCapabilityTable = rawCapTable,
            RawCapabilityDescriptors = rawCapDescriptors
        };
    }

    private static OpenLogicalChannel DecodeOpenLogicalChannel(PerDecoder dec)
    {
        // OpenLogicalChannel ::= SEQUENCE {
        //   forwardLogicalChannelNumber LogicalChannelNumber,
        //   forwardLogicalChannelParameters SEQUENCE {
        //     portNumber INTEGER (0..65535) OPTIONAL,                -- [0]
        //     dataType DataType,
        //     multiplexParameters CHOICE { ... },
        //     ...
        //   },
        //   reverseLogicalChannelParameters SEQUENCE { ... } OPTIONAL,  -- [0]
        //   ...
        //   -- extension: separateStack (used for H.245 tunneling)
        // }
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(1); // reverseLogicalChannelParameters

        var lcn = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.LCN_MIN, H245Constants.LCN_MAX);

        // forwardLogicalChannelParameters SEQUENCE (extensible)
        var fwdHasExt = dec.ReadExtensionBit();
        var fwdOpts = dec.ReadOptionalBitmap(1); // portNumber

        if (fwdOpts[0])
        {
            dec.ReadConstrainedWholeNumber(0, 65535); // portNumber — skip
        }

        // DataType CHOICE (6 root, extensible)
        var dataType = dec.ReadChoiceIndex(H245Constants.DATA_TYPE_ROOT_COUNT, extensible: true);

        // Skip the actual data type contents (AudioCapability, VideoCapability, etc.)
        // These are deeply nested extensible types — we just need the CHOICE index
        SkipDataTypeContents(dec, dataType);

        // multiplexParameters CHOICE
        // For H.323/H.225: h2250LogicalChannelParameters (CHOICE index 0 in the
        // extensible CHOICE with 3 root alternatives for v2)
        int sessionId = 0;
        IPEndPoint mediaChannel = null;
        IPEndPoint mediaControlChannel = null;

        var muxChoice = dec.ReadChoiceIndex(3, extensible: true);

        if (muxChoice == 0)
        {
            // H2250LogicalChannelParameters SEQUENCE (extensible)
            // SEQUENCE {
            //   nonStandard        SEQUENCE OF NonStandardParameter OPTIONAL,  -- [0]
            //   sessionID          INTEGER (0..255),
            //   associatedSessionID INTEGER (1..255) OPTIONAL,                  -- [1]
            //   mediaChannel       TransportAddress OPTIONAL,                   -- [2]
            //   mediaGuaranteedDelivery BOOLEAN OPTIONAL,                       -- [3]
            //   mediaControlChannel TransportAddress OPTIONAL,                  -- [4]
            //   mediaControlGuaranteedDelivery BOOLEAN OPTIONAL,                -- [5]
            //   ...
            // }
            var h2250HasExt = dec.ReadExtensionBit();
            var h2250Opts = dec.ReadOptionalBitmap(6);

            // nonStandard OPTIONAL
            if (h2250Opts[0])
            {
                var nsCount = dec.ReadLengthDeterminant();
                for (var i = 0; i < nsCount; i++)
                {
                    H225Types.SkipNonStandardParameter(dec);
                }
            }

            sessionId = (int)dec.ReadConstrainedWholeNumber(0, 255);

            // associatedSessionID OPTIONAL
            if (h2250Opts[1])
            {
                dec.ReadConstrainedWholeNumber(1, 255);
            }

            // mediaChannel OPTIONAL
            if (h2250Opts[2])
            {
                mediaChannel = H225Types.ReadTransportAddress(dec);
            }

            // mediaGuaranteedDelivery OPTIONAL
            if (h2250Opts[3])
            {
                dec.ReadBoolean();
            }

            // mediaControlChannel OPTIONAL
            if (h2250Opts[4])
            {
                mediaControlChannel = H225Types.ReadTransportAddress(dec);
            }

            // mediaControlGuaranteedDelivery OPTIONAL
            if (h2250Opts[5])
            {
                dec.ReadBoolean();
            }

            if (h2250HasExt)
            {
                try { dec.ReadExtensionAdditions(); } catch { }
            }
        }

        // Skip forward extensions
        if (fwdHasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        // reverseLogicalChannelParameters OPTIONAL — skip
        if (opts[0])
        {
            SkipReverseLogicalChannelParameters(dec);
        }

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new OpenLogicalChannel
        {
            ForwardLogicalChannelNumber = lcn,
            DataType = dataType,
            SessionId = sessionId,
            MediaChannel = mediaChannel,
            MediaControlChannel = mediaControlChannel
        };
    }

    private static CloseLogicalChannel DecodeCloseLogicalChannel(PerDecoder dec)
    {
        // CloseLogicalChannel ::= SEQUENCE {
        //   forwardLogicalChannelNumber LogicalChannelNumber,
        //   source CHOICE { user(0), lcse(1) },
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        var lcn = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.LCN_MIN, H245Constants.LCN_MAX);

        var source = dec.ReadChoiceIndex(H245Constants.CLC_SOURCE_ROOT_COUNT);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new CloseLogicalChannel
        {
            ForwardLogicalChannelNumber = lcn,
            Source = source
        };
    }

    private static RoundTripDelayRequest DecodeRoundTripDelayRequest(PerDecoder dec)
    {
        // RoundTripDelayRequest ::= SEQUENCE {
        //   sequenceNumber INTEGER (0..255),
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        var seqNum = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.RTD_SEQ_MIN, H245Constants.RTD_SEQ_MAX);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new RoundTripDelayRequest { SequenceNumber = seqNum };
    }

    // ──────────────────────────────────────────────────────────
    //  Response decoders
    // ──────────────────────────────────────────────────────────

    private static MasterSlaveDeterminationAck DecodeMasterSlaveDeterminationAck(PerDecoder dec)
    {
        // MasterSlaveDeterminationAck ::= SEQUENCE {
        //   decision CHOICE { master(0), slave(1) },
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        var decision = dec.ReadChoiceIndex(H245Constants.MSD_DECISION_ROOT_COUNT);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new MasterSlaveDeterminationAck { Decision = decision };
    }

    private static MasterSlaveDeterminationReject DecodeMasterSlaveDeterminationReject(PerDecoder dec)
    {
        // MasterSlaveDeterminationReject ::= SEQUENCE {
        //   cause CHOICE { identicalNumbers(0) },
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        var cause = dec.ReadChoiceIndex(H245Constants.MSD_REJECT_ROOT_COUNT);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new MasterSlaveDeterminationReject { Cause = cause };
    }

    private static TerminalCapabilitySetAck DecodeTerminalCapabilitySetAck(PerDecoder dec)
    {
        // TerminalCapabilitySetAck ::= SEQUENCE {
        //   sequenceNumber INTEGER (0..255),
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        var seqNum = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.TCS_SEQ_MIN, H245Constants.TCS_SEQ_MAX);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new TerminalCapabilitySetAck { SequenceNumber = seqNum };
    }

    private static TerminalCapabilitySetReject DecodeTerminalCapabilitySetReject(PerDecoder dec)
    {
        // TerminalCapabilitySetReject ::= SEQUENCE {
        //   sequenceNumber INTEGER (0..255),
        //   cause CHOICE { unspecified(0), undefinedTableEntryUsed(1),
        //     descriptorCapacityExceeded(2), tableEntryCapacityExceeded(3) },
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        var seqNum = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.TCS_SEQ_MIN, H245Constants.TCS_SEQ_MAX);

        var cause = dec.ReadChoiceIndex(H245Constants.TCS_REJ_ROOT_COUNT, extensible: true);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new TerminalCapabilitySetReject { SequenceNumber = seqNum, Cause = cause };
    }

    private static OpenLogicalChannelAck DecodeOpenLogicalChannelAck(PerDecoder dec)
    {
        // OpenLogicalChannelAck ::= SEQUENCE {
        //   forwardLogicalChannelNumber LogicalChannelNumber,
        //   reverseLogicalChannelParameters SEQUENCE { ... } OPTIONAL,  -- [0]
        //   ...
        //   -- extension: forwardMultiplexAckParameters CHOICE { h2250LogicalChannelAckParameters }
        // }
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(1); // reverseLogicalChannelParameters

        var lcn = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.LCN_MIN, H245Constants.LCN_MAX);

        // reverseLogicalChannelParameters OPTIONAL — skip
        if (opts[0])
        {
            SkipReverseLogicalChannelParameters(dec);
        }

        IPEndPoint mediaChannel = null;
        IPEndPoint mediaControlChannel = null;
        int? sessionId = null;

        // Extension additions — look for forwardMultiplexAckParameters
        if (hasExt && dec.HasData)
        {
            try
            {
                var extensions = dec.ReadExtensionAdditions();

                // First extension addition = forwardMultiplexAckParameters
                if (extensions.Length > 0 && extensions[0] != null)
                {
                    var extDec = new PerDecoder(extensions[0]);

                    // forwardMultiplexAckParameters CHOICE (1 root, extensible)
                    // Only alternative: h2250LogicalChannelAckParameters (index 0)
                    var fmapChoice = extDec.ReadChoiceIndex(1, extensible: true);

                    if (fmapChoice == 0)
                    {
                        // H2250LogicalChannelAckParameters SEQUENCE (extensible)
                        // SEQUENCE {
                        //   nonStandard SEQUENCE OF NonStandardParameter OPTIONAL, -- [0]
                        //   mediaChannel TransportAddress OPTIONAL,                -- [1]
                        //   mediaControlChannel TransportAddress OPTIONAL,         -- [2]
                        //   sessionID INTEGER (1..255) OPTIONAL,                   -- [3]
                        //   ...
                        // }
                        var ackHasExt = extDec.ReadExtensionBit();
                        var ackOpts = extDec.ReadOptionalBitmap(4);

                        // nonStandard OPTIONAL
                        if (ackOpts[0])
                        {
                            var nsCount = extDec.ReadLengthDeterminant();
                            for (var i = 0; i < nsCount; i++)
                            {
                                H225Types.SkipNonStandardParameter(extDec);
                            }
                        }

                        if (ackOpts[1])
                        {
                            mediaChannel = H225Types.ReadTransportAddress(extDec);
                        }

                        if (ackOpts[2])
                        {
                            mediaControlChannel = H225Types.ReadTransportAddress(extDec);
                        }

                        if (ackOpts[3])
                        {
                            sessionId = (int)extDec.ReadConstrainedWholeNumber(1, 255);
                        }
                    }
                }
            }
            catch
            {
                // Best-effort extension parsing
            }
        }

        return new OpenLogicalChannelAck
        {
            ForwardLogicalChannelNumber = lcn,
            MediaChannel = mediaChannel,
            MediaControlChannel = mediaControlChannel,
            SessionId = sessionId
        };
    }

    private static OpenLogicalChannelReject DecodeOpenLogicalChannelReject(PerDecoder dec)
    {
        // OpenLogicalChannelReject ::= SEQUENCE {
        //   forwardLogicalChannelNumber LogicalChannelNumber,
        //   cause CHOICE { ... 6 root, extensible },
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        var lcn = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.LCN_MIN, H245Constants.LCN_MAX);

        var cause = dec.ReadChoiceIndex(H245Constants.OLC_REJ_ROOT_COUNT, extensible: true);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new OpenLogicalChannelReject
        {
            ForwardLogicalChannelNumber = lcn,
            Cause = cause
        };
    }

    private static CloseLogicalChannelAck DecodeCloseLogicalChannelAck(PerDecoder dec)
    {
        // CloseLogicalChannelAck ::= SEQUENCE {
        //   forwardLogicalChannelNumber LogicalChannelNumber,
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        var lcn = (int)dec.ReadConstrainedWholeNumber(
            H245Constants.LCN_MIN, H245Constants.LCN_MAX);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new CloseLogicalChannelAck { ForwardLogicalChannelNumber = lcn };
    }

    // ──────────────────────────────────────────────────────────
    //  Encode
    // ──────────────────────────────────────────────────────────

    public static byte[] EncodeMasterSlaveDetermination(MasterSlaveDetermination msd)
    {
        var enc = new PerEncoder();

        // Top-level: request (0)
        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        // RequestMessage: MasterSlaveDetermination (1)
        enc.WriteChoiceIndex(H245Constants.REQ_MASTER_SLAVE_DETERMINATION,
            H245Constants.REQ_ROOT_COUNT, extensible: true);

        // SEQUENCE (extensible)
        enc.WriteExtensionBit(false);
        enc.WriteConstrainedWholeNumber(msd.TerminalType,
            H245Constants.TERMINAL_TYPE_MIN, H245Constants.TERMINAL_TYPE_MAX);
        enc.WriteConstrainedWholeNumber(msd.StatusDeterminationNumber,
            H245Constants.STATUS_DETERMINATION_MIN, H245Constants.STATUS_DETERMINATION_MAX);

        return enc.ToArray();
    }

    public static byte[] EncodeMasterSlaveDeterminationAck(MasterSlaveDeterminationAck ack)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.RSP_MASTER_SLAVE_DETERMINATION_ACK,
            H245Constants.RSP_ROOT_COUNT, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(ack.Decision, H245Constants.MSD_DECISION_ROOT_COUNT);

        return enc.ToArray();
    }

    public static byte[] EncodeMasterSlaveDeterminationReject(MasterSlaveDeterminationReject rej)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.RSP_MASTER_SLAVE_DETERMINATION_REJECT,
            H245Constants.RSP_ROOT_COUNT, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(rej.Cause, H245Constants.MSD_REJECT_ROOT_COUNT);

        return enc.ToArray();
    }

    public static byte[] EncodeTerminalCapabilitySetAck(TerminalCapabilitySetAck ack)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.RSP_TERMINAL_CAPABILITY_SET_ACK,
            H245Constants.RSP_ROOT_COUNT, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteConstrainedWholeNumber(ack.SequenceNumber,
            H245Constants.TCS_SEQ_MIN, H245Constants.TCS_SEQ_MAX);

        return enc.ToArray();
    }

    public static byte[] EncodeTerminalCapabilitySetReject(TerminalCapabilitySetReject rej)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.RSP_TERMINAL_CAPABILITY_SET_REJECT,
            H245Constants.RSP_ROOT_COUNT, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteConstrainedWholeNumber(rej.SequenceNumber,
            H245Constants.TCS_SEQ_MIN, H245Constants.TCS_SEQ_MAX);
        enc.WriteChoiceIndex(rej.Cause, H245Constants.TCS_REJ_ROOT_COUNT, extensible: true);

        return enc.ToArray();
    }

    public static byte[] EncodeCloseLogicalChannel(CloseLogicalChannel clc)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.REQ_CLOSE_LOGICAL_CHANNEL,
            H245Constants.REQ_ROOT_COUNT, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteConstrainedWholeNumber(clc.ForwardLogicalChannelNumber,
            H245Constants.LCN_MIN, H245Constants.LCN_MAX);
        enc.WriteChoiceIndex(clc.Source, H245Constants.CLC_SOURCE_ROOT_COUNT);

        return enc.ToArray();
    }

    public static byte[] EncodeCloseLogicalChannelAck(CloseLogicalChannelAck ack)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.RSP_CLOSE_LOGICAL_CHANNEL_ACK,
            H245Constants.RSP_ROOT_COUNT, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteConstrainedWholeNumber(ack.ForwardLogicalChannelNumber,
            H245Constants.LCN_MIN, H245Constants.LCN_MAX);

        return enc.ToArray();
    }

    public static byte[] EncodeOpenLogicalChannelReject(OpenLogicalChannelReject rej)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.RSP_OPEN_LOGICAL_CHANNEL_REJECT,
            H245Constants.RSP_ROOT_COUNT, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteConstrainedWholeNumber(rej.ForwardLogicalChannelNumber,
            H245Constants.LCN_MIN, H245Constants.LCN_MAX);
        enc.WriteChoiceIndex(rej.Cause, H245Constants.OLC_REJ_ROOT_COUNT, extensible: true);

        return enc.ToArray();
    }

    public static byte[] EncodeRoundTripDelayRequest(RoundTripDelayRequest req)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.REQ_ROUND_TRIP_DELAY_REQUEST,
            H245Constants.REQ_ROOT_COUNT, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteConstrainedWholeNumber(req.SequenceNumber,
            H245Constants.RTD_SEQ_MIN, H245Constants.RTD_SEQ_MAX);

        return enc.ToArray();
    }

    // ──────────────────────────────────────────────────────────
    //  Skip helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Skip DataType contents after CHOICE index has been read.
    /// These are deeply nested capability types — we skip them wholesale.
    /// </summary>
    private static void SkipDataTypeContents(PerDecoder dec, int dataTypeChoice)
    {
        switch (dataTypeChoice)
        {
            case H245Constants.DATA_TYPE_NON_STANDARD:
            {
                H225Types.SkipNonStandardParameter(dec);
            }
            break;

            case H245Constants.DATA_TYPE_NULL_DATA:
            {
                // NULL — nothing to read
            }
            break;

            case H245Constants.DATA_TYPE_VIDEO_DATA:
            case H245Constants.DATA_TYPE_AUDIO_DATA:
            case H245Constants.DATA_TYPE_DATA:
            case H245Constants.DATA_TYPE_ENCRYPT_DATA:
            {
                // These are deeply nested extensible CHOICEs (AudioCapability,
                // VideoCapability, etc.) with many alternatives.
                // For proxy purposes we don't need to decode them — we forward raw.
                // Best-effort: skip the extensible CHOICE contents.
                SkipCapabilityChoice(dec);
            }
            break;

            default:
            {
                // Extension — was read as open type, nothing more to skip
            }
            break;
        }
    }

    /// <summary>
    /// Best-effort skip for a capability CHOICE (AudioCapability, VideoCapability, etc.).
    /// These are extensible CHOICEs with many nested types.
    /// We read the choice index, then skip the SEQUENCE contents.
    /// </summary>
    private static void SkipCapabilityChoice(PerDecoder dec)
    {
        // AudioCapability has 14 root alternatives (extensible)
        // VideoCapability has 4 root alternatives (extensible)
        // DataApplicationCapability is a SEQUENCE
        // EncryptionMode has 2 root alternatives
        // Since we don't know which data type this is at this point,
        // we can't reliably skip arbitrary nested content.
        // The caller should use raw byte forwarding for the entire message.
        // This is only called during decode for inspection; the full message
        // bytes are always forwarded intact by the H245Handler.
    }

    private static void SkipMultiplexCapability(PerDecoder dec)
    {
        // MultiplexCapability CHOICE (4 root, extensible)
        // We skip it by reading choice index, then skipping the SEQUENCE
        var choice = dec.ReadChoiceIndex(4, extensible: true);

        // H222Capability(0), H223Capability(1), H226Capability(2), H2250Capability(3)
        // All are extensible SEQUENCEs. For NetMeeting H.323, index 3 is used.
        // Skip by reading extension bit and handling the SEQUENCE
        if (choice == 3)
        {
            // H2250Capability — complex extensible SEQUENCE
            // Best-effort: read extension bit + optionals + known fields
            var hasExt = dec.ReadExtensionBit();
            var opts = dec.ReadOptionalBitmap(1); // nonStandard

            dec.ReadConstrainedWholeNumber(0, 255); // maximumAudioDelayJitter

            // ReceiveAndTransmitMultiplexCapability
            // receiveMultipointCapability, transmitMultipointCapability, etc.
            // These are complex — skip remaining as best-effort
            SkipMultipointCapability(dec); // receiveMultipointCapability
            SkipMultipointCapability(dec); // transmitMultipointCapability
            SkipMultipointCapability(dec); // receiveAndTransmitMultipointCapability

            // mcCapability SEQUENCE { centralizedConferenceMC, decentralizedConferenceMC }
            dec.ReadBoolean(); // centralizedConferenceMC
            dec.ReadBoolean(); // decentralizedConferenceMC

            if (opts[0])
            {
                var nsCount = dec.ReadLengthDeterminant();
                for (var i = 0; i < nsCount; i++)
                {
                    H225Types.SkipNonStandardParameter(dec);
                }
            }

            if (hasExt)
            {
                try { dec.ReadExtensionAdditions(); } catch { }
            }
        }
        // For other mux types, let parsing continue (they're rare in H.323)
    }

    private static void SkipMultipointCapability(PerDecoder dec)
    {
        // MultipointCapability ::= SEQUENCE {
        //   multicastCapability BOOLEAN,
        //   multiUniCastConference BOOLEAN,
        //   mediaDistributionCapability SEQUENCE OF MediaDistributionCapability,
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();

        dec.ReadBoolean(); // multicastCapability
        dec.ReadBoolean(); // multiUniCastConference

        // SEQUENCE OF MediaDistributionCapability
        var count = dec.ReadLengthDeterminant();
        for (var i = 0; i < count; i++)
        {
            SkipMediaDistributionCapability(dec);
        }

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }
    }

    private static void SkipMediaDistributionCapability(PerDecoder dec)
    {
        // MediaDistributionCapability ::= SEQUENCE {
        //   centralizedControl  BOOLEAN,
        //   distributedControl  BOOLEAN,
        //   centralizedAudio    BOOLEAN,
        //   distributedAudio    BOOLEAN,
        //   centralizedVideo    BOOLEAN,
        //   distributedVideo    BOOLEAN,
        //   centralizedData     SEQUENCE OF DataApplicationCapability OPTIONAL,  -- [0]
        //   distributedData     SEQUENCE OF DataApplicationCapability OPTIONAL,  -- [1]
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(2);

        for (var i = 0; i < 6; i++)
        {
            dec.ReadBoolean();
        }

        // Skip data capability sequences if present
        // These are deeply nested — best-effort
        if (opts[0])
        {
            SkipAndCaptureSetOf(dec);
        }

        if (opts[1])
        {
            SkipAndCaptureSetOf(dec);
        }

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }
    }

    private static void SkipReverseLogicalChannelParameters(PerDecoder dec)
    {
        // reverseLogicalChannelParameters SEQUENCE {
        //   dataType DataType,
        //   multiplexParameters CHOICE { ... } OPTIONAL,  -- [0]
        //   ...
        // }
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(1);

        var dataType = dec.ReadChoiceIndex(H245Constants.DATA_TYPE_ROOT_COUNT, extensible: true);
        SkipDataTypeContents(dec, dataType);

        if (opts[0])
        {
            // multiplexParameters CHOICE — skip
            var muxChoice = dec.ReadChoiceIndex(2, extensible: true);
            if (muxChoice == 0)
            {
                // h2250LogicalChannelParameters — extensible SEQUENCE
                var h2250HasExt = dec.ReadExtensionBit();
                var h2250Opts = dec.ReadOptionalBitmap(6);

                if (h2250Opts[0])
                {
                    var nsCount = dec.ReadLengthDeterminant();
                    for (var i = 0; i < nsCount; i++)
                    {
                        H225Types.SkipNonStandardParameter(dec);
                    }
                }

                dec.ReadConstrainedWholeNumber(0, 255); // sessionID

                if (h2250Opts[1]) { dec.ReadConstrainedWholeNumber(1, 255); }
                if (h2250Opts[2]) { H225Types.SkipTransportAddress(dec); }
                if (h2250Opts[3]) { dec.ReadBoolean(); }
                if (h2250Opts[4]) { H225Types.SkipTransportAddress(dec); }
                if (h2250Opts[5]) { dec.ReadBoolean(); }

                if (h2250HasExt)
                {
                    try { dec.ReadExtensionAdditions(); } catch { }
                }
            }
        }

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }
    }

    /// <summary>
    /// Skip a SET OF / SEQUENCE OF by reading the length determinant
    /// and consuming all remaining items. Returns null (items are discarded).
    /// </summary>
    private static byte[] SkipAndCaptureSetOf(PerDecoder dec)
    {
        // The length determinant gives the count of elements.
        // We can't reliably skip complex nested elements without full type knowledge.
        // For pass-through proxy purposes, we rely on the entire message being forwarded
        // as raw bytes — this method is a best-effort placeholder.
        //
        // In practice, TCS capability tables aren't needed for call proxying.
        // The H245Handler forwards complete TPKT frames without re-encoding.
        return null;
    }
}
