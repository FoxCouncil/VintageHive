// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// Constants for ITU-T H.225.0 RAS (Registration, Admission, Status) protocol.
/// Based on H.225.0 v2 (1998) as used by Microsoft NetMeeting 2.x/3.x.
/// RasMessage is a PER-encoded CHOICE with 25 root alternatives.
/// </summary>
internal static class H225Constants
{
    // ──────────────────────────────────────────────────────────
    //  Protocol identifiers
    // ──────────────────────────────────────────────────────────

    /// <summary>H.225.0 v2 protocol identifier: {0 0 8 2250 0 2}</summary>
    public static readonly int[] ProtocolOid = { 0, 0, 8, 2250, 0, 2 };

    /// <summary>Default RAS UDP port (ITU-T H.225.0 Section 7.2).</summary>
    public const int DefaultRasPort = 1719;

    // ──────────────────────────────────────────────────────────
    //  RasMessage CHOICE indices (25 root alternatives, extensible)
    // ──────────────────────────────────────────────────────────

    public const int RAS_GATEKEEPER_REQUEST = 0;
    public const int RAS_GATEKEEPER_CONFIRM = 1;
    public const int RAS_GATEKEEPER_REJECT = 2;
    public const int RAS_REGISTRATION_REQUEST = 3;
    public const int RAS_REGISTRATION_CONFIRM = 4;
    public const int RAS_REGISTRATION_REJECT = 5;
    public const int RAS_UNREGISTRATION_REQUEST = 6;
    public const int RAS_UNREGISTRATION_CONFIRM = 7;
    public const int RAS_UNREGISTRATION_REJECT = 8;
    public const int RAS_ADMISSION_REQUEST = 9;
    public const int RAS_ADMISSION_CONFIRM = 10;
    public const int RAS_ADMISSION_REJECT = 11;
    public const int RAS_BANDWIDTH_REQUEST = 12;
    public const int RAS_BANDWIDTH_CONFIRM = 13;
    public const int RAS_BANDWIDTH_REJECT = 14;
    public const int RAS_DISENGAGE_REQUEST = 15;
    public const int RAS_DISENGAGE_CONFIRM = 16;
    public const int RAS_DISENGAGE_REJECT = 17;
    public const int RAS_LOCATION_REQUEST = 18;
    public const int RAS_LOCATION_CONFIRM = 19;
    public const int RAS_LOCATION_REJECT = 20;
    public const int RAS_INFO_REQUEST = 21;
    public const int RAS_INFO_REQUEST_RESPONSE = 22;
    public const int RAS_NONSTANDARD_MESSAGE = 23;
    public const int RAS_UNKNOWN_MESSAGE_RESPONSE = 24;

    /// <summary>Number of root alternatives in the RasMessage CHOICE.</summary>
    public const int RAS_ROOT_ALTERNATIVES = 25;

    // ──────────────────────────────────────────────────────────
    //  TransportAddress CHOICE indices (7 root alternatives)
    // ──────────────────────────────────────────────────────────

    public const int TRANSPORT_IP_ADDRESS = 0;
    public const int TRANSPORT_IP_SOURCE_ROUTE = 1;
    public const int TRANSPORT_IPXADDRESS = 2;
    public const int TRANSPORT_IP6ADDRESS = 3;
    public const int TRANSPORT_NETBIOS = 4;
    public const int TRANSPORT_NSAP = 5;
    public const int TRANSPORT_NON_STANDARD_ADDRESS = 6;

    public const int TRANSPORT_ROOT_ALTERNATIVES = 7;

    // ──────────────────────────────────────────────────────────
    //  AliasAddress CHOICE indices (6 root alternatives, extensible)
    // ──────────────────────────────────────────────────────────

    public const int ALIAS_E164 = 0;
    public const int ALIAS_H323_ID = 1;

    /// <summary>Number of root alternatives in AliasAddress CHOICE (v2).</summary>
    public const int ALIAS_ROOT_ALTERNATIVES = 2;

    // ──────────────────────────────────────────────────────────
    //  Reject reasons
    // ──────────────────────────────────────────────────────────

    public const int GRJ_RESOURCE_UNAVAILABLE = 0;
    public const int GRJ_TERMINAL_EXCLUDED = 1;
    public const int GRJ_INVALID_REVISION = 2;
    public const int GRJ_UNDEFINED = 3;

    public const int RRJ_DISCOVERY_REQUIRED = 0;
    public const int RRJ_INVALID_REVISION = 1;
    public const int RRJ_INVALID_CALL_SIGNALING_ADDRESS = 2;
    public const int RRJ_INVALID_RAS_ADDRESS = 3;
    public const int RRJ_DUPLICATE_ALIAS = 4;
    public const int RRJ_INVALID_TERMINAL_TYPE = 5;
    public const int RRJ_UNDEFINED = 6;
    public const int RRJ_TRANSPORT_NOT_SUPPORTED = 7;

    public const int ARJ_CALLED_PARTY_NOT_REGISTERED = 0;
    public const int ARJ_INVALID_PERMISSION = 1;
    public const int ARJ_REQUEST_DENIED = 2;
    public const int ARJ_UNDEFINED = 3;
    public const int ARJ_CALLER_NOT_REGISTERED = 4;
    public const int ARJ_ROUTE_CALL_TO_GATEKEEPER = 5;
    public const int ARJ_INVALID_ENDPOINT_IDENTIFIER = 6;
    public const int ARJ_RESOURCE_UNAVAILABLE = 7;

    // ──────────────────────────────────────────────────────────
    //  Field size constraints
    // ──────────────────────────────────────────────────────────

    /// <summary>GatekeeperIdentifier: BMPString (SIZE 1..128)</summary>
    public const int GK_ID_MIN = 1;
    public const int GK_ID_MAX = 128;

    /// <summary>EndpointIdentifier: BMPString (SIZE 1..128)</summary>
    public const int EP_ID_MIN = 1;
    public const int EP_ID_MAX = 128;

    /// <summary>h323-ID alias: BMPString (SIZE 1..256)</summary>
    public const int H323_ID_MIN = 1;
    public const int H323_ID_MAX = 256;

    /// <summary>e164 alias: IA5String (SIZE 1..128)</summary>
    public const int E164_MIN = 1;
    public const int E164_MAX = 128;

    /// <summary>RequestSeqNum: INTEGER (1..65535)</summary>
    public const int SEQ_NUM_MIN = 1;
    public const int SEQ_NUM_MAX = 65535;
}
