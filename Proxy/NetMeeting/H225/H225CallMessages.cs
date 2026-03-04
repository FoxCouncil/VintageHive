// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// Parsed H323-UU-PDU envelope. Wraps one of the H.225.0 UUIE message types.
/// </summary>
internal class H225CallMessage
{
    /// <summary>CHOICE index from h323-message-body.</summary>
    public int BodyType { get; init; }

    public SetupUuie Setup { get; init; }
    public CallProceedingUuie CallProceeding { get; init; }
    public ConnectUuie Connect { get; init; }
    public AlertingUuie Alerting { get; init; }
    public ReleaseCompleteUuie ReleaseComplete { get; init; }
    public FacilityUuie Facility { get; init; }
}

// ──────────────────────────────────────────────────────────
//  UUIE message data types
// ──────────────────────────────────────────────────────────

internal class SetupUuie
{
    public int[] ProtocolIdentifier { get; init; }
    public IPEndPoint H245Address { get; init; }
    public string[] SourceAliases { get; init; }
    public string[] DestinationAliases { get; init; }
    public IPEndPoint DestCallSignalAddress { get; init; }
    public bool ActiveMC { get; init; }
    public byte[] ConferenceId { get; init; }
    public int ConferenceGoal { get; init; } // 0=create, 1=join, 2=invite
    public int CallType { get; init; }       // 0=pointToPoint
}

internal class CallProceedingUuie
{
    public int[] ProtocolIdentifier { get; init; }
    public IPEndPoint H245Address { get; init; }
}

internal class ConnectUuie
{
    public int[] ProtocolIdentifier { get; init; }
    public IPEndPoint H245Address { get; init; }
    public byte[] ConferenceId { get; init; }
}

internal class AlertingUuie
{
    public int[] ProtocolIdentifier { get; init; }
    public IPEndPoint H245Address { get; init; }
}

internal class ReleaseCompleteUuie
{
    public int[] ProtocolIdentifier { get; init; }
    public int? Reason { get; init; } // null = not present
}

internal class FacilityUuie
{
    public int[] ProtocolIdentifier { get; init; }
    public IPEndPoint AlternativeAddress { get; init; }
    public string[] AlternativeAliases { get; init; }
    public byte[] ConferenceId { get; init; }
    public int Reason { get; init; }
}

/// <summary>
/// PER codec for H323-UU-PDU (H.225.0 call signaling messages).
/// These are embedded inside Q.931 User-User IEs.
///
/// H323-UU-PDU ::= SEQUENCE {
///     h323-message-body CHOICE {
///         setup(0), callProceeding(1), connect(2), alerting(3),
///         information(4), releaseComplete(5), facility(6), ...
///     },
///     nonStandardData NonStandardParameter OPTIONAL,
///     ...
/// }
/// </summary>
internal static class H225CallCodec
{
    // h323-message-body CHOICE root alternatives
    public const int BODY_SETUP = 0;
    public const int BODY_CALL_PROCEEDING = 1;
    public const int BODY_CONNECT = 2;
    public const int BODY_ALERTING = 3;
    public const int BODY_INFORMATION = 4;
    public const int BODY_RELEASE_COMPLETE = 5;
    public const int BODY_FACILITY = 6;
    public const int BODY_ROOT_COUNT = 7;

    // ConferenceGoal CHOICE (3 root, extensible)
    public const int GOAL_CREATE = 0;
    public const int GOAL_JOIN = 1;
    public const int GOAL_INVITE = 2;

    // ReleaseCompleteReason CHOICE (12 root, extensible)
    public const int REL_NO_BANDWIDTH = 0;
    public const int REL_GK_RESOURCES = 1;
    public const int REL_UNREACHABLE_DEST = 2;
    public const int REL_DEST_REJECTION = 3;
    public const int REL_INVALID_REVISION = 4;
    public const int REL_NO_PERMISSION = 5;
    public const int REL_UNREACHABLE_GK = 6;
    public const int REL_GW_RESOURCES = 7;
    public const int REL_BAD_FORMAT_ADDR = 8;
    public const int REL_ADAPTIVE_BUSY = 9;
    public const int REL_IN_CONF = 10;
    public const int REL_UNDEFINED_REASON = 11;
    public const int REL_ROOT_COUNT = 12;

    // FacilityReason CHOICE (4 root, extensible)
    public const int FAC_ROUTE_TO_GK = 0;
    public const int FAC_CALL_FORWARDED = 1;
    public const int FAC_ROUTE_TO_MC = 2;
    public const int FAC_UNDEFINED = 3;
    public const int FAC_ROOT_COUNT = 4;

    // ──────────────────────────────────────────────────────────
    //  Decode
    // ──────────────────────────────────────────────────────────

    public static H225CallMessage Decode(byte[] data)
    {
        var dec = new PerDecoder(data);

        // H323-UU-PDU SEQUENCE (extensible)
        var pduHasExtensions = dec.ReadExtensionBit();
        var pduOptionals = dec.ReadOptionalBitmap(1); // nonStandardData

        // h323-message-body CHOICE (7 root, extensible)
        var bodyChoice = dec.ReadChoiceIndex(BODY_ROOT_COUNT, extensible: true);

        H225CallMessage result;

        switch (bodyChoice)
        {
            case BODY_SETUP:
            {
                result = new H225CallMessage { BodyType = bodyChoice, Setup = DecodeSetupUuie(dec) };
            }
            break;

            case BODY_CALL_PROCEEDING:
            {
                result = new H225CallMessage { BodyType = bodyChoice, CallProceeding = DecodeCallProceedingUuie(dec) };
            }
            break;

            case BODY_CONNECT:
            {
                result = new H225CallMessage { BodyType = bodyChoice, Connect = DecodeConnectUuie(dec) };
            }
            break;

            case BODY_ALERTING:
            {
                result = new H225CallMessage { BodyType = bodyChoice, Alerting = DecodeAlertingUuie(dec) };
            }
            break;

            case BODY_RELEASE_COMPLETE:
            {
                result = new H225CallMessage { BodyType = bodyChoice, ReleaseComplete = DecodeReleaseCompleteUuie(dec) };
            }
            break;

            case BODY_FACILITY:
            {
                result = new H225CallMessage { BodyType = bodyChoice, Facility = DecodeFacilityUuie(dec) };
            }
            break;

            default:
            {
                // Information (4) or unknown — return generic
                result = new H225CallMessage { BodyType = bodyChoice };
            }
            break;
        }

        // Skip nonStandardData if present
        if (pduOptionals[0])
        {
            H225Types.SkipNonStandardParameter(dec);
        }

        // Skip H323-UU-PDU extensions
        if (pduHasExtensions && dec.HasData)
        {
            try
            {
                dec.ReadExtensionAdditions();
            }
            catch
            {
                // Best-effort: some extensions may have consumed remaining bits
            }
        }

        return result;
    }

    // ──────────────────────────────────────────────────────────
    //  Encode
    // ──────────────────────────────────────────────────────────

    public static byte[] EncodeSetup(SetupUuie setup)
    {
        var enc = new PerEncoder();

        // H323-UU-PDU wrapper
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false); // no nonStandardData

        // h323-message-body CHOICE = setup (0)
        enc.WriteChoiceIndex(BODY_SETUP, BODY_ROOT_COUNT, extensible: true);

        // Setup-UUIE SEQUENCE (extensible)
        // OPTIONAL fields: h245Address[0], sourceAddress[1], destinationAddress[2],
        //   destCallSignalAddress[3], destExtraCallInfo[4], destExtraCRV[5], callServices[6]
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(
            setup.H245Address != null,       // h245Address
            setup.SourceAliases != null,     // sourceAddress
            setup.DestinationAliases != null, // destinationAddress
            setup.DestCallSignalAddress != null, // destCallSignalAddress
            false,                            // destExtraCallInfo
            false,                            // destExtraCRV
            false                             // callServices
        );

        enc.WriteObjectIdentifier(setup.ProtocolIdentifier ?? H225Constants.ProtocolOid);

        if (setup.H245Address != null)
        {
            H225Types.WriteTransportAddress(enc, setup.H245Address);
        }

        if (setup.SourceAliases != null)
        {
            H225Types.WriteAliasAddresses(enc, setup.SourceAliases);
        }

        // sourceInfo (EndpointType) — always present, not optional
        H225Types.WriteEndpointType(enc);

        if (setup.DestinationAliases != null)
        {
            H225Types.WriteAliasAddresses(enc, setup.DestinationAliases);
        }

        if (setup.DestCallSignalAddress != null)
        {
            H225Types.WriteTransportAddress(enc, setup.DestCallSignalAddress);
        }

        // activeMC
        enc.WriteBoolean(setup.ActiveMC);

        // conferenceID OCTET STRING (SIZE 16)
        enc.WriteOctetString(setup.ConferenceId ?? new byte[16], lb: 16, ub: 16);

        // conferenceGoal CHOICE (3 root, extensible)
        enc.WriteChoiceIndex(setup.ConferenceGoal, 3, extensible: true);

        // callType CHOICE (4 root, extensible)
        enc.WriteChoiceIndex(setup.CallType, 4, extensible: true);

        return enc.ToArray();
    }

    public static byte[] EncodeCallProceeding(CallProceedingUuie cp)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false); // no nonStandardData

        enc.WriteChoiceIndex(BODY_CALL_PROCEEDING, BODY_ROOT_COUNT, extensible: true);

        // CallProceeding-UUIE SEQUENCE (extensible)
        // OPTIONAL: h245Address[0]
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(cp.H245Address != null);

        enc.WriteObjectIdentifier(cp.ProtocolIdentifier ?? H225Constants.ProtocolOid);

        // destinationInfo (EndpointType) — present in v2
        H225Types.WriteEndpointType(enc);

        if (cp.H245Address != null)
        {
            H225Types.WriteTransportAddress(enc, cp.H245Address);
        }

        return enc.ToArray();
    }

    public static byte[] EncodeConnect(ConnectUuie conn)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false); // no nonStandardData

        enc.WriteChoiceIndex(BODY_CONNECT, BODY_ROOT_COUNT, extensible: true);

        // Connect-UUIE SEQUENCE (extensible)
        // OPTIONAL: h245Address[0]
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(conn.H245Address != null);

        enc.WriteObjectIdentifier(conn.ProtocolIdentifier ?? H225Constants.ProtocolOid);

        if (conn.H245Address != null)
        {
            H225Types.WriteTransportAddress(enc, conn.H245Address);
        }

        // destinationInfo (EndpointType)
        H225Types.WriteEndpointType(enc);

        // conferenceID OCTET STRING (SIZE 16)
        enc.WriteOctetString(conn.ConferenceId ?? new byte[16], lb: 16, ub: 16);

        return enc.ToArray();
    }

    public static byte[] EncodeAlerting(AlertingUuie alerting)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false);

        enc.WriteChoiceIndex(BODY_ALERTING, BODY_ROOT_COUNT, extensible: true);

        // Alerting-UUIE SEQUENCE (extensible)
        // OPTIONAL: h245Address[0]
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(alerting.H245Address != null);

        enc.WriteObjectIdentifier(alerting.ProtocolIdentifier ?? H225Constants.ProtocolOid);

        // destinationInfo (EndpointType)
        H225Types.WriteEndpointType(enc);

        if (alerting.H245Address != null)
        {
            H225Types.WriteTransportAddress(enc, alerting.H245Address);
        }

        return enc.ToArray();
    }

    public static byte[] EncodeReleaseComplete(ReleaseCompleteUuie rc)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false);

        enc.WriteChoiceIndex(BODY_RELEASE_COMPLETE, BODY_ROOT_COUNT, extensible: true);

        // ReleaseComplete-UUIE SEQUENCE (extensible)
        // OPTIONAL: reason[0]
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(rc.Reason.HasValue);

        enc.WriteObjectIdentifier(rc.ProtocolIdentifier ?? H225Constants.ProtocolOid);

        if (rc.Reason.HasValue)
        {
            // ReleaseCompleteReason CHOICE (12 root, extensible)
            enc.WriteChoiceIndex(rc.Reason.Value, REL_ROOT_COUNT, extensible: true);
        }

        return enc.ToArray();
    }

    public static byte[] EncodeFacility(FacilityUuie fac)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false);

        enc.WriteChoiceIndex(BODY_FACILITY, BODY_ROOT_COUNT, extensible: true);

        // Facility-UUIE SEQUENCE (extensible)
        // OPTIONAL: alternativeAddress[0], alternativeAliasAddress[1], conferenceID[2]
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(
            fac.AlternativeAddress != null,
            fac.AlternativeAliases != null,
            fac.ConferenceId != null
        );

        enc.WriteObjectIdentifier(fac.ProtocolIdentifier ?? H225Constants.ProtocolOid);

        if (fac.AlternativeAddress != null)
        {
            H225Types.WriteTransportAddress(enc, fac.AlternativeAddress);
        }

        if (fac.AlternativeAliases != null)
        {
            H225Types.WriteAliasAddresses(enc, fac.AlternativeAliases);
        }

        if (fac.ConferenceId != null)
        {
            enc.WriteOctetString(fac.ConferenceId, lb: 16, ub: 16);
        }

        // reason CHOICE (4 root, extensible)
        enc.WriteChoiceIndex(fac.Reason, FAC_ROOT_COUNT, extensible: true);

        return enc.ToArray();
    }

    // ──────────────────────────────────────────────────────────
    //  Decoders
    // ──────────────────────────────────────────────────────────

    private static SetupUuie DecodeSetupUuie(PerDecoder dec)
    {
        // Setup-UUIE SEQUENCE (extensible)
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(7);
        // [0]h245Address [1]sourceAddress [2]destinationAddress
        // [3]destCallSignalAddress [4]destExtraCallInfo [5]destExtraCRV [6]callServices

        var protocolId = dec.ReadObjectIdentifier();

        IPEndPoint h245Addr = null;
        if (opts[0])
        {
            h245Addr = H225Types.ReadTransportAddress(dec);
        }

        string[] srcAliases = null;
        if (opts[1])
        {
            srcAliases = H225Types.ReadAliasAddresses(dec);
        }

        // sourceInfo (EndpointType) — always present
        H225Types.SkipEndpointType(dec);

        string[] destAliases = null;
        if (opts[2])
        {
            destAliases = H225Types.ReadAliasAddresses(dec);
        }

        IPEndPoint destCallSignal = null;
        if (opts[3])
        {
            destCallSignal = H225Types.ReadTransportAddress(dec);
        }

        // destExtraCallInfo OPTIONAL
        if (opts[4])
        {
            H225Types.SkipAliasAddresses(dec);
        }

        // destExtraCRV OPTIONAL — SEQUENCE OF CallReferenceValue (INTEGER 0..65535)
        if (opts[5])
        {
            var count = dec.ReadLengthDeterminant();
            for (var i = 0; i < count; i++)
            {
                dec.ReadConstrainedWholeNumber(0, 65535);
            }
        }

        // activeMC
        var activeMC = dec.ReadBoolean();

        // conferenceID OCTET STRING (SIZE 16)
        var confId = dec.ReadOctetString(lb: 16, ub: 16);

        // conferenceGoal CHOICE (3 root, extensible)
        var goal = dec.ReadChoiceIndex(3, extensible: true);

        // callServices OPTIONAL (QseriesOptions)
        if (opts[6])
        {
            SkipQseriesOptions(dec);
        }

        // callType CHOICE (4 root, extensible)
        var callType = dec.ReadChoiceIndex(4, extensible: true);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new SetupUuie
        {
            ProtocolIdentifier = protocolId,
            H245Address = h245Addr,
            SourceAliases = srcAliases,
            DestinationAliases = destAliases,
            DestCallSignalAddress = destCallSignal,
            ActiveMC = activeMC,
            ConferenceId = confId,
            ConferenceGoal = goal,
            CallType = callType
        };
    }

    private static CallProceedingUuie DecodeCallProceedingUuie(PerDecoder dec)
    {
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(1); // h245Address

        var protocolId = dec.ReadObjectIdentifier();

        // destinationInfo (EndpointType)
        H225Types.SkipEndpointType(dec);

        IPEndPoint h245Addr = null;
        if (opts[0])
        {
            h245Addr = H225Types.ReadTransportAddress(dec);
        }

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new CallProceedingUuie
        {
            ProtocolIdentifier = protocolId,
            H245Address = h245Addr
        };
    }

    private static ConnectUuie DecodeConnectUuie(PerDecoder dec)
    {
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(1); // h245Address

        var protocolId = dec.ReadObjectIdentifier();

        IPEndPoint h245Addr = null;
        if (opts[0])
        {
            h245Addr = H225Types.ReadTransportAddress(dec);
        }

        // destinationInfo (EndpointType)
        H225Types.SkipEndpointType(dec);

        // conferenceID
        var confId = dec.ReadOctetString(lb: 16, ub: 16);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new ConnectUuie
        {
            ProtocolIdentifier = protocolId,
            H245Address = h245Addr,
            ConferenceId = confId
        };
    }

    private static AlertingUuie DecodeAlertingUuie(PerDecoder dec)
    {
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(1); // h245Address

        var protocolId = dec.ReadObjectIdentifier();

        // destinationInfo
        H225Types.SkipEndpointType(dec);

        IPEndPoint h245Addr = null;
        if (opts[0])
        {
            h245Addr = H225Types.ReadTransportAddress(dec);
        }

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new AlertingUuie
        {
            ProtocolIdentifier = protocolId,
            H245Address = h245Addr
        };
    }

    private static ReleaseCompleteUuie DecodeReleaseCompleteUuie(PerDecoder dec)
    {
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(1); // reason

        var protocolId = dec.ReadObjectIdentifier();

        int? reason = null;
        if (opts[0])
        {
            reason = dec.ReadChoiceIndex(REL_ROOT_COUNT, extensible: true);
        }

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new ReleaseCompleteUuie
        {
            ProtocolIdentifier = protocolId,
            Reason = reason
        };
    }

    private static FacilityUuie DecodeFacilityUuie(PerDecoder dec)
    {
        var hasExt = dec.ReadExtensionBit();
        var opts = dec.ReadOptionalBitmap(3); // alternativeAddress, alternativeAliases, conferenceID

        var protocolId = dec.ReadObjectIdentifier();

        IPEndPoint altAddr = null;
        if (opts[0])
        {
            altAddr = H225Types.ReadTransportAddress(dec);
        }

        string[] altAliases = null;
        if (opts[1])
        {
            altAliases = H225Types.ReadAliasAddresses(dec);
        }

        byte[] confId = null;
        if (opts[2])
        {
            confId = dec.ReadOctetString(lb: 16, ub: 16);
        }

        // reason CHOICE (4 root, extensible)
        var reason = dec.ReadChoiceIndex(FAC_ROOT_COUNT, extensible: true);

        if (hasExt)
        {
            try { dec.ReadExtensionAdditions(); } catch { }
        }

        return new FacilityUuie
        {
            ProtocolIdentifier = protocolId,
            AlternativeAddress = altAddr,
            AlternativeAliases = altAliases,
            ConferenceId = confId,
            Reason = reason
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static void SkipQseriesOptions(PerDecoder dec)
    {
        var hasExt = dec.ReadExtensionBit();
        for (var i = 0; i < 6; i++) { dec.ReadBoolean(); }
        if (hasExt) { try { dec.ReadExtensionAdditions(); } catch { } }
    }
}
