// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using VintageHive.Proxy.NetMeeting.Rtp;

namespace Adversarial2.Rtp;

// ----------------------------------------------------------
//  Adversarial RTP / RTCP header parser tests
//
//  Targets RtpHeader.TryParse / RtcpHeader.TryParse in
//  Proxy/NetMeeting/Rtp/RtpPacket.cs. These exercise hostile
//  peer input: truncated buffers, bad version bits, oversized
//  CSRC counts, extension/padding flags that the parser never
//  actually consumes, and boundary field values. They assert
//  the parser's ACTUAL observed behavior (verified by running),
//  not the wire spec.
//
//  Key observed facts about this parser (documented by tests):
//   * It only ever READS the first 12 bytes. CSRC words, the
//     extension header, and padding trailer are NEVER read - the
//     relay forwards raw bytes, so the parser is stats-only.
//   * length checks use the `length` parameter; the payload reads
//     are bounded by data.Length. In the relay both come from the
//     same datagram so they agree.
//   * RTP validates that the CSRC list fits within `length`.
//   * RTCP does NOT validate its 16-bit word-length field against
//     the buffer at all - it is stored verbatim.
// ----------------------------------------------------------

[TestClass]
public class RtpHeaderAdversarialTests
{
    // Build a raw byte[] with a caller-chosen byte0/byte1 and zeroed
    // seq/timestamp/ssrc, sized to `total` (>= 2). Kept local so this
    // file shares nothing with other test files.
    private static byte[] RawRtp(int total, byte b0, byte b1)
    {
        var buf = new byte[total];

        if (total > 0)
        {
            buf[0] = b0;
        }

        if (total > 1)
        {
            buf[1] = b1;
        }

        return buf;
    }

    // ---- length / truncation boundaries -------------------------------

    [TestMethod]
    public void TryParse_LengthZero_ReturnsFalse()
    {
        var data = new byte[32];
        data[0] = 0x80; // V=2
        Assert.IsFalse(RtpHeader.TryParse(data, 0, out var header));
        Assert.AreEqual(default, header);
    }

    [TestMethod]
    public void TryParse_NegativeLength_ReturnsFalse()
    {
        // -1 < RTP_HEADER_MIN short-circuits before any indexing, so no throw.
        var data = new byte[32];
        data[0] = 0x80;
        Assert.IsFalse(RtpHeader.TryParse(data, -1, out _));
        Assert.IsFalse(RtpHeader.TryParse(data, int.MinValue, out _));
    }

    [TestMethod]
    public void TryParse_EmptyArray_ReturnsFalse()
    {
        Assert.IsFalse(RtpHeader.TryParse(Array.Empty<byte>(), 0, out _));
    }

    [TestMethod]
    public void TryParse_LengthGovernsNotBufferSize_TooShort()
    {
        // Physically large buffer but the datagram was only 11 bytes.
        var data = new byte[1500];
        data[0] = 0x80; // V=2
        Assert.IsFalse(RtpHeader.TryParse(data, 11, out _));
    }

    [TestMethod]
    public void TryParse_ExactMinLength_Accepts()
    {
        // 12 is the min; exactly at the boundary must parse.
        var data = RawRtp(12, 0x80, 0x00);
        Assert.IsTrue(RtpHeader.TryParse(data, 12, out var header));
        Assert.AreEqual(0, header.CsrcCount);
        Assert.AreEqual(12, header.HeaderSize);
    }

    // ---- version bits -------------------------------------------------

    [TestMethod]
    public void TryParse_VersionThree_ReturnsFalse()
    {
        // Top two bits = 11b -> version 3, not 2.
        var data = RawRtp(12, 0xC0, 0x00);
        Assert.IsFalse(RtpHeader.TryParse(data, 12, out _));
    }

    [TestMethod]
    public void TryParse_AllOnesFirstByte_VersionThree_ReturnsFalse()
    {
        // 0xFF -> version 3, padding/ext/CC all set. Version gate rejects first.
        var data = RawRtp(12, 0xFF, 0xFF);
        Assert.IsFalse(RtpHeader.TryParse(data, 12, out _));
    }

    // ---- CSRC count vs buffer -----------------------------------------

    [TestMethod]
    public void TryParse_MaxCsrcCount_RequiresFullList()
    {
        // CC=15 -> header must be 12 + 15*4 = 72 bytes.
        // One byte short of the CSRC list => reject.
        var justShort = RawRtp(72, 0x8F, 0x00);
        Assert.IsFalse(RtpHeader.TryParse(justShort, 71, out _));

        // Exactly 72 => accept, and HeaderSize reflects the full list.
        var exact = RawRtp(72, 0x8F, 0x00);
        Assert.IsTrue(RtpHeader.TryParse(exact, 72, out var header));
        Assert.AreEqual(15, header.CsrcCount);
        Assert.AreEqual(72, header.HeaderSize);
    }

    [TestMethod]
    public void TryParse_CsrcCountExceedsLength_EvenWhenBufferIsHuge()
    {
        // Buffer physically holds 100 bytes, but the datagram length is 12.
        // CC=15 claims a 72-byte header -> must be rejected on `length`.
        var data = new byte[100];
        data[0] = 0x8F; // V=2, CC=15
        Assert.IsFalse(RtpHeader.TryParse(data, 12, out _));
    }

    [TestMethod]
    public void TryParse_CsrcListFitsWithinLength_SmallerThanBuffer()
    {
        // length=72 governs; the extra buffer past 72 is irrelevant.
        var data = new byte[100];
        data[0] = 0x8F; // V=2, CC=15
        Assert.IsTrue(RtpHeader.TryParse(data, 72, out var header));
        Assert.AreEqual(15, header.CsrcCount);
    }

    [TestMethod]
    public void TryParse_CsrcCountOne_OffByOne()
    {
        // CC=1 needs 16 bytes. 15 => false, 16 => true.
        var short15 = RawRtp(16, 0x81, 0x00);
        Assert.IsFalse(RtpHeader.TryParse(short15, 15, out _));

        var ok16 = RawRtp(16, 0x81, 0x00);
        Assert.IsTrue(RtpHeader.TryParse(ok16, 16, out var header));
        Assert.AreEqual(1, header.CsrcCount);
        Assert.AreEqual(16, header.HeaderSize);
    }

    [TestMethod]
    public void TryParse_LargeCsrcHeader_ReadsOnlyFirst12Bytes()
    {
        // Fill the whole 72-byte CSRC region with 0xAA. The parser must not
        // read past byte 11, so seq/timestamp/ssrc come only from the fixed
        // header (all zero here), not from the CSRC bytes.
        var data = new byte[72];
        data[0] = 0x8F; // V=2, CC=15
        for (var i = 12; i < 72; i++)
        {
            data[i] = 0xAA;
        }

        Assert.IsTrue(RtpHeader.TryParse(data, 72, out var header));
        Assert.AreEqual(0, header.SequenceNumber);
        Assert.AreEqual(0u, header.Timestamp);
        Assert.AreEqual(0u, header.Ssrc);
    }

    // ---- extension / padding flags are NOT consumed -------------------

    [TestMethod]
    public void TryParse_ExtensionBitSet_NoExtensionData_Accepted()
    {
        // X=1 but only a 12-byte header with no extension words. The parser
        // records Extension=true and never tries to read the extension length,
        // so a hostile "extension length past the end" cannot cause an OOB.
        var data = RawRtp(12, 0x90, 0x00); // V=2, X=1
        Assert.IsTrue(RtpHeader.TryParse(data, 12, out var header));
        Assert.IsTrue(header.Extension);
        Assert.AreEqual(12, header.HeaderSize); // extension not counted
    }

    [TestMethod]
    public void TryParse_PaddingBitSet_NoPayload_Accepted()
    {
        // P=1 with a bare 12-byte header and no payload/padding trailer.
        // The parser never reads the trailing padding-length byte.
        var data = RawRtp(12, 0xA0, 0x00); // V=2, P=1
        Assert.IsTrue(RtpHeader.TryParse(data, 12, out var header));
        Assert.IsTrue(header.Padding);
    }

    [TestMethod]
    public void TryParse_PaddingLengthLargerThanPayload_Accepted()
    {
        // P=1, a single payload byte, and a trailing padding-count of 0xFF
        // claiming 255 bytes of padding that do not exist. A spec-compliant
        // depacketizer would reject; this stats parser ignores padding and
        // accepts. Documents the missing validation (safe here: never read).
        var data = new byte[13];
        data[0] = 0xA0;  // V=2, P=1
        data[12] = 0xFF; // bogus padding length
        Assert.IsTrue(RtpHeader.TryParse(data, 13, out var header));
        Assert.IsTrue(header.Padding);
    }

    // ---- field boundary values ----------------------------------------

    [TestMethod]
    public void TryParse_AllFieldsMaxedOut_RawBytes()
    {
        // Valid version (0x80) but marker + max PT + max seq/ts/ssrc.
        var data = new byte[12];
        data[0] = 0x80; // V=2, no flags, CC=0
        data[1] = 0xFF; // M=1, PT=0x7F (127)
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), uint.MaxValue);

        Assert.IsTrue(RtpHeader.TryParse(data, 12, out var header));
        Assert.IsTrue(header.Marker);
        Assert.AreEqual(127, header.PayloadType);
        Assert.AreEqual(65535, header.SequenceNumber);
        Assert.AreEqual(uint.MaxValue, header.Timestamp);
        Assert.AreEqual(uint.MaxValue, header.Ssrc);
    }

    [TestMethod]
    public void TryParse_AllZeroBodyValidVersion_MinValues()
    {
        var data = RawRtp(12, 0x80, 0x00);
        Assert.IsTrue(RtpHeader.TryParse(data, 12, out var header));
        Assert.IsFalse(header.Marker);
        Assert.AreEqual(0, header.PayloadType);
        Assert.AreEqual(0, header.SequenceNumber);
        Assert.AreEqual(0u, header.Timestamp);
        Assert.AreEqual(0u, header.Ssrc);
    }

    [TestMethod]
    public void TryParse_MarkerBitDoesNotBleedIntoPayloadType()
    {
        // data[1]=0x80 -> marker set, PT must be 0 (masked with 0x7F).
        var data = RawRtp(12, 0x80, 0x80);
        Assert.IsTrue(RtpHeader.TryParse(data, 12, out var header));
        Assert.IsTrue(header.Marker);
        Assert.AreEqual(0, header.PayloadType);
    }

    [TestMethod]
    public void HeaderSize_MatchesFormulaAcrossCsrcRange()
    {
        // Sanity across the full 0..15 CSRC range with an exactly-sized buffer.
        for (var cc = 0; cc <= 15; cc++)
        {
            var total = 12 + (cc * 4);
            var data = RawRtp(total, (byte)(0x80 | cc), 0x00);
            Assert.IsTrue(RtpHeader.TryParse(data, total, out var header), $"cc={cc}");
            Assert.AreEqual(cc, header.CsrcCount, $"cc={cc}");
            Assert.AreEqual(total, header.HeaderSize, $"cc={cc}");
        }
    }
}

[TestClass]
public class RtcpHeaderAdversarialTests
{
    private static byte[] RawRtcp(int total, byte b0, byte b1)
    {
        var buf = new byte[total];

        if (total > 0)
        {
            buf[0] = b0;
        }

        if (total > 1)
        {
            buf[1] = b1;
        }

        return buf;
    }

    [TestMethod]
    public void TryParse_LengthZero_ReturnsFalse()
    {
        Assert.IsFalse(RtcpHeader.TryParse(new byte[16], 0, out _));
    }

    [TestMethod]
    public void TryParse_NegativeLength_ReturnsFalse()
    {
        // -1 < 8 short-circuits before indexing.
        Assert.IsFalse(RtcpHeader.TryParse(new byte[16], -1, out _));
        Assert.IsFalse(RtcpHeader.TryParse(new byte[16], int.MinValue, out _));
    }

    [TestMethod]
    public void TryParse_ExactMinLength8_Accepts()
    {
        var data = RawRtcp(8, 0x80, 200); // V=2, PT=SR
        Assert.IsTrue(RtcpHeader.TryParse(data, 8, out var header));
        Assert.AreEqual(200, header.PacketType);
        Assert.AreEqual(0, header.Count);
    }

    [TestMethod]
    public void TryParse_VersionThree_ReturnsFalse()
    {
        var data = RawRtcp(8, 0xC0, 200); // V=3
        Assert.IsFalse(RtcpHeader.TryParse(data, 8, out _));
    }

    [TestMethod]
    public void TryParse_PacketTypeBelowRange_ReturnsFalse()
    {
        // 199 is one below RTCP_SR (200).
        var data = RawRtcp(8, 0x80, 199);
        Assert.IsFalse(RtcpHeader.TryParse(data, 8, out _));
    }

    [TestMethod]
    public void TryParse_PacketTypeAboveRange_ReturnsFalse()
    {
        // 205 is one above RTCP_APP (204).
        var data = RawRtcp(8, 0x80, 205);
        Assert.IsFalse(RtcpHeader.TryParse(data, 8, out _));
    }

    [TestMethod]
    public void TryParse_PacketTypeRangeBoundaries_Accept()
    {
        var sr = RawRtcp(8, 0x80, 200);
        Assert.IsTrue(RtcpHeader.TryParse(sr, 8, out var h200));
        Assert.AreEqual(200, h200.PacketType);

        var app = RawRtcp(8, 0x80, 204);
        Assert.IsTrue(RtcpHeader.TryParse(app, 8, out var h204));
        Assert.AreEqual(204, h204.PacketType);
    }

    [TestMethod]
    public void TryParse_CountFieldMax_Accepted()
    {
        // data[0]=0x9F -> V=2, P=0, count=0x1F (31), the 5-bit max.
        var data = RawRtcp(8, 0x9F, 201); // RR
        Assert.IsTrue(RtcpHeader.TryParse(data, 8, out var header));
        Assert.AreEqual(31, header.Count);
    }

    [TestMethod]
    public void TryParse_PaddingBitSet_Accepted()
    {
        // data[0]=0xA0 -> V=2, P=1, count=0.
        var data = RawRtcp(8, 0xA0, 203); // BYE
        Assert.IsTrue(RtcpHeader.TryParse(data, 8, out var header));
        Assert.IsTrue(header.Padding);
        Assert.AreEqual(0, header.Count);
    }

    [TestMethod]
    public void TryParse_WordLengthFieldNotValidatedAgainstBuffer()
    {
        // The 16-bit length field claims 65535 words (~256 KB) but the datagram
        // is only 8 bytes. The parser stores the value verbatim without any
        // bounds check - it never uses Length to index. Document that a hostile
        // peer can inject an arbitrary Length with no rejection.
        var data = RawRtcp(8, 0x80, 200);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), ushort.MaxValue);
        Assert.IsTrue(RtcpHeader.TryParse(data, 8, out var header));
        Assert.AreEqual(65535, header.Length);
    }

    [TestMethod]
    public void TryParse_MaxSsrcAndWordLength_RawBytes()
    {
        var data = RawRtcp(8, 0x80, 204); // APP
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), uint.MaxValue);
        Assert.IsTrue(RtcpHeader.TryParse(data, 8, out var header));
        Assert.AreEqual(65535, header.Length);
        Assert.AreEqual(uint.MaxValue, header.Ssrc);
    }

    [TestMethod]
    public void TryParse_SevenBytes_ReadsPacketTypeButRejectsOnLength()
    {
        // 7 bytes is below the 8-byte minimum; reject before reading SSRC.
        var data = RawRtcp(7, 0x80, 200);
        Assert.IsFalse(RtcpHeader.TryParse(data, 7, out _));
    }
}
