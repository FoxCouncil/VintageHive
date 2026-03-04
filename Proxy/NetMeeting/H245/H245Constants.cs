// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.H245;

/// <summary>
/// Constants for ITU-T H.245 multimedia system control protocol.
/// Based on H.245 v3 (1998) as used by Microsoft NetMeeting 2.x/3.x.
///
/// MultimediaSystemControlMessage is a CHOICE with 4 root alternatives
/// (NOT extensible at the top level per the ASN.1 module).
/// </summary>
internal static class H245Constants
{
    // ──────────────────────────────────────────────────────────
    //  MultimediaSystemControlMessage CHOICE (4 root, NOT extensible)
    // ──────────────────────────────────────────────────────────

    public const int MSG_REQUEST = 0;
    public const int MSG_RESPONSE = 1;
    public const int MSG_COMMAND = 2;
    public const int MSG_INDICATION = 3;
    public const int MSG_ROOT_COUNT = 4;

    // ──────────────────────────────────────────────────────────
    //  RequestMessage CHOICE (11 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int REQ_NON_STANDARD = 0;
    public const int REQ_MASTER_SLAVE_DETERMINATION = 1;
    public const int REQ_TERMINAL_CAPABILITY_SET = 2;
    public const int REQ_OPEN_LOGICAL_CHANNEL = 3;
    public const int REQ_CLOSE_LOGICAL_CHANNEL = 4;
    public const int REQ_REQUEST_CHANNEL_CLOSE = 5;
    public const int REQ_MULTIPLEX_ENTRY_SEND = 6;
    public const int REQ_REQUEST_MULTIPLEX_ENTRY = 7;
    public const int REQ_REQUEST_MODE = 8;
    public const int REQ_ROUND_TRIP_DELAY_REQUEST = 9;
    public const int REQ_MAINTENANCE_LOOP_REQUEST = 10;
    public const int REQ_ROOT_COUNT = 11;

    // ──────────────────────────────────────────────────────────
    //  ResponseMessage CHOICE (15 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int RSP_NON_STANDARD = 0;
    public const int RSP_MASTER_SLAVE_DETERMINATION_ACK = 1;
    public const int RSP_MASTER_SLAVE_DETERMINATION_REJECT = 2;
    public const int RSP_TERMINAL_CAPABILITY_SET_ACK = 3;
    public const int RSP_TERMINAL_CAPABILITY_SET_REJECT = 4;
    public const int RSP_OPEN_LOGICAL_CHANNEL_ACK = 5;
    public const int RSP_OPEN_LOGICAL_CHANNEL_REJECT = 6;
    public const int RSP_CLOSE_LOGICAL_CHANNEL_ACK = 7;
    public const int RSP_REQUEST_CHANNEL_CLOSE_ACK = 8;
    public const int RSP_REQUEST_CHANNEL_CLOSE_REJECT = 9;
    public const int RSP_MULTIPLEX_ENTRY_SEND_ACK = 10;
    public const int RSP_MULTIPLEX_ENTRY_SEND_REJECT = 11;
    public const int RSP_REQUEST_MULTIPLEX_ENTRY_ACK = 12;
    public const int RSP_REQUEST_MULTIPLEX_ENTRY_REJECT = 13;
    public const int RSP_REQUEST_MODE_ACK = 14;
    public const int RSP_ROOT_COUNT = 15;

    // ──────────────────────────────────────────────────────────
    //  CommandMessage CHOICE (3 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int CMD_NON_STANDARD = 0;
    public const int CMD_MAINTENANCE_LOOP_OFF = 1;
    public const int CMD_SEND_TERMINAL_CAPABILITY_SET = 2;
    public const int CMD_ROOT_COUNT = 3;

    // ──────────────────────────────────────────────────────────
    //  IndicationMessage CHOICE (8 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int IND_NON_STANDARD = 0;
    public const int IND_FUNCTION_NOT_UNDERSTOOD = 1;
    public const int IND_MASTER_SLAVE_DETERMINATION_RELEASE = 2;
    public const int IND_TERMINAL_CAPABILITY_SET_RELEASE = 3;
    public const int IND_OPEN_LOGICAL_CHANNEL_CONFIRM = 4;
    public const int IND_REQUEST_CHANNEL_CLOSE_RELEASE = 5;
    public const int IND_MULTIPLEX_ENTRY_SEND_RELEASE = 6;
    public const int IND_REQUEST_MULTIPLEX_ENTRY_RELEASE = 7;
    public const int IND_ROOT_COUNT = 8;

    // ──────────────────────────────────────────────────────────
    //  MasterSlaveDetermination
    // ──────────────────────────────────────────────────────────

    /// <summary>terminalType INTEGER (0..255)</summary>
    public const int TERMINAL_TYPE_MIN = 0;
    public const int TERMINAL_TYPE_MAX = 255;

    /// <summary>statusDeterminationNumber INTEGER (0..16777215) — 24-bit random</summary>
    public const int STATUS_DETERMINATION_MIN = 0;
    public const int STATUS_DETERMINATION_MAX = 16777215;

    /// <summary>MasterSlaveDeterminationAck decision: master</summary>
    public const int MSD_DECISION_MASTER = 0;

    /// <summary>MasterSlaveDeterminationAck decision: slave</summary>
    public const int MSD_DECISION_SLAVE = 1;

    /// <summary>MasterSlaveDetermination decision CHOICE root count.</summary>
    public const int MSD_DECISION_ROOT_COUNT = 2;

    // ──────────────────────────────────────────────────────────
    //  MasterSlaveDeterminationReject cause
    // ──────────────────────────────────────────────────────────

    /// <summary>identicalNumbers: both sides picked the same random number.</summary>
    public const int MSD_REJECT_IDENTICAL = 0;
    public const int MSD_REJECT_ROOT_COUNT = 1;

    // ──────────────────────────────────────────────────────────
    //  TerminalCapabilitySet
    // ──────────────────────────────────────────────────────────

    /// <summary>sequenceNumber INTEGER (0..255)</summary>
    public const int TCS_SEQ_MIN = 0;
    public const int TCS_SEQ_MAX = 255;

    // ──────────────────────────────────────────────────────────
    //  TerminalCapabilitySetReject cause
    // ──────────────────────────────────────────────────────────

    public const int TCS_REJ_UNSPECIFIED = 0;
    public const int TCS_REJ_UNDEFINED_TABLE_ENTRY_USED = 1;
    public const int TCS_REJ_DESCRIPTOR_CAPACITY_EXCEEDED = 2;
    public const int TCS_REJ_TABLE_ENTRY_CAPACITY_EXCEEDED = 3;
    public const int TCS_REJ_ROOT_COUNT = 4;

    // ──────────────────────────────────────────────────────────
    //  OpenLogicalChannel / DataType
    // ──────────────────────────────────────────────────────────

    /// <summary>forwardLogicalChannelNumber INTEGER (1..65535)</summary>
    public const int LCN_MIN = 1;
    public const int LCN_MAX = 65535;

    /// <summary>DataType CHOICE (6 root, extensible)</summary>
    public const int DATA_TYPE_NON_STANDARD = 0;
    public const int DATA_TYPE_NULL_DATA = 1;
    public const int DATA_TYPE_VIDEO_DATA = 2;
    public const int DATA_TYPE_AUDIO_DATA = 3;
    public const int DATA_TYPE_DATA = 4;
    public const int DATA_TYPE_ENCRYPT_DATA = 5;
    public const int DATA_TYPE_ROOT_COUNT = 6;

    /// <summary>H2250LogicalChannelParameters sessionID: 1 = audio, 2 = video, 3 = data.</summary>
    public const int SESSION_AUDIO = 1;
    public const int SESSION_VIDEO = 2;
    public const int SESSION_DATA = 3;

    // ──────────────────────────────────────────────────────────
    //  OpenLogicalChannelReject cause
    // ──────────────────────────────────────────────────────────

    public const int OLC_REJ_UNSPECIFIED = 0;
    public const int OLC_REJ_UNSUPPORTED_TYPE = 1;
    public const int OLC_REJ_DATA_TYPE_NOT_SUPPORTED = 2;
    public const int OLC_REJ_DATA_TYPE_NOT_AVAILABLE = 3;
    public const int OLC_REJ_UNKNOWN_DATA_TYPE = 4;
    public const int OLC_REJ_DATA_TYPE_AL_COMBINATION_NOT_SUPPORTED = 5;
    public const int OLC_REJ_ROOT_COUNT = 6;

    // ──────────────────────────────────────────────────────────
    //  CloseLogicalChannel source
    // ──────────────────────────────────────────────────────────

    /// <summary>CloseLogicalChannel source CHOICE: user initiated.</summary>
    public const int CLC_SOURCE_USER = 0;

    /// <summary>CloseLogicalChannel source CHOICE: logical channel signaling entity.</summary>
    public const int CLC_SOURCE_LCSE = 1;

    public const int CLC_SOURCE_ROOT_COUNT = 2;

    // ──────────────────────────────────────────────────────────
    //  RoundTripDelay
    // ──────────────────────────────────────────────────────────

    /// <summary>sequenceNumber INTEGER (0..255)</summary>
    public const int RTD_SEQ_MIN = 0;
    public const int RTD_SEQ_MAX = 255;

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>Return a friendly name for a top-level message type + sub-CHOICE.</summary>
    public static string MessageName(int topLevel, int subIndex)
    {
        return topLevel switch
        {
            MSG_REQUEST => subIndex switch
            {
                REQ_MASTER_SLAVE_DETERMINATION => "MasterSlaveDetermination",
                REQ_TERMINAL_CAPABILITY_SET => "TerminalCapabilitySet",
                REQ_OPEN_LOGICAL_CHANNEL => "OpenLogicalChannel",
                REQ_CLOSE_LOGICAL_CHANNEL => "CloseLogicalChannel",
                REQ_REQUEST_CHANNEL_CLOSE => "RequestChannelClose",
                REQ_ROUND_TRIP_DELAY_REQUEST => "RoundTripDelayRequest",
                _ => $"Request({subIndex})"
            },

            MSG_RESPONSE => subIndex switch
            {
                RSP_MASTER_SLAVE_DETERMINATION_ACK => "MasterSlaveDeterminationAck",
                RSP_MASTER_SLAVE_DETERMINATION_REJECT => "MasterSlaveDeterminationReject",
                RSP_TERMINAL_CAPABILITY_SET_ACK => "TerminalCapabilitySetAck",
                RSP_TERMINAL_CAPABILITY_SET_REJECT => "TerminalCapabilitySetReject",
                RSP_OPEN_LOGICAL_CHANNEL_ACK => "OpenLogicalChannelAck",
                RSP_OPEN_LOGICAL_CHANNEL_REJECT => "OpenLogicalChannelReject",
                RSP_CLOSE_LOGICAL_CHANNEL_ACK => "CloseLogicalChannelAck",
                _ => $"Response({subIndex})"
            },

            MSG_COMMAND => $"Command({subIndex})",
            MSG_INDICATION => $"Indication({subIndex})",
            _ => $"Unknown({topLevel},{subIndex})"
        };
    }
}
