// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Pna;

namespace Adversarial4.Pna;

// Adversarial coverage of the PURE byte-in / byte-out helpers in PnaCommand:
//   BuildPnaTagResponse, BuildPnaStreamPacket, BuildChallengeBlock, ParseClientRequest,
//   HexDump, FourCCToString, ReadRmChunkAsync, ReadRmDataPacketAsync.
// No sockets, no listeners, no Mind.Db. Stream helpers are driven with in-memory MemoryStream only.

internal static class PnaTestBytes
{
    // Concatenate byte-array parts into one contiguous buffer.
    public static byte[] Cat(params byte[][] parts)
    {
        var ms = new MemoryStream();

        foreach (var p in parts)
        {
            ms.Write(p, 0, p.Length);
        }

        return ms.ToArray();
    }

    // 11-byte PNA client-hello header: "PNA\0" + version + zero padding.
    public static byte[] PnaHeader(byte version)
    {
        return new byte[] { 0x50, 0x4E, 0x41, 0x00, version, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    }

    public static byte[] Ascii(string s)
    {
        return Encoding.ASCII.GetBytes(s);
    }
}

[TestClass]
public class BuildPnaTagResponseTests
{
    [TestMethod]
    public void FourByteFields_ProducesTwelveBytePnaResponse()
    {
        var response = PnaCommand.BuildPnaTagResponse(new byte[] { 0x0A, 0x11, 0x22, 0x33 });

        Assert.AreEqual(12, response.Length);

        // Bytes 0-3 = "PNA\0" magic.
        Assert.AreEqual(0x50, response[0]);
        Assert.AreEqual(0x4E, response[1]);
        Assert.AreEqual(0x41, response[2]);
        Assert.AreEqual(0x00, response[3]);

        // Bytes 4-7 echo the client fields verbatim.
        Assert.AreEqual(0x0A, response[4]);
        Assert.AreEqual(0x11, response[5]);
        Assert.AreEqual(0x22, response[6]);
        Assert.AreEqual(0x33, response[7]);

        // Bytes 8-11 = zero body (no opcodes).
        Assert.AreEqual(0x00, response[8]);
        Assert.AreEqual(0x00, response[9]);
        Assert.AreEqual(0x00, response[10]);
        Assert.AreEqual(0x00, response[11]);
    }

    [TestMethod]
    public void OverlongFields_CopiesOnlyFirstFour()
    {
        var response = PnaCommand.BuildPnaTagResponse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.AreEqual(12, response.Length);
        Assert.AreEqual(1, response[4]);
        Assert.AreEqual(4, response[7]);
        Assert.AreEqual(0, response[8]);
    }

    [TestMethod]
    public void ShortFields_ThrowsArgumentException()
    {
        // Only 3 bytes: the 4-byte BlockCopy overruns the source and throws.
        Assert.ThrowsExactly<ArgumentException>(() => PnaCommand.BuildPnaTagResponse(new byte[] { 1, 2, 3 }));
    }

    [TestMethod]
    public void EmptyFields_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PnaCommand.BuildPnaTagResponse(Array.Empty<byte>()));
    }

    [TestMethod]
    public void NullFields_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PnaCommand.BuildPnaTagResponse(null!));
    }
}

[TestClass]
public class BuildPnaStreamPacketTests
{
    [TestMethod]
    public void MinimumSixByteRmPacket_ProducesNineBytePnaPacket()
    {
        // version(2) + length(2) + stream_hi(1) + one data byte = 6 bytes.
        var rm = new byte[] { 0x00, 0x01, 0x00, 0x06, 0x77, 0xAA };

        var packet = PnaCommand.BuildPnaStreamPacket(rm, 0x1234, 0x7F);

        Assert.IsNotNull(packet);
        Assert.AreEqual(9, packet.Length);

        Assert.AreEqual(0x5A, packet[0]);           // marker
        Assert.AreEqual(0x00, packet[1]);           // fof1 hi (rmLength = 0x0006)
        Assert.AreEqual(0x06, packet[2]);           // fof1 lo
        Assert.AreEqual(0x00, packet[3]);           // fof2 hi
        Assert.AreEqual(0x06, packet[4]);           // fof2 lo
        Assert.AreEqual(0x12, packet[5]);           // seq hi
        Assert.AreEqual(0x34, packet[6]);           // seq lo
        Assert.AreEqual(0x5A, packet[7]);           // marker
        Assert.AreEqual(0x7F, packet[8]);           // index2 override replaces the sole data byte
    }

    [TestMethod]
    public void LargerRmPacket_CopiesBodyWithIndex2Override()
    {
        var rm = new byte[20];
        for (int i = 0; i < rm.Length; i++)
        {
            rm[i] = (byte)(0xB0 + i);
        }
        rm[2] = 0x00;
        rm[3] = 0x14; // declared length 20

        var packet = PnaCommand.BuildPnaStreamPacket(rm, 0x0001, 0x42);

        Assert.IsNotNull(packet);
        Assert.AreEqual(8 + (20 - 5), packet.Length); // 23
        Assert.AreEqual(0x42, packet[8]);              // overridden first data byte

        // packet[9..] must equal rm[6..] verbatim.
        for (int i = 9; i < packet.Length; i++)
        {
            Assert.AreEqual(rm[i - 3], packet[i]);
        }
    }

    [TestMethod]
    public void FiveByteRmPacket_ReturnsNull()
    {
        Assert.IsNull(PnaCommand.BuildPnaStreamPacket(new byte[] { 0, 1, 0, 5, 9 }, 0, 0));
    }

    [TestMethod]
    public void EmptyRmPacket_ReturnsNull()
    {
        Assert.IsNull(PnaCommand.BuildPnaStreamPacket(Array.Empty<byte>(), 0, 0));
    }

    [TestMethod]
    public void NullRmPacket_ThrowsNullReference()
    {
        Assert.ThrowsExactly<NullReferenceException>(() => PnaCommand.BuildPnaStreamPacket(null!, 0, 0));
    }
}

[TestClass]
public class BuildChallengeBlockTests
{
    [TestMethod]
    public void Returns72ZeroBytesWithSafeFirstByte()
    {
        var block = PnaCommand.BuildChallengeBlock();

        Assert.AreEqual(72, block.Length);
        Assert.AreNotEqual(0x72, block[0]); // must not trigger the checksum framing path

        foreach (var b in block)
        {
            Assert.AreEqual(0x00, b);
        }
    }
}

[TestClass]
public class ParseClientRequestTests
{
    [TestMethod]
    public void EmptyBuffer_ReturnsDefaults()
    {
        var r = PnaCommand.ParseClientRequest(Array.Empty<byte>(), 0);

        Assert.IsNull(r.ClientString);
        Assert.IsNull(r.PathRequest);
        Assert.IsNull(r.Challenge);
        Assert.AreEqual(0u, r.Bandwidth);
        Assert.AreEqual(0, r.PnaVersion);
        Assert.IsNull(r.PnaHeaderFields);
    }

    [TestMethod]
    public void NullBufferWithZeroLength_ToleratedNoThrow()
    {
        // length < 11 short-circuits before any data[] access, so null data is tolerated here.
        var r = PnaCommand.ParseClientRequest(null!, 0);

        Assert.IsNull(r.PnaHeaderFields);
        Assert.IsNull(r.ClientString);
    }

    [TestMethod]
    public void SubHeaderLength_ReturnsDefaults()
    {
        var data = PnaTestBytes.PnaHeader(0x0A); // 11 bytes of valid header
        var r = PnaCommand.ParseClientRequest(data, 10); // but only 10 declared

        Assert.IsNull(r.PnaHeaderFields);
        Assert.AreEqual(0, r.PnaVersion);
    }

    [TestMethod]
    public void WrongMagic_ReturnsDefaults()
    {
        var data = new byte[20]; // all zero -> data[0] != 'P'
        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.IsNull(r.PnaHeaderFields);
        Assert.AreEqual(0, r.PnaVersion);
    }

    [TestMethod]
    public void ValidHeaderNoChunks_SetsVersionAndFields()
    {
        var data = PnaTestBytes.PnaHeader(0x08);
        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual(0x08, r.PnaVersion);
        Assert.IsNotNull(r.PnaHeaderFields);
        Assert.AreEqual(4, r.PnaHeaderFields.Length);
        Assert.AreEqual(0x08, r.PnaHeaderFields[0]);
        Assert.AreEqual(0x00, r.PnaHeaderFields[1]);
        Assert.IsNull(r.ClientString);
        Assert.IsNull(r.PathRequest);
        Assert.IsNull(r.Challenge);
        Assert.AreEqual(0u, r.Bandwidth);
    }

    [TestMethod]
    public void LoneTagByteNearEnd_NoOverreadReturnsNull()
    {
        // A single 0x63 as the last byte: loop upper bound (length-3) never reaches it. No OOB.
        var data = PnaTestBytes.Cat(PnaTestBytes.PnaHeader(0x0A), new byte[] { 0x63 });
        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.IsNull(r.ClientString);
    }

    [TestMethod]
    public void ClientString_ParsedAndTrimmed()
    {
        var str = PnaTestBytes.Ascii("TestClientString123"); // 19 chars, >= 10
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x63, 0x00, (byte)str.Length },
            str);

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual("TestClientString123", r.ClientString);
    }

    [TestMethod]
    public void ClientString_ExactBoundaryLength_Accepted()
    {
        var str = PnaTestBytes.Ascii("ABCDEFGHIJ"); // exactly 10
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x63, 0x00, 0x0A },
            str);

        // i(11) + 3 + 10 == length(24) exactly.
        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual("ABCDEFGHIJ", r.ClientString);
    }

    [TestMethod]
    public void ClientString_DeclaredLengthOverrunsBuffer_Rejected()
    {
        var str = PnaTestBytes.Ascii("ABCDEFGHIJ"); // 10 present
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x63, 0x00, 0x0B }, // declares 11
            str);

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.IsNull(r.ClientString); // guarded, no crash, no overread
    }

    [TestMethod]
    public void ClientString_TooShortLength_Rejected()
    {
        var str = PnaTestBytes.Ascii("short"); // 5 chars, below the strLen>=10 floor
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x63, 0x00, 0x05 },
            str);

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.IsNull(r.ClientString);
    }

    [TestMethod]
    public void ClientString_NonAsciiBytes_ReplacedWithQuestionMarks()
    {
        // 0xFF and 0xC3 are non-ASCII -> Encoding.ASCII maps each to '?'.
        var body = new byte[] { 0x41, 0x42, 0xFF, 0xC3, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A };
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x63, 0x00, (byte)body.Length },
            body);

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual("AB??CDEFGHIJ", r.ClientString);
    }

    [TestMethod]
    public void ClientString_EmbeddedNull_OnlyTrailingTrimmed()
    {
        // "Test\0MoreData\0" -> TrimEnd('\0') keeps the interior null.
        var body = new byte[] { 0x54, 0x65, 0x73, 0x74, 0x00, 0x4D, 0x6F, 0x72, 0x65, 0x44, 0x61, 0x74, 0x61, 0x00 };
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x63, 0x00, (byte)body.Length },
            body);

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual("Test\0MoreData", r.ClientString);
        Assert.IsTrue(r.ClientString.Contains('\0'));
    }

    [TestMethod]
    public void PathRequest_Parsed()
    {
        var path = PnaTestBytes.Ascii("/media/song.rm"); // 14 chars
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x00, 0x52, 0x00, (byte)path.Length },
            path);

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual("/media/song.rm", r.PathRequest);
    }

    [TestMethod]
    public void PathRequest_ZeroLength_Rejected()
    {
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x00, 0x52, 0x00, 0x00, 0x11, 0x22, 0x33, 0x44 });

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.IsNull(r.PathRequest);
    }

    [TestMethod]
    public void PathRequest_DeclaredLengthOverrunsBuffer_Rejected()
    {
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x00, 0x52, 0xFF, 0xFF }, // declares 65535
            PnaTestBytes.Ascii("/x"));

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.IsNull(r.PathRequest); // guarded, no crash
    }

    [TestMethod]
    public void Bandwidth_Parsed()
    {
        // Trailing padding is required: the bandwidth scan loop bound is (i < length - 8), so a
        // chunk starting at index 11 is only reached when the buffer extends past index 19.
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x00, 0x05, 0x00, 0x04, 0x00, 0x02, 0xAB, 0xCD },
            new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual(0x0002ABCDu, r.Bandwidth);
    }

    [TestMethod]
    public void Bandwidth_ShortChunkLen_LeavesZero()
    {
        // chunkLen = 2 (< 4) -> parser breaks without reading a value.
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x00, 0x05, 0x00, 0x02, 0xAB, 0xCD, 0x00, 0x00, 0x00 });

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual(0u, r.Bandwidth);
    }

    [TestMethod]
    public void Challenge_Parsed()
    {
        var chal = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x00, 0x04, 0x00, 0x08 },
            chal);

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.IsNotNull(r.Challenge);
        CollectionAssert.AreEqual(chal, r.Challenge);
        Assert.AreEqual(0u, r.Bandwidth);
    }

    [TestMethod]
    public void Challenge_DeclaredLengthOverrunsBuffer_Rejected()
    {
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x00, 0x04, 0x01, 0x00 }, // declares 256
            new byte[] { 0x11, 0x22 });            // only 2 present

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.IsNull(r.Challenge); // guarded, no crash
    }

    [TestMethod]
    public void BackToBackChunks_AllFieldsParsed()
    {
        // Header + CLIENT_STRING + PATH + BANDWIDTH(len 5) + CHALLENGE(len 8) laid out so the
        // independent heuristic scans each latch onto the intended chunk.
        var data = PnaTestBytes.Cat(
            PnaTestBytes.PnaHeader(0x0A),
            new byte[] { 0x63, 0x00, 0x10 }, PnaTestBytes.Ascii("PlayerRealApp_v6"),
            new byte[] { 0x00, 0x52, 0x00, 0x08 }, PnaTestBytes.Ascii("/test.rm"),
            new byte[] { 0x00, 0x05, 0x00, 0x05, 0x00, 0x02, 0xAB, 0xCD, 0xEF },
            new byte[] { 0x00, 0x04, 0x00, 0x08, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 });

        var r = PnaCommand.ParseClientRequest(data, data.Length);

        Assert.AreEqual(0x0A, r.PnaVersion);
        Assert.AreEqual("PlayerRealApp_v6", r.ClientString);
        Assert.AreEqual("/test.rm", r.PathRequest);
        Assert.AreEqual(0x0002ABCDu, r.Bandwidth);
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }, r.Challenge);
    }
}

[TestClass]
public class FourCCToStringTests
{
    [TestMethod]
    public void KnownRmTags_DecodeToFourCC()
    {
        Assert.AreEqual(".RMF", PnaCommand.FourCCToString(PnaCommand.RM_RMF_TAG));
        Assert.AreEqual("PROP", PnaCommand.FourCCToString(PnaCommand.RM_PROP_TAG));
        Assert.AreEqual("MDPR", PnaCommand.FourCCToString(PnaCommand.RM_MDPR_TAG));
        Assert.AreEqual("CONT", PnaCommand.FourCCToString(PnaCommand.RM_CONT_TAG));
        Assert.AreEqual("DATA", PnaCommand.FourCCToString(PnaCommand.RM_DATA_TAG));
    }

    [TestMethod]
    public void PnaTag_ProducesTrailingNull()
    {
        var s = PnaCommand.FourCCToString(PnaCommand.PNA_TAG);

        Assert.AreEqual(4, s.Length);
        Assert.AreEqual("PNA\0", s);
    }

    [TestMethod]
    public void HighBitBytes_MapToQuestionMarks()
    {
        // All four bytes >= 0x80 -> ASCII replacement char '?'.
        var s = PnaCommand.FourCCToString(0xFF80C0E0u);

        Assert.AreEqual("????", s);
    }
}

[TestClass]
public class HexDumpTests
{
    [TestMethod]
    public void SmallBuffer_ContainsHexAndAscii()
    {
        var data = new byte[] { 0x50, 0x4E, 0x41, 0x00 };
        var dump = PnaCommand.HexDump(data, 0, data.Length);

        Assert.IsTrue(dump.Contains("50 4E 41 00"), dump);
        Assert.IsTrue(dump.Contains("PNA"), dump);
    }

    [TestMethod]
    public void OffsetPastEnd_ReturnsEmptyNoCrash()
    {
        var dump = PnaCommand.HexDump(new byte[4], 10, 5);

        Assert.AreEqual(string.Empty, dump);
    }

    [TestMethod]
    public void LengthPastEnd_ClampsToBuffer()
    {
        var data = new byte[] { 0xAA, 0xBB };
        // Declared length 100 far exceeds the 2-byte buffer; must clamp, not overread.
        var dump = PnaCommand.HexDump(data, 0, 100);

        Assert.IsTrue(dump.Contains("AA BB"), dump);
    }

    [TestMethod]
    public void ExceedsMaxBytes_EmitsMoreBytesFooter()
    {
        var data = new byte[300];
        var dump = PnaCommand.HexDump(data, 0, 300); // default maxBytes = 256

        Assert.IsTrue(dump.Contains("44 more bytes"), dump);
    }

    [TestMethod]
    public void NullData_ThrowsNullReference()
    {
        Assert.ThrowsExactly<NullReferenceException>(() => PnaCommand.HexDump(null!));
    }
}

[TestClass]
public class ReadRmChunkAsyncTests
{
    [TestMethod]
    public async Task EmptyStream_ReturnsEofTuple()
    {
        var (tag, chunk) = await PnaCommand.ReadRmChunkAsync(new MemoryStream(Array.Empty<byte>()));

        Assert.AreEqual(0u, tag);
        Assert.IsNull(chunk);
    }

    [TestMethod]
    public async Task TruncatedHeader_ReturnsEofTuple()
    {
        var (tag, chunk) = await PnaCommand.ReadRmChunkAsync(new MemoryStream(new byte[] { 0x2E, 0x52, 0x4D, 0x46 }));

        Assert.AreEqual(0u, tag);
        Assert.IsNull(chunk);
    }

    [TestMethod]
    public async Task ValidChunk_ReadsFullBody()
    {
        var buf = PnaTestBytes.Cat(
            new byte[] { 0x43, 0x4F, 0x4E, 0x54, 0x00, 0x00, 0x00, 0x0C }, // CONT, size 12
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var (tag, chunk) = await PnaCommand.ReadRmChunkAsync(new MemoryStream(buf));

        Assert.AreEqual(PnaCommand.RM_CONT_TAG, tag);
        Assert.IsNotNull(chunk);
        Assert.AreEqual(12, chunk.Length);
        Assert.AreEqual(0xDE, chunk[8]);
        Assert.AreEqual(0xEF, chunk[11]);
    }

    [TestMethod]
    public async Task SizeBelowMinimum_ReturnsHeaderOnly()
    {
        // size field = 4 (< 8): parser returns just the 8-byte header, no body read.
        var buf = new byte[] { 0x2E, 0x52, 0x4D, 0x46, 0x00, 0x00, 0x00, 0x04 };

        var (tag, chunk) = await PnaCommand.ReadRmChunkAsync(new MemoryStream(buf));

        Assert.AreEqual(PnaCommand.RM_RMF_TAG, tag);
        Assert.IsNotNull(chunk);
        Assert.AreEqual(8, chunk.Length);
    }

    [TestMethod]
    public async Task SizeAboveOneMegCap_ReturnsHeaderOnlyNoAllocation()
    {
        // size field = 0x00100001 (> 1MB cap): rejected, returns header only. No huge allocation.
        var buf = new byte[] { 0x44, 0x41, 0x54, 0x41, 0x00, 0x10, 0x00, 0x01 };

        var (tag, chunk) = await PnaCommand.ReadRmChunkAsync(new MemoryStream(buf));

        Assert.AreEqual(PnaCommand.RM_DATA_TAG, tag);
        Assert.IsNotNull(chunk);
        Assert.AreEqual(8, chunk.Length);
    }

    [TestMethod]
    public async Task DeclaredSizeOverrunsStream_ReturnsZeroPaddedBestEffort()
    {
        // size claims 100 but only 10 body bytes are present. Allocation is capped at the declared
        // size (100, well under 1MB), the available bytes are copied, and the tail stays zero-filled.
        var buf = PnaTestBytes.Cat(
            new byte[] { 0x50, 0x52, 0x4F, 0x50, 0x00, 0x00, 0x00, 0x64 }, // PROP, size 100
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A });

        var (tag, chunk) = await PnaCommand.ReadRmChunkAsync(new MemoryStream(buf));

        Assert.AreEqual(PnaCommand.RM_PROP_TAG, tag);
        Assert.IsNotNull(chunk);
        Assert.AreEqual(100, chunk.Length);
        Assert.AreEqual(0x01, chunk[8]);   // first body byte
        Assert.AreEqual(0x0A, chunk[17]);  // last available body byte
        Assert.AreEqual(0x00, chunk[50]);  // tail beyond available data is zero
        Assert.AreEqual(0x00, chunk[99]);
    }
}

[TestClass]
public class ReadRmDataPacketAsyncTests
{
    [TestMethod]
    public async Task EmptyStream_ReturnsNull()
    {
        Assert.IsNull(await PnaCommand.ReadRmDataPacketAsync(new MemoryStream(Array.Empty<byte>())));
    }

    [TestMethod]
    public async Task TruncatedHeader_ReturnsNull()
    {
        Assert.IsNull(await PnaCommand.ReadRmDataPacketAsync(new MemoryStream(new byte[] { 0x00, 0x00 })));
    }

    [TestMethod]
    public async Task LengthBelowMinimum_ReturnsNull()
    {
        // length field = 2 (< 4): rejected.
        var buf = new byte[] { 0x00, 0x00, 0x00, 0x02 };

        Assert.IsNull(await PnaCommand.ReadRmDataPacketAsync(new MemoryStream(buf)));
    }

    [TestMethod]
    public async Task ValidPacket_ReturnsFullBytes()
    {
        var buf = PnaTestBytes.Cat(
            new byte[] { 0x00, 0x00, 0x00, 0x0A }, // version 0, length 10
            new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 });

        var packet = await PnaCommand.ReadRmDataPacketAsync(new MemoryStream(buf));

        Assert.IsNotNull(packet);
        Assert.AreEqual(10, packet.Length);
        Assert.AreEqual(0x0A, packet[3]);
        Assert.AreEqual(0x11, packet[4]);
        Assert.AreEqual(0x66, packet[9]);
    }

    [TestMethod]
    public async Task DeclaredLengthOverrunsStream_ReturnsNull()
    {
        // length claims 100 but only 6 body bytes follow: truncation -> null (no partial packet).
        var buf = PnaTestBytes.Cat(
            new byte[] { 0x00, 0x00, 0x00, 0x64 },
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 });

        Assert.IsNull(await PnaCommand.ReadRmDataPacketAsync(new MemoryStream(buf)));
    }
}
