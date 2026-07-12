// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.GCC;
using VintageHive.Proxy.NetMeeting.T120;

namespace Adversarial2.Gcc;

// ==========================================================
//  Adversarial tests for the NetMeeting T.120 stack parsers:
//    - X.224 (ISO 8073) Class 0 transport PDU parser
//    - MCS (T.125) BER Connect-Initial/Response decoders
//    - MCS domain PDU (PER) decoder
//    - GCC (T.124) PER ConnectData / ConferenceCreate codec
//
//  Focus: truncated PDUs, oversized/negative declared lengths,
//  bad choice tags, user-data length mismatches, and anything a
//  hostile peer could send. These assert OBSERVED behavior of the
//  code as written; bugs that would over-allocate, corrupt, or
//  crash the host are documented in the returned bug list rather
//  than exercised destructively.
// ==========================================================

// ----------------------------------------------------------
//  X.224 transport PDU adversarial parsing
// ----------------------------------------------------------

[TestClass]
public class X224AdversarialTests
{
    [TestMethod]
    public void Parse_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => X224Message.Parse(null!));
    }

    [TestMethod]
    public void Parse_Empty_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => X224Message.Parse(Array.Empty<byte>()));
    }

    [TestMethod]
    public void Parse_CrLengthIndicatorTooSmall_Throws()
    {
        // CR type (0xE0) but LI = 5 (< required 6) with a full 7-byte buffer.
        var pdu = new byte[] { 5, 0xE0, 0, 0, 0, 0, 0 };
        Assert.ThrowsExactly<ArgumentException>(() => X224Message.Parse(pdu));
    }

    [TestMethod]
    public void Parse_CrBufferShorterThanFixedHeader_Throws()
    {
        // CR with LI = 6 but only 6 bytes present (need 7).
        var pdu = new byte[] { 6, 0xE0, 0, 0, 0, 0 };
        Assert.ThrowsExactly<ArgumentException>(() => X224Message.Parse(pdu));
    }

    [TestMethod]
    public void Parse_CrOversizedLengthIndicator_SilentlyDropsVariableParams()
    {
        // LI claims 200 bytes of variable params, but the buffer holds only the
        // 7 fixed bytes. The parser cannot copy them and silently yields null Data
        // rather than rejecting the inconsistent length. Documented as a swallow.
        var pdu = new byte[] { 200, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00 };
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_CR, parsed.Type);
        Assert.AreEqual((ushort)0x0001, parsed.SrcRef);
        Assert.IsNull(parsed.Data);
    }

    [TestMethod]
    public void Parse_DtTooShort_Throws()
    {
        // DT type (0xF0) but only 2 bytes (needs at least 3 for the EOT/NR byte).
        var pdu = new byte[] { 2, 0xF0 };
        Assert.ThrowsExactly<ArgumentException>(() => X224Message.Parse(pdu));
    }

    [TestMethod]
    public void Parse_DtOversizedLengthIndicator_SilentlyDropsUserData()
    {
        // LI = 200 pushes the computed header end (1 + 200 = 201) past the buffer,
        // so the payload after the header is silently discarded instead of rejected.
        var pdu = new byte[] { 200, 0xF0, 0x80, 0xAA, 0xBB, 0xCC };
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_DT, parsed.Type);
        Assert.IsTrue(parsed.Eot);
        Assert.IsNull(parsed.Data);
    }

    [TestMethod]
    public void Parse_DtLengthIndicatorZero_ShiftsTypeByteIntoUserData()
    {
        // A hostile LI = 0 makes header end = 1, so everything from byte index 1
        // onward (including the 0xF0 type byte and the EOT/NR byte) is treated as
        // user data. This mis-frames the PDU but does not crash. Documented.
        var pdu = new byte[] { 0, 0xF0, 0x80, 0x11, 0x22 };
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_DT, parsed.Type);
        CollectionAssert.AreEqual(new byte[] { 0xF0, 0x80, 0x11, 0x22 }, parsed.Data);
    }

    [TestMethod]
    public void Parse_DrTooShort_Throws()
    {
        // DR type (0x80) with only 6 bytes (needs 7).
        var pdu = new byte[] { 6, 0x80, 0, 0, 0, 0 };
        Assert.ThrowsExactly<ArgumentException>(() => X224Message.Parse(pdu));
    }

    [TestMethod]
    public void Parse_UnknownPduType_Throws()
    {
        // Upper nibble 0x9 is not CR/CC/DT/DR.
        var pdu = new byte[] { 6, 0x90, 0, 0, 0, 0, 0 };
        Assert.ThrowsExactly<NotSupportedException>(() => X224Message.Parse(pdu));
    }

    [TestMethod]
    public void Parse_ZeroTypeNibble_Throws()
    {
        // 0x00 upper nibble is not a known type either.
        var pdu = new byte[] { 6, 0x00, 0, 0, 0, 0, 0 };
        Assert.ThrowsExactly<NotSupportedException>(() => X224Message.Parse(pdu));
    }
}

// ----------------------------------------------------------
//  MCS Connect-Initial / Connect-Response (BER) adversarial
// ----------------------------------------------------------

[TestClass]
public class McsBerAdversarialTests
{
    // 8 INTEGER(0) fields wrapped in a SEQUENCE, the shape ReadBerDomainParameters expects.
    private static byte[] ValidDomainParametersSeq()
    {
        var inner = new List<byte>();

        for (var i = 0; i < 8; i++)
        {
            inner.Add(0x02); // INTEGER
            inner.Add(0x01); // length 1
            inner.Add(0x00); // value 0
        }

        var seq = new List<byte> { 0x30, (byte)inner.Count };
        seq.AddRange(inner);
        return seq.ToArray();
    }

    // Build a BER Connect-Response prefix up to (but not including) the userData field.
    private static byte[] ConnectResponsePrefix()
    {
        var bytes = new List<byte>
        {
            0x7F, 0x66, // Connect-Response tag
            0x00,       // totalLen (unused by the decoder)
            0x0A, 0x01, 0x00, // result ENUMERATED 0
            0x02, 0x01, 0x00  // calledConnectId INTEGER 0
        };
        bytes.AddRange(ValidDomainParametersSeq());
        return bytes.ToArray();
    }

    private static byte[] ConnectResponseWithUserData(byte[] userDataField)
    {
        var bytes = new List<byte>(ConnectResponsePrefix());
        bytes.AddRange(userDataField);
        return bytes.ToArray();
    }

    [TestMethod]
    public void DecodeConnectInitial_WrongSecondTagByte_Throws()
    {
        // 0x7F 0x66 is the Connect-Response tag, not Initial.
        var data = new byte[] { 0x7F, 0x66, 0x00 };
        Assert.ThrowsExactly<ArgumentException>(() => McsCodec.DecodeConnectInitial(data));
    }

    [TestMethod]
    public void DecodeConnectInitial_TruncatedAfterTag_Throws()
    {
        // Tag present, but the length octet is missing. No bounds checking in the
        // BER helpers means an index-out-of-range escapes.
        var data = new byte[] { 0x7F, 0x65 };
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => McsCodec.DecodeConnectInitial(data));
    }

    [TestMethod]
    public void DecodeConnectInitial_TruncatedInsideBody_Throws()
    {
        // Tag + length claim more content than is present.
        var data = new byte[] { 0x7F, 0x65, 0x20 };
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => McsCodec.DecodeConnectInitial(data));
    }

    [TestMethod]
    public void DecodeConnectResponse_TruncatedAfterTag_Throws()
    {
        var data = new byte[] { 0x7F, 0x66 };
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => McsCodec.DecodeConnectResponse(data));
    }

    [TestMethod]
    public void DecodeConnectResponse_TruncatedInsideDomainParameters_Throws()
    {
        // Full prefix except the domain-parameters SEQUENCE is cut short.
        var bytes = new List<byte>
        {
            0x7F, 0x66, 0x00,
            0x0A, 0x01, 0x00,
            0x02, 0x01, 0x00,
            0x30, 0x18 // SEQUENCE claiming 24 bytes, but none follow
        };
        Assert.ThrowsExactly<IndexOutOfRangeException>(
            () => McsCodec.DecodeConnectResponse(bytes.ToArray()));
    }

    [TestMethod]
    public void DecodeConnectResponse_UserDataLengthExceedsBuffer_Throws()
    {
        // userData OCTET STRING declares 4096 bytes (BER long form) but supplies none. ReadBerLength
        // now rejects a declared length that runs past the buffer before anything is allocated,
        // closing the memory-exhaustion DoS a ~2GB declared length would otherwise cause.
        var userData = new byte[] { 0x04, 0x82, 0x10, 0x00 }; // tag, long-form len = 0x1000
        var data = ConnectResponseWithUserData(userData);
        Assert.ThrowsExactly<InvalidDataException>(() => McsCodec.DecodeConnectResponse(data));
    }

    [TestMethod]
    public void DecodeConnectResponse_NegativeDeclaredLength_Throws()
    {
        // BER long-form length 0xFFFFFFFF. ReadBerLength accumulates in a long so it no longer wraps
        // to -1, and the over-buffer check rejects it as malformed instead of hitting a negative alloc.
        var userData = new byte[] { 0x04, 0x84, 0xFF, 0xFF, 0xFF, 0xFF };
        var data = ConnectResponseWithUserData(userData);
        Assert.ThrowsExactly<InvalidDataException>(() => McsCodec.DecodeConnectResponse(data));
    }

    [TestMethod]
    public void DecodeConnectResponse_EmptyUserData_Succeeds()
    {
        // Control: a well-formed empty userData decodes cleanly, confirming the
        // hand-crafted prefix is structurally valid.
        var data = ConnectResponseWithUserData(new byte[] { 0x04, 0x00 });
        var decoded = McsCodec.DecodeConnectResponse(data);

        Assert.AreEqual(0, decoded.Result);
        Assert.AreEqual(0, decoded.CalledConnectId);
        Assert.AreEqual(0, decoded.UserData.Length);
    }
}

// ----------------------------------------------------------
//  MCS domain PDU (PER) adversarial parsing
// ----------------------------------------------------------

[TestClass]
public class McsDomainPduAdversarialTests
{
    [TestMethod]
    public void DecodeDomainPdu_Empty_Throws()
    {
        // No bits at all: even reading the CHOICE index runs off the end.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => McsCodec.DecodeDomainPdu(Array.Empty<byte>()));
    }

    [TestMethod]
    public void DecodeDomainPdu_ErectDomainTruncated_Throws()
    {
        // ErectDomainRequest CHOICE index, then nothing for the two whole numbers.
        var edr = McsCodec.EncodeErectDomainRequest(subHeight: 5, subInterval: 5);
        var truncated = edr[..1]; // keep only the choice byte region
        Assert.ThrowsExactly<InvalidOperationException>(() => McsCodec.DecodeDomainPdu(truncated));
    }

    [TestMethod]
    public void DecodeDomainPdu_SendDataOversizedOctetString_Throws()
    {
        // Hand-build a SendDataRequest header, then write an octet-string length
        // determinant claiming 200 octets while supplying none. PER ReadOctets is
        // bounds-checked, so this is rejected rather than over-read or over-allocated.
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(McsConstants.DOMAIN_SEND_DATA_REQUEST, McsConstants.DOMAIN_ROOT_COUNT);
        enc.WriteConstrainedWholeNumber(1001, 1, 65535);
        enc.WriteConstrainedWholeNumber(7, 0, 65535);
        enc.WriteEnumerated(0, McsConstants.PRIORITY_ROOT_COUNT);
        enc.WriteBits(0xC0 >> 6, 2); // BEGIN + END segmentation
        enc.WriteLengthDeterminant(200); // lies: 200 octets promised, zero delivered
        var evil = enc.ToArray();

        Assert.ThrowsExactly<InvalidOperationException>(() => McsCodec.DecodeDomainPdu(evil));
    }

    [TestMethod]
    public void DecodeDomainPdu_OutOfRangeChoiceIndex_Rejected()
    {
        // 0xF8 -> extension bit is absent (not extensible), the 5-bit constrained choice reads
        // 0b11111 = 31, which is beyond the 28 defined alternatives. DecodeDomainPdu now range-checks
        // the CHOICE index and rejects the malformed PDU instead of returning an empty bogus type.
        Assert.ThrowsExactly<InvalidDataException>(() => McsCodec.DecodeDomainPdu(new byte[] { 0xF8 }));
    }

    [TestMethod]
    public void DecodeDomainPdu_ChannelJoinTruncatedMidField_Throws()
    {
        var good = McsCodec.EncodeChannelJoinRequest(userId: 1001, channelId: 7);
        var truncated = good[..2]; // choice + partial first whole number
        Assert.ThrowsExactly<InvalidOperationException>(() => McsCodec.DecodeDomainPdu(truncated));
    }
}

// ----------------------------------------------------------
//  GCC (T.124) ConnectData / ConferenceCreate adversarial
// ----------------------------------------------------------

[TestClass]
public class GccCodecAdversarialTests
{
    [TestMethod]
    public void DecodeConnectData_Empty_Throws()
    {
        // Reading the Key CHOICE bit off an empty buffer fails.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => GccCodec.DecodeConnectData(Array.Empty<byte>()));
    }

    [TestMethod]
    public void DecodeConnectData_OidLengthOverrunsBuffer_Throws()
    {
        // First bit 0 = Key.object; after octet alignment the OID length byte is
        // 0x7F (127) but no OID octets follow. PER ReadOctets is bounds-checked.
        var data = new byte[] { 0x00, 0x7F };
        Assert.ThrowsExactly<InvalidOperationException>(() => GccCodec.DecodeConnectData(data));
    }

    [TestMethod]
    public void DecodeConnectData_FragmentedLengthDeterminant_Throws()
    {
        // Length determinant with the top two bits set (0xC0..) signals PER
        // fragmentation, which the decoder explicitly refuses.
        var data = new byte[] { 0x00, 0xC1, 0x00, 0x00 };
        Assert.ThrowsExactly<NotSupportedException>(() => GccCodec.DecodeConnectData(data));
    }

    [TestMethod]
    public void DecodeConnectData_H221KeyTruncated_Throws()
    {
        // First bit 1 = Key.h221NonStandard: OCTET STRING SIZE(4..255). The
        // constrained length is present but the key octets are missing.
        var data = new byte[] { 0x80 };
        Assert.ThrowsExactly<InvalidOperationException>(() => GccCodec.DecodeConnectData(data));
    }

    [TestMethod]
    public void DecodeConferenceCreateRequest_WrongPduChoice_Throws()
    {
        // A ConferenceCreateResponse is a valid ConnectData wrapping CHOICE index 1.
        // Decoding it as a request must reject the choice tag mismatch.
        var responseBytes = GccCodec.EncodeConferenceCreateResponse(new ConferenceCreateResponse
        {
            NodeId = 1001,
            Tag = 1,
            Result = GccConstants.RESULT_SUCCESS
        });

        Assert.ThrowsExactly<ArgumentException>(
            () => GccCodec.DecodeConferenceCreateRequest(responseBytes));
    }

    [TestMethod]
    public void DecodeConferenceCreateResponse_WrongPduChoice_Throws()
    {
        // Mirror: a request decoded as a response.
        var requestBytes = GccCodec.EncodeConferenceCreateRequest(new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "1",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC
        });

        Assert.ThrowsExactly<ArgumentException>(
            () => GccCodec.DecodeConferenceCreateResponse(requestBytes));
    }

    [TestMethod]
    public void DecodeConferenceCreateRequest_TruncatedConnectPdu_Throws()
    {
        // Valid ConnectData wrapper but a 1-byte inner connectPDU: the CHOICE index
        // decodes to ConferenceCreateRequest, then the mandatory SEQUENCE body runs
        // out of bits.
        var wrapper = GccCodec.EncodeConnectData(GccConstants.T124_OID, new byte[] { 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(
            () => GccCodec.DecodeConferenceCreateRequest(wrapper));
    }

    [TestMethod]
    public void DecodeConferenceCreateRequest_TruncatedValidEncoding_Throws()
    {
        // Encode a full valid request, then chop the tail so required fields are cut.
        var full = GccCodec.EncodeConferenceCreateRequest(new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "12345",
            LockedConference = true,
            ListedConference = true,
            ConducibleConference = true,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC
        });

        // Keep only the ConnectData header region so the inner PDU is unusable.
        var truncated = full[..(full.Length - 3)];
        Assert.ThrowsExactly<InvalidOperationException>(
            () => GccCodec.DecodeConferenceCreateRequest(truncated));
    }

    [TestMethod]
    public void DecodeConnectData_NonUtf8Payload_TreatedAsOpaqueBytes()
    {
        // The connectPDU is an opaque OCTET STRING: arbitrary binary (including
        // invalid UTF-8 sequences) must round-trip byte-for-byte without any text
        // interpretation.
        var binary = new byte[] { 0xFF, 0xFE, 0x00, 0x80, 0xC0, 0xAF };
        var wrapper = GccCodec.EncodeConnectData(GccConstants.T124_OID, binary);
        var decoded = GccCodec.DecodeConnectData(wrapper);

        CollectionAssert.AreEqual(binary, decoded.ConnectPdu);
    }
}