// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.GCC;

// ──────────────────────────────────────────────────────────
//  ConnectData wrapper (outermost GCC envelope)
// ──────────────────────────────────────────────────────────

/// <summary>
/// T.124 ConnectData — wraps a ConnectGCCPDU inside MCS Connect-Initial/Response userData.
///
/// ConnectData ::= SEQUENCE {
///     t124Identifier  Key,           -- OBJECT IDENTIFIER {0 0 20 124 0 1}
///     connectPDU      OCTET STRING   -- PER-encoded ConnectGCCPDU
/// }
/// </summary>
internal class GccConnectData
{
    /// <summary>T.124 protocol OID (normally {0 0 20 124 0 1}).</summary>
    public int[] Identifier { get; init; }

    /// <summary>Raw PER-encoded ConnectGCCPDU.</summary>
    public byte[] ConnectPdu { get; init; }
}

// ──────────────────────────────────────────────────────────
//  User data blocks
// ──────────────────────────────────────────────────────────

/// <summary>
/// One entry from UserData ::= SET OF SEQUENCE { key Key, value OCTET STRING OPTIONAL }.
/// </summary>
internal class GccUserDataBlock
{
    /// <summary>If key is object OID.</summary>
    public int[] ObjectKey { get; init; }

    /// <summary>If key is H.221 non-standard (4..255 bytes, typically 4).</summary>
    public byte[] H221Key { get; init; }

    /// <summary>Opaque application data (may be null if value OPTIONAL absent).</summary>
    public byte[] Value { get; init; }

    /// <summary>Whether this block uses an H.221 key.</summary>
    public bool IsH221 => H221Key != null;
}

// ──────────────────────────────────────────────────────────
//  ConferenceCreateRequest
// ──────────────────────────────────────────────────────────

/// <summary>
/// Parsed GCC ConferenceCreateRequest from MCS Connect-Initial userData.
/// </summary>
internal class ConferenceCreateRequest
{
    public string ConferenceNameNumeric { get; init; }
    public string ConferenceNameText { get; init; }
    public bool LockedConference { get; init; }
    public bool ListedConference { get; init; }
    public bool ConducibleConference { get; init; }
    public int TerminationMethod { get; init; }
    public GccUserDataBlock[] UserData { get; init; }
}

// ──────────────────────────────────────────────────────────
//  ConferenceCreateResponse
// ──────────────────────────────────────────────────────────

/// <summary>
/// GCC ConferenceCreateResponse for MCS Connect-Response userData.
/// </summary>
internal class ConferenceCreateResponse
{
    /// <summary>Node ID assigned to this participant (1001..65535).</summary>
    public int NodeId { get; init; }

    /// <summary>Conference tag (arbitrary integer, typically 1).</summary>
    public int Tag { get; init; }

    /// <summary>Result code.</summary>
    public int Result { get; init; }

    /// <summary>Optional user data blocks to send to the client.</summary>
    public GccUserDataBlock[] UserData { get; init; }
}

// ──────────────────────────────────────────────────────────
//  GCC Codec
// ──────────────────────────────────────────────────────────

/// <summary>
/// PER codec for GCC T.124 messages.
/// Handles ConnectData wrapper, ConferenceCreateRequest/Response.
/// </summary>
internal static class GccCodec
{
    // ──────────────────────────────────────────────────────────
    //  ConnectData encode/decode
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Encode a ConnectData wrapper: Key(OID) + connectPDU.
    /// </summary>
    public static byte[] EncodeConnectData(int[] identifier, byte[] connectPdu)
    {
        var enc = new PerEncoder();

        // Key CHOICE (2 root, NOT extensible)
        enc.WriteChoiceIndex(GccConstants.KEY_OBJECT, GccConstants.KEY_ROOT_COUNT);

        // OBJECT IDENTIFIER
        enc.WriteObjectIdentifier(identifier);

        // connectPDU OCTET STRING (unconstrained)
        enc.WriteOctetString(connectPdu);

        return enc.ToArray();
    }

    /// <summary>
    /// Decode a ConnectData wrapper.
    /// </summary>
    public static GccConnectData DecodeConnectData(byte[] data)
    {
        var dec = new PerDecoder(data);

        // Key CHOICE (2 root, NOT extensible)
        var keyChoice = dec.ReadChoiceIndex(GccConstants.KEY_ROOT_COUNT);

        int[] oid = null;
        byte[] h221Key = null;

        if (keyChoice == GccConstants.KEY_OBJECT)
        {
            oid = dec.ReadObjectIdentifier();
        }
        else
        {
            // H221NonStandardIdentifier: OCTET STRING SIZE(4..255)
            h221Key = dec.ReadOctetString(lb: 4, ub: 255);
        }

        // connectPDU OCTET STRING (unconstrained)
        var connectPdu = dec.ReadOctetString();

        return new GccConnectData
        {
            Identifier = oid,
            ConnectPdu = connectPdu
        };
    }

    /// <summary>
    /// Check if data looks like a T.124 ConnectData with the standard OID.
    /// Quick heuristic: first bit is 0 (Key.object), then OID length = 5,
    /// then OID content starts with 0x00 0x14 0x7C.
    /// </summary>
    public static bool IsT124ConnectData(byte[] data)
    {
        // Minimum: 1 bit (choice) + 5 bytes OID + at least 1 byte connectPDU
        if (data == null || data.Length < 8)
        {
            return false;
        }

        // First bit = 0 means Key.object. After alignment, byte 1 = OID length = 5.
        // Byte 2..6 = OID content starting with 00 14 7C 00 01.
        return (data[0] & 0x80) == 0 &&
               data[1] == 5 &&
               data[2] == 0x00 && data[3] == 0x14 && data[4] == 0x7C &&
               data[5] == 0x00 && data[6] == 0x01;
    }

    // ──────────────────────────────────────────────────────────
    //  ConferenceCreateRequest decode
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Decode the full GCC pipeline: ConnectData → ConnectGCCPDU → ConferenceCreateRequest.
    /// Input is the raw MCS Connect-Initial userData.
    /// </summary>
    public static ConferenceCreateRequest DecodeConferenceCreateRequest(byte[] mcsUserData)
    {
        // Layer 1: ConnectData wrapper
        var connectData = DecodeConnectData(mcsUserData);

        // Layer 2: ConnectGCCPDU
        var dec = new PerDecoder(connectData.ConnectPdu);

        // ConnectGCCPDU CHOICE (8 root, extensible)
        var choiceIndex = dec.ReadChoiceIndex(GccConstants.CONNECT_ROOT_COUNT, extensible: true);

        if (choiceIndex != GccConstants.CONNECT_CONFERENCE_CREATE_REQUEST)
        {
            throw new ArgumentException(
                $"Expected ConferenceCreateRequest (0), got {GccConstants.ConnectPduName(choiceIndex)}");
        }

        return DecodeConferenceCreateRequestBody(dec);
    }

    /// <summary>
    /// Decode ConferenceCreateRequest SEQUENCE body from the decoder.
    /// </summary>
    private static ConferenceCreateRequest DecodeConferenceCreateRequestBody(PerDecoder dec)
    {
        // ConferenceCreateRequest SEQUENCE (extensible)
        var hasExtensions = dec.ReadExtensionBit();

        // 8 OPTIONAL fields
        var optionals = dec.ReadOptionalBitmap(GccConstants.CCR_OPTIONAL_COUNT);

        // conferenceName: ConferenceName SEQUENCE (extensible)
        var confNameExt = dec.ReadExtensionBit();
        var confNameTextPresent = dec.ReadBit();

        // numeric: SimpleNumericString SIZE(1..255)
        var numericStr = ReadSimpleNumericString(dec, 1, 255);

        // text: TextString SIZE(1..255) OPTIONAL
        string textStr = null;
        if (confNameTextPresent)
        {
            textStr = dec.ReadBMPString(lb: 1, ub: 255);
        }

        // Skip ConferenceName extensions
        if (confNameExt)
        {
            dec.ReadExtensionAdditions();
        }

        // convenerPassword OPTIONAL
        if (optionals[GccConstants.CCR_OPT_CONVENER_PASSWORD])
        {
            SkipPassword(dec);
        }

        // password OPTIONAL
        if (optionals[GccConstants.CCR_OPT_PASSWORD])
        {
            SkipPassword(dec);
        }

        // lockedConference BOOLEAN (required)
        var locked = dec.ReadBoolean();

        // listedConference BOOLEAN (required)
        var listed = dec.ReadBoolean();

        // conducibleConference BOOLEAN (required)
        var conducible = dec.ReadBoolean();

        // terminationMethod ENUMERATED (2 root, extensible)
        var termMethod = dec.ReadEnumerated(GccConstants.TERMINATION_ROOT_COUNT, extensible: true);

        // conductorPrivileges OPTIONAL
        if (optionals[GccConstants.CCR_OPT_CONDUCTOR_PRIVILEGES])
        {
            SkipSetOfPrivilege(dec);
        }

        // conductedPrivileges OPTIONAL
        if (optionals[GccConstants.CCR_OPT_CONDUCTED_PRIVILEGES])
        {
            SkipSetOfPrivilege(dec);
        }

        // nonConductedPrivileges OPTIONAL
        if (optionals[GccConstants.CCR_OPT_NON_CONDUCTED_PRIVILEGES])
        {
            SkipSetOfPrivilege(dec);
        }

        // conferenceDescription TextString OPTIONAL
        if (optionals[GccConstants.CCR_OPT_CONFERENCE_DESCRIPTION])
        {
            dec.ReadBMPString(lb: 0, ub: 255);
        }

        // callerIdentifier TextString OPTIONAL
        if (optionals[GccConstants.CCR_OPT_CALLER_IDENTIFIER])
        {
            dec.ReadBMPString(lb: 0, ub: 255);
        }

        // userData OPTIONAL
        GccUserDataBlock[] userData = null;
        if (optionals[GccConstants.CCR_OPT_USER_DATA])
        {
            userData = DecodeUserDataSet(dec);
        }

        // Skip CCR extensions
        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }

        return new ConferenceCreateRequest
        {
            ConferenceNameNumeric = numericStr,
            ConferenceNameText = textStr,
            LockedConference = locked,
            ListedConference = listed,
            ConducibleConference = conducible,
            TerminationMethod = termMethod,
            UserData = userData
        };
    }

    // ──────────────────────────────────────────────────────────
    //  ConferenceCreateResponse encode/decode
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Encode the full GCC pipeline: ConferenceCreateResponse → ConnectGCCPDU → ConnectData.
    /// Returns bytes suitable for MCS Connect-Response userData.
    /// </summary>
    public static byte[] EncodeConferenceCreateResponse(ConferenceCreateResponse response)
    {
        // Build the ConnectGCCPDU content
        var inner = new PerEncoder();

        // ConnectGCCPDU CHOICE (8 root, extensible)
        inner.WriteChoiceIndex(GccConstants.CONNECT_CONFERENCE_CREATE_RESPONSE,
            GccConstants.CONNECT_ROOT_COUNT, extensible: true);

        // ConferenceCreateResponse SEQUENCE (extensible)
        inner.WriteExtensionBit(false); // No extensions

        // 1 OPTIONAL field: userData
        inner.WriteOptionalBitmap(response.UserData != null && response.UserData.Length > 0);

        // nodeID: UserID (INTEGER 1001..65535)
        inner.WriteConstrainedWholeNumber(response.NodeId, 1001, 65535);

        // tag: INTEGER (unconstrained)
        inner.WriteUnconstrainedWholeNumber(response.Tag);

        // result: ENUMERATED (5 root, extensible)
        inner.WriteEnumerated(response.Result, GccConstants.RESULT_ROOT_COUNT, extensible: true);

        // userData OPTIONAL
        if (response.UserData != null && response.UserData.Length > 0)
        {
            EncodeUserDataSet(inner, response.UserData);
        }

        var connectPdu = inner.ToArray();

        // Wrap in ConnectData
        return EncodeConnectData(GccConstants.T124_OID, connectPdu);
    }

    /// <summary>
    /// Decode a ConferenceCreateResponse from MCS Connect-Response userData.
    /// </summary>
    public static ConferenceCreateResponse DecodeConferenceCreateResponse(byte[] mcsUserData)
    {
        // Layer 1: ConnectData wrapper
        var connectData = DecodeConnectData(mcsUserData);

        // Layer 2: ConnectGCCPDU
        var dec = new PerDecoder(connectData.ConnectPdu);

        // ConnectGCCPDU CHOICE (8 root, extensible)
        var choiceIndex = dec.ReadChoiceIndex(GccConstants.CONNECT_ROOT_COUNT, extensible: true);

        if (choiceIndex != GccConstants.CONNECT_CONFERENCE_CREATE_RESPONSE)
        {
            throw new ArgumentException(
                $"Expected ConferenceCreateResponse (1), got {GccConstants.ConnectPduName(choiceIndex)}");
        }

        // ConferenceCreateResponse SEQUENCE (extensible)
        var hasExtensions = dec.ReadExtensionBit();

        // 1 OPTIONAL field: userData
        var optionals = dec.ReadOptionalBitmap(GccConstants.CCRESP_OPTIONAL_COUNT);

        // nodeID: UserID (INTEGER 1001..65535)
        var nodeId = (int)dec.ReadConstrainedWholeNumber(1001, 65535);

        // tag: INTEGER (unconstrained)
        var tag = (int)dec.ReadUnconstrainedWholeNumber();

        // result: ENUMERATED (5 root, extensible)
        var result = dec.ReadEnumerated(GccConstants.RESULT_ROOT_COUNT, extensible: true);

        // userData OPTIONAL
        GccUserDataBlock[] userData = null;
        if (optionals[GccConstants.CCRESP_OPT_USER_DATA])
        {
            userData = DecodeUserDataSet(dec);
        }

        // Skip extensions
        if (hasExtensions)
        {
            dec.ReadExtensionAdditions();
        }

        return new ConferenceCreateResponse
        {
            NodeId = nodeId,
            Tag = tag,
            Result = result,
            UserData = userData
        };
    }

    // ──────────────────────────────────────────────────────────
    //  ConferenceCreateRequest encode (for testing/client simulation)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Encode the full GCC pipeline: ConferenceCreateRequest → ConnectGCCPDU → ConnectData.
    /// Returns bytes suitable for MCS Connect-Initial userData.
    /// </summary>
    public static byte[] EncodeConferenceCreateRequest(ConferenceCreateRequest request)
    {
        var inner = new PerEncoder();

        // ConnectGCCPDU CHOICE (8 root, extensible)
        inner.WriteChoiceIndex(GccConstants.CONNECT_CONFERENCE_CREATE_REQUEST,
            GccConstants.CONNECT_ROOT_COUNT, extensible: true);

        // ConferenceCreateRequest SEQUENCE (extensible)
        inner.WriteExtensionBit(false); // No extensions

        // 8 OPTIONAL fields
        inner.WriteOptionalBitmap(
            false, // convenerPassword
            false, // password
            false, // conductorPrivileges
            false, // conductedPrivileges
            false, // nonConductedPrivileges
            false, // conferenceDescription
            false, // callerIdentifier
            request.UserData != null && request.UserData.Length > 0 // userData
        );

        // conferenceName: ConferenceName SEQUENCE (extensible)
        inner.WriteExtensionBit(false); // No ConferenceName extensions
        inner.WriteBit(request.ConferenceNameText != null); // text OPTIONAL present?

        // numeric: SimpleNumericString SIZE(1..255)
        WriteSimpleNumericString(inner, request.ConferenceNameNumeric, 1, 255);

        // text: TextString SIZE(1..255) OPTIONAL
        if (request.ConferenceNameText != null)
        {
            inner.WriteBMPString(request.ConferenceNameText, lb: 1, ub: 255);
        }

        // lockedConference BOOLEAN
        inner.WriteBoolean(request.LockedConference);

        // listedConference BOOLEAN
        inner.WriteBoolean(request.ListedConference);

        // conducibleConference BOOLEAN
        inner.WriteBoolean(request.ConducibleConference);

        // terminationMethod ENUMERATED (2 root, extensible)
        inner.WriteEnumerated(request.TerminationMethod,
            GccConstants.TERMINATION_ROOT_COUNT, extensible: true);

        // userData OPTIONAL
        if (request.UserData != null && request.UserData.Length > 0)
        {
            EncodeUserDataSet(inner, request.UserData);
        }

        var connectPdu = inner.ToArray();

        // Wrap in ConnectData
        return EncodeConnectData(GccConstants.T124_OID, connectPdu);
    }

    // ──────────────────────────────────────────────────────────
    //  UserData SET OF SEQUENCE { key Key, value OCTET STRING OPTIONAL }
    // ──────────────────────────────────────────────────────────

    private static GccUserDataBlock[] DecodeUserDataSet(PerDecoder dec)
    {
        // SET OF length (unconstrained)
        var count = dec.ReadLengthDeterminant();
        var blocks = new GccUserDataBlock[count];

        for (var i = 0; i < count; i++)
        {
            blocks[i] = DecodeUserDataBlock(dec);
        }

        return blocks;
    }

    private static GccUserDataBlock DecodeUserDataBlock(PerDecoder dec)
    {
        // SEQUENCE { key Key, value OCTET STRING OPTIONAL }
        // This SEQUENCE is NOT extensible and has 1 OPTIONAL (value)
        var valuePresent = dec.ReadBit();

        // Key CHOICE (2 root, NOT extensible)
        var keyChoice = dec.ReadChoiceIndex(GccConstants.KEY_ROOT_COUNT);

        int[] objectKey = null;
        byte[] h221Key = null;

        if (keyChoice == GccConstants.KEY_OBJECT)
        {
            objectKey = dec.ReadObjectIdentifier();
        }
        else
        {
            // H221NonStandardIdentifier: OCTET STRING SIZE(4..255)
            h221Key = dec.ReadOctetString(lb: 4, ub: 255);
        }

        // value OCTET STRING OPTIONAL (unconstrained)
        byte[] value = null;
        if (valuePresent)
        {
            value = dec.ReadOctetString();
        }

        return new GccUserDataBlock
        {
            ObjectKey = objectKey,
            H221Key = h221Key,
            Value = value
        };
    }

    private static void EncodeUserDataSet(PerEncoder enc, GccUserDataBlock[] blocks)
    {
        // SET OF length
        enc.WriteLengthDeterminant(blocks.Length);

        foreach (var block in blocks)
        {
            EncodeUserDataBlock(enc, block);
        }
    }

    private static void EncodeUserDataBlock(PerEncoder enc, GccUserDataBlock block)
    {
        // SEQUENCE { key Key, value OCTET STRING OPTIONAL }
        enc.WriteBit(block.Value != null); // value present?

        if (block.IsH221)
        {
            enc.WriteChoiceIndex(GccConstants.KEY_H221_NON_STANDARD, GccConstants.KEY_ROOT_COUNT);
            enc.WriteOctetString(block.H221Key, lb: 4, ub: 255);
        }
        else
        {
            enc.WriteChoiceIndex(GccConstants.KEY_OBJECT, GccConstants.KEY_ROOT_COUNT);
            enc.WriteObjectIdentifier(block.ObjectKey);
        }

        if (block.Value != null)
        {
            enc.WriteOctetString(block.Value);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  SimpleNumericString (NumericString FROM("0123456789"))
    //
    //  PER encoding: constrained length + 4 bits per character.
    //  Alphabet: '0'→0, '1'→1, ..., '9'→9.
    // ──────────────────────────────────────────────────────────

    internal static void WriteSimpleNumericString(PerEncoder enc, string value, int lb, int ub)
    {
        // Constrained length (lb..ub)
        enc.WriteConstrainedLengthDeterminant(value.Length, lb, ub);

        // Octet-align before character data
        enc.AlignToOctet();

        // Each character: 4 bits (NumericString alphabet, FROM constraint)
        foreach (var ch in value)
        {
            var charValue = ch - '0';
            enc.WriteBits((uint)charValue, 4);
        }
    }

    internal static string ReadSimpleNumericString(PerDecoder dec, int lb, int ub)
    {
        // Constrained length
        var length = dec.ReadConstrainedLengthDeterminant(lb, ub);

        // Octet-align before character data
        dec.AlignToOctet();

        // Each character: 4 bits
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            var charValue = (int)dec.ReadBits(4);
            chars[i] = (char)('0' + charValue);
        }

        return new string(chars);
    }

    // ──────────────────────────────────────────────────────────
    //  Skip helpers (for OPTIONAL fields we don't need to parse)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Skip a Password field.
    /// Password ::= SEQUENCE { numeric SimpleNumericString SIZE(0..255),
    ///                          text    TextString SIZE(0..255) OPTIONAL, ... }
    /// </summary>
    private static void SkipPassword(PerDecoder dec)
    {
        // Password SEQUENCE (extensible)
        var hasExt = dec.ReadExtensionBit();
        var textPresent = dec.ReadBit();

        // numeric: SimpleNumericString SIZE(0..255)
        ReadSimpleNumericString(dec, 0, 255);

        // text: TextString OPTIONAL
        if (textPresent)
        {
            dec.ReadBMPString(lb: 0, ub: 255);
        }

        if (hasExt)
        {
            dec.ReadExtensionAdditions();
        }
    }

    /// <summary>
    /// Skip SET OF Privilege.
    /// Privilege ::= ENUMERATED { terminate(0), ejectUser(1), add(2),
    ///     lockUnlock(3), transfer(4), ... }
    /// </summary>
    private static void SkipSetOfPrivilege(PerDecoder dec)
    {
        var count = dec.ReadLengthDeterminant();
        for (var i = 0; i < count; i++)
        {
            // Privilege ENUMERATED (5 root, extensible)
            dec.ReadEnumerated(5, extensible: true);
        }
    }
}
