// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// MMS (MS-MMSP) protocol tests. The live streaming session is ffmpeg/ASF/socket-bound, but the wire
// primitives are pure: the TcpMessageHeader framing, the Data-packet header, the message parser, and
// the null-terminated UTF-16LE string codec MMS uses for its command fields.

using VintageHive.Proxy.Mms;

namespace Mms;

[TestClass]
public class MmsTcpMessageTests
{
    [TestMethod]
    public void BuildTcpMessage_LaysOutHeaderPerSpec()
    {
        // 8-byte command fields keep the MMS body a clean 16 bytes (chunkLen+MID = 8, plus 8 fields), no padding.
        var fields = new byte[] { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17 };
        var packet = MmsCommand.BuildTcpMessage(MmsCommand.MID_ConnectedEX, seq: 0x0042, timeSent: 1.5, commandFields: fields);

        // TcpMessageHeader (32 bytes) per MS-MMSP 2.2.3.
        Assert.AreEqual(0x01, packet[0]);                                              // rep
        Assert.AreEqual(0x00, packet[1]);                                              // version
        Assert.AreEqual(MmsCommand.SESSION_ID, BitConverter.ToUInt32(packet, 4));      // sessionId 0xB00BFACE
        Assert.AreEqual(MmsCommand.SEAL, BitConverter.ToUInt32(packet, 12));           // seal "MMS "
        Assert.AreEqual((ushort)0x0042, BitConverter.ToUInt16(packet, 20));            // seq
        Assert.AreEqual(1.5, BitConverter.ToDouble(packet, 24));                       // timeSent (double)
        Assert.AreEqual(MmsCommand.MID_ConnectedEX, BitConverter.ToUInt32(packet, 36));// MID

        // Whole packet is 8-byte aligned and chunk-counted per spec.
        Assert.AreEqual(0, packet.Length % 8);
        Assert.AreEqual(packet.Length / 8, BitConverter.ToInt32(packet, 16));          // chunkCount
    }

    [TestMethod]
    public void BuildTcpMessage_RoundTripsThroughParse()
    {
        var fields = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var packet = MmsCommand.BuildTcpMessage(MmsCommand.MID_StartedPlaying, 1, 0.0, fields);

        // ParseMmsMessage consumes the MMS body - everything after the 32-byte TcpMessageHeader.
        var (mid, parsed) = MmsCommand.ParseMmsMessage(packet[MmsCommand.TCP_HEADER_SIZE..]);

        Assert.AreEqual(MmsCommand.MID_StartedPlaying, mid);
        CollectionAssert.AreEqual(fields, parsed);
    }

    [TestMethod]
    public void BuildTcpMessage_PadsBodyToEightBytes()
    {
        // 4 command bytes -> body = 8 (chunkLen+MID) + 4 = 12 -> padded up to 16.
        var packet = MmsCommand.BuildTcpMessage(MmsCommand.MID_Ping, 0, 0.0, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });

        Assert.AreEqual(MmsCommand.TCP_HEADER_SIZE + 16, packet.Length);
        Assert.AreEqual(0, packet.Length % 8);
    }

    [TestMethod]
    public void ParseMmsMessage_TooShort_ReturnsZero()
    {
        var (mid, fields) = MmsCommand.ParseMmsMessage(new byte[] { 0, 0, 0, 0 });

        Assert.AreEqual(0u, mid);
        Assert.AreEqual(0, fields.Length);
    }

    [TestMethod]
    public void ParseMmsMessage_ExtractsMidAndFields()
    {
        // [chunkLen(4)][MID(4)][fields...] - MID here is MID_OpenFile (0x00030005).
        var body = new byte[] { 0x02, 0, 0, 0, 0x05, 0x00, 0x03, 0x00, 0xDE, 0xAD };
        var (mid, fields) = MmsCommand.ParseMmsMessage(body);

        Assert.AreEqual(0x00030005u, mid);
        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD }, fields);
    }
}

[TestClass]
public class MmsDataPacketTests
{
    [TestMethod]
    public void BuildDataPacket_LaysOutHeaderAndPayload()
    {
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };
        var packet = MmsCommand.BuildDataPacket(locationId: 0x11223344, incarnation: 0x05, afFlags: 0x08, asfPayload: payload);

        Assert.AreEqual(8 + payload.Length, packet.Length);
        Assert.AreEqual(0x11223344u, BitConverter.ToUInt32(packet, 0));                 // LocationId (LE)
        Assert.AreEqual(0x05, packet[4]);                                               // playIncarnation
        Assert.AreEqual(0x08, packet[5]);                                               // AFFlags
        Assert.AreEqual((ushort)(8 + payload.Length), BitConverter.ToUInt16(packet, 6));// PacketSize = total size
        CollectionAssert.AreEqual(payload, packet[8..]);
    }

    [TestMethod]
    public void BuildDataPacket_EmptyPayload_IsEightByteHeader()
    {
        var packet = MmsCommand.BuildDataPacket(1, 0, 0, Array.Empty<byte>());

        Assert.AreEqual(8, packet.Length);
        Assert.AreEqual((ushort)8, BitConverter.ToUInt16(packet, 6));
    }
}

[TestClass]
public class MmsUnicodeStringTests
{
    [TestMethod]
    public void Encode_IsNullTerminatedUtf16Le()
    {
        // "Hi" = 4 bytes UTF-16LE + a 2-byte null terminator.
        CollectionAssert.AreEqual(new byte[] { 0x48, 0x00, 0x69, 0x00, 0x00, 0x00 }, MmsCommand.EncodeUnicodeString("Hi"));
    }

    [TestMethod]
    public void Encode_Empty_IsJustTerminator()
    {
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00 }, MmsCommand.EncodeUnicodeString(""));
    }

    [TestMethod]
    public void EncodeExtract_RoundTrips()
    {
        const string value = "Funnel Of The Gods";
        Assert.AreEqual(value, MmsCommand.ExtractUnicodeString(MmsCommand.EncodeUnicodeString(value), 0));
    }

    [TestMethod]
    public void Extract_StopsAtDoubleNull()
    {
        // "A" then a terminator, then trailing "B" that must not bleed into the result.
        var fields = new byte[] { 0x41, 0x00, 0x00, 0x00, 0x42, 0x00 };
        Assert.AreEqual("A", MmsCommand.ExtractUnicodeString(fields, 0));
    }

    [TestMethod]
    public void Extract_AtOffset()
    {
        // Skip a 4-byte prefix, read the UTF-16LE "OK" that follows.
        var fields = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x4F, 0x00, 0x4B, 0x00, 0x00, 0x00 };
        Assert.AreEqual("OK", MmsCommand.ExtractUnicodeString(fields, 4));
    }

    [TestMethod]
    public void Extract_OffsetPastEnd_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, MmsCommand.ExtractUnicodeString(new byte[] { 0x41, 0x00 }, 8));
    }
}
