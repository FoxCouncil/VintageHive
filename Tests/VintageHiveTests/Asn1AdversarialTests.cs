// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System;
using System.Text;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace Adversarial.Asn1;

// ----------------------------------------------------------
//  BerDecoder adversarial tests
//
//  Hostile BER: truncated TLV, oversized/malformed lengths,
//  wrong tags, length fields that run past the buffer, and
//  deeply nested framing. Asserts the decoder's real, observed
//  defensive behavior (throws InvalidOperationException) rather
//  than silently mis-parsing.
// ----------------------------------------------------------

[TestClass]
public class BerDecoderAdversarialTests
{
    [TestMethod]
    public void EmptyBuffer_ReadTag_Throws()
    {
        var dec = new BerDecoder(Array.Empty<byte>());
        Assert.IsFalse(dec.HasData);
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadTag());
    }

    [TestMethod]
    public void EmptyBuffer_IsNextTag_ReturnsFalse_NoThrow()
    {
        var dec = new BerDecoder(Array.Empty<byte>());
        Assert.IsFalse(dec.IsNextTag(BerTag.SEQUENCE));
        Assert.IsFalse(dec.IsNextTag(0x00));
    }

    [TestMethod]
    public void MultiByteTag_Truncated_ContinuationRunsOffEnd_Throws()
    {
        // 0xBF = context constructed, low 5 bits = 0x1F -> multi-byte tag follows.
        // 0x81 has the continuation bit set, but the buffer ends -> decoder must throw.
        var dec = new BerDecoder(new byte[] { 0xBF, 0x81 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadTag());
    }

    [TestMethod]
    public void MultiByteTag_ExtremeContinuation_OverflowsButDoesNotCrash()
    {
        // 0x1F marker (universal primitive) followed by many continuation bytes.
        // The internal tag-number accumulator (int) overflows via repeated <<7,
        // but this must terminate gracefully on the non-continuation byte (0x7F),
        // not hang or throw.
        var dec = new BerDecoder(new byte[] { 0x1F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F });
        var tag = dec.ReadTag();
        Assert.AreEqual(BerTag.CLASS_UNIVERSAL, tag.Class);
        Assert.IsFalse(dec.HasData); // all bytes consumed, terminated on 0x7F
    }

    [TestMethod]
    public void Length_FiveByteCount_Throws()
    {
        // Length prefix 0x85 claims a 5-byte length field; decoder caps at 4.
        var dec = new BerDecoder(new byte[] { 0x04, 0x85, 0x00, 0x00, 0x00, 0x00, 0x00 });
        dec.ReadTag();
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadLength());
    }

    [TestMethod]
    public void Length_LongForm_Truncated_Throws()
    {
        // 0x82 promises two length octets but only one is present.
        var dec = new BerDecoder(new byte[] { 0x30, 0x82, 0x01 });
        dec.ReadTag();
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadLength());
    }

    [TestMethod]
    public void OctetString_LengthPastEnd_Throws()
    {
        // Declares 5 value bytes, supplies 2.
        var dec = new BerDecoder(new byte[] { 0x04, 0x05, 0x41, 0x42 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOctetString());
    }

    [TestMethod]
    public void Sequence_LengthPastEnd_Throws()
    {
        // SEQUENCE claims 10 content bytes, only 1 present.
        var dec = new BerDecoder(new byte[] { 0x30, 0x0A, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadSequence());
    }

    [TestMethod]
    public void Skip_LengthPastEnd_Throws()
    {
        var dec = new BerDecoder(new byte[] { 0x04, 0x05, 0x41 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.Skip());
    }

    [TestMethod]
    public void ReadRawTlv_LengthPastEnd_Throws()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x7F, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadRawTlv());
    }

    [TestMethod]
    public void Integer_ZeroLength_Throws()
    {
        // INTEGER with a zero-length value is malformed; ReadIntegerValue rejects 0.
        var dec = new BerDecoder(new byte[] { 0x02, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadInteger());
    }

    [TestMethod]
    public void Integer_OversizedLength_Throws()
    {
        // INTEGER length 5 exceeds the 4-byte int cap.
        var dec = new BerDecoder(new byte[] { 0x02, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadInteger());
    }

    [TestMethod]
    public void Long_OversizedLength_Throws()
    {
        // INTEGER (read as long) length 9 exceeds the 8-byte cap.
        var data = new byte[] { 0x02, 0x09, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var dec = new BerDecoder(data);
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadLong());
    }

    [TestMethod]
    public void Boolean_WrongLength_Throws()
    {
        // BOOLEAN must be exactly one content octet.
        var dec = new BerDecoder(new byte[] { 0x01, 0x02, 0x00, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBoolean());

        var dec0 = new BerDecoder(new byte[] { 0x01, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec0.ReadBoolean());
    }

    [TestMethod]
    public void Null_NonZeroLength_Throws()
    {
        var dec = new BerDecoder(new byte[] { 0x05, 0x01, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadNull());
    }

    [TestMethod]
    public void Integer_WrongTag_Throws()
    {
        // A SEQUENCE where an INTEGER is expected.
        var dec = new BerDecoder(new byte[] { 0x30, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadInteger());
    }

    [TestMethod]
    public void Sequence_WrongTag_Throws()
    {
        // A SET (0x31) where a SEQUENCE (0x30) is expected.
        var dec = new BerDecoder(new byte[] { 0x31, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadSequence());
    }

    [TestMethod]
    public void ApplicationConstructed_WrongTagNumber_Throws()
    {
        // APPLICATION CONSTRUCTED 2 supplied, caller expects 1.
        var dec = new BerDecoder(new byte[] { 0x62, 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadApplicationConstructed(1));
    }

    [TestMethod]
    public void ContextPrimitive_LengthPastEnd_Throws()
    {
        // CONTEXT [0] primitive declares 6 bytes, none present.
        var dec = new BerDecoder(new byte[] { 0x80, 0x06 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadContextPrimitive(0));
    }

    [TestMethod]
    public void DeeplyNested_Truncated_Throws()
    {
        // Build a valid deeply nested chain, then truncate it so an inner
        // Slice runs past the buffer. Must throw, not silently return garbage.
        var enc = new BerEncoder();
        BuildNested(enc, 60);
        var full = enc.ToArray();
        var truncated = new byte[full.Length - 1];
        Array.Copy(full, truncated, truncated.Length);

        var dec = new BerDecoder(truncated);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            var cur = dec;
            for (var i = 0; i < 60; i++)
            {
                cur = cur.ReadSequence();
            }
        });
    }

    [TestMethod]
    public void DeeplyNested_Valid_DecodesWithoutStackOverflow()
    {
        // 200 levels of SEQUENCE nesting decoded iteratively - no crash, no hang.
        var enc = new BerEncoder();
        BuildNested(enc, 200);

        var dec = new BerDecoder(enc.ToArray());
        var cur = dec;
        for (var i = 0; i < 200; i++)
        {
            cur = cur.ReadSequence();
        }
        Assert.IsFalse(cur.HasData);
    }

    private static void BuildNested(BerEncoder enc, int depth)
    {
        if (depth == 0)
        {
            return;
        }
        enc.WriteSequence(inner => BuildNested(inner, depth - 1));
    }
}

// ----------------------------------------------------------
//  BerEncoder adversarial tests
// ----------------------------------------------------------

[TestClass]
public class BerEncoderAdversarialTests
{
    [TestMethod]
    public void WriteLength_Negative_Throws()
    {
        var enc = new BerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteLength(-1));
    }

    [TestMethod]
    public void WriteLength_IntMin_Throws()
    {
        var enc = new BerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteLength(int.MinValue));
    }

    [TestMethod]
    public void WriteTag_MultiByte_IntMaxTagNumber_DoesNotCrash()
    {
        // Adversarially large multi-byte tag number - must encode without overflow crash.
        var enc = new BerEncoder();
        enc.WriteTag(0xBF, int.MaxValue);
        var bytes = enc.ToArray();
        Assert.AreEqual(0xBF, bytes[0]);
        Assert.IsTrue(bytes.Length > 1);
        // Every continuation byte except the last must have the high bit set.
        for (var i = 1; i < bytes.Length - 1; i++)
        {
            Assert.IsTrue((bytes[i] & 0x80) != 0, $"byte {i} missing continuation bit");
        }
        Assert.IsTrue((bytes[^1] & 0x80) == 0, "final tag byte must not have continuation bit");
    }
}

// ----------------------------------------------------------
//  PerDecoder adversarial tests
//
//  Hostile PER: bit reads past the end, out-of-width bit
//  requests, fragmentation markers, malformed length
//  determinants, octet reads past the end, and inverted
//  constraint bounds.
// ----------------------------------------------------------

[TestClass]
public class PerDecoderAdversarialTests
{
    [TestMethod]
    public void ReadBits_CountAbove32_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dec.ReadBits(33));
    }

    [TestMethod]
    public void ReadBits_NegativeCount_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0xFF });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dec.ReadBits(-1));
    }

    [TestMethod]
    public void ReadBitsLong_CountAbove64_Throws()
    {
        var dec = new PerDecoder(new byte[16]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dec.ReadBitsLong(65));
    }

    [TestMethod]
    public void ReadBits_MaxWidth32_ReadsFullValue()
    {
        var dec = new PerDecoder(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.AreEqual(0xFFFFFFFFu, dec.ReadBits(32));
        Assert.IsFalse(dec.HasData);
    }

    [TestMethod]
    public void ReadBitsLong_MaxWidth64_ReadsFullValue()
    {
        var dec = new PerDecoder(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.AreEqual(ulong.MaxValue, dec.ReadBitsLong(64));
        Assert.IsFalse(dec.HasData);
    }

    [TestMethod]
    public void ReadBits_ZeroWidth_ReturnsZero_NoConsume()
    {
        var dec = new PerDecoder(Array.Empty<byte>());
        Assert.AreEqual(0u, dec.ReadBits(0));
        Assert.AreEqual(0, dec.BitPosition);
    }

    [TestMethod]
    public void ReadBits_PastEnd_Throws()
    {
        // 8 bits available, ask for 16.
        var dec = new PerDecoder(new byte[] { 0xAA });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBits(16));
    }

    [TestMethod]
    public void ReadOctets_PastEnd_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOctets(2));
    }

    [TestMethod]
    public void ReadOctets_Zero_ReturnsEmpty_NoThrow()
    {
        var dec = new PerDecoder(Array.Empty<byte>());
        var result = dec.ReadOctets(0);
        Assert.AreEqual(0, result.Length);
    }

    [TestMethod]
    public void ConstrainedWholeNumber_InvertedBounds_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0x00 });
        Assert.ThrowsExactly<ArgumentException>(() => dec.ReadConstrainedWholeNumber(10, 5));
    }

    [TestMethod]
    public void ConstrainedWholeNumber_RangeOne_ReturnsLb_NoConsume()
    {
        // Range of 1 encodes zero bits - reading it on an empty buffer must succeed.
        var dec = new PerDecoder(Array.Empty<byte>());
        Assert.AreEqual(7L, dec.ReadConstrainedWholeNumber(7, 7));
        Assert.AreEqual(0, dec.BitPosition);
    }

    [TestMethod]
    public void ConstrainedWholeNumber_TwoOctetRange_Truncated_Throws()
    {
        // Range 65536 -> aligned 2-octet read, but only 1 octet present.
        var dec = new PerDecoder(new byte[] { 0x12 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadConstrainedWholeNumber(0, 65535));
    }

    [TestMethod]
    public void LengthDeterminant_FragmentationMarker_Throws()
    {
        // First byte 0xC0 -> 11xxxxxx signals fragmentation, unsupported.
        var dec = new PerDecoder(new byte[] { 0xC0 });
        Assert.ThrowsExactly<NotSupportedException>(() => dec.ReadLengthDeterminant());
    }

    [TestMethod]
    public void LengthDeterminant_LongForm_Truncated_Throws()
    {
        // 0x80 -> two-byte form, but the second byte is missing.
        var dec = new PerDecoder(new byte[] { 0x80 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadLengthDeterminant());
    }

    [TestMethod]
    public void OctetString_FixedSize_LargerThanBuffer_Throws()
    {
        // Fixed 10-octet OCTET STRING against a 1-byte buffer.
        var dec = new PerDecoder(new byte[] { 0x00 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOctetString(lb: 10, ub: 10));
    }

    [TestMethod]
    public void OctetString_Unconstrained_LengthPastEnd_Throws()
    {
        // Length determinant says 5 octets, but none follow.
        var dec = new PerDecoder(new byte[] { 0x05 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadOctetString());
    }

    [TestMethod]
    public void IA5String_Unconstrained_Truncated_Throws()
    {
        // Length 4 declared, only partial character data present.
        var dec = new PerDecoder(new byte[] { 0x04, 0x41 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadIA5String());
    }

    [TestMethod]
    public void BMPString_Unconstrained_Truncated_Throws()
    {
        // Length 2 (chars) -> 32 bits needed, only 8 present after the length octet.
        var dec = new PerDecoder(new byte[] { 0x02, 0x41 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBMPString());
    }

    [TestMethod]
    public void ObjectIdentifier_Truncated_Throws()
    {
        // Declares 5 content octets, supplies 1.
        var dec = new PerDecoder(new byte[] { 0x05, 0x2A });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadObjectIdentifier());
    }

    [TestMethod]
    public void ObjectIdentifier_TrailingContinuationBit_TerminatesGracefully()
    {
        // Content octet 0x80 has the base-128 continuation bit set but is the last
        // byte. The OID sub-identifier loop is bounded by the buffer, so it must
        // terminate (not hang) and produce a value.
        var dec = new PerDecoder(new byte[] { 0x02, 0x81, 0x80 });
        var oid = dec.ReadObjectIdentifier();
        Assert.AreEqual(3, oid.Length); // two from first octet + one accumulated component
    }

    [TestMethod]
    public void NormallySmallNumber_LargePath_Truncated_Throws()
    {
        // 0x80: first bit = 1 (large form) -> semi-constrained encoding follows,
        // but the buffer is exhausted after the marker byte.
        var dec = new PerDecoder(new byte[] { 0x80 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadNormallySmallNumber());
    }

    [TestMethod]
    public void ReadBit_PastEnd_Throws()
    {
        var dec = new PerDecoder(Array.Empty<byte>());
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBit());
    }

    [TestMethod]
    public void UnconstrainedWholeNumber_Truncated_Throws()
    {
        // Length determinant 4, but no content octets follow.
        var dec = new PerDecoder(new byte[] { 0x04 });
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadUnconstrainedWholeNumber());
    }
}

// ----------------------------------------------------------
//  PerEncoder adversarial tests
// ----------------------------------------------------------

[TestClass]
public class PerEncoderAdversarialTests
{
    [TestMethod]
    public void WriteBits_CountAbove32_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteBits(0, 33));
    }

    [TestMethod]
    public void WriteBitsLong_CountAbove64_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteBitsLong(0, 65));
    }

    [TestMethod]
    public void WriteConstrainedWholeNumber_InvertedBounds_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentException>(() => enc.WriteConstrainedWholeNumber(0, 10, 5));
    }

    [TestMethod]
    public void WriteConstrainedWholeNumber_ValueAboveUpper_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteConstrainedWholeNumber(10, 0, 5));
    }

    [TestMethod]
    public void WriteConstrainedWholeNumber_ValueBelowLower_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => enc.WriteConstrainedWholeNumber(-1, 0, 5));
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
    public void WriteObjectIdentifier_NegativeComponent_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentException>(() => enc.WriteObjectIdentifier(new[] { 1, 2, -5 }));
    }

    [TestMethod]
    public void WriteOctets_WhenNotAligned_Throws()
    {
        // Writing raw octets while a partial byte is pending violates alignment.
        var enc = new PerEncoder();
        enc.WriteBit(true); // 1 bit -> not octet aligned
        Assert.ThrowsExactly<InvalidOperationException>(() => enc.WriteOctets(new byte[] { 0x00 }));
    }
}