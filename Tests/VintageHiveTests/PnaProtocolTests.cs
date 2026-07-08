// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// PNA (RealPlayer PNM) protocol tests. Live transcoding is ffmpeg/socket-bound, but the wire framing
// is pure: the RM->PNA 0x5a stream-packet builder, the client-hello parser, and RM FourCC handling.
// (The Cook codec itself is covered by CookEncoderTests.)

using System.Text;
using VintageHive.Proxy.Pna;

namespace Pna;

[TestClass]
public class PnaStreamPacketTests
{
    // A 16-byte RM data packet: version(2) length(2) stream_num(2) timestamp(4) reserved(1) flags(1) + 4 audio.
    private static byte[] SampleRmPacket() => new byte[]
    {
        0x00, 0x00,             // version
        0x00, 0x10,             // length field = 16
        0x00, 0x00,             // stream number
        0x00, 0x00, 0x00, 0x64, // timestamp
        0x00,                   // reserved
        0x02,                   // flags
        0xAA, 0xBB, 0xCC, 0xDD, // audio payload
    };

    [TestMethod]
    public void Build_ProducesCorrectFraming()
    {
        // PNA stream header (8 bytes): [0x5a][fof1(2,BE)][fof2(2,BE)][seq(2,BE)][0x5a] then data.
        var packet = PnaCommand.BuildPnaStreamPacket(SampleRmPacket(), seq: 0x1234, index2: 0x42);

        Assert.AreEqual(19, packet.Length);                        // 8 header + (16 - 5) data

        Assert.AreEqual(0x5A, packet[0]);                          // leading marker
        Assert.AreEqual(0x5A, packet[7]);                          // trailing marker

        Assert.AreEqual(0x00, packet[1]); Assert.AreEqual(0x10, packet[2]); // fof1 = RM length (16)
        Assert.AreEqual(0x00, packet[3]); Assert.AreEqual(0x10, packet[4]); // fof2 = RM length
        Assert.AreEqual(0x12, packet[5]); Assert.AreEqual(0x34, packet[6]); // seq big-endian

        Assert.AreEqual(0x42, packet[8]);                          // first data byte overridden with index2
        Assert.AreEqual(0xDD, packet[18]);                         // last audio byte carried through
    }

    [TestMethod]
    public void Build_TooShortPacket_ReturnsNull()
    {
        Assert.IsNull(PnaCommand.BuildPnaStreamPacket(new byte[5], 1, 0));
    }
}

[TestClass]
public class PnaClientRequestTests
{
    [TestMethod]
    public void Parse_NonPnaData_ReturnsEmpty()
    {
        var result = PnaCommand.ParseClientRequest(new byte[] { 0x47, 0x45, 0x54, 0x20, 0, 0, 0, 0, 0, 0, 0, 0 }, 12);

        Assert.IsNull(result.ClientString);
        Assert.IsNull(result.PathRequest);
    }

    [TestMethod]
    public void Parse_TooShort_ReturnsEmpty()
    {
        Assert.IsNull(PnaCommand.ParseClientRequest(new byte[] { 0x50, 0x4E, 0x41 }, 3).ClientString);
    }

    [TestMethod]
    public void Parse_ReadsVersionAndHeaderFields()
    {
        var data = new byte[] { 0x50, 0x4E, 0x41, 0x00, 0x0A, 0x01, 0x02, 0x03, 0x00, 0x00, 0x00 };

        var result = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual(0x0A, result.PnaVersion);
        CollectionAssert.AreEqual(new byte[] { 0x0A, 0x01, 0x02, 0x03 }, result.PnaHeaderFields);
    }

    [TestMethod]
    public void Parse_ExtractsClientStringAndPath()
    {
        var req = new List<byte>();
        req.AddRange(new byte[] { 0x50, 0x4E, 0x41, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }); // 11-byte header

        var cs = Encoding.ASCII.GetBytes("WinNT_5.1_test\0");
        req.Add(PnaCommand.TAG_CLIENT_STRING);
        req.Add((byte)(cs.Length >> 8));
        req.Add((byte)(cs.Length & 0xFF));
        req.AddRange(cs);

        var path = Encoding.ASCII.GetBytes("/stream");
        req.Add(0x00);
        req.Add(PnaCommand.TAG_PATH_REQUEST);
        req.Add((byte)(path.Length >> 8));
        req.Add((byte)(path.Length & 0xFF));
        req.AddRange(path);

        req.AddRange(new byte[] { 0x79, 0x42 }); // trailing "yB"

        var data = req.ToArray();
        var result = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual("WinNT_5.1_test", result.ClientString);
        Assert.AreEqual("/stream", result.PathRequest);
    }
}

[TestClass]
public class PnaFourCCTests
{
    [TestMethod]
    public void FourCC_RmfTag() => Assert.AreEqual(".RMF", PnaCommand.FourCCToString(PnaCommand.RM_RMF_TAG));

    [TestMethod]
    public void FourCC_DataTag() => Assert.AreEqual("DATA", PnaCommand.FourCCToString(PnaCommand.RM_DATA_TAG));

    [TestMethod]
    public void FourCC_BigEndianOrder() => Assert.AreEqual("MDPR", PnaCommand.FourCCToString(PnaCommand.RM_MDPR_TAG));
}
