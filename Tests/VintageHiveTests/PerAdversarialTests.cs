// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.Asn1;

namespace Adversarial2.Per;

// ----------------------------------------------------------
//  Adversarial tests for the aligned PER (X.691) bit-level
//  decoder and encoder. These exercise hostile/malformed
//  inputs: truncated buffers, bit counts of 0 and > remaining,
//  length determinants that overrun, choice/enum indices out of
//  range, and boundary integer values. They deliberately avoid
//  the happy-path coverage already in PerTests.cs.
// ----------------------------------------------------------

[TestClass]
public class AdvPerDecoderBitTests
{
    [TestMethod]
    public void ReadBits_ZeroCount_ReturnsZero_NoAdvance()
    {
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.AreEqual(0u, dec.ReadBits(0));
        Assert.AreEqual(0, dec.BitPosition);
        Assert.IsTrue(dec.HasData);
    }

    [TestMethod]
    public void ReadBitsLong_ZeroCount_ReturnsZero_NoAdvance()
    {
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.AreEqual(0ul, dec.ReadBitsLong(0));
        Assert.AreEqual(0, dec.BitPosition);
    }

    [TestMethod]
    public void ReadBits_NegativeCount_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dec.ReadBits(-1));
    }

    [TestMethod]
    public void ReadBits_CountAbove32_Throws()
    {
        var dec = new PerDecoder(new byte[8]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dec.ReadBits(33));
    }

    [TestMethod]
    public void ReadBitsLong_CountAbove64_Throws()
    {
        var dec = new PerDecoder(new byte[16]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dec.ReadBitsLong(65));
    }

    [TestMethod]
    public void ReadBitsLong_64Bits_AllOnes_ReturnsMaxValue()
    {
        // Boundary: read the full 64-bit width from an all-0xFF buffer.
        var dec = new PerDecoder(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.AreEqual(ulong.MaxValue, dec.ReadBitsLong(64));
        Assert.IsFalse(dec.HasData);
    }

    [TestMethod]
    public void ReadBits_MoreThanRemaining_Throws()
    {
        // 1-byte buffer, request 9 bits: the 9th ReadBit runs off the end.
        var dec = new PerDecoder(new byte[] { 0xAB });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBits(9));
    }

    [TestMethod]
    public void ReadBits_ExactlyToEnd_ThenReadThrows()
    {
        var dec = new PerDecoder(new byte[] { 0xAB });
        Assert.AreEqual(0xABu, dec.ReadBits(8));
        Assert.IsFalse(dec.HasData);
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBit());
    }

    [TestMethod]
    public void AlignToOctet_PastPartialEnd_OvershootsSilently_ThenReadThrows()
    {
        // Decoder whose declared length is a non-octet count (5 bits).
        // Aligning from bit 1 jumps to bit 8, well past the 5-bit end,
        // WITHOUT throwing; the next actual read is what fails.
        var dec = new PerDecoder(new byte[] { 0xFF }, 0, 5);
        dec.ReadBit();
        dec.AlignToOctet(); // must not throw even though it overshoots
        Assert.IsFalse(dec.HasData);
        Assert.IsTrue(dec.BitsRemaining < 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBit());
    }

    [TestMethod]
    public void ReadOctets_Zero_ReturnsEmpty_NoAdvance()
    {
        var dec = new PerDecoder(new byte[] { 0xAA, 0xBB });
        var result = dec.ReadOctets(0);
        Assert.AreEqual(0, result.Length);
        Assert.AreEqual(0, dec.BitPosition);
    }

    [TestMethod]
    public void ReadOctets_Aligned_ExceedsRemaining_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0x01, 0x02 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOctets(3));
    }

    [TestMethod]
    public void ReadOctets_Unaligned_ExceedsRemaining_Throws()
    {
        // Consume 1 bit, then ask for a full octet the buffer can no longer supply.
        var dec = new PerDecoder(new byte[] { 0xFF });
        dec.ReadBit();
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOctets(1));
    }

    [TestMethod]
    public void ReadOptionalBitmap_Zero_ReturnsEmpty()
    {
        var dec = new PerDecoder(new byte[] { 0xFF });
        var bitmap = dec.ReadOptionalBitmap(0);
        Assert.AreEqual(0, bitmap.Length);
        Assert.AreEqual(0, dec.BitPosition);
    }

    [TestMethod]
    public void ReadOptionalBitmap_ExceedsBuffer_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0xFF }); // only 8 bits
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOptionalBitmap(9));
    }
}

[TestClass]
public class AdvPerDecoderLengthTests
{
    [TestMethod]
    public void ReadLengthDeterminant_FragmentationByte_Throws()
    {
        // 0xC0 = 11xxxxxx signals fragmentation, which the decoder rejects.
        var dec = new PerDecoder(new byte[] { 0xC0, 0x00, 0x00 });
        Assert.ThrowsExactly<NotSupportedException>(() => dec.ReadLengthDeterminant());
    }

    [TestMethod]
    public void ReadLengthDeterminant_LongFormFirstByte_MissingSecond_Throws()
    {
        // 0x80 = 10xxxxxx signals a 2-byte length, but there is no second byte.
        var dec = new PerDecoder(new byte[] { 0x80 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadLengthDeterminant());
    }

    [TestMethod]
    public void ReadLengthDeterminant_EmptyBuffer_Throws()
    {
        var dec = new PerDecoder(Array.Empty<byte>());
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadLengthDeterminant());
    }

    [TestMethod]
    public void ReadLengthDeterminant_LongFormMaxValue_16383()
    {
        // Boundary: 0xBF 0xFF = 10 111111 11111111 = 16383, the largest non-fragmented length.
        var dec = new PerDecoder(new byte[] { 0xBF, 0xFF });
        Assert.AreEqual(16383, dec.ReadLengthDeterminant());
    }
}

[TestClass]
public class AdvPerDecoderWholeNumberTests
{
    [TestMethod]
    public void ReadConstrainedWholeNumber_InvertedBounds_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.ThrowsExactly<ArgumentException>(() => dec.ReadConstrainedWholeNumber(10, 5));
    }

    [TestMethod]
    public void ReadConstrainedWholeNumber_EqualBounds_ConsumesNoBits()
    {
        // range == 1: the value is predetermined, no bits are read.
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.AreEqual(5L, dec.ReadConstrainedWholeNumber(5, 5));
        Assert.AreEqual(0, dec.BitPosition);
    }

    [TestMethod]
    public void ReadConstrainedWholeNumber_Range256_Truncated_Throws()
    {
        // range 256 aligns then reads an octet; an empty buffer cannot supply it.
        var dec = new PerDecoder(Array.Empty<byte>());
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadConstrainedWholeNumber(0, 255));
    }

    [TestMethod]
    public void ReadConstrainedWholeNumber_LargeRange_TruncatedContentOctets_Throws()
    {
        // range > 65536 uses a length-prefixed form: 2-bit numBytes (here 4),
        // then that many content octets. Only the numBytes bits are present.
        var dec = new PerDecoder(new byte[] { 0xC0 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadConstrainedWholeNumber(0, 100000));
    }

    [TestMethod]
    public void ReadSemiConstrainedWholeNumber_TruncatedContent_Throws()
    {
        // Length determinant = 4 octets, but none follow.
        var dec = new PerDecoder(new byte[] { 0x04 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadSemiConstrainedWholeNumber(0));
    }

    [TestMethod]
    public void ReadUnconstrainedWholeNumber_ZeroLength_ReturnsZero()
    {
        // Length determinant 0 => canonical zero, no content octets.
        var dec = new PerDecoder(new byte[] { 0x00 });
        Assert.AreEqual(0L, dec.ReadUnconstrainedWholeNumber());
    }

    [TestMethod]
    public void ReadUnconstrainedWholeNumber_TruncatedContent_Throws()
    {
        // Length determinant 4, but no content octets are present.
        var dec = new PerDecoder(new byte[] { 0x04 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadUnconstrainedWholeNumber());
    }

    [TestMethod]
    public void UnconstrainedWholeNumber_LongMinValue_RoundTrips()
    {
        // Boundary: full-width negative two's-complement value.
        var enc = new PerEncoder();
        enc.WriteUnconstrainedWholeNumber(long.MinValue);
        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(long.MinValue, dec.ReadUnconstrainedWholeNumber());
    }

    [TestMethod]
    public void UnconstrainedWholeNumber_LongMaxValue_RoundTrips()
    {
        var enc = new PerEncoder();
        enc.WriteUnconstrainedWholeNumber(long.MaxValue);
        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(long.MaxValue, dec.ReadUnconstrainedWholeNumber());
    }
}

[TestClass]
public class AdvPerDecoderChoiceEnumTests
{
    [TestMethod]
    public void ReadChoiceIndex_ZeroAlternatives_Throws()
    {
        // numAlternatives 0 => constrained(0, -1) => empty range => ArgumentException.
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.ThrowsExactly<ArgumentException>(() => dec.ReadChoiceIndex(0));
    }

    [TestMethod]
    public void ReadChoiceIndex_SingleAlternative_ConsumesNoBits()
    {
        // 1 alternative => range 1 => index is predetermined 0, nothing read.
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.AreEqual(0, dec.ReadChoiceIndex(1));
        Assert.AreEqual(0, dec.BitPosition);
    }

    [TestMethod]
    public void ReadEnumerated_ZeroRoot_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.ThrowsExactly<ArgumentException>(() => dec.ReadEnumerated(0));
    }

    [TestMethod]
    public void ReadEnumerated_ExtensibleExtension_TruncatedIndex_Throws()
    {
        // Extension bit set (1), then a normally-small number whose 6 value bits
        // run past this 4-bit-long decoder.
        var dec = new PerDecoder(new byte[] { 0x80 }, 0, 4);
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadEnumerated(4, extensible: true));
    }

    [TestMethod]
    public void ReadChoiceIndex_ExtensibleExtension_TruncatedNormallySmall_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0x80 }, 0, 4);
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadChoiceIndex(4, extensible: true));
    }

    [TestMethod]
    public void ReadNormallySmallNumber_TruncatedSmall_Throws()
    {
        // First bit 0 (small) => 6 value bits follow, but only 3 bits exist.
        var dec = new PerDecoder(new byte[] { 0x00 }, 0, 3);
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadNormallySmallNumber());
    }
}

[TestClass]
public class AdvPerDecoderStringAndBlobTests
{
    [TestMethod]
    public void ReadOctetString_UnconstrainedLengthExceedsBuffer_Throws()
    {
        // Length determinant says 5 octets; none follow it.
        var dec = new PerDecoder(new byte[] { 0x05 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOctetString());
    }

    [TestMethod]
    public void ReadIA5String_LengthExceedsBuffer_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0x05 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadIA5String());
    }

    [TestMethod]
    public void ReadBMPString_LengthExceedsBuffer_Throws()
    {
        // 2 characters => 32 bits of content demanded, none present.
        var dec = new PerDecoder(new byte[] { 0x02 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBMPString());
    }

    [TestMethod]
    public void ReadBMPString_OddTrailingByte_Throws()
    {
        // Length 1 (=> 16 bits) but only a single content octet remains after
        // the length byte, so the char's second byte runs off the end.
        var dec = new PerDecoder(new byte[] { 0x01, 0x41 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBMPString());
    }

    [TestMethod]
    public void ReadIA5String_ControlAndHighBytes_DecodeVerbatim()
    {
        // APER IA5String is 8 bits/char here; the decoder does not sanitize.
        // A hostile peer can smuggle NUL and 0x7F..0xFF straight through.
        var dec = new PerDecoder(new byte[] { 0x03, 0x00, 0x7F, 0xFF });
        var s = dec.ReadIA5String();
        Assert.AreEqual(3, s.Length);
        Assert.AreEqual(0x00, (int)s[0]); // NUL passes through unsanitized
        Assert.AreEqual(0x7F, (int)s[1]); // DEL passes through unsanitized
        Assert.AreEqual(0xFF, (int)s[2]); // 0xFF widened to U+00FF, outside valid IA5
    }

    [TestMethod]
    public void ReadObjectIdentifier_TruncatedContent_Throws()
    {
        // Length determinant says 6 content octets; only 2 follow.
        var dec = new PerDecoder(new byte[] { 0x06, 0x2A, 0x03 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadObjectIdentifier());
    }

    [TestMethod]
    public void ReadObjectIdentifier_EmptyContent_ReturnsEmpty()
    {
        // Length determinant 0 => zero content octets => no components.
        var dec = new PerDecoder(new byte[] { 0x00 });
        var oid = dec.ReadObjectIdentifier();
        Assert.AreEqual(0, oid.Length);
    }

    [TestMethod]
    public void ReadObjectIdentifier_DanglingContinuationBit_DoesNotHang()
    {
        // Content { 0x00, 0x81 }: the final octet has the continuation bit set
        // but the buffer ends. The base-128 loop must terminate, not spin.
        var dec = new PerDecoder(new byte[] { 0x02, 0x00, 0x81 });
        var oid = dec.ReadObjectIdentifier();
        CollectionAssert.AreEqual(new int[] { 0, 0, 1 }, oid);
    }

    [TestMethod]
    public void ReadOpenType_LengthExceedsBuffer_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0x08 }); // claims 8 octets, none present
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOpenType());
    }
}

[TestClass]
public class AdvPerDecoderExtensionTests
{
    [TestMethod]
    public void ReadExtensionAdditions_TruncatedOpenType_Throws()
    {
        // count = normallySmall(0)+1 = 1, presence bit 1 (present), then an open
        // type whose declared length overruns the buffer.
        // Byte 0: 0 (small) 000000 => 7 bits of normally-small(0).
        // Bit 7 (last bit of byte 0): presence bit = 1.
        // Byte 1: length determinant 0x08 (8 octets) with no content following.
        var dec = new PerDecoder(new byte[] { 0x01, 0x08 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadExtensionAdditions());
    }

    [TestMethod]
    public void TryReadExtensionAdditions_TruncatedOpenType_ReturnsFalse()
    {
        // Best-effort variant swallows the truncation and reports failure.
        var dec = new PerDecoder(new byte[] { 0x01, 0x08 });
        Assert.IsFalse(dec.TryReadExtensionAdditions());
    }

    [TestMethod]
    public void TryReadExtensionAdditions_EmptyBuffer_ReturnsFalse()
    {
        var dec = new PerDecoder(Array.Empty<byte>());
        Assert.IsFalse(dec.TryReadExtensionAdditions());
    }
}

[TestClass]
public class AdvPerEncoderTests
{
    [TestMethod]
    public void WriteBits_CountAbove32_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteBits(0, 33));
    }

    [TestMethod]
    public void WriteBits_NegativeCount_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteBits(0, -1));
    }

    [TestMethod]
    public void WriteBitsLong_CountAbove64_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteBitsLong(0, 65));
    }

    [TestMethod]
    public void WriteBits_ZeroCount_WritesNothing()
    {
        var enc = new PerEncoder();
        enc.WriteBits(0xFFFFFFFF, 0);
        Assert.AreEqual(0, enc.TotalBits);
    }

    [TestMethod]
    public void WriteConstrainedWholeNumber_ValueBelowLowerBound_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteConstrainedWholeNumber(4, 5, 10));
    }

    [TestMethod]
    public void WriteConstrainedWholeNumber_ValueAboveUpperBound_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteConstrainedWholeNumber(11, 5, 10));
    }

    [TestMethod]
    public void WriteConstrainedWholeNumber_InvertedBounds_Throws()
    {
        // range <= 0 is rejected before the value-range check.
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentException>(() => enc.WriteConstrainedWholeNumber(0, 10, 5));
    }

    [TestMethod]
    public void WriteSemiConstrainedWholeNumber_BelowLowerBound_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteSemiConstrainedWholeNumber(5, 10));
    }

    [TestMethod]
    public void WriteLengthDeterminant_Negative_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteLengthDeterminant(-1));
    }

    [TestMethod]
    public void WriteNormallySmallNumber_Negative_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteNormallySmallNumber(-1));
    }

    [TestMethod]
    public void WriteOctets_WhenUnaligned_Throws()
    {
        var enc = new PerEncoder();
        enc.WriteBit(true); // 1 bit => no longer octet-aligned
        Assert.ThrowsExactly<InvalidOperationException>(() => enc.WriteOctets(new byte[] { 0x01 }));
    }

    [TestMethod]
    public void WriteObjectIdentifier_NegativeComponent_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentException>(() => enc.WriteObjectIdentifier(new int[] { 1, 2, -5 }));
    }

    [TestMethod]
    public void BitsNeeded_ZeroAndNegativeRange_ReturnsZero()
    {
        // Degenerate ranges must not produce a negative bit width.
        Assert.AreEqual(0, PerEncoder.BitsNeeded(0));
        Assert.AreEqual(0, PerEncoder.BitsNeeded(-100));
        Assert.AreEqual(0, PerEncoder.BitsNeeded(1));
    }
}