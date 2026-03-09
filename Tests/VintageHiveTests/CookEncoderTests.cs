// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using VintageHive.Processors.LocalServer.Streaming;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  CookBitWriter tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class CookBitWriterTests
{
    [TestMethod]
    public void Write_SingleBit_One()
    {
        var bw = new CookBitWriter(1);
        bw.Write(1, 1);
        var buf = bw.ToArray();

        Assert.AreEqual(0x80, buf[0]); // MSB set
        Assert.AreEqual(1, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_SingleBit_Zero()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0, 1);
        var buf = bw.ToArray();

        Assert.AreEqual(0x00, buf[0]);
        Assert.AreEqual(1, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_EightBits_FullByte()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0xA5, 8);
        var buf = bw.ToArray();

        Assert.AreEqual(0xA5, buf[0]);
        Assert.AreEqual(8, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_MultipleCalls_CrossesByteBoundary()
    {
        var bw = new CookBitWriter(2);
        bw.Write(0x07, 3); // 111
        bw.Write(0x00, 5); // 00000
        bw.Write(0x03, 2); // 11
        var buf = bw.ToArray();

        // Byte 0: 111_00000 = 0xE0
        Assert.AreEqual(0xE0, buf[0]);
        // Byte 1: 11_000000 = 0xC0
        Assert.AreEqual(0xC0, buf[1]);
        Assert.AreEqual(10, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_Overflow_Ignored()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0xFF, 8);
        bw.Write(0xFF, 1); // overflow — should be ignored

        Assert.AreEqual(8, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_ZeroBits_NoOp()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0xFF, 0);

        Assert.AreEqual(0, bw.BitsWritten);
    }

    [TestMethod]
    public void PadToEnd_FillsRemainingWithZeros()
    {
        var bw = new CookBitWriter(4);
        bw.Write(0xAB, 8);
        bw.PadToEnd();
        var buf = bw.ToArray();

        Assert.AreEqual(32, bw.BitsWritten);
        Assert.AreEqual(0xAB, buf[0]);
        Assert.AreEqual(0x00, buf[1]);
        Assert.AreEqual(0x00, buf[2]);
        Assert.AreEqual(0x00, buf[3]);
    }

    [TestMethod]
    public void ToArray_ReturnsCopy()
    {
        var bw = new CookBitWriter(2);
        bw.Write(0xFFFF, 16);
        var buf1 = bw.ToArray();
        var buf2 = bw.ToArray();

        Assert.AreNotSame(buf1, buf2);
        CollectionAssert.AreEqual(buf1, buf2);
    }

    [TestMethod]
    public void Write_SixteenBits_TwoBytes()
    {
        var bw = new CookBitWriter(2);
        bw.Write(0xCAFE, 16);
        var buf = bw.ToArray();

        Assert.AreEqual(0xCA, buf[0]);
        Assert.AreEqual(0xFE, buf[1]);
    }
}

// ──────────────────────────────────────────────────────────
//  BigEndianWriter tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class BigEndianWriterTests
{
    [TestMethod]
    public void WriteU8_SingleByte()
    {
        using var ms = new MemoryStream();
        using var bw = new BigEndianWriter(ms);
        bw.WriteU8(0x42);

        Assert.AreEqual(1, ms.Length);
        Assert.AreEqual(0x42, ms.ToArray()[0]);
    }

    [TestMethod]
    public void WriteU16_BigEndianOrder()
    {
        using var ms = new MemoryStream();
        using var bw = new BigEndianWriter(ms);
        bw.WriteU16(0x1234);

        var buf = ms.ToArray();
        Assert.AreEqual(0x12, buf[0]);
        Assert.AreEqual(0x34, buf[1]);
    }

    [TestMethod]
    public void WriteU32_BigEndianOrder()
    {
        using var ms = new MemoryStream();
        using var bw = new BigEndianWriter(ms);
        bw.WriteU32(0xDEADBEEF);

        var buf = ms.ToArray();
        Assert.AreEqual(0xDE, buf[0]);
        Assert.AreEqual(0xAD, buf[1]);
        Assert.AreEqual(0xBE, buf[2]);
        Assert.AreEqual(0xEF, buf[3]);
    }

    [TestMethod]
    public void WriteBytes_CopiesExactly()
    {
        using var ms = new MemoryStream();
        using var bw = new BigEndianWriter(ms);
        var data = new byte[] { 0x01, 0x02, 0x03 };
        bw.WriteBytes(data);

        CollectionAssert.AreEqual(data, ms.ToArray());
    }

    [TestMethod]
    public void LeaveOpen_DoesNotDisposeStream()
    {
        var ms = new MemoryStream();
        var bw = new BigEndianWriter(ms, leaveOpen: true);
        bw.WriteU8(0xFF);
        bw.Dispose();

        // Stream should still be accessible
        ms.Position = 0;
        Assert.AreEqual(0xFF, ms.ReadByte());
        ms.Dispose();
    }
}

// ──────────────────────────────────────────────────────────
//  CookData table tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class CookDataTests
{
    [TestMethod]
    public void HuffmanTables_QuantCodes_Populated()
    {
        // 13 codebooks with 24 symbols each
        Assert.AreEqual(13, CookData.QuantCodes.Length);
        Assert.AreEqual(13, CookData.QuantBits.Length);

        for (int i = 0; i < 13; i++)
        {
            Assert.AreEqual(24, CookData.QuantCodes[i].Length);
            Assert.AreEqual(24, CookData.QuantBits[i].Length);
        }
    }

    [TestMethod]
    public void HuffmanTables_QuantBits_AllNonZero()
    {
        // Every symbol should have a code assignment (non-zero bit length)
        for (int cb = 0; cb < 13; cb++)
        {
            for (int sym = 0; sym < 24; sym++)
            {
                Assert.IsTrue(CookData.QuantBits[cb][sym] > 0,
                    $"QuantBits[{cb}][{sym}] is 0 (no code assigned)");
            }
        }
    }

    [TestMethod]
    public void HuffmanTables_VqCodes_SevenCategories()
    {
        Assert.AreEqual(7, CookData.VqCodes.Length);
        Assert.AreEqual(7, CookData.VqBits.Length);
    }

    [TestMethod]
    public void HuffmanTables_VqTableSizes_MatchMultiplierPow()
    {
        // VQ table size for each category should be VqMult[cat]^VqGroupSize[cat]
        for (int cat = 0; cat < 7; cat++)
        {
            int expected = 1;
            for (int d = 0; d < CookData.VqGroupSize[cat]; d++)
            {
                expected *= CookData.VqMult[cat];
            }
            Assert.AreEqual(expected, CookData.VqCodes[cat].Length,
                $"VQ category {cat}: expected {expected}, got {CookData.VqCodes[cat].Length}");
        }
    }

    [TestMethod]
    public void HuffmanTables_CplCodes_FiveCodebooks()
    {
        Assert.AreEqual(5, CookData.CplCodes.Length);
        Assert.AreEqual(5, CookData.CplBits.Length);
    }

    [TestMethod]
    public void FlavourTable_HasEntries()
    {
        Assert.AreEqual(34, CookData.CookFlavours.Length);
    }

    [TestMethod]
    public void FlavourTable_Flavour9_MatchesJazz1()
    {
        // Flavor 9 is the jazz1.rm reference: 11025Hz, 2ch, 19kbps
        var f = CookData.CookFlavours[9];
        Assert.AreEqual(11025u, f.SampleRate);
        Assert.AreEqual(2, f.Channels);
        Assert.AreEqual(19, f.Bitrate);
        Assert.AreEqual(10, f.FramesPerBlock);
    }

    [TestMethod]
    public void BitrateParams_Flavour9_CorrectValues()
    {
        var f = CookData.CookFlavours[9];
        var br = CookData.BitrateParamsTable[f.BrIds[0]];
        Assert.AreEqual(2, br.Channels);
        Assert.AreEqual(464, br.FrameBits);
        Assert.AreEqual(11, br.MaxSubbands);
    }

    [TestMethod]
    public void VqMult_MatchesExpectedValues()
    {
        // Critical bug fix verification: VqMult must be kmax+1 (number of levels)
        var expected = new[] { 14, 10, 7, 5, 4, 3, 2 };
        CollectionAssert.AreEqual(expected, CookData.VqMult);
    }

    [TestMethod]
    public void PreemphasisCoeffs_Has23Sets()
    {
        // 23 sets × 3 sections × 5 coefficients = 345 floats
        Assert.AreEqual(345, CookData.PreemphasisCoeffs.Length);
    }

    [TestMethod]
    public void WindowFilterCoeffs_Has10Coefficients()
    {
        // 2 biquad sections × 5 coefficients = 10 floats
        Assert.AreEqual(10, CookData.WindowFilterCoeffs.Length);
    }

    [TestMethod]
    public void GainPowBase_Has23Entries()
    {
        Assert.AreEqual(23, CookData.GainPowBase.Length);
        Assert.AreEqual(1.0, CookData.GainPowBase[11]); // center = 2^0
    }

    [TestMethod]
    public void ExpBitsTab_DescendingOrder()
    {
        // Expected bits per category should decrease (more compression = fewer bits)
        for (int i = 0; i < CookData.ExpBitsTab.Length - 1; i++)
        {
            Assert.IsTrue(CookData.ExpBitsTab[i] >= CookData.ExpBitsTab[i + 1],
                $"ExpBitsTab[{i}]={CookData.ExpBitsTab[i]} < ExpBitsTab[{i + 1}]={CookData.ExpBitsTab[i + 1]}");
        }
    }
}

// ──────────────────────────────────────────────────────────
//  CookEncoder construction tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class CookEncoderConstructionTests
{
    [TestMethod]
    public void Constructor_Flavor9_Succeeds()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        Assert.IsNotNull(encoder);
    }

    [TestMethod]
    public void Constructor_Mono8kHz_Succeeds()
    {
        var encoder = new CookEncoder(8000, 1, 8000);
        Assert.IsNotNull(encoder);
    }

    [TestMethod]
    public void Constructor_Stereo44kHz_Succeeds()
    {
        var encoder = new CookEncoder(44100, 2, 95000);
        Assert.IsNotNull(encoder);
    }

    [TestMethod]
    public void Constructor_InvalidParams_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CookEncoder(48000, 1, 128000));
    }
}

// ──────────────────────────────────────────────────────────
//  CookEncoder encoding tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class CookEncoderEncodingTests
{
    [TestMethod]
    public void EncodePcm_Silence_ProducesPackets()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        // Flavor 9: frameSize=256, channels=2, sub_packet_h=10, framesPerPacket=10
        // GENR interleave group = 10 * 10 = 100 cook frames
        // PCM per frame = 256 * 2 * 2 = 1024 bytes
        // Need 100 frames = 102400 bytes for one interleave group
        int pcmBytes = 256 * 2 * 2 * 100;
        var silence = new byte[pcmBytes];
        var packets = encoder.EncodePcm(silence);

        Assert.IsTrue(packets.Count > 0, "Expected packets from a full interleave group");
    }

    [TestMethod]
    public void EncodePcm_Silence_ProducesCorrectPacketCount()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        // Flavor 9: one GENR group of 100 frames produces sub_packet_h=10 RM packets
        int pcmBytes = 256 * 2 * 2 * 100;
        var silence = new byte[pcmBytes];
        var packets = encoder.EncodePcm(silence);

        Assert.AreEqual(10, packets.Count);
    }

    [TestMethod]
    public void EncodePcm_InsufficientData_ReturnsEmpty()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        // Only 100 bytes — not enough for even one frame
        var pcm = new byte[100];
        var packets = encoder.EncodePcm(pcm);

        Assert.AreEqual(0, packets.Count);
    }

    [TestMethod]
    public void EncodePcm_SineWave_ProducesPackets()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        int totalSamples = 256 * 100; // 100 frames = one full GENR group
        var pcm = new byte[totalSamples * 2 * 2]; // stereo s16le
        for (int i = 0; i < totalSamples; i++)
        {
            short sample = (short)(Math.Sin(2.0 * Math.PI * 440.0 * i / 11025) * 8000);
            for (int ch = 0; ch < 2; ch++)
            {
                int idx = (i * 2 + ch) * 2;
                pcm[idx] = (byte)(sample & 0xFF);
                pcm[idx + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }

        var packets = encoder.EncodePcm(pcm);
        Assert.AreEqual(10, packets.Count);
    }

    [TestMethod]
    public void EncodePcm_RmPacketHeaders_Valid()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        int pcmBytes = 256 * 2 * 2 * 100;
        var silence = new byte[pcmBytes];
        var packets = encoder.EncodePcm(silence);

        foreach (var pkt in packets)
        {
            // RM data packet: version(2) + length(2) + stream(2) + timestamp(4) + reserved(1) + flags(1) + payload
            Assert.IsTrue(pkt.Length >= 12, "Packet too small for RM header");

            ushort version = BinaryPrimitives.ReadUInt16BigEndian(pkt.AsSpan(0));
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(pkt.AsSpan(2));
            ushort stream = BinaryPrimitives.ReadUInt16BigEndian(pkt.AsSpan(4));

            Assert.AreEqual(0, version);
            Assert.AreEqual(pkt.Length, length);
            Assert.AreEqual(0, stream);
        }
    }

    [TestMethod]
    public void EncodePcm_FirstPacket_HasKeyframeFlag()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        int pcmBytes = 256 * 2 * 2 * 100;
        var silence = new byte[pcmBytes];
        var packets = encoder.EncodePcm(silence);

        // First packet in group should have keyframe flag (0x02)
        Assert.AreEqual(0x02, packets[0][11]);

        // Remaining packets should not have keyframe
        for (int i = 1; i < packets.Count; i++)
        {
            Assert.AreEqual(0x00, packets[i][11], $"Packet {i} should not be keyframe");
        }
    }

    [TestMethod]
    public void EncodePcm_RawFrames_CollectsAllFrames()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        int pcmBytes = 256 * 2 * 2 * 100;
        var silence = new byte[pcmBytes];
        encoder.EncodePcm(silence);

        Assert.AreEqual(100, encoder.RawFrames.Count);
    }

    [TestMethod]
    public void EncodePcm_RawFrameSize_MatchesSubPacketSize()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        // Flavor 9: FrameBits=464, subPacketSize=464/8=58
        int pcmBytes = 256 * 2 * 2 * 100;
        var silence = new byte[pcmBytes];
        encoder.EncodePcm(silence);

        foreach (var frame in encoder.RawFrames)
        {
            Assert.AreEqual(58, frame.Length);
        }
    }

    [TestMethod]
    public void EncodePcm_XorScramble_Applied()
    {
        var encoder = new CookEncoder(11025, 2, 19000);

        // Encode silence — all-zero coefficients should still produce non-zero frames
        // because of XOR scrambling and Huffman-coded spectral envelope
        int pcmBytes = 256 * 2 * 2 * 100;
        var silence = new byte[pcmBytes];
        encoder.EncodePcm(silence);

        // At least some raw frames should have non-zero bytes
        bool anyNonZero = false;
        foreach (var frame in encoder.RawFrames)
        {
            foreach (byte b in frame)
            {
                if (b != 0)
                {
                    anyNonZero = true;
                    break;
                }
            }
            if (anyNonZero)
            {
                break;
            }
        }
        Assert.IsTrue(anyNonZero, "Expected non-zero bytes in XOR-scrambled frames");
    }
}

// ──────────────────────────────────────────────────────────
//  CookEncoder container tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class CookEncoderContainerTests
{
    [TestMethod]
    public void GetExtradata_Flavor9_IndependentStereo()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var extradata = encoder.GetExtradata();

        // Independent stereo (JsBits=0): version+chMode(4) + samples(2) + subbands(2) = 8 bytes
        Assert.AreEqual(8, extradata.Length);

        // version = 0x01, chMode = 0x02 (independent stereo)
        uint versionAndMode = BinaryPrimitives.ReadUInt32BigEndian(extradata);
        Assert.AreEqual(0x01000002u, versionAndMode);
    }

    [TestMethod]
    public void BuildRa5Header_StartsWithMagic()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var header = encoder.BuildRa5Header();

        // .ra\xFD magic
        Assert.AreEqual(0x2E, header[0]); // '.'
        Assert.AreEqual(0x72, header[1]); // 'r'
        Assert.AreEqual(0x61, header[2]); // 'a'
        Assert.AreEqual(0xFD, header[3]); // 0xFD
    }

    [TestMethod]
    public void BuildRa5Header_Version5()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var header = encoder.BuildRa5Header();

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        Assert.AreEqual(5, version);
    }

    [TestMethod]
    public void BuildRa5Header_ContainsCookCodecId()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var header = encoder.BuildRa5Header();

        // Find "cook" in the header
        bool found = false;
        for (int i = 0; i <= header.Length - 4; i++)
        {
            if (header[i] == 0x63 && header[i + 1] == 0x6F &&
                header[i + 2] == 0x6F && header[i + 3] == 0x6B)
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected 'cook' codec ID in Ra5 header");
    }

    [TestMethod]
    public void BuildRa5Header_ContainsGenrInterleaver()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var header = encoder.BuildRa5Header();

        // Find "genr" in the header
        bool found = false;
        for (int i = 0; i <= header.Length - 4; i++)
        {
            if (header[i] == 0x67 && header[i + 1] == 0x65 &&
                header[i + 2] == 0x6E && header[i + 3] == 0x72)
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected 'genr' interleaver ID in Ra5 header");
    }

    [TestMethod]
    public void BuildRmFileHeaders_StartsWithRmfChunk()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var headers = encoder.BuildRmFileHeaders("Test Station");

        // .RMF magic
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(headers.AsSpan(0));
        Assert.AreEqual(0x2E524D46u, magic); // ".RMF"
    }

    [TestMethod]
    public void BuildRmFileHeaders_ContainsPropContMdprData()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var headers = encoder.BuildRmFileHeaders("Test Station");

        // Convert to string for easier searching
        var chunkIds = new List<string>();
        for (int i = 0; i <= headers.Length - 4; i++)
        {
            if (headers[i] >= 0x20 && headers[i] <= 0x7E &&
                headers[i + 1] >= 0x20 && headers[i + 1] <= 0x7E &&
                headers[i + 2] >= 0x20 && headers[i + 2] <= 0x7E &&
                headers[i + 3] >= 0x20 && headers[i + 3] <= 0x7E)
            {
                string id = System.Text.Encoding.ASCII.GetString(headers, i, 4);
                if (id == "PROP" || id == "CONT" || id == "MDPR" || id == "DATA")
                {
                    chunkIds.Add(id);
                }
            }
        }

        CollectionAssert.Contains(chunkIds, "PROP");
        CollectionAssert.Contains(chunkIds, "CONT");
        CollectionAssert.Contains(chunkIds, "MDPR");
        CollectionAssert.Contains(chunkIds, "DATA");
    }

    [TestMethod]
    public void BuildRmHeaders_ForStreaming_NoPropNumPackets()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var headers = encoder.BuildRmHeaders("Stream");

        // PNA streaming headers should not start with .RMF chunk
        // (they start directly with PROP)
        uint firstChunk = BinaryPrimitives.ReadUInt32BigEndian(headers.AsSpan(0));
        Assert.AreEqual(0x50524F50u, firstChunk); // "PROP"
    }

    [TestMethod]
    public void GenerateTestRmFile_ProducesValidRm()
    {
        // Need ~2.4s for one GENR group (100 frames × 256 samples / 11025Hz)
        var rmData = CookEncoder.GenerateTestRmFile(3.0f);

        Assert.IsTrue(rmData.Length > 0);

        // Should start with .RMF
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(rmData.AsSpan(0));
        Assert.AreEqual(0x2E524D46u, magic);
    }

    [TestMethod]
    public void GenerateTestRmFile_ContainsDataPackets()
    {
        var rmData = CookEncoder.GenerateTestRmFile(5.0f);

        // Should be substantially larger than just headers (headers are ~300 bytes)
        Assert.IsTrue(rmData.Length > 500, $"Expected data packets, got only {rmData.Length} bytes");
    }
}

// ──────────────────────────────────────────────────────────
//  RealCodecProfile tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class RealCodecProfileTests
{
    [TestMethod]
    public void SelectForBandwidth_ZeroDefaultsToCookStereo11k()
    {
        var profile = RealCodecProfile.SelectForBandwidth(0);

        Assert.AreEqual(RealCodecType.Cook, profile.CodecType);
        Assert.AreEqual(11025, profile.SampleRate);
        Assert.AreEqual(2, profile.Channels);
    }

    [TestMethod]
    public void SelectForBandwidth_144Modem_SelectsRa144()
    {
        // 14.4k modem = ~1800 bytes/sec
        var profile = RealCodecProfile.SelectForBandwidth(1800);

        Assert.AreEqual(RealCodecType.Ra144, profile.CodecType);
        Assert.AreEqual(8000, profile.SampleRate);
        Assert.AreEqual(1, profile.Channels);
    }

    [TestMethod]
    public void SelectForBandwidth_288Modem_SelectsCookStereo11k()
    {
        // 28.8k modem = ~3600 bytes/sec → ~24 kbps usable
        var profile = RealCodecProfile.SelectForBandwidth(3600);

        Assert.AreEqual(RealCodecType.Cook, profile.CodecType);
        Assert.AreEqual(11025, profile.SampleRate);
        Assert.AreEqual(2, profile.Channels);
    }

    [TestMethod]
    public void SelectForBandwidth_56kModem_SelectsCookStereo22k()
    {
        // 56k modem = ~7000 bytes/sec → ~48 kbps usable
        var profile = RealCodecProfile.SelectForBandwidth(7000);

        Assert.AreEqual(RealCodecType.Cook, profile.CodecType);
        Assert.AreEqual(22050, profile.SampleRate);
    }

    [TestMethod]
    public void SelectForBandwidth_Broadband_SelectsCookStereo44k()
    {
        // Broadband = 50000 bytes/sec → ~340 kbps usable
        var profile = RealCodecProfile.SelectForBandwidth(50000);

        Assert.AreEqual(RealCodecType.Cook, profile.CodecType);
        Assert.AreEqual(44100, profile.SampleRate);
    }

    [TestMethod]
    public void Profile_Key_DistinguishesCodecTypes()
    {
        Assert.AreEqual("ra144", RealCodecProfile.Ra144.Key);
        Assert.IsTrue(RealCodecProfile.CookStereo11k.Key.StartsWith("cook-"));
        Assert.AreNotEqual(RealCodecProfile.CookStereo11k.Key, RealCodecProfile.CookStereo22k.Key);
    }

    [TestMethod]
    public void Profile_ToString_ContainsUsefulInfo()
    {
        var s = RealCodecProfile.CookStereo11k.ToString();

        Assert.IsTrue(s.Contains("Cook"), $"Expected 'Cook' in '{s}'");
        Assert.IsTrue(s.Contains("11025"), $"Expected '11025' in '{s}'");
    }

    [TestMethod]
    public void CookEncoder_AcceptsMultipleFlavors()
    {
        // Verify CookEncoder can be constructed with different profiles
        var profiles = new[]
        {
            RealCodecProfile.CookMono8k,
            RealCodecProfile.CookStereo11k,
            RealCodecProfile.CookStereo22k,
            RealCodecProfile.CookStereo44k,
        };

        foreach (var p in profiles)
        {
            var encoder = new CookEncoder(p.SampleRate, p.Channels, p.Bitrate);
            Assert.IsNotNull(encoder, $"Failed to create encoder for {p}");
        }
    }

    [TestMethod]
    public void CookEncoder_DifferentFlavors_ProduceDifferentFrameSizes()
    {
        // 11025Hz → frameSize=256, 22050Hz → frameSize=512
        var enc11k = new CookEncoder(11025, 2, 19000);
        var enc22k = new CookEncoder(22050, 2, 32000);

        var ra5_11k = enc11k.BuildRa5Header();
        var ra5_22k = enc22k.BuildRa5Header();

        // Headers should be different sizes or contain different sample rate values
        Assert.AreNotEqual(BitConverter.ToString(ra5_11k), BitConverter.ToString(ra5_22k));
    }
}
