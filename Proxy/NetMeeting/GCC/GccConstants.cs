// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.GCC;

/// <summary>
/// GCC (Generic Conference Control) T.124 constants.
///
/// GCC sits above MCS (T.125) in the T.120 stack:
///   MCS Connect-Initial userData → PER(ConnectData) → ConnectGCCPDU
///   MCS SendData                 → GCC runtime PDUs
///
/// Conference creation uses ConnectGCCPDU (ConferenceCreateRequest/Response).
/// </summary>
internal static class GccConstants
{
    // ──────────────────────────────────────────────────────────
    //  T.124 Object Identifier: {0 0 20 124 0 1}
    // ──────────────────────────────────────────────────────────

    /// <summary>T.124 protocol version 1 OID.</summary>
    public static readonly int[] T124_OID = { 0, 0, 20, 124, 0, 1 };

    // ──────────────────────────────────────────────────────────
    //  Key CHOICE (2 root alternatives, NOT extensible)
    // ──────────────────────────────────────────────────────────

    public const int KEY_ROOT_COUNT = 2;
    public const int KEY_OBJECT = 0;
    public const int KEY_H221_NON_STANDARD = 1;

    // ──────────────────────────────────────────────────────────
    //  ConnectGCCPDU CHOICE (8 root alternatives, extensible)
    // ──────────────────────────────────────────────────────────

    public const int CONNECT_ROOT_COUNT = 8;
    public const int CONNECT_CONFERENCE_CREATE_REQUEST = 0;
    public const int CONNECT_CONFERENCE_CREATE_RESPONSE = 1;
    public const int CONNECT_CONFERENCE_QUERY_REQUEST = 2;
    public const int CONNECT_CONFERENCE_QUERY_RESPONSE = 3;
    public const int CONNECT_CONFERENCE_JOIN_REQUEST = 4;
    public const int CONNECT_CONFERENCE_JOIN_RESPONSE = 5;
    public const int CONNECT_CONFERENCE_INVITE_REQUEST = 6;
    public const int CONNECT_CONFERENCE_INVITE_RESPONSE = 7;

    // ──────────────────────────────────────────────────────────
    //  ConferenceCreateRequest — 8 OPTIONAL fields in root
    // ──────────────────────────────────────────────────────────

    public const int CCR_OPTIONAL_COUNT = 8;

    // Optional field indices in the bitmap
    public const int CCR_OPT_CONVENER_PASSWORD = 0;
    public const int CCR_OPT_PASSWORD = 1;
    public const int CCR_OPT_CONDUCTOR_PRIVILEGES = 2;
    public const int CCR_OPT_CONDUCTED_PRIVILEGES = 3;
    public const int CCR_OPT_NON_CONDUCTED_PRIVILEGES = 4;
    public const int CCR_OPT_CONFERENCE_DESCRIPTION = 5;
    public const int CCR_OPT_CALLER_IDENTIFIER = 6;
    public const int CCR_OPT_USER_DATA = 7;

    // ──────────────────────────────────────────────────────────
    //  ConferenceCreateResponse — 1 OPTIONAL field in root
    // ──────────────────────────────────────────────────────────

    public const int CCRESP_OPTIONAL_COUNT = 1;
    public const int CCRESP_OPT_USER_DATA = 0;

    // ──────────────────────────────────────────────────────────
    //  Result enumeration (5 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int RESULT_SUCCESS = 0;
    public const int RESULT_USER_REJECTED = 1;
    public const int RESULT_RESOURCES_NOT_AVAILABLE = 2;
    public const int RESULT_REJECTED_FOR_SYMMETRY_BREAKING = 3;
    public const int RESULT_LOCKED_CONFERENCE_NOT_SUPPORTED = 4;
    public const int RESULT_ROOT_COUNT = 5;

    // ──────────────────────────────────────────────────────────
    //  TerminationMethod enumeration (2 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int TERMINATION_AUTOMATIC = 0;
    public const int TERMINATION_MANUAL = 1;
    public const int TERMINATION_ROOT_COUNT = 2;

    // ──────────────────────────────────────────────────────────
    //  H.221 non-standard key constants
    // ──────────────────────────────────────────────────────────

    /// <summary>Microsoft "Duca" H.221 key (0x44 0x75 0x63 0x61).</summary>
    public static readonly byte[] H221_KEY_MICROSOFT = { 0x44, 0x75, 0x63, 0x61 };

    // ──────────────────────────────────────────────────────────
    //  Helper methods
    // ──────────────────────────────────────────────────────────

    /// <summary>Return a friendly name for a ConnectGCCPDU CHOICE index.</summary>
    public static string ConnectPduName(int index)
    {
        return index switch
        {
            CONNECT_CONFERENCE_CREATE_REQUEST => "ConferenceCreateRequest",
            CONNECT_CONFERENCE_CREATE_RESPONSE => "ConferenceCreateResponse",
            CONNECT_CONFERENCE_QUERY_REQUEST => "ConferenceQueryRequest",
            CONNECT_CONFERENCE_QUERY_RESPONSE => "ConferenceQueryResponse",
            CONNECT_CONFERENCE_JOIN_REQUEST => "ConferenceJoinRequest",
            CONNECT_CONFERENCE_JOIN_RESPONSE => "ConferenceJoinResponse",
            CONNECT_CONFERENCE_INVITE_REQUEST => "ConferenceInviteRequest",
            CONNECT_CONFERENCE_INVITE_RESPONSE => "ConferenceInviteResponse",
            _ => $"ConnectGCCPDU({index})"
        };
    }

    /// <summary>Return a friendly name for a Result value.</summary>
    public static string ResultName(int result)
    {
        return result switch
        {
            RESULT_SUCCESS => "success",
            RESULT_USER_REJECTED => "userRejected",
            RESULT_RESOURCES_NOT_AVAILABLE => "resourcesNotAvailable",
            RESULT_REJECTED_FOR_SYMMETRY_BREAKING => "rejectedForSymmetryBreaking",
            RESULT_LOCKED_CONFERENCE_NOT_SUPPORTED => "lockedConferenceNotSupported",
            _ => $"Result({result})"
        };
    }
}
