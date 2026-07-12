// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Processors.LocalServer.Streaming;

namespace Adversarial2.Streaming;

[TestClass]
public class StreamingCookBitWriterAdversarialTests
{
    [TestMethod]
    public void Write_NegativeNumBits_NoOp()
    {
        var bw = new CookBitWriter(2);
        bw.Write(0xFFFFFFFF, -5);

        Assert.AreEqual(0, bw.BitsWritten);
        var buf = bw.ToArray();
        Assert.AreEqual(0x00, buf[0]);
        Assert.AreEqual(0x00, buf[1]);
    }

    [TestMethod]
    public void Write_IntMinNumBits_NoOp()
    {
        var bw = new CookBitWriter(2);
        bw.Write(0xFFFFFFFF, int.MinValue);

        Assert.AreEqual(0, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_ValueBitsAboveNumBits_Masked()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0xFFFFFFFF, 4);
        var buf = bw.ToArray();

        Assert.AreEqual(0xF0, buf[0]);
        Assert.AreEqual(4, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_HighBitOutsideWidth_Ignored()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0x10, 4);
        var buf = bw.ToArray();

        Assert.AreEqual(0x00, buf[0]);
        Assert.AreEqual(4, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_FullThirtyTwoBits_AllOnes()
    {
        var bw = new CookBitWriter(4);
        bw.Write(0xFFFFFFFF, 32);
        var buf = bw.ToArray();

        Assert.AreEqual(0xFF, buf[0]);
        Assert.AreEqual(0xFF, buf[1]);
        Assert.AreEqual(0xFF, buf[2]);
        Assert.AreEqual(0xFF, buf[3]);
        Assert.AreEqual(32, bw.BitsWritten);
    }

    [TestMethod]
    public void Write_PartialThenOverflowingWrite_SecondIgnoredEntirely()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0x0F, 4);
        bw.Write(0x1F, 5);
        var buf = bw.ToArray();

        Assert.AreEqual(4, bw.BitsWritten);
        Assert.AreEqual(0xF0, buf[0]);
    }

    [TestMethod]
    public void Write_ExactlyFillThenExtra_ExtraIgnored()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0xAB, 8);
        bw.Write(1, 1);
        bw.Write(0xFF, 8);

        Assert.AreEqual(8, bw.BitsWritten);
        Assert.AreEqual(0xAB, bw.ToArray()[0]);
    }

    [TestMethod]
    public void Constructor_ZeroByteSize_AllWritesIgnored()
    {
        var bw = new CookBitWriter(0);
        bw.Write(0xFF, 1);
        bw.Write(0xFF, 8);

        Assert.AreEqual(0, bw.BitsWritten);
        Assert.AreEqual(0, bw.ToArray().Length);
    }

    [TestMethod]
    public void PadToEnd_ZeroByteSize_StaysZero()
    {
        var bw = new CookBitWriter(0);
        bw.PadToEnd();

        Assert.AreEqual(0, bw.BitsWritten);
        Assert.AreEqual(0, bw.ToArray().Length);
    }

    [TestMethod]
    public void Constructor_NegativeByteSize_Throws()
    {
        Assert.ThrowsExactly<OverflowException>(() => new CookBitWriter(-1));
    }

    [TestMethod]
    public void PadToEnd_ThenWrite_Ignored()
    {
        var bw = new CookBitWriter(2);
        bw.Write(0xAB, 8);
        bw.PadToEnd();
        Assert.AreEqual(16, bw.BitsWritten);

        bw.Write(0xFF, 4);
        Assert.AreEqual(16, bw.BitsWritten);

        var buf = bw.ToArray();
        Assert.AreEqual(0xAB, buf[0]);
        Assert.AreEqual(0x00, buf[1]);
    }

    [TestMethod]
    public void Write_ZeroWidthAtBoundary_NoOp()
    {
        var bw = new CookBitWriter(1);
        bw.Write(0xFF, 8);
        bw.Write(0, 0);

        Assert.AreEqual(8, bw.BitsWritten);
    }
}

[TestClass]
public class StreamingRealCodecProfileAdversarialTests
{
    [TestMethod]
    public void SelectForBandwidth_JustBelow15kbps_Ra144()
    {
        var p = RealCodecProfile.SelectForBandwidth(2205);
        Assert.AreEqual(RealCodecType.Ra144, p.CodecType);
    }

    [TestMethod]
    public void SelectForBandwidth_JustAtOrAbove15kbps_CookStereo11k()
    {
        var p = RealCodecProfile.SelectForBandwidth(2206);
        Assert.AreEqual(RealCodecType.Cook, p.CodecType);
        Assert.AreEqual(11025, p.SampleRate);
    }

    [TestMethod]
    public void SelectForBandwidth_JustBelow25kbps_CookStereo11k()
    {
        var p = RealCodecProfile.SelectForBandwidth(3676);
        Assert.AreEqual(11025, p.SampleRate);
    }

    [TestMethod]
    public void SelectForBandwidth_JustAtOrAbove25kbps_CookStereo22k()
    {
        var p = RealCodecProfile.SelectForBandwidth(3677);
        Assert.AreEqual(22050, p.SampleRate);
    }

    [TestMethod]
    public void SelectForBandwidth_JustBelow50kbps_CookStereo22k()
    {
        var p = RealCodecProfile.SelectForBandwidth(7352);
        Assert.AreEqual(22050, p.SampleRate);
    }

    [TestMethod]
    public void SelectForBandwidth_JustAtOrAbove50kbps_CookStereo44k()
    {
        var p = RealCodecProfile.SelectForBandwidth(7353);
        Assert.AreEqual(44100, p.SampleRate);
    }

    [TestMethod]
    public void SelectForBandwidth_One_TinyBandwidth_Ra144()
    {
        var p = RealCodecProfile.SelectForBandwidth(1);
        Assert.AreEqual(RealCodecType.Ra144, p.CodecType);
    }

    [TestMethod]
    public void SelectForBandwidth_UIntMax_NoOverflow_CookStereo44k()
    {
        var p = RealCodecProfile.SelectForBandwidth(uint.MaxValue);
        Assert.AreEqual(RealCodecType.Cook, p.CodecType);
        Assert.AreEqual(44100, p.SampleRate);
    }
}

[TestClass]
public class StreamingCookEncoderConstructionAdversarialTests
{
    [TestMethod]
    public void Constructor_ZeroSampleRate_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CookEncoder(0, 2, 19000));
    }

    [TestMethod]
    public void Constructor_NegativeSampleRate_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CookEncoder(-11025, 2, 19000));
    }

    [TestMethod]
    public void Constructor_UnknownSampleRate_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CookEncoder(16000, 1, 8000));
    }

    [TestMethod]
    public void Constructor_ZeroChannels_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CookEncoder(11025, 0, 19000));
    }

    [TestMethod]
    public void Constructor_NegativeChannels_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CookEncoder(11025, -2, 19000));
    }

    [TestMethod]
    public void Constructor_TooManyChannels_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CookEncoder(11025, 3, 19000));
    }

    [TestMethod]
    public void Constructor_OverlargeBitrate_NoMatch_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CookEncoder(11025, 2, int.MaxValue));
    }

    [TestMethod]
    public void Constructor_NegativeBitrate_Lenient_Constructs()
    {
        var encoder = new CookEncoder(11025, 2, -1);
        Assert.IsNotNull(encoder);
    }

    [TestMethod]
    public void Constructor_ZeroBitrate_Lenient_Constructs()
    {
        var encoder = new CookEncoder(11025, 2, 0);
        Assert.IsNotNull(encoder);
    }
}

[TestClass]
public class StreamingCookEncoderEncodePcmAdversarialTests
{
    private const int FrameBytes = 1024;
    private const int SubPacketSize = 58;
    private const int FramesPerGroup = 100;

    [TestMethod]
    public void EncodePcm_Empty_ReturnsEmpty_NoThrow()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var packets = encoder.EncodePcm(ReadOnlySpan<byte>.Empty);

        Assert.AreEqual(0, packets.Count);
    }

    [TestMethod]
    public void EncodePcm_SingleByte_Buffers_ReturnsEmpty()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var packets = encoder.EncodePcm(new byte[] { 0x42 });

        Assert.AreEqual(0, packets.Count);
    }

    [TestMethod]
    public void EncodePcm_OneFrameMinusOneByte_ReturnsEmpty()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var packets = encoder.EncodePcm(new byte[FrameBytes - 1]);

        Assert.AreEqual(0, packets.Count);
    }

    [TestMethod]
    public void EncodePcm_ExactlyOneFrame_NoPacketsButFrameEncoded()
    {
        var encoder = new CookEncoder(11025, 2, 19000) { CollectRawFrames = true };
        var packets = encoder.EncodePcm(new byte[FrameBytes]);

        Assert.AreEqual(0, packets.Count);
        Assert.AreEqual(1, encoder.RawFrames.Count);
        Assert.AreEqual(SubPacketSize, encoder.RawFrames[0].Length);
    }

    [TestMethod]
    public void EncodePcm_FragmentedFrame_ReassembledAcrossCalls()
    {
        var encoder = new CookEncoder(11025, 2, 19000) { CollectRawFrames = true };

        var whole = new byte[FrameBytes];
        for (int i = 0; i < whole.Length; i++)
        {
            whole[i] = (byte)(i * 7);
        }

        var p1 = encoder.EncodePcm(whole.AsSpan(0, 500));
        var p2 = encoder.EncodePcm(whole.AsSpan(500, FrameBytes - 500));

        Assert.AreEqual(0, p1.Count);
        Assert.AreEqual(0, p2.Count);
        Assert.AreEqual(1, encoder.RawFrames.Count);
        Assert.AreEqual(SubPacketSize, encoder.RawFrames[0].Length);
    }

    [TestMethod]
    public void EncodePcm_TruncatedGroup_99Frames_NoPacketsEmitted()
    {
        var encoder = new CookEncoder(11025, 2, 19000) { CollectRawFrames = true };

        var pcm = new byte[FrameBytes * (FramesPerGroup - 1)];
        var packets = encoder.EncodePcm(pcm);

        Assert.AreEqual(0, packets.Count);
        Assert.AreEqual(FramesPerGroup - 1, encoder.RawFrames.Count);
    }

    [TestMethod]
    public void EncodePcm_MaxAmplitudePcm_DoesNotCrash_ValidSizes()
    {
        var encoder = new CookEncoder(11025, 2, 19000) { CollectRawFrames = true };
        var pcm = new byte[FrameBytes * FramesPerGroup];
        for (int i = 0; i < pcm.Length; i += 2)
        {
            pcm[i] = 0xFF;
            pcm[i + 1] = 0x7F;
        }

        var packets = encoder.EncodePcm(pcm);

        Assert.AreEqual(10, packets.Count);
        Assert.AreEqual(FramesPerGroup, encoder.RawFrames.Count);
        foreach (var f in encoder.RawFrames)
        {
            Assert.AreEqual(SubPacketSize, f.Length);
        }
    }

    [TestMethod]
    public void EncodePcm_MinAmplitudePcm_DoesNotCrash_ValidSizes()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var pcm = new byte[FrameBytes * FramesPerGroup];
        for (int i = 0; i < pcm.Length; i += 2)
        {
            pcm[i] = 0x00;
            pcm[i + 1] = 0x80;
        }

        var packets = encoder.EncodePcm(pcm);
        Assert.AreEqual(10, packets.Count);
    }

    [TestMethod]
    public void EncodePcm_TwoGroupsSequential_StateResetsBetweenGroups()
    {
        var encoder = new CookEncoder(11025, 2, 19000) { CollectRawFrames = true };
        var group = new byte[FrameBytes * FramesPerGroup];

        var first = encoder.EncodePcm(group);
        var second = encoder.EncodePcm(group);

        Assert.AreEqual(10, first.Count);
        Assert.AreEqual(10, second.Count);
        Assert.AreEqual(FramesPerGroup * 2, encoder.RawFrames.Count);
    }

    [TestMethod]
    public void EncodePcm_GroupPlusStragglerBytes_OnlyGroupEmitted()
    {
        var encoder = new CookEncoder(11025, 2, 19000);
        var pcm = new byte[FrameBytes * FramesPerGroup + 3];

        var packets = encoder.EncodePcm(pcm);
        Assert.AreEqual(10, packets.Count);
    }
}