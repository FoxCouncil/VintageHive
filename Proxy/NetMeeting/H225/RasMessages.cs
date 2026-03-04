// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// Parsed RAS message envelope. The <see cref="Type"/> field identifies the
/// CHOICE alternative; typed data is in the specific message property.
/// </summary>
internal class RasMessage
{
    public int Type { get; init; }
    public int RequestSeqNum { get; init; }

    // Request fields (populated on decode)
    public GatekeeperRequest Grq { get; init; }
    public RegistrationRequest Rrq { get; init; }
    public UnregistrationRequest Urq { get; init; }
    public AdmissionRequest Arq { get; init; }
    public DisengageRequest Drq { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Request data types (decoded from NetMeeting client)
// ──────────────────────────────────────────────────────────

internal class GatekeeperRequest
{
    public int RequestSeqNum { get; init; }
    public int[] ProtocolIdentifier { get; init; }
    public IPEndPoint RasAddress { get; init; }
    public string GatekeeperIdentifier { get; init; }
    public string[] Aliases { get; init; }
}

internal class RegistrationRequest
{
    public int RequestSeqNum { get; init; }
    public int[] ProtocolIdentifier { get; init; }
    public IPEndPoint[] CallSignalAddresses { get; init; }
    public IPEndPoint[] RasAddresses { get; init; }
    public bool DiscoverComplete { get; init; }
    public string[] Aliases { get; init; }
    public string GatekeeperIdentifier { get; init; }
    public int TimeToLive { get; init; }
}

internal class UnregistrationRequest
{
    public int RequestSeqNum { get; init; }
    public IPEndPoint[] CallSignalAddresses { get; init; }
    public string EndpointIdentifier { get; init; }
    public string[] Aliases { get; init; }
}

internal class AdmissionRequest
{
    public int RequestSeqNum { get; init; }
    public int CallType { get; init; } // 0=pointToPoint, 1=oneToN, 2=nToOne, 3=nToN
    public string EndpointIdentifier { get; init; }
    public string[] DestinationAliases { get; init; }
    public string[] SourceAliases { get; init; }
    public IPEndPoint SrcCallSignalAddress { get; init; }
    public int BandWidth { get; init; }
    public bool AnswerCall { get; init; }
}

internal class DisengageRequest
{
    public int RequestSeqNum { get; init; }
    public string EndpointIdentifier { get; init; }
    public int ConferenceId { get; init; }
    public int DisengageReason { get; init; } // 0=forcedDrop, 1=normalDrop, 2=undefinedReason
}

/// <summary>
/// PER encode/decode for H.225.0 RAS messages.
/// Decodes requests from NetMeeting clients, encodes responses from the gatekeeper.
/// </summary>
internal static class RasCodec
{
    // ──────────────────────────────────────────────────────────
    //  Top-level RasMessage dispatch
    // ──────────────────────────────────────────────────────────

    public static RasMessage Decode(byte[] data)
    {
        var dec = new PerDecoder(data);

        // RasMessage is an extensible CHOICE with 25 root alternatives
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        switch (choice)
        {
            case H225Constants.RAS_GATEKEEPER_REQUEST:
            {
                var grq = DecodeGatekeeperRequest(dec);
                return new RasMessage { Type = choice, RequestSeqNum = grq.RequestSeqNum, Grq = grq };
            }

            case H225Constants.RAS_REGISTRATION_REQUEST:
            {
                var rrq = DecodeRegistrationRequest(dec);
                return new RasMessage { Type = choice, RequestSeqNum = rrq.RequestSeqNum, Rrq = rrq };
            }

            case H225Constants.RAS_UNREGISTRATION_REQUEST:
            {
                var urq = DecodeUnregistrationRequest(dec);
                return new RasMessage { Type = choice, RequestSeqNum = urq.RequestSeqNum, Urq = urq };
            }

            case H225Constants.RAS_ADMISSION_REQUEST:
            {
                var arq = DecodeAdmissionRequest(dec);
                return new RasMessage { Type = choice, RequestSeqNum = arq.RequestSeqNum, Arq = arq };
            }

            case H225Constants.RAS_DISENGAGE_REQUEST:
            {
                var drq = DecodeDisengageRequest(dec);
                return new RasMessage { Type = choice, RequestSeqNum = drq.RequestSeqNum, Drq = drq };
            }

            default:
            {
                // Return a generic message for unknown types — server can send UnknownMessageResponse
                return new RasMessage { Type = choice, RequestSeqNum = 0 };
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Response encoders
    // ──────────────────────────────────────────────────────────

    public static byte[] EncodeGatekeeperConfirm(int seqNum, IPEndPoint rasAddress, string gatekeeperId)
    {
        var enc = new PerEncoder();

        // RasMessage CHOICE: GatekeeperConfirm = index 1
        enc.WriteChoiceIndex(H225Constants.RAS_GATEKEEPER_CONFIRM, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // GatekeeperConfirm SEQUENCE (extensible)
        // Root: requestSeqNum, protocolIdentifier, nonStandardData OPTIONAL,
        //       gatekeeperIdentifier OPTIONAL, rasAddress
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, gatekeeperId != null); // nonStandardData, gatekeeperIdentifier

        // requestSeqNum
        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        // protocolIdentifier
        enc.WriteObjectIdentifier(H225Constants.ProtocolOid);

        // gatekeeperIdentifier OPTIONAL
        if (gatekeeperId != null)
        {
            enc.WriteBMPString(gatekeeperId, lb: H225Constants.GK_ID_MIN, ub: H225Constants.GK_ID_MAX);
        }

        // rasAddress
        H225Types.WriteTransportAddress(enc, rasAddress);

        return enc.ToArray();
    }

    public static byte[] EncodeGatekeeperReject(int seqNum, int rejectReason)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H225Constants.RAS_GATEKEEPER_REJECT, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // GatekeeperReject SEQUENCE (extensible)
        // Root: requestSeqNum, protocolIdentifier, nonStandardData OPTIONAL,
        //       gatekeeperIdentifier OPTIONAL, rejectReason
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, false); // nonStandardData, gatekeeperIdentifier

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        enc.WriteObjectIdentifier(H225Constants.ProtocolOid);

        // rejectReason CHOICE (4 root alternatives, extensible)
        enc.WriteChoiceIndex(rejectReason, 4, extensible: true);

        return enc.ToArray();
    }

    public static byte[] EncodeRegistrationConfirm(int seqNum, IPEndPoint[] callSignalAddresses,
        string gatekeeperId, string endpointId, int timeToLive)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H225Constants.RAS_REGISTRATION_CONFIRM, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // RegistrationConfirm SEQUENCE (extensible)
        // Root: requestSeqNum, protocolIdentifier, nonStandardData OPTIONAL,
        //       callSignalAddress (SEQ OF), terminalAlias (SEQ OF) OPTIONAL,
        //       gatekeeperIdentifier OPTIONAL, endpointIdentifier
        enc.WriteExtensionBit(false);

        var hasGkId = gatekeeperId != null;
        enc.WriteOptionalBitmap(false, false, hasGkId); // nonStandardData, terminalAlias, gatekeeperIdentifier

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        enc.WriteObjectIdentifier(H225Constants.ProtocolOid);

        // callSignalAddress SEQUENCE OF TransportAddress
        H225Types.WriteTransportAddresses(enc, callSignalAddresses);

        // gatekeeperIdentifier OPTIONAL
        if (hasGkId)
        {
            enc.WriteBMPString(gatekeeperId, lb: H225Constants.GK_ID_MIN, ub: H225Constants.GK_ID_MAX);
        }

        // endpointIdentifier
        enc.WriteBMPString(endpointId, lb: H225Constants.EP_ID_MIN, ub: H225Constants.EP_ID_MAX);

        return enc.ToArray();
    }

    public static byte[] EncodeRegistrationReject(int seqNum, int rejectReason)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H225Constants.RAS_REGISTRATION_REJECT, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // RegistrationReject SEQUENCE (extensible)
        // Root: requestSeqNum, protocolIdentifier, nonStandardData OPTIONAL,
        //       rejectReason, gatekeeperIdentifier OPTIONAL
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, false); // nonStandardData, gatekeeperIdentifier

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        enc.WriteObjectIdentifier(H225Constants.ProtocolOid);

        // rejectReason CHOICE (8 root alternatives, extensible)
        enc.WriteChoiceIndex(rejectReason, 8, extensible: true);

        return enc.ToArray();
    }

    public static byte[] EncodeUnregistrationConfirm(int seqNum)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H225Constants.RAS_UNREGISTRATION_CONFIRM, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // UnregistrationConfirm SEQUENCE (extensible)
        // Root: requestSeqNum, nonStandardData OPTIONAL
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false); // nonStandardData

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        return enc.ToArray();
    }

    public static byte[] EncodeAdmissionConfirm(int seqNum, int bandWidth,
        IPEndPoint destCallSignalAddress)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H225Constants.RAS_ADMISSION_CONFIRM, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // AdmissionConfirm SEQUENCE (extensible)
        // Root: requestSeqNum, bandWidth, callModel, destCallSignalAddress,
        //       irrFrequency OPTIONAL, nonStandardData OPTIONAL
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, false); // irrFrequency, nonStandardData

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        // bandWidth INTEGER (0..4294967295)
        enc.WriteConstrainedWholeNumber(bandWidth, 0, 4294967295L);

        // callModel CHOICE (2 root alternatives, extensible): 0=direct, 1=gatekeeperRouted
        enc.WriteChoiceIndex(0, 2, extensible: true); // direct

        // destCallSignalAddress
        H225Types.WriteTransportAddress(enc, destCallSignalAddress);

        return enc.ToArray();
    }

    public static byte[] EncodeAdmissionReject(int seqNum, int rejectReason)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H225Constants.RAS_ADMISSION_REJECT, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // AdmissionReject SEQUENCE (extensible)
        // Root: requestSeqNum, rejectReason, nonStandardData OPTIONAL
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false); // nonStandardData

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        // rejectReason CHOICE (8 root alternatives, extensible)
        enc.WriteChoiceIndex(rejectReason, 8, extensible: true);

        return enc.ToArray();
    }

    public static byte[] EncodeDisengageConfirm(int seqNum)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H225Constants.RAS_DISENGAGE_CONFIRM, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // DisengageConfirm SEQUENCE (extensible)
        // Root: requestSeqNum, nonStandardData OPTIONAL
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false); // nonStandardData

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        return enc.ToArray();
    }

    public static byte[] EncodeUnknownMessageResponse(int seqNum)
    {
        var enc = new PerEncoder();

        enc.WriteChoiceIndex(H225Constants.RAS_UNKNOWN_MESSAGE_RESPONSE, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // UnknownMessageResponse SEQUENCE (extensible)
        // Root: requestSeqNum
        enc.WriteExtensionBit(false);

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        return enc.ToArray();
    }

    // ──────────────────────────────────────────────────────────
    //  Request decoders
    // ──────────────────────────────────────────────────────────

    private static GatekeeperRequest DecodeGatekeeperRequest(PerDecoder dec)
    {
        // GatekeeperRequest SEQUENCE (extensible)
        // Root: requestSeqNum, protocolIdentifier, nonStandardData OPTIONAL,
        //       rasAddress, endpointType, gatekeeperIdentifier OPTIONAL,
        //       endpointAlias (SEQ OF AliasAddress) OPTIONAL
        var hasExtensions = dec.ReadExtensionBit();
        var optionals = dec.ReadOptionalBitmap(3); // nonStandardData, gatekeeperIdentifier, endpointAlias

        var seqNum = (int)dec.ReadConstrainedWholeNumber(H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        var protocolId = dec.ReadObjectIdentifier();

        // nonStandardData OPTIONAL
        if (optionals[0])
        {
            H225Types.SkipNonStandardParameter(dec);
        }

        // rasAddress
        var rasAddress = H225Types.ReadTransportAddress(dec);

        // endpointType
        H225Types.SkipEndpointType(dec);

        // gatekeeperIdentifier OPTIONAL
        string gkId = null;
        if (optionals[1])
        {
            gkId = dec.ReadBMPString(lb: H225Constants.GK_ID_MIN, ub: H225Constants.GK_ID_MAX);
        }

        // endpointAlias OPTIONAL
        string[] aliases = null;
        if (optionals[2])
        {
            aliases = H225Types.ReadAliasAddresses(dec);
        }

        // Skip extensions
        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }

        return new GatekeeperRequest
        {
            RequestSeqNum = seqNum,
            ProtocolIdentifier = protocolId,
            RasAddress = rasAddress,
            GatekeeperIdentifier = gkId,
            Aliases = aliases
        };
    }

    private static RegistrationRequest DecodeRegistrationRequest(PerDecoder dec)
    {
        // RegistrationRequest SEQUENCE (extensible)
        // Root: requestSeqNum, protocolIdentifier, nonStandardData OPTIONAL,
        //       discoveryComplete, callSignalAddress (SEQ OF), rasAddress (SEQ OF),
        //       terminalType, terminalAlias (SEQ OF) OPTIONAL,
        //       gatekeeperIdentifier OPTIONAL, endpointVendor
        var hasExtensions = dec.ReadExtensionBit();
        var optionals = dec.ReadOptionalBitmap(3); // nonStandardData, terminalAlias, gatekeeperIdentifier

        var seqNum = (int)dec.ReadConstrainedWholeNumber(H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        var protocolId = dec.ReadObjectIdentifier();

        if (optionals[0])
        {
            H225Types.SkipNonStandardParameter(dec);
        }

        var discoveryComplete = dec.ReadBoolean();

        var callSignalAddresses = H225Types.ReadTransportAddresses(dec);
        var rasAddresses = H225Types.ReadTransportAddresses(dec);

        // terminalType (EndpointType)
        H225Types.SkipEndpointType(dec);

        // terminalAlias OPTIONAL
        string[] aliases = null;
        if (optionals[1])
        {
            aliases = H225Types.ReadAliasAddresses(dec);
        }

        // gatekeeperIdentifier OPTIONAL
        string gkId = null;
        if (optionals[2])
        {
            gkId = dec.ReadBMPString(lb: H225Constants.GK_ID_MIN, ub: H225Constants.GK_ID_MAX);
        }

        // endpointVendor (VendorIdentifier) — skip it
        H225Types.SkipVendorIdentifier(dec);

        // Read extension additions for timeToLive etc.
        var timeToLive = 0;

        if (hasExtensions)
        {
            var extensions = dec.ReadExtensionAdditions();

            // Extension 0 in RRQ is alternateEndpoints, extension 1 is timeToLive
            if (extensions.Length > 1 && extensions[1] != null)
            {
                var ttlDec = new PerDecoder(extensions[1]);
                timeToLive = (int)ttlDec.ReadConstrainedWholeNumber(1, 4294967295L);
            }
        }

        return new RegistrationRequest
        {
            RequestSeqNum = seqNum,
            ProtocolIdentifier = protocolId,
            CallSignalAddresses = callSignalAddresses,
            RasAddresses = rasAddresses,
            DiscoverComplete = discoveryComplete,
            Aliases = aliases,
            GatekeeperIdentifier = gkId,
            TimeToLive = timeToLive
        };
    }

    private static UnregistrationRequest DecodeUnregistrationRequest(PerDecoder dec)
    {
        // UnregistrationRequest SEQUENCE (extensible)
        // Root: requestSeqNum, callSignalAddress (SEQ OF),
        //       endpointAlias (SEQ OF) OPTIONAL, nonStandardData OPTIONAL,
        //       endpointIdentifier OPTIONAL
        var hasExtensions = dec.ReadExtensionBit();
        var optionals = dec.ReadOptionalBitmap(3); // endpointAlias, nonStandardData, endpointIdentifier

        var seqNum = (int)dec.ReadConstrainedWholeNumber(H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        var callSignalAddresses = H225Types.ReadTransportAddresses(dec);

        string[] aliases = null;
        if (optionals[0])
        {
            aliases = H225Types.ReadAliasAddresses(dec);
        }

        if (optionals[1])
        {
            H225Types.SkipNonStandardParameter(dec);
        }

        string endpointId = null;
        if (optionals[2])
        {
            endpointId = dec.ReadBMPString(lb: H225Constants.EP_ID_MIN, ub: H225Constants.EP_ID_MAX);
        }

        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }

        return new UnregistrationRequest
        {
            RequestSeqNum = seqNum,
            CallSignalAddresses = callSignalAddresses,
            EndpointIdentifier = endpointId,
            Aliases = aliases
        };
    }

    private static AdmissionRequest DecodeAdmissionRequest(PerDecoder dec)
    {
        // AdmissionRequest SEQUENCE (extensible)
        // Root: requestSeqNum, callType, callModel OPTIONAL,
        //       endpointIdentifier, destinationInfo (SEQ OF) OPTIONAL,
        //       destCallSignalAddress OPTIONAL, destExtraCallInfo (SEQ OF) OPTIONAL,
        //       srcInfo (SEQ OF), srcCallSignalAddress OPTIONAL,
        //       bandWidth, callReferenceValue, nonStandardData OPTIONAL,
        //       callServices OPTIONAL, conferenceID OCTET STRING (SIZE 16),
        //       activeMC, answerCall
        var hasExtensions = dec.ReadExtensionBit();

        // 7 OPTIONAL root fields:
        // [0] callModel, [1] destinationInfo, [2] destCallSignalAddress,
        // [3] destExtraCallInfo, [4] srcCallSignalAddress, [5] nonStandardData,
        // [6] callServices
        var optionals = dec.ReadOptionalBitmap(7);

        var seqNum = (int)dec.ReadConstrainedWholeNumber(H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        // callType CHOICE (4 root alternatives, extensible)
        var callType = dec.ReadChoiceIndex(4, extensible: true);

        // callModel OPTIONAL
        if (optionals[0])
        {
            dec.ReadChoiceIndex(2, extensible: true); // direct or gatekeeperRouted
        }

        // endpointIdentifier
        var endpointId = dec.ReadBMPString(lb: H225Constants.EP_ID_MIN, ub: H225Constants.EP_ID_MAX);

        // destinationInfo OPTIONAL
        string[] destAliases = null;
        if (optionals[1])
        {
            destAliases = H225Types.ReadAliasAddresses(dec);
        }

        // destCallSignalAddress OPTIONAL
        if (optionals[2])
        {
            H225Types.SkipTransportAddress(dec);
        }

        // destExtraCallInfo OPTIONAL
        if (optionals[3])
        {
            H225Types.SkipAliasAddresses(dec);
        }

        // srcInfo SEQUENCE OF AliasAddress
        var srcAliases = H225Types.ReadAliasAddresses(dec);

        // srcCallSignalAddress OPTIONAL
        IPEndPoint srcCallSignal = null;
        if (optionals[4])
        {
            srcCallSignal = H225Types.ReadTransportAddress(dec);
        }

        // bandWidth INTEGER (0..4294967295)
        var bandWidth = (int)dec.ReadConstrainedWholeNumber(0, 4294967295L);

        // callReferenceValue INTEGER (0..65535)
        dec.ReadConstrainedWholeNumber(0, 65535);

        // nonStandardData OPTIONAL
        if (optionals[5])
        {
            H225Types.SkipNonStandardParameter(dec);
        }

        // callServices OPTIONAL (QseriesOptions)
        if (optionals[6])
        {
            SkipQseriesOptions(dec);
        }

        // conferenceID OCTET STRING (SIZE 16)
        dec.ReadOctetString(lb: 16, ub: 16);

        // activeMC BOOLEAN
        dec.ReadBoolean();

        // answerCall BOOLEAN
        var answerCall = dec.ReadBoolean();

        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }

        return new AdmissionRequest
        {
            RequestSeqNum = seqNum,
            CallType = callType,
            EndpointIdentifier = endpointId,
            DestinationAliases = destAliases,
            SourceAliases = srcAliases,
            SrcCallSignalAddress = srcCallSignal,
            BandWidth = bandWidth,
            AnswerCall = answerCall
        };
    }

    private static DisengageRequest DecodeDisengageRequest(PerDecoder dec)
    {
        // DisengageRequest SEQUENCE (extensible)
        // Root: requestSeqNum, endpointIdentifier, conferenceID OCTET STRING (SIZE 16),
        //       callReferenceValue, disengageReason, nonStandardData OPTIONAL
        var hasExtensions = dec.ReadExtensionBit();
        var optionals = dec.ReadOptionalBitmap(1); // nonStandardData

        var seqNum = (int)dec.ReadConstrainedWholeNumber(H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        var endpointId = dec.ReadBMPString(lb: H225Constants.EP_ID_MIN, ub: H225Constants.EP_ID_MAX);

        // conferenceID OCTET STRING (SIZE 16)
        dec.ReadOctetString(lb: 16, ub: 16);

        // callReferenceValue INTEGER (0..65535)
        dec.ReadConstrainedWholeNumber(0, 65535);

        // disengageReason CHOICE (3 root alternatives, extensible)
        var reason = dec.ReadChoiceIndex(3, extensible: true);

        if (optionals[0])
        {
            H225Types.SkipNonStandardParameter(dec);
        }

        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }

        return new DisengageRequest
        {
            RequestSeqNum = seqNum,
            EndpointIdentifier = endpointId,
            DisengageReason = reason
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Skip helpers for complex types used in ARQ
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// QseriesOptions: SEQUENCE of 6 BOOLEANs (extensible).
    /// </summary>
    private static void SkipQseriesOptions(PerDecoder dec)
    {
        var hasExtensions = dec.ReadExtensionBit();

        // 6 root BOOLEANs: q932Full, q951Full, q952, q953, q955, q956
        for (var i = 0; i < 6; i++)
        {
            dec.ReadBoolean();
        }

        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }
    }

}
