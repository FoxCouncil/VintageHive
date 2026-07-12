// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Mms;

#pragma warning disable MSTEST0025 // Use Assert.Fail instead of always-failing Assert.AreEqual

namespace Adversarial3.Mms;

// ===================================================================
// ParseMmsMessage(byte[] messageBody) -> (uint mid, byte[] fields)
// Body layout: chunkLen(4) + MID(4) + commandFields
// ===================================================================
[TestClass]
public class MmsParseMmsMessageTests
{
    [TestMethod]
    public void ParseMmsMessage_EmptyArray_ReturnsZeroMidAndEmptyFields()
    {
        var (mid, fields) = MmsCommand.ParseMmsMessage(Array.Empty<byte>());

        Assert.AreEqual(0u, mid);
        Assert.AreEqual(0, fields.Length);
    }

    [TestMethod]
    public void ParseMmsMessage_OneByte_ReturnsZeroMidAndEmptyFields()
    {
        var (mid, fields) = MmsCommand.ParseMmsMessage(new byte[] { 0xFF });

        Assert.AreEqual(0u, mid);
        Assert.AreEqual(0, fields.Length);
    }

    [TestMethod]
    public void ParseMmsMessage_SevenBytes_ShortOfHeader_ReturnsZeroMidEmptyFields()
    {
        // 7 bytes is one short of the 8-byte minimum (chunkLen + MID); MID at offset 4 would be out of range.
        var body = new byte[] { 1, 2, 3, 4, 5, 6, 7 };

        var (mid, fields) = MmsCommand.ParseMmsMessage(body);

        Assert.AreEqual(0u, mid);
        Assert.AreEqual(0, fields.Length);
    }

    [TestMethod]
    public void ParseMmsMessage_ExactlyEightBytes_ReadsMidNoFields()
    {
        // chunkLen(4) = {0,0,0,0}, MID(4) at offset 4 = 0x00040001 little-endian.
        var body = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x04, 0x00 };

        var (mid, fields) = MmsCommand.ParseMmsMessage(body);

        Assert.AreEqual(MmsCommand.MID_ConnectedEX, mid);
        Assert.AreEqual(0, fields.Length);
    }

    [TestMethod]
    public void ParseMmsMessage_NineBytes_ReturnsSingleFieldByte()
    {
        // One byte past the header: fields = body[8..] = one byte.
        var body = new byte[] { 0, 0, 0, 0, 0x02, 0x00, 0x03, 0x00, 0xAB };

        var (mid, fields) = MmsCommand.ParseMmsMessage(body);

        Assert.AreEqual(MmsCommand.MID_ConnectFunnel, mid);
        Assert.AreEqual(1, fields.Length);
        Assert.AreEqual(0xAB, fields[0]);
    }

    [TestMethod]
    public void ParseMmsMessage_WithFields_SlicesFromOffsetEight()
    {
        var body = new byte[] { 9, 9, 9, 9, 0x21, 0x00, 0x04, 0x00, 0xDE, 0xAD, 0xBE, 0xEF };

        var (mid, fields) = MmsCommand.ParseMmsMessage(body);

        Assert.AreEqual(MmsCommand.MID_ReportStreamSwitch, mid);
        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, fields);
    }

    [TestMethod]
    public void ParseMmsMessage_MidHighValue_ReadsFullUInt32()
    {
        // MID = 0xFFFFFFFF at offset 4 must round-trip as the full unsigned value.
        var body = new byte[] { 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF };

        var (mid, fields) = MmsCommand.ParseMmsMessage(body);

        Assert.AreEqual(0xFFFFFFFFu, mid);
        Assert.AreEqual(0, fields.Length);
    }

    [TestMethod]
    public void ParseMmsMessage_ReturnedFieldsIsCopy_NotAliasOfInput()
    {
        var body = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0x10, 0x20 };

        var (_, fields) = MmsCommand.ParseMmsMessage(body);

        // messageBody[8..] produces a fresh array; mutating it must not touch the source.
        fields[0] = 0x99;
        Assert.AreEqual(0x10, body[8]);
    }

    [TestMethod]
    public void ParseMmsMessage_Null_ThrowsNullReferenceException()
    {
        // messageBody.Length dereferences null; documents observed (unguarded) behavior.
        Assert.ThrowsExactly<NullReferenceException>(() => MmsCommand.ParseMmsMessage(null!));
    }
}

// ===================================================================
// ExtractUnicodeString(byte[] fields, int offset)
// Null-terminated UTF-16LE scan.
// ===================================================================
[TestClass]
public class MmsExtractUnicodeStringTests
{
    [TestMethod]
    public void Extract_OffsetEqualsLength_ReturnsEmpty()
    {
        var fields = new byte[] { 0x41, 0x00, 0x00, 0x00 };

        Assert.AreEqual(string.Empty, MmsCommand.ExtractUnicodeString(fields, 4));
    }

    [TestMethod]
    public void Extract_OffsetPastLength_ReturnsEmpty()
    {
        var fields = new byte[] { 0x41, 0x00 };

        Assert.AreEqual(string.Empty, MmsCommand.ExtractUnicodeString(fields, 99));
    }

    [TestMethod]
    public void Extract_EmptyBufferOffsetZero_ReturnsEmpty()
    {
        // offset(0) >= length(0) short-circuits before any indexing.
        Assert.AreEqual(string.Empty, MmsCommand.ExtractUnicodeString(Array.Empty<byte>(), 0));
    }

    [TestMethod]
    public void Extract_OffsetAtLastByte_ReturnsEmpty()
    {
        // offset = length-1: the while guard (end+1 < length) is false immediately, count = 0.
        var fields = new byte[] { 0x41, 0x00, 0x42 };

        Assert.AreEqual(string.Empty, MmsCommand.ExtractUnicodeString(fields, 2));
    }

    [TestMethod]
    public void Extract_SimpleTerminatedString()
    {
        // "AB" + 0x0000 terminator.
        var fields = new byte[] { 0x41, 0x00, 0x42, 0x00, 0x00, 0x00 };

        Assert.AreEqual("AB", MmsCommand.ExtractUnicodeString(fields, 0));
    }

    [TestMethod]
    public void Extract_NoTerminatorEvenLength_ReadsWholeBufferNoOverrun()
    {
        // "XY" with no trailing null; the scan must consume all bytes without reading out of bounds.
        var fields = Encoding.Unicode.GetBytes("XY");

        Assert.AreEqual("XY", MmsCommand.ExtractUnicodeString(fields, 0));
    }

    [TestMethod]
    public void Extract_OddLengthNoTerminator_DropsTrailingByteNoOverrun()
    {
        // "A"(2 bytes) + a lone 0x42 with no room for a 2-byte terminator.
        // The final odd byte is silently dropped and there is no out-of-bounds read.
        var fields = new byte[] { 0x41, 0x00, 0x42 };

        Assert.AreEqual("A", MmsCommand.ExtractUnicodeString(fields, 0));
    }

    [TestMethod]
    public void Extract_TerminatorImmediatelyAtOffset_ReturnsEmpty()
    {
        var fields = new byte[] { 0x00, 0x00, 0x41, 0x00 };

        Assert.AreEqual(string.Empty, MmsCommand.ExtractUnicodeString(fields, 0));
    }

    [TestMethod]
    public void Extract_ControlAndCrLfChars_PreservedUntilTerminator()
    {
        // Embedded CR (000D) and LF (000A) as UTF-16LE units are kept verbatim; only 0x0000 stops the scan.
        var fields = new byte[] { 0x0D, 0x00, 0x0A, 0x00, 0x00, 0x00 };

        Assert.AreEqual("\r\n", MmsCommand.ExtractUnicodeString(fields, 0));
    }

    [TestMethod]
    public void Extract_LoneHighSurrogate_ReturnsReplacementCharNoThrow()
    {
        // 0xD83D is a lone high surrogate; UnicodeEncoding's default fallback yields U+FFFD, no exception.
        var fields = new byte[] { 0x3D, 0xD8, 0x00, 0x00 };

        var result = MmsCommand.ExtractUnicodeString(fields, 0);

        Assert.AreEqual("�", result);
    }

    [TestMethod]
    public void Extract_MidBufferOffset_StartsScanAtOffset()
    {
        // Two terminated strings back to back; extracting at the second offset reads only "B".
        var first = MmsCommand.EncodeUnicodeString("A");   // 4 bytes: 0x41 0x00 0x00 0x00
        var second = MmsCommand.EncodeUnicodeString("B");  // 4 bytes
        var fields = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, fields, 0, first.Length);
        Buffer.BlockCopy(second, 0, fields, first.Length, second.Length);

        Assert.AreEqual("B", MmsCommand.ExtractUnicodeString(fields, first.Length));
    }

    [TestMethod]
    public void Extract_NegativeOffset_ThrowsIndexOutOfRange()
    {
        // offset(-1) is not >= length, so the guard passes; the loop then indexes fields[-1] and throws.
        var fields = new byte[] { 0x41, 0x00, 0x42, 0x00 };

        Assert.ThrowsExactly<IndexOutOfRangeException>(() => MmsCommand.ExtractUnicodeString(fields, -1));
    }

    [TestMethod]
    public void Extract_Null_ThrowsNullReferenceException()
    {
        // fields.Length dereferences null before any offset check.
        Assert.ThrowsExactly<NullReferenceException>(() => MmsCommand.ExtractUnicodeString(null!, 0));
    }
}

// ===================================================================
// EncodeUnicodeString(string) and round-trips
// ===================================================================
[TestClass]
public class MmsEncodeUnicodeStringTests
{
    [TestMethod]
    public void Encode_EmptyString_JustNullTerminator()
    {
        var bytes = MmsCommand.EncodeUnicodeString(string.Empty);

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00 }, bytes);
    }

    [TestMethod]
    public void Encode_Ascii_AppendsTwoByteTerminator()
    {
        var bytes = MmsCommand.EncodeUnicodeString("Hi");

        CollectionAssert.AreEqual(new byte[] { 0x48, 0x00, 0x69, 0x00, 0x00, 0x00 }, bytes);
    }

    [TestMethod]
    public void Encode_LengthIsCharsTimesTwoPlusTwo()
    {
        var bytes = MmsCommand.EncodeUnicodeString("Funnel");

        Assert.AreEqual(("Funnel".Length * 2) + 2, bytes.Length);
    }

    [TestMethod]
    public void Encode_Null_ThrowsArgumentNullException()
    {
        // Encoding.Unicode.GetBytes(null) throws before allocation.
        Assert.ThrowsExactly<ArgumentNullException>(() => MmsCommand.EncodeUnicodeString(null!));
    }

    [TestMethod]
    public void RoundTrip_AsciiString()
    {
        var encoded = MmsCommand.EncodeUnicodeString("Hello");

        Assert.AreEqual("Hello", MmsCommand.ExtractUnicodeString(encoded, 0));
    }

    [TestMethod]
    public void RoundTrip_EmptyString()
    {
        var encoded = MmsCommand.EncodeUnicodeString(string.Empty);

        Assert.AreEqual(string.Empty, MmsCommand.ExtractUnicodeString(encoded, 0));
    }

    [TestMethod]
    public void RoundTrip_NonAsciiBmp()
    {
        // Cyrillic + accented Latin, all inside the BMP.
        const string s = "éЖ中";
        var encoded = MmsCommand.EncodeUnicodeString(s);

        Assert.AreEqual(s, MmsCommand.ExtractUnicodeString(encoded, 0));
    }

    [TestMethod]
    public void RoundTrip_NonBmpSurrogatePair()
    {
        // U+1F600 encodes as a UTF-16LE surrogate pair (4 bytes); the terminator scan must not
        // mistake the 0x00 low byte of the pair for a terminator.
        const string emoji = "\U0001F600";
        var encoded = MmsCommand.EncodeUnicodeString(emoji);

        Assert.AreEqual(6, encoded.Length);
        Assert.AreEqual(emoji, MmsCommand.ExtractUnicodeString(encoded, 0));
    }

    [TestMethod]
    public void RoundTrip_EmbeddedNull_TruncatesAtNull()
    {
        // Null-terminated encoding cannot represent embedded U+0000: everything after it is lost on decode.
        var encoded = MmsCommand.EncodeUnicodeString("A\0B");

        Assert.AreEqual("A", MmsCommand.ExtractUnicodeString(encoded, 0));
    }

    [TestMethod]
    public void RoundTrip_LongString_512Chars()
    {
        var s = new string('Z', 512);
        var encoded = MmsCommand.EncodeUnicodeString(s);

        Assert.AreEqual((512 * 2) + 2, encoded.Length);
        Assert.AreEqual(s, MmsCommand.ExtractUnicodeString(encoded, 0));
    }
}

// ===================================================================
// BuildTcpMessage(uint mid, ushort seq, double timeSent, byte[] commandFields)
// Full 32-byte TcpMessageHeader + 8-byte-aligned MMS body.
// ===================================================================
[TestClass]
public class MmsBuildTcpMessageTests
{
    private static byte[] Build(uint mid, ushort seq, double t, byte[] fields)
    {
        return MmsCommand.BuildTcpMessage(mid, seq, t, fields);
    }

    [TestMethod]
    public void Build_EmptyFields_ProducesFortyByteAlignedPacket()
    {
        var packet = Build(MmsCommand.MID_ConnectedEX, 1, 0.0, Array.Empty<byte>());

        // body = chunkLen(4)+MID(4)+0 = 8 -> padded 8 -> total 32+8 = 40.
        Assert.AreEqual(40, packet.Length);
        Assert.AreEqual(0, packet.Length % 8);
    }

    [TestMethod]
    public void Build_HeaderFixedFields()
    {
        var packet = Build(MmsCommand.MID_ConnectedEX, 0x1234, 3.5, new byte[] { 1, 2, 3 });

        Assert.AreEqual(0x01, packet[0]);                                  // rep
        Assert.AreEqual(0x00, packet[1]);                                  // version
        Assert.AreEqual(0x00, packet[2]);                                  // versionMinor
        Assert.AreEqual(0x00, packet[3]);                                  // padding
        Assert.AreEqual(MmsCommand.SESSION_ID, BitConverter.ToUInt32(packet, 4));
        Assert.AreEqual(MmsCommand.SEAL, BitConverter.ToUInt32(packet, 12));
        Assert.AreEqual((ushort)0x1234, BitConverter.ToUInt16(packet, 20)); // seq
        Assert.AreEqual((ushort)0, BitConverter.ToUInt16(packet, 22));      // MBZ
        Assert.AreEqual(3.5, BitConverter.ToDouble(packet, 24));            // timeSent
    }

    [TestMethod]
    public void Build_MessageLengthAndChunkCount_ThreeFieldBytes()
    {
        // fields=3 -> mmsBody=11 -> padded 16 -> messageLength=16+16=32, total=48, chunkCount=6, mmsChunkLen=2.
        var packet = Build(MmsCommand.MID_ConnectedEX, 0, 0.0, new byte[] { 1, 2, 3 });

        Assert.AreEqual(48, packet.Length);
        Assert.AreEqual(32, BitConverter.ToInt32(packet, 8));   // messageLength = paddedMmsLen(16) + 16
        Assert.AreEqual(6, BitConverter.ToInt32(packet, 16));   // chunkCount = total(48)/8
        Assert.AreEqual(2, BitConverter.ToInt32(packet, 32));   // mmsChunkLen = paddedMmsLen(16)/8
        Assert.AreEqual(MmsCommand.MID_ConnectedEX, BitConverter.ToUInt32(packet, 36));
    }

    [TestMethod]
    public void Build_FieldsCopiedAtOffset40AndZeroPadded()
    {
        var packet = Build(MmsCommand.MID_ConnectedEX, 0, 0.0, new byte[] { 0xAA, 0xBB, 0xCC });

        Assert.AreEqual(0xAA, packet[40]);
        Assert.AreEqual(0xBB, packet[41]);
        Assert.AreEqual(0xCC, packet[42]);
        // Remaining bytes to the 8-byte boundary must be zero padding.
        Assert.AreEqual(0x00, packet[43]);
        Assert.AreEqual(0x00, packet[44]);
        Assert.AreEqual(0x00, packet[45]);
        Assert.AreEqual(0x00, packet[46]);
        Assert.AreEqual(0x00, packet[47]);
    }

    [TestMethod]
    public void Build_PaddingBoundaries_AllRoundUpToEight()
    {
        // (mmsBody + 7) & ~7 where mmsBody = 8 + len.
        Assert.AreEqual(40, Build(0, 0, 0.0, new byte[0]).Length);  // body 8  -> pad 8  -> 40
        Assert.AreEqual(48, Build(0, 0, 0.0, new byte[1]).Length);  // body 9  -> pad 16 -> 48
        Assert.AreEqual(48, Build(0, 0, 0.0, new byte[7]).Length);  // body 15 -> pad 16 -> 48
        Assert.AreEqual(48, Build(0, 0, 0.0, new byte[8]).Length);  // body 16 -> pad 16 -> 48
        Assert.AreEqual(56, Build(0, 0, 0.0, new byte[9]).Length);  // body 17 -> pad 24 -> 56
    }

    [TestMethod]
    public void Build_SeqMaxValue_StoredAsUInt16()
    {
        var packet = Build(0, ushort.MaxValue, 0.0, Array.Empty<byte>());

        Assert.AreEqual(ushort.MaxValue, BitConverter.ToUInt16(packet, 20));
        Assert.AreEqual((ushort)0, BitConverter.ToUInt16(packet, 22)); // MBZ untouched
    }

    [TestMethod]
    public void Build_NaNTimeSent_RoundTripsBitPattern()
    {
        var packet = Build(0, 0, double.NaN, Array.Empty<byte>());

        Assert.IsTrue(double.IsNaN(BitConverter.ToDouble(packet, 24)));
    }

    [TestMethod]
    public void Build_NullFields_ThrowsNullReferenceException()
    {
        // commandFields.Length dereferences null.
        Assert.ThrowsExactly<NullReferenceException>(() => MmsCommand.BuildTcpMessage(0, 0, 0.0, null!));
    }

    [TestMethod]
    public void Build_RoundTripThroughParseMmsMessage()
    {
        // The MMS body is everything from offset 32 onward; ParseMmsMessage should recover MID and fields.
        var fields = new byte[] { 0x11, 0x22, 0x33 };
        var packet = Build(MmsCommand.MID_StartedPlaying, 7, 1.0, fields);

        var body = packet[32..];
        var (mid, parsed) = MmsCommand.ParseMmsMessage(body);

        Assert.AreEqual(MmsCommand.MID_StartedPlaying, mid);
        // parsed includes the 8-byte-boundary zero padding after the 3 real bytes.
        Assert.IsTrue(parsed.Length >= 3);
        Assert.AreEqual(0x11, parsed[0]);
        Assert.AreEqual(0x22, parsed[1]);
        Assert.AreEqual(0x33, parsed[2]);
    }
}

// ===================================================================
// BuildDataPacket(uint locationId, byte incarnation, byte afFlags, byte[] asfPayload)
// 8-byte header + payload. PacketSize field is a ushort.
// ===================================================================
[TestClass]
public class MmsBuildDataPacketTests
{
    [TestMethod]
    public void BuildData_EmptyPayload_EightByteHeaderOnly()
    {
        var packet = MmsCommand.BuildDataPacket(0x12345678u, 0x0B, 0x08, Array.Empty<byte>());

        Assert.AreEqual(8, packet.Length);
        Assert.AreEqual(0x12345678u, BitConverter.ToUInt32(packet, 0)); // LocationId
        Assert.AreEqual(0x0B, packet[4]);                               // incarnation
        Assert.AreEqual(0x08, packet[5]);                               // AFFlags
        Assert.AreEqual((ushort)8, BitConverter.ToUInt16(packet, 6));   // PacketSize = total
    }

    [TestMethod]
    public void BuildData_SmallPayload_CopiesAtOffsetEight()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var packet = MmsCommand.BuildDataPacket(0, 1, 0, payload);

        Assert.AreEqual(12, packet.Length);
        Assert.AreEqual((ushort)12, BitConverter.ToUInt16(packet, 6));
        CollectionAssert.AreEqual(payload, packet[8..]);
    }

    [TestMethod]
    public void BuildData_PacketSizeField_MatchesTotalForModeratePayload()
    {
        var payload = new byte[1000];
        var packet = MmsCommand.BuildDataPacket(0, 0, 0, payload);

        Assert.AreEqual(1008, packet.Length);
        Assert.AreEqual((ushort)1008, BitConverter.ToUInt16(packet, 6));
    }

    [TestMethod]
    public void BuildData_PayloadAtUShortBoundary_65527_StillCorrect()
    {
        // total = 8 + 65527 = 65535 = ushort.MaxValue, the last size that fits.
        var payload = new byte[65527];
        var packet = MmsCommand.BuildDataPacket(0, 0, 0, payload);

        Assert.AreEqual(65535, packet.Length);
        Assert.AreEqual((ushort)65535, BitConverter.ToUInt16(packet, 6));
    }

    [TestMethod]
    public void BuildData_PayloadOverflowsUShort_65528_Throws()
    {
        // total = 8 + 65528 = 65536 would truncate the ushort PacketSize field to 0 and desync the
        // reader, so BuildDataPacket now rejects it loudly instead of shipping a mismatched length.
        var payload = new byte[65528];

        Assert.ThrowsExactly<ArgumentException>(() => MmsCommand.BuildDataPacket(0, 0, 0, payload));
    }

    [TestMethod]
    public void BuildData_LocationIdMaxValue_RoundTrips()
    {
        var packet = MmsCommand.BuildDataPacket(0xFFFFFFFFu, 0xFF, 0xFF, new byte[] { 1 });

        Assert.AreEqual(0xFFFFFFFFu, BitConverter.ToUInt32(packet, 0));
        Assert.AreEqual(0xFF, packet[4]);
        Assert.AreEqual(0xFF, packet[5]);
    }

    [TestMethod]
    public void BuildData_NullPayload_ThrowsNullReferenceException()
    {
        // asfPayload.Length dereferences null.
        Assert.ThrowsExactly<NullReferenceException>(() => MmsCommand.BuildDataPacket(0, 0, 0, null!));
    }
}

#pragma warning restore MSTEST0025