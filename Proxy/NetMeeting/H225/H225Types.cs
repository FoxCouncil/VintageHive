// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// H.225.0 ASN.1 type codecs for TransportAddress, AliasAddress,
/// EndpointType, and related structures used by RAS messages.
/// Encode/decode using PER ALIGNED as per ITU-T X.691.
/// </summary>
internal static class H225Types
{
    // ──────────────────────────────────────────────────────────
    //  TransportAddress (CHOICE, 7 root alternatives, NOT extensible in v2)
    //
    //  We only implement ipAddress (index 0):
    //    ipAddress SEQUENCE { ip OCTET STRING (SIZE 4), port INTEGER (0..65535) }
    // ──────────────────────────────────────────────────────────

    public static void WriteTransportAddress(PerEncoder enc, IPEndPoint ep)
    {
        enc.WriteChoiceIndex(H225Constants.TRANSPORT_IP_ADDRESS, H225Constants.TRANSPORT_ROOT_ALTERNATIVES);

        // ipAddress is a SEQUENCE (not extensible)
        enc.WriteOctetString(ep.Address.GetAddressBytes(), lb: 4, ub: 4);
        enc.WriteConstrainedWholeNumber(ep.Port, 0, 65535);
    }

    public static IPEndPoint ReadTransportAddress(PerDecoder dec)
    {
        var choice = dec.ReadChoiceIndex(H225Constants.TRANSPORT_ROOT_ALTERNATIVES);

        if (choice != H225Constants.TRANSPORT_IP_ADDRESS)
        {
            throw new NotSupportedException($"TransportAddress variant {choice} not supported");
        }

        var ip = dec.ReadOctetString(lb: 4, ub: 4);
        var port = (int)dec.ReadConstrainedWholeNumber(0, 65535);

        return new IPEndPoint(new IPAddress(ip), port);
    }

    public static void SkipTransportAddress(PerDecoder dec)
    {
        var choice = dec.ReadChoiceIndex(H225Constants.TRANSPORT_ROOT_ALTERNATIVES);

        switch (choice)
        {
            case H225Constants.TRANSPORT_IP_ADDRESS:
            {
                dec.ReadOctetString(lb: 4, ub: 4); // ip
                dec.ReadConstrainedWholeNumber(0, 65535); // port
            }
            break;

            default:
            {
                throw new NotSupportedException($"Cannot skip TransportAddress variant {choice}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  SEQUENCE OF TransportAddress
    // ──────────────────────────────────────────────────────────

    public static void WriteTransportAddresses(PerEncoder enc, IPEndPoint[] endpoints)
    {
        enc.WriteLengthDeterminant(endpoints.Length);

        foreach (var ep in endpoints)
        {
            WriteTransportAddress(enc, ep);
        }
    }

    public static IPEndPoint[] ReadTransportAddresses(PerDecoder dec)
    {
        var count = dec.ReadLengthDeterminant();
        var result = new IPEndPoint[count];

        for (var i = 0; i < count; i++)
        {
            result[i] = ReadTransportAddress(dec);
        }

        return result;
    }

    public static void SkipTransportAddresses(PerDecoder dec)
    {
        var count = dec.ReadLengthDeterminant();

        for (var i = 0; i < count; i++)
        {
            SkipTransportAddress(dec);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  AliasAddress (CHOICE, extensible)
    //
    //  v2 root: e164 (0), h323-ID (1)
    //  Extensions (v3+): url-ID, transport-ID, email-ID, partyNumber
    //  NetMeeting typically uses h323-ID for display name.
    // ──────────────────────────────────────────────────────────

    public static void WriteAliasAddress(PerEncoder enc, string alias, bool isE164 = false)
    {
        if (isE164)
        {
            enc.WriteChoiceIndex(H225Constants.ALIAS_E164, H225Constants.ALIAS_ROOT_ALTERNATIVES, extensible: true);
            enc.WriteIA5String(alias, lb: H225Constants.E164_MIN, ub: H225Constants.E164_MAX);
        }
        else
        {
            enc.WriteChoiceIndex(H225Constants.ALIAS_H323_ID, H225Constants.ALIAS_ROOT_ALTERNATIVES, extensible: true);
            enc.WriteBMPString(alias, lb: H225Constants.H323_ID_MIN, ub: H225Constants.H323_ID_MAX);
        }
    }

    public static (string Alias, bool IsE164) ReadAliasAddress(PerDecoder dec)
    {
        var choice = dec.ReadChoiceIndex(H225Constants.ALIAS_ROOT_ALTERNATIVES, extensible: true);

        switch (choice)
        {
            case H225Constants.ALIAS_E164:
            {
                return (dec.ReadIA5String(lb: H225Constants.E164_MIN, ub: H225Constants.E164_MAX), true);
            }

            case H225Constants.ALIAS_H323_ID:
            {
                return (dec.ReadBMPString(lb: H225Constants.H323_ID_MIN, ub: H225Constants.H323_ID_MAX), false);
            }

            default:
            {
                throw new NotSupportedException($"AliasAddress variant {choice} not supported");
            }
        }
    }

    public static void SkipAliasAddress(PerDecoder dec)
    {
        ReadAliasAddress(dec); // Just discard result
    }

    // ──────────────────────────────────────────────────────────
    //  SEQUENCE OF AliasAddress
    // ──────────────────────────────────────────────────────────

    public static void WriteAliasAddresses(PerEncoder enc, string[] aliases)
    {
        enc.WriteLengthDeterminant(aliases.Length);

        foreach (var alias in aliases)
        {
            WriteAliasAddress(enc, alias);
        }
    }

    public static string[] ReadAliasAddresses(PerDecoder dec)
    {
        var count = dec.ReadLengthDeterminant();
        var result = new string[count];

        for (var i = 0; i < count; i++)
        {
            var (alias, _) = ReadAliasAddress(dec);
            result[i] = alias;
        }

        return result;
    }

    public static void SkipAliasAddresses(PerDecoder dec)
    {
        var count = dec.ReadLengthDeterminant();

        for (var i = 0; i < count; i++)
        {
            SkipAliasAddress(dec);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  EndpointType (SEQUENCE, extensible)
    //
    //  SEQUENCE {
    //    nonStandardData  NonStandardParameter OPTIONAL,  -- [0]
    //    vendor           VendorIdentifier OPTIONAL,       -- [1]
    //    gatekeeper       GatekeeperInfo OPTIONAL,         -- [2]
    //    gateway          GatewayInfo OPTIONAL,             -- [3]
    //    mcu              McuInfo OPTIONAL,                 -- [4]
    //    terminal         TerminalInfo OPTIONAL,            -- [5]
    //    mc               BOOLEAN,                          -- [6]
    //    undefinedNode     BOOLEAN,                         -- [7]
    //    ...
    //  }
    //
    //  NetMeeting sends: vendor present, terminal present, mc=FALSE, undefinedNode=FALSE
    // ──────────────────────────────────────────────────────────

    public static void WriteEndpointType(PerEncoder enc, bool isTerminal = true)
    {
        // Extension bit
        enc.WriteExtensionBit(false);

        // Optional bitmap: nonStandardData, vendor, gatekeeper, gateway, mcu, terminal (6 optionals)
        enc.WriteOptionalBitmap(false, false, false, false, false, isTerminal);

        // terminal (TerminalInfo) is an extensible SEQUENCE with only optional extensions
        if (isTerminal)
        {
            // TerminalInfo: extensible SEQUENCE { ..., no root components }
            enc.WriteExtensionBit(false);
        }

        // mc = FALSE
        enc.WriteBoolean(false);

        // undefinedNode = FALSE
        enc.WriteBoolean(false);
    }

    public static void SkipEndpointType(PerDecoder dec)
    {
        // Extension bit
        var hasExtensions = dec.ReadExtensionBit();

        // 6 optional root fields
        var optionals = dec.ReadOptionalBitmap(6);

        // nonStandardData OPTIONAL
        if (optionals[0])
        {
            SkipNonStandardParameter(dec);
        }

        // vendor OPTIONAL
        if (optionals[1])
        {
            SkipVendorIdentifier(dec);
        }

        // gatekeeper OPTIONAL (GatekeeperInfo: extensible SEQUENCE, no root)
        if (optionals[2])
        {
            SkipEmptyExtensibleSequence(dec);
        }

        // gateway OPTIONAL (GatewayInfo: extensible SEQUENCE with optional fields)
        if (optionals[3])
        {
            SkipGatewayInfo(dec);
        }

        // mcu OPTIONAL (McuInfo: extensible SEQUENCE, no root)
        if (optionals[4])
        {
            SkipEmptyExtensibleSequence(dec);
        }

        // terminal OPTIONAL (TerminalInfo: extensible SEQUENCE, no root)
        if (optionals[5])
        {
            SkipEmptyExtensibleSequence(dec);
        }

        // mc BOOLEAN
        dec.ReadBoolean();

        // undefinedNode BOOLEAN
        dec.ReadBoolean();

        // Extension additions
        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  NonStandardParameter
    //
    //  SEQUENCE {
    //    nonStandardIdentifier  NonStandardIdentifier,  -- CHOICE
    //    data                   OCTET STRING
    //  }
    // ──────────────────────────────────────────────────────────

    public static void SkipNonStandardParameter(PerDecoder dec)
    {
        SkipNonStandardIdentifier(dec);
        dec.ReadOctetString(); // unconstrained data
    }

    // ──────────────────────────────────────────────────────────
    //  NonStandardIdentifier (CHOICE, 3 root alternatives, NOT extensible)
    //
    //  object     OBJECT IDENTIFIER,
    //  h221NonStandard  H221NonStandard,
    //  nonStandardAddress  OCTET STRING (SIZE 1..20)
    //  -- v2 only has object (0) and h221NonStandard (1), v4+ adds index 2
    //  -- For NetMeeting v2 compatibility, we handle 2 root alternatives
    // ──────────────────────────────────────────────────────────

    private static void SkipNonStandardIdentifier(PerDecoder dec)
    {
        // NonStandardIdentifier CHOICE (3 root alternatives in full H.225.0)
        // v2 has only 2: object (0), h221NonStandard (1)
        // We read as 3 to be safe — reading fewer would break on newer messages
        var choice = dec.ReadChoiceIndex(3);

        switch (choice)
        {
            case 0: // OBJECT IDENTIFIER
            {
                dec.ReadObjectIdentifier();
            }
            break;

            case 1: // H221NonStandard
            {
                SkipH221NonStandard(dec);
            }
            break;

            case 2: // nonStandardAddress OCTET STRING (SIZE 1..20)
            {
                dec.ReadOctetString(lb: 1, ub: 20);
            }
            break;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  H221NonStandard
    //
    //  SEQUENCE {
    //    t35CountryCode    INTEGER (0..255),
    //    t35Extension      INTEGER (0..255),
    //    manufacturerCode  INTEGER (0..65535)
    //  }
    // ──────────────────────────────────────────────────────────

    private static void SkipH221NonStandard(PerDecoder dec)
    {
        dec.ReadConstrainedWholeNumber(0, 255);   // t35CountryCode
        dec.ReadConstrainedWholeNumber(0, 255);   // t35Extension
        dec.ReadConstrainedWholeNumber(0, 65535); // manufacturerCode
    }

    // ──────────────────────────────────────────────────────────
    //  VendorIdentifier
    //
    //  SEQUENCE {
    //    vendor         H221NonStandard,
    //    productId      OCTET STRING (SIZE 1..256) OPTIONAL,
    //    versionId      OCTET STRING (SIZE 1..256) OPTIONAL,
    //    ...
    //  }
    // ──────────────────────────────────────────────────────────

    internal static void SkipVendorIdentifier(PerDecoder dec)
    {
        var hasExtensions = dec.ReadExtensionBit();
        var optionals = dec.ReadOptionalBitmap(2); // productId, versionId

        SkipH221NonStandard(dec);

        if (optionals[0])
        {
            dec.ReadOctetString(lb: 1, ub: 256); // productId
        }

        if (optionals[1])
        {
            dec.ReadOctetString(lb: 1, ub: 256); // versionId
        }

        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  GatewayInfo
    //
    //  SEQUENCE {
    //    protocol  SEQUENCE OF SupportedProtocols OPTIONAL,
    //    nonStandardData  NonStandardParameter OPTIONAL,
    //    ...
    //  }
    // ──────────────────────────────────────────────────────────

    private static void SkipGatewayInfo(PerDecoder dec)
    {
        var hasExtensions = dec.ReadExtensionBit();
        var optionals = dec.ReadOptionalBitmap(2);

        if (optionals[0])
        {
            // SEQUENCE OF SupportedProtocols — skip as open types
            var count = dec.ReadLengthDeterminant();

            for (var i = 0; i < count; i++)
            {
                SkipSupportedProtocols(dec);
            }
        }

        if (optionals[1])
        {
            SkipNonStandardParameter(dec);
        }

        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  SupportedProtocols (CHOICE, extensible, 9 root alternatives in v2)
    //  We only need to skip past it, not decode the contents.
    // ──────────────────────────────────────────────────────────

    private static void SkipSupportedProtocols(PerDecoder dec)
    {
        // This is an extensible CHOICE. Each alternative is a SEQUENCE
        // with nonStandardData OPTIONAL and dataRatesSupported OPTIONAL.
        // For skip purposes, we'd need full type knowledge.
        // NetMeeting never sends gateway type, so this shouldn't be reached.
        throw new NotSupportedException("SupportedProtocols skip not implemented — NetMeeting does not use Gateway type");
    }

    // ──────────────────────────────────────────────────────────
    //  Extensible SEQUENCE with no root components
    //  (GatekeeperInfo, McuInfo, TerminalInfo)
    // ──────────────────────────────────────────────────────────

    private static void SkipEmptyExtensibleSequence(PerDecoder dec)
    {
        var hasExtensions = dec.ReadExtensionBit();

        // No root fields to read

        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }
    }
}
