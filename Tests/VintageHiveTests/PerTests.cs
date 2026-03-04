// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  Bit-level operation tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerBitTests
{
    [TestMethod]
    public void WriteBit_True_SetsMsb()
    {
        var enc = new PerEncoder();
        enc.WriteBit(true);
        // 1 bit written → partial byte 0x80, padded to 1 byte
        var bytes = enc.ToArray();
        Assert.AreEqual(1, bytes.Length);
        Assert.AreEqual(0x80, bytes[0]);
    }

    [TestMethod]
    public void WriteBit_False_LeavesZero()
    {
        var enc = new PerEncoder();
        enc.WriteBit(false);
        var bytes = enc.ToArray();
        Assert.AreEqual(1, bytes.Length);
        Assert.AreEqual(0x00, bytes[0]);
    }

    [TestMethod]
    public void WriteBits_8Bits_ProducesFullByte()
    {
        var enc = new PerEncoder();
        enc.WriteBits(0xA5, 8); // 10100101
        var bytes = enc.ToArray();
        Assert.AreEqual(1, bytes.Length);
        Assert.AreEqual(0xA5, bytes[0]);
    }

    [TestMethod]
    public void WriteBits_3Bits_CorrectPosition()
    {
        var enc = new PerEncoder();
        enc.WriteBits(0x05, 3); // 101
        // Should be 10100000 = 0xA0
        var bytes = enc.ToArray();
        Assert.AreEqual(0xA0, bytes[0]);
    }

    [TestMethod]
    public void WriteBits_MixedBits_SpanningBytes()
    {
        var enc = new PerEncoder();
        enc.WriteBits(0x07, 3); // 111
        enc.WriteBits(0x00, 5); // 00000
        // First byte: 11100000 = 0xE0
        enc.WriteBits(0xFF, 8); // 11111111
        // Second byte: 0xFF
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        Assert.AreEqual(0xE0, bytes[0]);
        Assert.AreEqual(0xFF, bytes[1]);
    }

    [TestMethod]
    public void AlignToOctet_PadsCorrectly()
    {
        var enc = new PerEncoder();
        enc.WriteBit(true);     // 1 bit used
        enc.AlignToOctet();     // should pad 7 zero bits
        Assert.AreEqual(8, enc.TotalBits);
        Assert.AreEqual(0, enc.BitOffset);
        enc.WriteBits(0x42, 8);
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        Assert.AreEqual(0x80, bytes[0]); // 10000000
        Assert.AreEqual(0x42, bytes[1]);
    }

    [TestMethod]
    public void AlignToOctet_WhenAligned_DoesNothing()
    {
        var enc = new PerEncoder();
        enc.WriteBits(0xFF, 8);
        Assert.AreEqual(0, enc.BitOffset);
        enc.AlignToOctet();
        Assert.AreEqual(8, enc.TotalBits); // unchanged
    }

    [TestMethod]
    public void ReadBit_ReadsCorrectly()
    {
        var dec = new PerDecoder(new byte[] { 0xA5 }); // 10100101
        Assert.IsTrue(dec.ReadBit());   // 1
        Assert.IsFalse(dec.ReadBit());  // 0
        Assert.IsTrue(dec.ReadBit());   // 1
        Assert.IsFalse(dec.ReadBit());  // 0
        Assert.IsFalse(dec.ReadBit());  // 0
        Assert.IsTrue(dec.ReadBit());   // 1
        Assert.IsFalse(dec.ReadBit());  // 0
        Assert.IsTrue(dec.ReadBit());   // 1
        Assert.IsFalse(dec.HasData);
    }

    [TestMethod]
    public void ReadBits_ReturnsCorrectValue()
    {
        var dec = new PerDecoder(new byte[] { 0xA5 }); // 10100101
        Assert.AreEqual(5u, dec.ReadBits(3)); // 101 = 5
        Assert.AreEqual(0u, dec.ReadBits(2)); // 00 = 0
        Assert.AreEqual(5u, dec.ReadBits(3)); // 101 = 5
    }

    [TestMethod]
    public void ReadBits_16Bits_SpanningBytes()
    {
        var dec = new PerDecoder(new byte[] { 0x12, 0x34 });
        Assert.AreEqual(0x1234u, dec.ReadBits(16));
    }

    [TestMethod]
    public void ReadOctets_Aligned_DirectCopy()
    {
        var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var dec = new PerDecoder(data);
        var result = dec.ReadOctets(4);
        CollectionAssert.AreEqual(data, result);
        Assert.IsFalse(dec.HasData);
    }

    [TestMethod]
    public void ReadOctets_Unaligned_BitByBit()
    {
        // Skip 4 bits, then read 2 octets from unaligned position
        var dec = new PerDecoder(new byte[] { 0xAB, 0xCD, 0xE0 }); // 10101011 11001101 11100000
        dec.ReadBits(4); // skip 1010
        // Remaining bits: 1011 11001101 11100000
        var result = dec.ReadOctets(2);
        // Should read: 10111100 11011110 = 0xBC, 0xDE
        Assert.AreEqual(0xBC, result[0]);
        Assert.AreEqual(0xDE, result[1]);
    }

    [TestMethod]
    public void AlignToOctet_Decoder_SkipsPadding()
    {
        var dec = new PerDecoder(new byte[] { 0x80, 0x42 });
        dec.ReadBit(); // read the 1 bit
        Assert.AreEqual(1, dec.BitPosition);
        dec.AlignToOctet();
        Assert.AreEqual(8, dec.BitPosition);
        Assert.AreEqual(0x42u, dec.ReadBits(8));
    }

    [TestMethod]
    public void ReadBit_BeyondEnd_Throws()
    {
        var dec = new PerDecoder(new byte[] { 0xFF });
        for (var i = 0; i < 8; i++)
        {
            dec.ReadBit();
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBit());
    }

    [TestMethod]
    public void BitRoundTrip_MixedBits()
    {
        var enc = new PerEncoder();
        enc.WriteBit(true);
        enc.WriteBits(0x0A, 4); // 1010
        enc.WriteBit(false);
        enc.WriteBits(0x03, 2); // 11
        // Total: 1 1010 0 11 = 11010011 = 0xD3
        var bytes = enc.ToArray();
        Assert.AreEqual(1, bytes.Length);
        Assert.AreEqual(0xD3, bytes[0]);

        var dec = new PerDecoder(bytes);
        Assert.IsTrue(dec.ReadBit());
        Assert.AreEqual(0x0Au, dec.ReadBits(4));
        Assert.IsFalse(dec.ReadBit());
        Assert.AreEqual(0x03u, dec.ReadBits(2));
    }
}

// ──────────────────────────────────────────────────────────
//  Constrained whole number tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerConstrainedWholeNumberTests
{
    [TestMethod]
    public void Range1_NoEncoding()
    {
        // Range = 1: value is predetermined, 0 bits
        var enc = new PerEncoder();
        enc.WriteConstrainedWholeNumber(5, 5, 5);
        Assert.AreEqual(0, enc.TotalBits);
    }

    [TestMethod]
    public void Range2_OneBit()
    {
        // Range = 2: 1 bit (like BOOLEAN)
        var enc = new PerEncoder();
        enc.WriteConstrainedWholeNumber(0, 0, 1);
        enc.WriteConstrainedWholeNumber(1, 0, 1);
        var bytes = enc.ToArray();
        // 01 = 0x40
        Assert.AreEqual(0x40, bytes[0]);
    }

    [TestMethod]
    public void Range4_TwoBits()
    {
        // Range = 4: 2 bits
        var enc = new PerEncoder();
        enc.WriteConstrainedWholeNumber(0, 0, 3); // 00
        enc.WriteConstrainedWholeNumber(3, 0, 3); // 11
        enc.WriteConstrainedWholeNumber(2, 0, 3); // 10
        enc.WriteConstrainedWholeNumber(1, 0, 3); // 01
        // 00 11 10 01 = 0x39
        var bytes = enc.ToArray();
        Assert.AreEqual(0x39, bytes[0]);
    }

    [TestMethod]
    public void Range8_ThreeBits()
    {
        // INTEGER (0..7), range 8, 3 bits
        var enc = new PerEncoder();
        enc.WriteConstrainedWholeNumber(5, 0, 7); // 101
        Assert.AreEqual(3, enc.TotalBits);
    }

    [TestMethod]
    public void Range255_8Bits_NotAligned()
    {
        // Range = 255: 8 bits, NOT octet-aligned
        var enc = new PerEncoder();
        enc.WriteBit(true); // 1 bit offset
        enc.WriteConstrainedWholeNumber(200, 0, 254); // 8 bits for 200 = 11001000
        // Total: 1 + 8 = 9 bits → 2 bytes
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        // 1 11001000 0 = 0xE4 0x00
        Assert.AreEqual(0xE4, bytes[0]);
        Assert.AreEqual(0x00, bytes[1]);
    }

    [TestMethod]
    public void Range256_OneOctet_Aligned()
    {
        // Range = 256: 1 octet, octet-aligned
        var enc = new PerEncoder();
        enc.WriteBit(true); // offset by 1
        enc.WriteConstrainedWholeNumber(200, 0, 255);
        // Align: pad 7 bits, then write 0xC8
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        Assert.AreEqual(0x80, bytes[0]); // 1 + 7 pad zeros
        Assert.AreEqual(0xC8, bytes[1]); // 200
    }

    [TestMethod]
    public void Range65536_TwoOctets_Aligned()
    {
        // Range = 65536: 2 octets, octet-aligned
        var enc = new PerEncoder();
        enc.WriteConstrainedWholeNumber(1000, 0, 65535);
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        Assert.AreEqual(0x03, bytes[0]); // 1000 >> 8
        Assert.AreEqual(0xE8, bytes[1]); // 1000 & 0xFF
    }

    [TestMethod]
    public void OffsetFromLowerBound()
    {
        // INTEGER (100..200), value 150 → offset = 50
        // Range = 101, BitsNeeded(101) = 7
        var enc = new PerEncoder();
        enc.WriteConstrainedWholeNumber(150, 100, 200);
        Assert.AreEqual(7, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(150, dec.ReadConstrainedWholeNumber(100, 200));
    }

    [TestMethod]
    public void LargeRange_LengthDeterminant()
    {
        // Range > 65536
        var enc = new PerEncoder();
        enc.WriteConstrainedWholeNumber(100000, 0, 1000000);
        var bytes = enc.ToArray();

        var dec = new PerDecoder(bytes);
        Assert.AreEqual(100000, dec.ReadConstrainedWholeNumber(0, 1000000));
    }

    [TestMethod]
    public void OutOfRange_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            enc.WriteConstrainedWholeNumber(10, 0, 5));
    }

    [TestMethod]
    public void RoundTrip_VariousRanges()
    {
        var testCases = new (long value, long lb, long ub)[]
        {
            (0, 0, 1),          // range 2
            (1, 0, 1),
            (3, 0, 7),          // range 8
            (0, 0, 127),        // range 128
            (127, 0, 127),
            (0, 0, 255),        // range 256
            (255, 0, 255),
            (0, 0, 65535),      // range 65536
            (65535, 0, 65535),
            (1000, 0, 65535),
            (5, 5, 5),          // range 1
            (50, 10, 100),      // offset encoding
        };

        foreach (var (value, lb, ub) in testCases)
        {
            var enc = new PerEncoder();
            enc.WriteConstrainedWholeNumber(value, lb, ub);
            var dec = new PerDecoder(enc.ToArray());
            Assert.AreEqual(value, dec.ReadConstrainedWholeNumber(lb, ub),
                $"Round-trip failed: value={value}, lb={lb}, ub={ub}");
        }
    }
}

// ──────────────────────────────────────────────────────────
//  Length determinant tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerLengthDeterminantTests
{
    [TestMethod]
    public void ShortForm_0()
    {
        var enc = new PerEncoder();
        enc.WriteLengthDeterminant(0);
        var bytes = enc.ToArray();
        Assert.AreEqual(1, bytes.Length);
        Assert.AreEqual(0x00, bytes[0]);
    }

    [TestMethod]
    public void ShortForm_127()
    {
        var enc = new PerEncoder();
        enc.WriteLengthDeterminant(127);
        var bytes = enc.ToArray();
        Assert.AreEqual(1, bytes.Length);
        Assert.AreEqual(0x7F, bytes[0]);
    }

    [TestMethod]
    public void LongForm_128()
    {
        var enc = new PerEncoder();
        enc.WriteLengthDeterminant(128);
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        Assert.AreEqual(0x80, bytes[0]); // 10 000000
        Assert.AreEqual(0x80, bytes[1]); // 10000000
    }

    [TestMethod]
    public void LongForm_200()
    {
        var enc = new PerEncoder();
        enc.WriteLengthDeterminant(200);
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        // 200 = 0xC8 → 10 000000 11001000
        Assert.AreEqual(0x80, bytes[0]);
        Assert.AreEqual(0xC8, bytes[1]);
    }

    [TestMethod]
    public void LongForm_16000()
    {
        var enc = new PerEncoder();
        enc.WriteLengthDeterminant(16000);
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        // 16000 = 0x3E80 → 10 111110 10000000
        Assert.AreEqual(0xBE, bytes[0]);
        Assert.AreEqual(0x80, bytes[1]);
    }

    [TestMethod]
    public void LongForm_16383()
    {
        var enc = new PerEncoder();
        enc.WriteLengthDeterminant(16383);
        var bytes = enc.ToArray();
        Assert.AreEqual(2, bytes.Length);
        // 16383 = 0x3FFF → 10 111111 11111111
        Assert.AreEqual(0xBF, bytes[0]);
        Assert.AreEqual(0xFF, bytes[1]);
    }

    [TestMethod]
    public void Fragmentation_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<NotSupportedException>(() =>
            enc.WriteLengthDeterminant(16384));
    }

    [TestMethod]
    public void RoundTrip_Lengths()
    {
        int[] lengths = { 0, 1, 50, 127, 128, 200, 1000, 5000, 16000, 16383 };

        foreach (var expected in lengths)
        {
            var enc = new PerEncoder();
            enc.WriteLengthDeterminant(expected);
            var dec = new PerDecoder(enc.ToArray());
            Assert.AreEqual(expected, dec.ReadLengthDeterminant(),
                $"Round-trip failed for length {expected}");
        }
    }

    [TestMethod]
    public void NormallySmallNumber_Small()
    {
        var enc = new PerEncoder();
        enc.WriteNormallySmallNumber(0);
        // 0 (small flag) + 000000 = 0000000 = 7 bits
        Assert.AreEqual(7, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(0, dec.ReadNormallySmallNumber());
    }

    [TestMethod]
    public void NormallySmallNumber_63()
    {
        var enc = new PerEncoder();
        enc.WriteNormallySmallNumber(63);
        Assert.AreEqual(7, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(63, dec.ReadNormallySmallNumber());
    }

    [TestMethod]
    public void NormallySmallNumber_Large_64()
    {
        var enc = new PerEncoder();
        enc.WriteNormallySmallNumber(64);
        // 1 (large flag) → semi-constrained encoding follows

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(64, dec.ReadNormallySmallNumber());
    }

    [TestMethod]
    public void NormallySmallNumber_RoundTrip()
    {
        int[] values = { 0, 1, 30, 63, 64, 100, 255, 1000 };

        foreach (var expected in values)
        {
            var enc = new PerEncoder();
            enc.WriteNormallySmallNumber(expected);
            var dec = new PerDecoder(enc.ToArray());
            Assert.AreEqual(expected, dec.ReadNormallySmallNumber(),
                $"Round-trip failed for {expected}");
        }
    }
}

// ──────────────────────────────────────────────────────────
//  Semi-constrained and unconstrained whole number tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerWholeNumberTests
{
    [TestMethod]
    public void SemiConstrained_Zero()
    {
        var enc = new PerEncoder();
        enc.WriteSemiConstrainedWholeNumber(0, 0);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(0L, dec.ReadSemiConstrainedWholeNumber(0));
    }

    [TestMethod]
    public void SemiConstrained_WithOffset()
    {
        var enc = new PerEncoder();
        enc.WriteSemiConstrainedWholeNumber(150, 100);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(150L, dec.ReadSemiConstrainedWholeNumber(100));
    }

    [TestMethod]
    public void SemiConstrained_LargeValue()
    {
        var enc = new PerEncoder();
        enc.WriteSemiConstrainedWholeNumber(100000, 0);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(100000L, dec.ReadSemiConstrainedWholeNumber(0));
    }

    [TestMethod]
    public void Unconstrained_Zero()
    {
        var enc = new PerEncoder();
        enc.WriteUnconstrainedWholeNumber(0);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(0L, dec.ReadUnconstrainedWholeNumber());
    }

    [TestMethod]
    public void Unconstrained_Positive()
    {
        var enc = new PerEncoder();
        enc.WriteUnconstrainedWholeNumber(42);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(42L, dec.ReadUnconstrainedWholeNumber());
    }

    [TestMethod]
    public void Unconstrained_Negative()
    {
        var enc = new PerEncoder();
        enc.WriteUnconstrainedWholeNumber(-1);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(-1L, dec.ReadUnconstrainedWholeNumber());
    }

    [TestMethod]
    public void Unconstrained_RoundTrip()
    {
        long[] values = { 0, 1, -1, 127, 128, -128, -129, 32767, -32768, 100000, -100000 };

        foreach (var expected in values)
        {
            var enc = new PerEncoder();
            enc.WriteUnconstrainedWholeNumber(expected);
            var dec = new PerDecoder(enc.ToArray());
            Assert.AreEqual(expected, dec.ReadUnconstrainedWholeNumber(),
                $"Round-trip failed for {expected}");
        }
    }
}

// ──────────────────────────────────────────────────────────
//  Boolean and Enumerated tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerBoolEnumTests
{
    [TestMethod]
    public void Boolean_True()
    {
        var enc = new PerEncoder();
        enc.WriteBoolean(true);
        var dec = new PerDecoder(enc.ToArray());
        Assert.IsTrue(dec.ReadBoolean());
    }

    [TestMethod]
    public void Boolean_False()
    {
        var enc = new PerEncoder();
        enc.WriteBoolean(false);
        var dec = new PerDecoder(enc.ToArray());
        Assert.IsFalse(dec.ReadBoolean());
    }

    [TestMethod]
    public void Boolean_MultiplePacked()
    {
        var enc = new PerEncoder();
        enc.WriteBoolean(true);
        enc.WriteBoolean(false);
        enc.WriteBoolean(true);
        enc.WriteBoolean(true);
        // 1011 0000 = 0xB0
        var bytes = enc.ToArray();
        Assert.AreEqual(0xB0, bytes[0]);

        var dec = new PerDecoder(bytes);
        Assert.IsTrue(dec.ReadBoolean());
        Assert.IsFalse(dec.ReadBoolean());
        Assert.IsTrue(dec.ReadBoolean());
        Assert.IsTrue(dec.ReadBoolean());
    }

    [TestMethod]
    public void Enumerated_NonExtensible()
    {
        // 5 root values (0-4), select index 3
        var enc = new PerEncoder();
        enc.WriteEnumerated(3, 5);
        // Range = 5, BitsNeeded(5) = 3, value 3 = 011
        Assert.AreEqual(3, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(3, dec.ReadEnumerated(5));
    }

    [TestMethod]
    public void Enumerated_Extensible_Root()
    {
        var enc = new PerEncoder();
        enc.WriteEnumerated(2, 4, extensible: true, isExtension: false);
        // Extension bit (0) + constrained(2, 0..3) = 0 + 10 = 3 bits

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(2, dec.ReadEnumerated(4, extensible: true));
    }

    [TestMethod]
    public void Enumerated_Extensible_Extension()
    {
        var enc = new PerEncoder();
        enc.WriteEnumerated(5, 4, extensible: true, isExtension: true);
        // Extension bit (1) + normally-small(5) = 1 + 0 000101 = 8 bits

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(5, dec.ReadEnumerated(4, extensible: true));
    }

    [TestMethod]
    public void Enumerated_RoundTrip()
    {
        for (var i = 0; i < 8; i++)
        {
            var enc = new PerEncoder();
            enc.WriteEnumerated(i, 8);
            var dec = new PerDecoder(enc.ToArray());
            Assert.AreEqual(i, dec.ReadEnumerated(8));
        }
    }
}

// ──────────────────────────────────────────────────────────
//  OCTET STRING tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerOctetStringTests
{
    [TestMethod]
    public void FixedSize_Empty()
    {
        var enc = new PerEncoder();
        enc.WriteOctetString(Array.Empty<byte>(), lb: 0, ub: 0);
        Assert.AreEqual(0, enc.TotalBits);
    }

    [TestMethod]
    public void FixedSize_1Byte()
    {
        var enc = new PerEncoder();
        enc.WriteOctetString(new byte[] { 0xAB }, lb: 1, ub: 1);
        // Fixed 1 byte (<=2): NOT octet-aligned
        Assert.AreEqual(8, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0xAB }, dec.ReadOctetString(lb: 1, ub: 1));
    }

    [TestMethod]
    public void FixedSize_2Bytes()
    {
        var enc = new PerEncoder();
        enc.WriteOctetString(new byte[] { 0xAB, 0xCD }, lb: 2, ub: 2);
        // Fixed 2 bytes (<=2): NOT octet-aligned
        Assert.AreEqual(16, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD }, dec.ReadOctetString(lb: 2, ub: 2));
    }

    [TestMethod]
    public void FixedSize_4Bytes_OctetAligned()
    {
        var enc = new PerEncoder();
        enc.WriteBit(true); // offset
        enc.WriteOctetString(new byte[] { 0x01, 0x02, 0x03, 0x04 }, lb: 4, ub: 4);
        // Fixed >=3: octet-aligned, no length
        var bytes = enc.ToArray();
        Assert.AreEqual(5, bytes.Length);
        Assert.AreEqual(0x80, bytes[0]);
        Assert.AreEqual(0x01, bytes[1]);
        Assert.AreEqual(0x02, bytes[2]);
        Assert.AreEqual(0x03, bytes[3]);
        Assert.AreEqual(0x04, bytes[4]);
    }

    [TestMethod]
    public void Constrained_Variable()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C }; // "Hel"
        var enc = new PerEncoder();
        enc.WriteOctetString(data, lb: 1, ub: 10);

        var dec = new PerDecoder(enc.ToArray());
        CollectionAssert.AreEqual(data, dec.ReadOctetString(lb: 1, ub: 10));
    }

    [TestMethod]
    public void Unconstrained()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var enc = new PerEncoder();
        enc.WriteOctetString(data);

        var dec = new PerDecoder(enc.ToArray());
        CollectionAssert.AreEqual(data, dec.ReadOctetString());
    }

    [TestMethod]
    public void Unconstrained_Empty()
    {
        var enc = new PerEncoder();
        enc.WriteOctetString(Array.Empty<byte>());

        var dec = new PerDecoder(enc.ToArray());
        CollectionAssert.AreEqual(Array.Empty<byte>(), dec.ReadOctetString());
    }

    [TestMethod]
    public void Unconstrained_Large()
    {
        var data = new byte[200];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i & 0xFF);
        }

        var enc = new PerEncoder();
        enc.WriteOctetString(data);

        var dec = new PerDecoder(enc.ToArray());
        CollectionAssert.AreEqual(data, dec.ReadOctetString());
    }
}

// ──────────────────────────────────────────────────────────
//  BIT STRING tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerBitStringTests
{
    [TestMethod]
    public void FixedSize_8Bits()
    {
        var enc = new PerEncoder();
        enc.WriteBitString(new byte[] { 0xA5 }, 8, lb: 8, ub: 8);
        // Fixed 8 bits (<=16): NOT octet-aligned
        Assert.AreEqual(8, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        var (data, bitCount) = dec.ReadBitString(lb: 8, ub: 8);
        Assert.AreEqual(8, bitCount);
        Assert.AreEqual(0xA5, data[0]);
    }

    [TestMethod]
    public void FixedSize_3Bits()
    {
        // 3-bit BIT STRING: value 101 → byte[0] = 0xA0
        var enc = new PerEncoder();
        enc.WriteBitString(new byte[] { 0xA0 }, 3, lb: 3, ub: 3);
        Assert.AreEqual(3, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        var (data, bitCount) = dec.ReadBitString(lb: 3, ub: 3);
        Assert.AreEqual(3, bitCount);
        // Top 3 bits should be 101
        Assert.IsTrue((data[0] & 0x80) != 0); // 1
        Assert.IsFalse((data[0] & 0x40) != 0); // 0
        Assert.IsTrue((data[0] & 0x20) != 0); // 1
    }

    [TestMethod]
    public void FixedSize_32Bits_Aligned()
    {
        var value = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var enc = new PerEncoder();
        enc.WriteBitString(value, 32, lb: 32, ub: 32);
        // Fixed > 16: octet-aligned

        var dec = new PerDecoder(enc.ToArray());
        var (data, bitCount) = dec.ReadBitString(lb: 32, ub: 32);
        Assert.AreEqual(32, bitCount);
        CollectionAssert.AreEqual(value, data);
    }

    [TestMethod]
    public void Unconstrained()
    {
        var value = new byte[] { 0xAB, 0xCD };
        var enc = new PerEncoder();
        enc.WriteBitString(value, 16);

        var dec = new PerDecoder(enc.ToArray());
        var (data, bitCount) = dec.ReadBitString();
        Assert.AreEqual(16, bitCount);
        CollectionAssert.AreEqual(value, data);
    }
}

// ──────────────────────────────────────────────────────────
//  OBJECT IDENTIFIER tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerObjectIdentifierTests
{
    [TestMethod]
    public void H225_OID()
    {
        // H.225.0 v1: {0 0 8 2250 0 1}
        var oid = new int[] { 0, 0, 8, 2250, 0, 1 };
        var enc = new PerEncoder();
        enc.WriteObjectIdentifier(oid);

        var dec = new PerDecoder(enc.ToArray());
        var result = dec.ReadObjectIdentifier();
        CollectionAssert.AreEqual(oid, result);
    }

    [TestMethod]
    public void SimpleOID()
    {
        // {1 2 3 4}
        var oid = new int[] { 1, 2, 3, 4 };
        var enc = new PerEncoder();
        enc.WriteObjectIdentifier(oid);

        var dec = new PerDecoder(enc.ToArray());
        CollectionAssert.AreEqual(oid, dec.ReadObjectIdentifier());
    }

    [TestMethod]
    public void RSA_OID()
    {
        // RSA: {1 2 840 113549 1 1 1}
        var oid = new int[] { 1, 2, 840, 113549, 1, 1, 1 };
        var enc = new PerEncoder();
        enc.WriteObjectIdentifier(oid);

        var dec = new PerDecoder(enc.ToArray());
        CollectionAssert.AreEqual(oid, dec.ReadObjectIdentifier());
    }

    [TestMethod]
    public void KnownBerEncoding_H225()
    {
        // Verify the BER content bytes for H.225.0 OID: {0 0 8 2250 0 1}
        // First two: 0*40 + 0 = 0x00
        // 8 → 0x08
        // 2250 = 17*128 + 74 → 0x91, 0x4A
        // 0 → 0x00
        // 1 → 0x01
        var enc = new PerEncoder();
        enc.WriteObjectIdentifier(new int[] { 0, 0, 8, 2250, 0, 1 });
        var bytes = enc.ToArray();

        // Length determinant = 6 bytes (short form: 0x06)
        Assert.AreEqual(0x06, bytes[0]);
        // BER content:
        Assert.AreEqual(0x00, bytes[1]); // 0*40+0
        Assert.AreEqual(0x08, bytes[2]); // 8
        Assert.AreEqual(0x91, bytes[3]); // 2250 high
        Assert.AreEqual(0x4A, bytes[4]); // 2250 low
        Assert.AreEqual(0x00, bytes[5]); // 0
        Assert.AreEqual(0x01, bytes[6]); // 1
    }

    [TestMethod]
    public void TooFewComponents_Throws()
    {
        var enc = new PerEncoder();
        Assert.ThrowsExactly<ArgumentException>(() =>
            enc.WriteObjectIdentifier(new int[] { 1 }));
    }
}

// ──────────────────────────────────────────────────────────
//  String tests (IA5String, BMPString)
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerStringTests
{
    [TestMethod]
    public void IA5String_FixedSize()
    {
        var enc = new PerEncoder();
        enc.WriteIA5String("Hello", lb: 5, ub: 5);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual("Hello", dec.ReadIA5String(lb: 5, ub: 5));
    }

    [TestMethod]
    public void IA5String_Constrained()
    {
        var enc = new PerEncoder();
        enc.WriteIA5String("Hi", lb: 1, ub: 20);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual("Hi", dec.ReadIA5String(lb: 1, ub: 20));
    }

    [TestMethod]
    public void IA5String_Unconstrained()
    {
        var enc = new PerEncoder();
        enc.WriteIA5String("Test string");

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual("Test string", dec.ReadIA5String());
    }

    [TestMethod]
    public void IA5String_Empty_Unconstrained()
    {
        var enc = new PerEncoder();
        enc.WriteIA5String("");

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual("", dec.ReadIA5String());
    }

    [TestMethod]
    public void BMPString_FixedSize()
    {
        var enc = new PerEncoder();
        enc.WriteBMPString("AB", lb: 2, ub: 2);
        // 2 chars × 16 bits = 32 bits = 4 bytes (+ alignment)

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual("AB", dec.ReadBMPString(lb: 2, ub: 2));
    }

    [TestMethod]
    public void BMPString_Constrained()
    {
        var enc = new PerEncoder();
        enc.WriteBMPString("Hello", lb: 1, ub: 256);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual("Hello", dec.ReadBMPString(lb: 1, ub: 256));
    }

    [TestMethod]
    public void BMPString_Unicode()
    {
        // BMPString with Unicode characters
        var value = "\u00C9mile"; // "Émile"
        var enc = new PerEncoder();
        enc.WriteBMPString(value, lb: 1, ub: 100);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(value, dec.ReadBMPString(lb: 1, ub: 100));
    }
}

// ──────────────────────────────────────────────────────────
//  SEQUENCE / CHOICE helper tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerSequenceChoiceTests
{
    [TestMethod]
    public void ExtensionBit_False()
    {
        var enc = new PerEncoder();
        enc.WriteExtensionBit(false);
        Assert.AreEqual(1, enc.TotalBits);

        var dec = new PerDecoder(enc.ToArray());
        Assert.IsFalse(dec.ReadExtensionBit());
    }

    [TestMethod]
    public void ExtensionBit_True()
    {
        var enc = new PerEncoder();
        enc.WriteExtensionBit(true);

        var dec = new PerDecoder(enc.ToArray());
        Assert.IsTrue(dec.ReadExtensionBit());
    }

    [TestMethod]
    public void OptionalBitmap()
    {
        var enc = new PerEncoder();
        enc.WriteOptionalBitmap(true, false, true, true, false);
        Assert.AreEqual(5, enc.TotalBits);
        // 10110 000 = 0xB0
        Assert.AreEqual(0xB0, enc.ToArray()[0]);

        var dec = new PerDecoder(enc.ToArray());
        var bitmap = dec.ReadOptionalBitmap(5);
        Assert.IsTrue(bitmap[0]);
        Assert.IsFalse(bitmap[1]);
        Assert.IsTrue(bitmap[2]);
        Assert.IsTrue(bitmap[3]);
        Assert.IsFalse(bitmap[4]);
    }

    [TestMethod]
    public void ChoiceIndex_NonExtensible()
    {
        // 5 alternatives, select index 3
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(3, 5);
        // BitsNeeded(5) = 3, value 3 = 011

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(3, dec.ReadChoiceIndex(5));
    }

    [TestMethod]
    public void ChoiceIndex_Extensible_Root()
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(2, 4, extensible: true);
        // Ext bit (0) + constrained(2, 0..3) = 0 + 10 = 3 bits

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(2, dec.ReadChoiceIndex(4, extensible: true));
    }

    [TestMethod]
    public void ChoiceIndex_Extensible_Extension()
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(5, 4, extensible: true, isExtension: true);
        // Ext bit (1) + normally-small(5) = 1 + 0 000101

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(5, dec.ReadChoiceIndex(4, extensible: true));
    }

    [TestMethod]
    public void OpenType_RoundTrip()
    {
        var enc = new PerEncoder();
        enc.WriteOpenType(inner =>
        {
            inner.WriteConstrainedWholeNumber(42, 0, 255);
            inner.AlignToOctet();
        });

        var dec = new PerDecoder(enc.ToArray());
        var openData = dec.ReadOpenType();
        Assert.AreEqual(1, openData.Length);
        Assert.AreEqual(42, openData[0]);
    }

    [TestMethod]
    public void ExtensionAdditions_AllPresent()
    {
        var enc = new PerEncoder();
        enc.WriteExtensionAdditions(
            inner => { inner.WriteBoolean(true); inner.AlignToOctet(); },
            inner => { inner.WriteConstrainedWholeNumber(99, 0, 255); inner.AlignToOctet(); }
        );

        var dec = new PerDecoder(enc.ToArray());
        var additions = dec.ReadExtensionAdditions();
        Assert.AreEqual(2, additions.Length);
        Assert.IsNotNull(additions[0]);
        Assert.IsNotNull(additions[1]);
    }

    [TestMethod]
    public void ExtensionAdditions_Mixed()
    {
        var enc = new PerEncoder();
        enc.WriteExtensionAdditions(
            inner => { inner.WriteBoolean(true); inner.AlignToOctet(); },
            null, // absent
            inner => { inner.WriteConstrainedWholeNumber(5, 0, 7); inner.AlignToOctet(); }
        );

        var dec = new PerDecoder(enc.ToArray());
        var additions = dec.ReadExtensionAdditions();
        Assert.AreEqual(3, additions.Length);
        Assert.IsNotNull(additions[0]);
        Assert.IsNull(additions[1]);
        Assert.IsNotNull(additions[2]);
    }

    [TestMethod]
    public void SequencePreamble_RoundTrip()
    {
        // Simulate a SEQUENCE { OPTIONAL a, REQUIRED b, OPTIONAL c } with extension
        var enc = new PerEncoder();
        enc.WriteExtensionBit(false);           // no extensions
        enc.WriteOptionalBitmap(true, false);    // a present, c absent
        enc.WriteConstrainedWholeNumber(42, 0, 255); // a value
        enc.WriteConstrainedWholeNumber(7, 0, 15);   // b value

        var dec = new PerDecoder(enc.ToArray());
        Assert.IsFalse(dec.ReadExtensionBit());
        var bitmap = dec.ReadOptionalBitmap(2);
        Assert.IsTrue(bitmap[0]);   // a present
        Assert.IsFalse(bitmap[1]);  // c absent
        Assert.AreEqual(42, dec.ReadConstrainedWholeNumber(0, 255)); // a
        Assert.AreEqual(7, dec.ReadConstrainedWholeNumber(0, 15));   // b
    }
}

// ──────────────────────────────────────────────────────────
//  Comprehensive round-trip tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class PerRoundTripTests
{
    [TestMethod]
    public void MixedTypes_RoundTrip()
    {
        var enc = new PerEncoder();
        enc.WriteBoolean(true);
        enc.WriteConstrainedWholeNumber(3, 0, 7);
        enc.WriteEnumerated(2, 5);
        enc.WriteOctetString(new byte[] { 0xDE, 0xAD }, lb: 1, ub: 10);
        enc.WriteIA5String("Hello", lb: 1, ub: 50);

        var dec = new PerDecoder(enc.ToArray());
        Assert.IsTrue(dec.ReadBoolean());
        Assert.AreEqual(3, dec.ReadConstrainedWholeNumber(0, 7));
        Assert.AreEqual(2, dec.ReadEnumerated(5));
        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD }, dec.ReadOctetString(lb: 1, ub: 10));
        Assert.AreEqual("Hello", dec.ReadIA5String(lb: 1, ub: 50));
    }

    [TestMethod]
    public void H323Like_GatekeeperRequest()
    {
        // Simulate the structure of an H.225.0 GatekeeperRequest PDU
        var enc = new PerEncoder();

        // RasMessage CHOICE index (GatekeeperRequest = 0, 24 root alternatives)
        enc.WriteChoiceIndex(0, 24, extensible: true);

        // GatekeeperRequest SEQUENCE preamble
        enc.WriteExtensionBit(false);       // no extensions
        enc.WriteOptionalBitmap(false);     // 1 optional field (gatekeeperIdentifier), absent

        // requestSeqNum (1..65535)
        enc.WriteConstrainedWholeNumber(1, 1, 65535);

        // protocolIdentifier OID
        enc.WriteObjectIdentifier(new int[] { 0, 0, 8, 2250, 0, 1 });

        // rasAddress TransportAddress CHOICE (ipAddress = 0, 7 alternatives)
        enc.WriteChoiceIndex(0, 7, extensible: true);

        // ip (4 octets fixed)
        enc.WriteOctetString(new byte[] { 192, 168, 1, 100 }, lb: 4, ub: 4);

        // port (0..65535)
        enc.WriteConstrainedWholeNumber(1719, 0, 65535);

        // endpointType (simplified)
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, false, false, false, false, false); // all optional absent

        // Decode it back
        var dec = new PerDecoder(enc.ToArray());

        Assert.AreEqual(0, dec.ReadChoiceIndex(24, extensible: true));

        Assert.IsFalse(dec.ReadExtensionBit());
        var optionals = dec.ReadOptionalBitmap(1);
        Assert.IsFalse(optionals[0]);

        Assert.AreEqual(1, dec.ReadConstrainedWholeNumber(1, 65535));

        var oid = dec.ReadObjectIdentifier();
        CollectionAssert.AreEqual(new int[] { 0, 0, 8, 2250, 0, 1 }, oid);

        Assert.AreEqual(0, dec.ReadChoiceIndex(7, extensible: true));

        var ip = dec.ReadOctetString(lb: 4, ub: 4);
        CollectionAssert.AreEqual(new byte[] { 192, 168, 1, 100 }, ip);

        Assert.AreEqual(1719, dec.ReadConstrainedWholeNumber(0, 65535));

        Assert.IsFalse(dec.ReadExtensionBit());
        var endpointOptionals = dec.ReadOptionalBitmap(6);
        Assert.IsTrue(endpointOptionals.All(x => !x));
    }

    [TestMethod]
    public void BitsNeeded_CorrectValues()
    {
        Assert.AreEqual(0, PerEncoder.BitsNeeded(1));  // range 1
        Assert.AreEqual(1, PerEncoder.BitsNeeded(2));  // range 2
        Assert.AreEqual(2, PerEncoder.BitsNeeded(3));  // range 3
        Assert.AreEqual(2, PerEncoder.BitsNeeded(4));  // range 4
        Assert.AreEqual(3, PerEncoder.BitsNeeded(5));  // range 5
        Assert.AreEqual(3, PerEncoder.BitsNeeded(8));  // range 8
        Assert.AreEqual(4, PerEncoder.BitsNeeded(9));  // range 9
        Assert.AreEqual(7, PerEncoder.BitsNeeded(128)); // range 128
        Assert.AreEqual(8, PerEncoder.BitsNeeded(255)); // range 255
        Assert.AreEqual(8, PerEncoder.BitsNeeded(256)); // range 256 (but uses octet-aligned path)
    }

    [TestMethod]
    public void ConstrainedLengthDeterminant_RoundTrip()
    {
        // Within 65535 → uses constrained whole number
        var enc = new PerEncoder();
        enc.WriteConstrainedLengthDeterminant(50, 0, 100);

        var dec = new PerDecoder(enc.ToArray());
        Assert.AreEqual(50, dec.ReadConstrainedLengthDeterminant(0, 100));
    }
}
