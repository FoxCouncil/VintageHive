// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.FileTransfer;

/// <summary>
/// T.127 Multipoint Binary File Transfer (MBFT) constants.
///
/// MBFT PDUs ride directly on MCS SendData (T.125), PER-encoded.
/// Transport: TCP:1503 → TPKT → X.224 → MCS SendData → MBFTPDU
///
/// The protocol uses separate control and data MCS channels:
///   - Control channel: signaling (offer, accept, reject, error, abort)
///   - Data channel: file content (start, data chunks)
///
/// Application protocol key OID: {0 0 20 127} (itu-t recommendation t t127)
/// </summary>
internal static class FileTransferConstants
{
    // ──────────────────────────────────────────────────────────
    //  T.127 Application Protocol OID
    // ──────────────────────────────────────────────────────────

    /// <summary>T.127 MBFT application protocol key: {0 0 20 127}.</summary>
    public static readonly int[] T127_OID = { 0, 0, 20, 127 };

    // ──────────────────────────────────────────────────────────
    //  MBFTPDU CHOICE indices (16 root alternatives, extensible)
    // ──────────────────────────────────────────────────────────

    public const int MBFT_ROOT_COUNT = 16;

    public const int PDU_FILE_OFFER = 0;
    public const int PDU_FILE_ACCEPT = 1;
    public const int PDU_FILE_REJECT = 2;
    public const int PDU_FILE_REQUEST = 3;
    public const int PDU_FILE_DENY = 4;
    public const int PDU_FILE_ERROR = 5;
    public const int PDU_FILE_ABORT = 6;
    public const int PDU_FILE_START = 7;
    public const int PDU_FILE_DATA = 8;
    public const int PDU_DIRECTORY_REQUEST = 9;
    public const int PDU_DIRECTORY_RESPONSE = 10;
    public const int PDU_PRIVILEGE_REQUEST = 11;
    public const int PDU_PRIVILEGE_ASSIGN = 12;
    public const int PDU_NON_STANDARD = 13;
    public const int PDU_PRIVATE_CHANNEL_JOIN_INVITE = 14;
    public const int PDU_PRIVATE_CHANNEL_JOIN_RESPONSE = 15;

    // ──────────────────────────────────────────────────────────
    //  File-RejectPDU reason (8 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int REJECT_ROOT_COUNT = 8;
    public const int REJECT_UNSPECIFIED = 0;
    public const int REJECT_FILE_EXISTS = 1;
    public const int REJECT_FILE_NOT_REQUIRED = 2;
    public const int REJECT_INSUFFICIENT_RESOURCES = 3;
    public const int REJECT_TRANSFER_LIMIT = 4;
    public const int REJECT_COMPRESSION_UNSUPPORTED = 5;
    public const int REJECT_UNABLE_TO_JOIN_CHANNEL = 6;
    public const int REJECT_PARAMETER_NOT_SUPPORTED = 7;

    // ──────────────────────────────────────────────────────────
    //  File-AbortPDU reason (5 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int ABORT_ROOT_COUNT = 5;
    public const int ABORT_UNSPECIFIED = 0;
    public const int ABORT_BANDWIDTH_REQUIRED = 1;
    public const int ABORT_TOKENS_REQUIRED = 2;
    public const int ABORT_CHANNELS_REQUIRED = 3;
    public const int ABORT_PRIORITY_REQUIRED = 4;

    // ──────────────────────────────────────────────────────────
    //  File-ErrorPDU error type (3 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int ERROR_TYPE_ROOT_COUNT = 3;
    public const int ERROR_TYPE_INFORMATIVE = 0;
    public const int ERROR_TYPE_TRANSIENT = 1;
    public const int ERROR_TYPE_PERMANENT = 2;

    // ──────────────────────────────────────────────────────────
    //  FileHeader context tags
    // ──────────────────────────────────────────────────────────

    public const int FH_TAG_FILENAME = 0;
    public const int FH_TAG_PERMITTED_ACTIONS = 1;
    public const int FH_TAG_CONTENTS_TYPE = 2;
    public const int FH_TAG_STORAGE_ACCOUNT = 3;
    public const int FH_TAG_DATE_CREATION = 4;
    public const int FH_TAG_DATE_MODIFICATION = 5;
    public const int FH_TAG_DATE_READ_ACCESS = 6;
    public const int FH_TAG_IDENTITY_CREATOR = 8;
    public const int FH_TAG_IDENTITY_MODIFIER = 9;
    public const int FH_TAG_IDENTITY_READER = 10;
    public const int FH_TAG_FILESIZE = 13;
    public const int FH_TAG_FUTURE_FILESIZE = 14;
    public const int FH_TAG_PROTOCOL_VERSION = 28;

    /// <summary>Total optional fields in FileHeader for the bitmap.</summary>
    public const int FH_OPTIONAL_COUNT = 24;

    // ──────────────────────────────────────────────────────────
    //  Microsoft NetMeeting NonStandard extension keys
    // ──────────────────────────────────────────────────────────

    /// <summary>Default MBFT non-standard key.</summary>
    public const string NS_KEY_MBFT = "NetMeeting 1 MBFT";

    /// <summary>Sent at the end of a file transfer.</summary>
    public const string NS_KEY_FILE_END = "NetMeeting 1 FileEnd";

    /// <summary>Sent when leaving a channel.</summary>
    public const string NS_KEY_CHANNEL_LEAVE = "NetMeeting 1 ChannelLeave";

    // ──────────────────────────────────────────────────────────
    //  Constraints
    // ──────────────────────────────────────────────────────────

    /// <summary>Maximum file data chunk per PDU (OCTET STRING SIZE 0..65535).</summary>
    public const int MAX_CHUNK_SIZE = 65535;

    /// <summary>Handle range: INTEGER(0..65535).</summary>
    public const int MAX_HANDLE = 65535;

    /// <summary>Channel ID range: INTEGER(0..65535).</summary>
    public const int MAX_CHANNEL_ID = 65535;

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>Return a friendly name for an MBFTPDU CHOICE index.</summary>
    public static string PduName(int index)
    {
        return index switch
        {
            PDU_FILE_OFFER => "File-Offer",
            PDU_FILE_ACCEPT => "File-Accept",
            PDU_FILE_REJECT => "File-Reject",
            PDU_FILE_REQUEST => "File-Request",
            PDU_FILE_DENY => "File-Deny",
            PDU_FILE_ERROR => "File-Error",
            PDU_FILE_ABORT => "File-Abort",
            PDU_FILE_START => "File-Start",
            PDU_FILE_DATA => "File-Data",
            PDU_DIRECTORY_REQUEST => "Directory-Request",
            PDU_DIRECTORY_RESPONSE => "Directory-Response",
            PDU_PRIVILEGE_REQUEST => "Privilege-Request",
            PDU_PRIVILEGE_ASSIGN => "Privilege-Assign",
            PDU_NON_STANDARD => "NonStandard",
            PDU_PRIVATE_CHANNEL_JOIN_INVITE => "PrivateChannelJoin-Invite",
            PDU_PRIVATE_CHANNEL_JOIN_RESPONSE => "PrivateChannelJoin-Response",
            _ => $"MBFT({index})"
        };
    }

    /// <summary>Return a friendly name for a reject reason.</summary>
    public static string RejectReasonName(int reason)
    {
        return reason switch
        {
            REJECT_UNSPECIFIED => "unspecified",
            REJECT_FILE_EXISTS => "fileExists",
            REJECT_FILE_NOT_REQUIRED => "fileNotRequired",
            REJECT_INSUFFICIENT_RESOURCES => "insufficientResources",
            REJECT_TRANSFER_LIMIT => "transferLimit",
            REJECT_COMPRESSION_UNSUPPORTED => "compressionUnsupported",
            REJECT_UNABLE_TO_JOIN_CHANNEL => "unableToJoinChannel",
            REJECT_PARAMETER_NOT_SUPPORTED => "parameterNotSupported",
            _ => $"RejectReason({reason})"
        };
    }
}
