// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Oscar.Services;

namespace Adversarial4.IcqMeta;

// Adversarial parse tests for IcqUserMetaRequest (Proxy/Oscar/IcqUserMetaRequest.cs).
//
// Wire layout the constructor decodes out of TLV 0x01 inside snac.RawData, all
// multi-byte fields LITTLE-endian (raw BitConverter, NOT network order):
//   [0..2]   dataChunkSize (must equal value.Length - 2, else IsValid=false)
//   [2..6]   ClientUin
//   [6..8]   RequestType
//   [8..10]  Sequence
//   [10..12] RequestSubType   (only when RequestType == CLI_META_INFO_REQ 0x07D0)
//   [12..]   subtype-specific payload
//
// The outer TLV frame itself (type + length) is NETWORK-order per OscarUtils.DecodeTlvs.
[TestClass]
public class IcqUserMetaRequestAdversarialTests
{
    #region Builders

    private static byte[] Cat(params byte[][] parts)
    {
        var list = new List<byte>();

        foreach (var p in parts)
        {
            if (p != null)
            {
                list.AddRange(p);
            }
        }

        return list.ToArray();
    }

    // Prepend a self-consistent little-endian dataChunkSize (== body length) so the
    // internal "tlvLength - 2 == dataChunkSize" gate passes.
    private static byte[] WithLen(byte[] body)
    {
        return Cat(BitConverter.GetBytes((ushort)body.Length), body);
    }

    // Wrap a value as a full TLV blob of type 0x0001 with a truthful network-order length.
    private static byte[] Tlv01(byte[] value)
    {
        return Cat(new byte[] { 0x00, 0x01, (byte)(value.Length >> 8), (byte)(value.Length & 0xFF) }, value);
    }

    // Wrap with an arbitrary (possibly lying) network-order declared length.
    private static byte[] TlvFrame(ushort type, ushort declaredLen, byte[] value)
    {
        return Cat(new byte[] { (byte)(type >> 8), (byte)(type & 0xFF), (byte)(declaredLen >> 8), (byte)(declaredLen & 0xFF) }, value);
    }

    private static Snac SnacWith(byte[] raw)
    {
        var snac = new Snac(OscarIcqService.FAMILY_ID, OscarIcqService.CLI_META_REQ);

        snac.Write(raw);

        return snac;
    }

    // A well-formed meta value: LEN + UIN + RequestType + Sequence + tail.
    private static byte[] MetaValue(uint clientUin, ushort requestType, ushort sequence, byte[] tail)
    {
        return WithLen(Cat(BitConverter.GetBytes(clientUin), BitConverter.GetBytes(requestType), BitConverter.GetBytes(sequence), tail ?? Array.Empty<byte>()));
    }

    #endregion

    #region Frame / TLV level attacks

    [TestMethod]
    public void EmptyRawData_ThrowsTooShort()
    {
        var snac = SnacWith(Array.Empty<byte>());

        // DecodeTlvs rejects anything under 4 bytes before any meta parsing runs.
        Assert.ThrowsExactly<ApplicationException>(() => new IcqUserMetaRequest(snac));
    }

    [TestMethod]
    public void RawDataThreeBytes_ThrowsTooShort()
    {
        var snac = SnacWith(new byte[] { 0x00, 0x01, 0x00 });

        Assert.ThrowsExactly<ApplicationException>(() => new IcqUserMetaRequest(snac));
    }

    [TestMethod]
    public void MissingTlv01_MarksInvalidNoThrow()
    {
        // A valid TLV of a different type (0x0005, empty). GetTlv(0x01) returns null; the constructor
        // now rejects it via IsValid=false instead of dereferencing null.
        var snac = SnacWith(new byte[] { 0x00, 0x05, 0x00, 0x00 });

        Assert.IsFalse(new IcqUserMetaRequest(snac).IsValid);
    }

    [TestMethod]
    public void Tlv01EmptyValue_MarksInvalidNoThrow()
    {
        // TLV 0x01 present but zero-length value is now rejected via IsValid=false instead of the
        // first slice value[..2] overrunning.
        var snac = SnacWith(Tlv01(Array.Empty<byte>()));

        Assert.IsFalse(new IcqUserMetaRequest(snac).IsValid);
    }

    [TestMethod]
    public void TlvFrameLengthOverrun_Throws()
    {
        // TLV 0x01 declares 0x00FF payload bytes but supplies only 4; DecodeTlvs slices past the buffer.
        var snac = SnacWith(TlvFrame(0x0001, 0x00FF, new byte[] { 0x00, 0x00, 0x00, 0x00 }));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new IcqUserMetaRequest(snac));
    }

    [TestMethod]
    public void MultipleTlvs_FindsTlv01AmongOthers()
    {
        // First a decoy TLV 0x0005, then the real 0x01 meta block (non-meta request type -> skips subtype).
        var value = MetaValue(42u, 0x1111, 3, null);

        var raw = Cat(new byte[] { 0x00, 0x05, 0x00, 0x00 }, Tlv01(value));

        var req = new IcqUserMetaRequest(SnacWith(raw));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(42u, req.ClientUin);
        Assert.AreEqual((ushort)0x1111, req.RequestType);
        Assert.AreEqual((ushort)3, req.Sequence);
    }

    #endregion

    #region dataChunkSize gate

    [TestMethod]
    public void InconsistentDataChunkSize_MarksInvalidNoThrow()
    {
        // Declared chunk size 0 but 8 trailing bytes -> tlvLength-2 (8) != 0 -> IsValid=false, early return.
        var value = Cat(new byte[] { 0x00, 0x00 }, new byte[8]);

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(value)));

        Assert.IsFalse(req.IsValid);
        Assert.AreEqual(0u, req.ClientUin);
        Assert.AreEqual((ushort)0, req.RequestType);
        Assert.AreEqual((ushort)0, req.Sequence);
        Assert.AreEqual((ushort)0, req.RequestSubType);
        Assert.AreEqual(0u, req.SearchUin);
        Assert.IsNull(req.XmlKey);
        Assert.IsNull(req.GetExtraData());
    }

    [TestMethod]
    public void TruncatedTwoByteValue_MarksInvalidNoThrow()
    {
        // value = [00 00]: dataChunkSize 0 == length-2 (0), self-consistent but far below the 10-byte
        // header minimum. The added length gate now rejects it instead of overrunning ClientUin [2..6].
        var snac = SnacWith(Tlv01(WithLen(Array.Empty<byte>())));

        Assert.IsFalse(new IcqUserMetaRequest(snac).IsValid);
    }

    #endregion

    #region Valid non-meta / minimal

    [TestMethod]
    public void NonMetaRequestType_ParsesHeaderSkipsSubtype()
    {
        // Exactly 10-byte value (LEN+UIN+RequestType+Sequence), RequestType != CLI_META_INFO_REQ.
        var value = MetaValue(0xDEADBEEF, 0x1234, 0xABCD, null);

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(value)));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(0xDEADBEEFu, req.ClientUin);
        Assert.AreEqual((ushort)0x1234, req.RequestType);
        Assert.AreEqual((ushort)0xABCD, req.Sequence);
        Assert.AreEqual((ushort)0, req.RequestSubType);
        Assert.AreEqual(0u, req.SearchUin);
        Assert.IsNull(req.XmlKey);
        Assert.IsNull(req.GetExtraData());
    }

    [TestMethod]
    public void MetaRequestType_MissingSubtype_MarksInvalidNoThrow()
    {
        // RequestType == CLI_META_INFO_REQ but value ends at 10 bytes: the subtype needs 12, so the
        // request is rejected via IsValid=false instead of overrunning the subtype slice [10..12].
        var value = MetaValue(1u, OscarIcqService.CLI_META_INFO_REQ, 0, null);

        var snac = SnacWith(Tlv01(value));

        Assert.IsFalse(new IcqUserMetaRequest(snac).IsValid);
    }

    #endregion

    #region FULLINFO / FIND_BY_UIN (SearchUin)

    [TestMethod]
    public void FullInfoRequest2_ParsesHugeSearchUin()
    {
        // subtype + 4-byte SearchUin at its maximum boundary value.
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_FULLINFO_REQUEST2), BitConverter.GetBytes(uint.MaxValue));

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(555u, OscarIcqService.CLI_META_INFO_REQ, 9, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(OscarIcqService.CLI_META_INFO_REQ, req.RequestType);
        Assert.AreEqual(OscarIcqService.CLI_FULLINFO_REQUEST2, req.RequestSubType);
        Assert.AreEqual(uint.MaxValue, req.SearchUin);
        Assert.IsNull(req.XmlKey);
        Assert.IsNull(req.GetExtraData());
    }

    [TestMethod]
    public void FindByUin_TrailingBytesSilentlyIgnored()
    {
        // 6 bytes after subtype: ToUInt32 reads only the first 4, the extra 2 are dropped with no error.
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_FIND_BY_UIN), BitConverter.GetBytes(777u), new byte[] { 0xAA, 0xBB });

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(1u, OscarIcqService.CLI_META_INFO_REQ, 1, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(OscarIcqService.CLI_FIND_BY_UIN, req.RequestSubType);
        Assert.AreEqual(777u, req.SearchUin);
    }

    [TestMethod]
    public void FindByUin_TruncatedSearchUin_MarksInvalidNoThrow()
    {
        // Only 2 bytes of UIN after subtype: the 4-byte SearchUin needs 16 total, so the request is
        // rejected via IsValid=false instead of ToUInt32 throwing over a 2-byte span.
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_FIND_BY_UIN), new byte[] { 0x01, 0x02 });

        var snac = SnacWith(Tlv01(MetaValue(1u, OscarIcqService.CLI_META_INFO_REQ, 1, tail)));

        Assert.IsFalse(new IcqUserMetaRequest(snac).IsValid);
    }

    #endregion

    #region REQ_XML_INFO (XmlKey)

    [TestMethod]
    public void ReqXmlInfo_ParsesXmlKey()
    {
        // stringLength counts a trailing terminator: the parser reads stringLength-1 chars.
        // stringLength=3 over bytes "AB\0" yields "AB".
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_REQ_XML_INFO), BitConverter.GetBytes((ushort)3), new byte[] { 0x41, 0x42, 0x00 });

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(OscarIcqService.CLI_REQ_XML_INFO, req.RequestSubType);
        Assert.AreEqual("AB", req.XmlKey);
        Assert.AreEqual(0u, req.SearchUin);
        Assert.IsNull(req.GetExtraData());
    }

    [TestMethod]
    public void ReqXmlInfo_NonAsciiBytes_ReplacedWithQuestionMarks()
    {
        // High bytes are not valid ASCII; Encoding.ASCII maps each to '?'. Lossy but no crash.
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_REQ_XML_INFO), BitConverter.GetBytes((ushort)3), new byte[] { 0x80, 0xFF, 0x00 });

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual("??", req.XmlKey);
    }

    [TestMethod]
    public void ReqXmlInfo_EmbeddedNul_Preserved()
    {
        // stringLength=4 -> reads 3 bytes; an embedded 0x00 survives ASCII decoding as '\0'.
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_REQ_XML_INFO), BitConverter.GetBytes((ushort)4), new byte[] { 0x41, 0x00, 0x42, 0x00 });

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(3, req.XmlKey.Length);
        Assert.AreEqual('A', req.XmlKey[0]);
        Assert.AreEqual('\0', req.XmlKey[1]);
        Assert.AreEqual('B', req.XmlKey[2]);
    }

    [TestMethod]
    public void ReqXmlInfo_StringLengthZero_YieldsEmptyKey()
    {
        // stringLength=0 is a degenerate but parseable key: it now yields an empty XmlKey instead of an
        // inverted [14..13] slice throwing.
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_REQ_XML_INFO), BitConverter.GetBytes((ushort)0));

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(string.Empty, req.XmlKey);
    }

    [TestMethod]
    public void ReqXmlInfo_StringLengthOverrun_MarksInvalidNoThrow()
    {
        // Attacker-declared stringLength 0xFFFF over a 2-byte string is now rejected via IsValid=false
        // instead of overrunning the buffer.
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_REQ_XML_INFO), BitConverter.GetBytes((ushort)0xFFFF), new byte[] { 0x41, 0x42 });

        var snac = SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail)));

        Assert.IsFalse(new IcqUserMetaRequest(snac).IsValid);
    }

    [TestMethod]
    public void ReqXmlInfo_MissingStringLength_MarksInvalidNoThrow()
    {
        // subtype present but value ends there: the stringLength field needs 14 bytes, so the request is
        // rejected via IsValid=false instead of reading [12..14] past the buffer.
        var tail = BitConverter.GetBytes(OscarIcqService.CLI_REQ_XML_INFO);

        var snac = SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail)));

        Assert.IsFalse(new IcqUserMetaRequest(snac).IsValid);
    }

    #endregion

    #region META_SET_PERMS_USERINFO and set-info else branch

    [TestMethod]
    public void SetPermsUserInfo_LongEnough_ValidNoExtraData()
    {
        // subtype + 4 permission bytes (total value length 16 > 15) -> bytes read into locals, nothing exposed.
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.META_SET_PERMS_USERINFO), new byte[] { 0x01, 0x02, 0x03, 0x04 });

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(OscarIcqService.META_SET_PERMS_USERINFO, req.RequestSubType);
        Assert.AreEqual(0u, req.SearchUin);
        Assert.IsNull(req.XmlKey);
        Assert.IsNull(req.GetExtraData());
    }

    [TestMethod]
    public void SetPermsUserInfo_TooShort_SkipsSilentlyValid()
    {
        // value length 12 (<= 15): the permission-byte block is skipped, no crash, IsValid stays true.
        var tail = BitConverter.GetBytes(OscarIcqService.META_SET_PERMS_USERINFO);

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(OscarIcqService.META_SET_PERMS_USERINFO, req.RequestSubType);
        Assert.IsNull(req.GetExtraData());
    }

    [TestMethod]
    public void SetInfoElseBranch_CapturesExtraData()
    {
        // An unhandled meta subtype (CLI_SET_BASIC_INFO) with trailing payload -> extraData = value[12..].
        var payload = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };

        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_SET_BASIC_INFO), payload);

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(OscarIcqService.CLI_SET_BASIC_INFO, req.RequestSubType);

        var extra = req.GetExtraData();

        Assert.IsNotNull(extra);
        CollectionAssert.AreEqual(payload, extra);
        Assert.AreEqual(0u, req.SearchUin);
        Assert.IsNull(req.XmlKey);
    }

    [TestMethod]
    public void SetInfoElseBranch_NoPayload_ExtraDataNull()
    {
        // Unhandled subtype with value length exactly 12: nothing past subtype, extraData stays null.
        var tail = BitConverter.GetBytes(OscarIcqService.CLI_SET_BASIC_INFO);

        var req = new IcqUserMetaRequest(SnacWith(Tlv01(MetaValue(9u, OscarIcqService.CLI_META_INFO_REQ, 2, tail))));

        Assert.IsTrue(req.IsValid);
        Assert.AreEqual(OscarIcqService.CLI_SET_BASIC_INFO, req.RequestSubType);
        Assert.IsNull(req.GetExtraData());
    }

    #endregion
}