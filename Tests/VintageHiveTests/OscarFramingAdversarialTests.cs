// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System;
using System.Collections.Generic;
using System.Text;
using VintageHive.Proxy.Oscar;

#pragma warning disable MSTEST0025 // Use Assert.Fail instead of always-failing Assert.AreEqual

namespace Adversarial6.OscarFraming;

// SECOND-PASS adversarial coverage for OSCAR FLAP/SNAC/TLV framing.
// The happy paths (OscarTests.cs) and the first adversarial pass (OscarCodecAdversarialTests.cs)
// already cover: undersized buffers, declared-length-exceeds-buffer throws, unknown frame types,
// ushort.MaxValue TLV boundaries, wrong-length ToUIntNN throws, and single-frame round trips.
// This file hunts the remaining edges: in-place mutation side effects, oversized (non-throwing)
// slices, multi-frame truncation of a LATER frame, exact-fit vs opaque/nested TLVs, duplicate and
// empty GetTlv lookups, ToCLSID off-by-one lengths and casing, RoastPassword narrowing/collisions,
// the (ushort)Data.Length framing wrap in Flap.Encode, and the NewReply "0 means inherit" quirk.

[TestClass]
public class OscarUtilsIntMutationAdversarialTests
{
    // GAP: the first pass asserted ToUInt16 does NOT mutate its input. It actually reverses the
    // caller's array IN PLACE (MakeNetworkByteOrder -> Array.Reverse, no clone) on a little-endian
    // host. In-repo callers happen to pass fresh range-slice copies, so this is latent, but a "read"
    // helper mutating its argument is a genuine footgun. These document the ACTUAL behavior.

    [TestMethod]
    public void ToUInt16_DoesNotMutateCallerArray()
    {
        var buffer = new byte[] { 0x12, 0x34 };

        var value = OscarUtils.ToUInt16(buffer);

        Assert.AreEqual((ushort)0x1234, value);

        // The conversion clones internally, so the caller's buffer is left intact.
        Assert.AreEqual(0x12, buffer[0], "ToUInt16 must not mutate the caller's buffer");
        Assert.AreEqual(0x34, buffer[1], "ToUInt16 must not mutate the caller's buffer");
    }

    [TestMethod]
    public void ToUInt16_CalledTwiceOnSameArray_YieldsSameResult()
    {
        // The read is idempotent: cloning internally means a second call sees the same input and
        // returns the same value.
        var buffer = new byte[] { 0x12, 0x34 };

        var first = OscarUtils.ToUInt16(buffer);
        var second = OscarUtils.ToUInt16(buffer);

        Assert.AreEqual((ushort)0x1234, first);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void ToUInt32_DoesNotMutateCallerArray()
    {
        var buffer = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var value = OscarUtils.ToUInt32(buffer);

        Assert.AreEqual((uint)0x01020304, value);
        Assert.AreEqual(0x01, buffer[0]);
        Assert.AreEqual(0x02, buffer[1]);
        Assert.AreEqual(0x03, buffer[2]);
        Assert.AreEqual(0x04, buffer[3]);
    }

    // GAP: the first pass only tested UNDER-sized slices (which throw). An OVER-sized slice does NOT
    // throw; it silently reads a reversed window and returns a garbage value. No length validation.

    [TestMethod]
    public void ToUInt16_ThreeByteSlice_SilentlyReturnsReversedWindow_NoThrow()
    {
        // {0x01,0x02,0x03} reversed -> {0x03,0x02,0x01}; ToUInt16 reads the first two (LE) => 0x0203.
        var value = OscarUtils.ToUInt16(new byte[] { 0x01, 0x02, 0x03 });

        Assert.AreEqual((ushort)0x0203, value);
    }

    [TestMethod]
    public void ToUInt32_SixByteSlice_SilentlyReturnsReversedWindow_NoThrow()
    {
        // {01,02,03,04,05,06} reversed -> {06,05,04,03,02,01}; first four (LE) => 0x03040506.
        var value = OscarUtils.ToUInt32(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 });

        Assert.AreEqual((uint)0x03040506, value);
    }

    [TestMethod]
    public void DecodeFlaps_DoesNotMutateInputBuffer()
    {
        // Contrast: DecodeFlaps reads header fields via range-slice COPIES, so the original buffer
        // is left intact even though ToUIntNN mutates in place internally.
        var data = new byte[] { 0x2A, 0x02, 0x00, 0x07, 0x00, 0x02, 0xAB, 0xCD };
        var snapshot = (byte[])data.Clone();

        OscarUtils.DecodeFlaps(data);

        CollectionAssert.AreEqual(snapshot, data, "DecodeFlaps must not mutate its input buffer");
    }

    [TestMethod]
    public void DecodeTlvs_DoesNotMutateInputBuffer()
    {
        var data = new byte[] { 0x00, 0x01, 0x00, 0x03, 0x46, 0x6F, 0x78 };
        var snapshot = (byte[])data.Clone();

        OscarUtils.DecodeTlvs(data);

        CollectionAssert.AreEqual(snapshot, data, "DecodeTlvs must not mutate its input buffer");
    }
}

[TestClass]
public class RoastPasswordAdversarialTests
{
    // GAP: the first pass only roasted ASCII (empty, >16 wrap). RoastPassword narrows each UTF-16
    // code unit to a single byte via (byte)password[idx], silently discarding the high byte.

    [TestMethod]
    public void RoastPassword_NonAsciiChar_NarrowsToLowByte()
    {
        // 'é' == U+00E9. (byte)0x00E9 == 0xE9; 0xE9 ^ ROAST_KEY[0]=0xF3 == 0x1A.
        var roasted = OscarUtils.RoastPassword("é");

        Assert.AreEqual(1, roasted.Length);
        Assert.AreEqual(0x1A, roasted[0]);
    }

    [TestMethod]
    public void RoastPassword_HighCodepointCollidesWithNul_SilentDataLoss()
    {
        // U+0100 narrows to 0x00, identical to U+0000. Two distinct passwords roast to the SAME
        // bytes: the high byte is silently lost, so the transform is not injective for non-ASCII.
        var fromHigh = OscarUtils.RoastPassword("Ā");
        var fromNul = OscarUtils.RoastPassword("\0");

        Assert.AreEqual(1, fromHigh.Length);
        Assert.AreEqual(1, fromNul.Length);
        CollectionAssert.AreEqual(fromNul, fromHigh, "high byte is dropped -> distinct inputs collide");
        Assert.AreEqual(0xF3, fromHigh[0]); // 0x00 ^ 0xF3
    }

    [TestMethod]
    public void RoastPassword_SurrogatePair_CountedAsTwoCodeUnits()
    {
        // A non-BMP character (U+1F600) is two UTF-16 code units, so the output length is 2, not 1,
        // and each half is roasted independently.
        var roasted = OscarUtils.RoastPassword("😀");

        Assert.AreEqual(2, roasted.Length);
    }

    [TestMethod]
    public void RoastPassword_LengthEqualsCodeUnitCount_NotByteCount()
    {
        // "€€" is two chars but four UTF-8 bytes; roast output tracks code units (2), never bytes.
        var roasted = OscarUtils.RoastPassword("€€");

        Assert.AreEqual(2, roasted.Length);
    }

    [TestMethod]
    public void RoastPassword_ExactlySixteenChars_UsesEveryKeyByteOnce()
    {
        // A 16-char password touches key[0..15] with no wrap; the 17th char (if any) wraps to key[0].
        var roasted = OscarUtils.RoastPassword("0123456789ABCDEF");

        Assert.AreEqual(16, roasted.Length);
        // '0' (0x30) ^ key[0] (0xF3) == 0xC3
        Assert.AreEqual(0xC3, roasted[0]);
        // 'F' (0x46) ^ key[15] (0x7C) == 0x3A
        Assert.AreEqual(0x3A, roasted[15]);
    }
}

[TestClass]
public class ToClsidAdversarialTests
{
    // GAP: OscarTests covers valid 16 + a 3-byte throw. The off-by-one lengths around 16 and the
    // output casing / all-ones formatting were untested.

    [TestMethod]
    public void ToCLSID_FifteenBytes_Throws()
    {
        Assert.ThrowsExactly<ApplicationException>(() => OscarUtils.ToCLSID(new byte[15]));
    }

    [TestMethod]
    public void ToCLSID_SeventeenBytes_Throws()
    {
        Assert.ThrowsExactly<ApplicationException>(() => OscarUtils.ToCLSID(new byte[17]));
    }

    [TestMethod]
    public void ToCLSID_EmptyArray_Throws()
    {
        Assert.ThrowsExactly<ApplicationException>(() => OscarUtils.ToCLSID(Array.Empty<byte>()));
    }

    [TestMethod]
    public void ToCLSID_AllOnes_FormatsUppercaseWithDashes()
    {
        var data = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            data[i] = 0xFF;
        }

        var result = OscarUtils.ToCLSID(data);

        Assert.AreEqual("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", result);
    }

    [TestMethod]
    public void ToCLSID_LowercaseNotUsed_HexIsUppercase()
    {
        // 0xAB..0xEF must render as uppercase A-F, never lowercase.
        var data = new byte[] { 0xAB, 0xCD, 0xEF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var result = OscarUtils.ToCLSID(data);

        Assert.AreEqual("ABCDEF00-0000-0000-0000-000000000000", result);
    }

    [TestMethod]
    public void ToCLSID_DoesNotMutateInput()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };
        var snapshot = (byte[])data.Clone();

        OscarUtils.ToCLSID(data);

        CollectionAssert.AreEqual(snapshot, data);
    }
}

[TestClass]
public class DecodeFlapsFramingAdversarialTests
{
    private static byte[] Concat(params byte[][] parts)
    {
        var list = new List<byte>();
        foreach (var p in parts)
        {
            list.AddRange(p);
        }
        return list.ToArray();
    }

    // GAP: the first pass truncated a SINGLE flap and appended trailing garbage. It never truncated
    // a LATER flap in a multi-flap stream after an earlier one decoded cleanly.

    [TestMethod]
    public void SecondFlap_DeclaresOverrunLength_ThrowsAfterFirstDecodes()
    {
        var data = Concat(
            new byte[] { 0x2A, 0x01, 0x00, 0x01, 0x00, 0x00 },              // complete SignOn, 0 payload
            new byte[] { 0x2A, 0x02, 0x00, 0x02, 0x00, 0x05, 0x41, 0x42 }); // claims 5, only 2 present

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OscarUtils.DecodeFlaps(data));
    }

    [TestMethod]
    public void SecondFlap_HasInvalidHeaderByte_Throws()
    {
        // First flap complete; the "second" starts with 0x2B instead of '*'.
        var data = Concat(
            new byte[] { 0x2A, 0x02, 0x00, 0x01, 0x00, 0x00 },
            new byte[] { 0x2B, 0x02, 0x00, 0x02, 0x00, 0x00 });

        Assert.ThrowsExactly<ArgumentException>(() => OscarUtils.DecodeFlaps(data));
    }

    [TestMethod]
    public void ThreeFlaps_AllDefinedTypes_DecodeInOrder()
    {
        var data = Concat(
            new byte[] { 0x2A, 0x01, 0x00, 0x01, 0x00, 0x00 }, // SignOn
            new byte[] { 0x2A, 0x03, 0x00, 0x02, 0x00, 0x00 }, // Error
            new byte[] { 0x2A, 0x05, 0x00, 0x03, 0x00, 0x00 }); // KeepAlive

        var flaps = OscarUtils.DecodeFlaps(data);

        Assert.AreEqual(3, flaps.Length);
        Assert.AreEqual(FlapFrameType.SignOn, flaps[0].Type);
        Assert.AreEqual(FlapFrameType.Error, flaps[1].Type);
        Assert.AreEqual(FlapFrameType.KeepAlive, flaps[2].Type);
        Assert.AreEqual((ushort)1, flaps[0].Sequence);
        Assert.AreEqual((ushort)3, flaps[2].Sequence);
    }

    [TestMethod]
    public void MaxSequenceNumber_RoundTripsThroughDecode()
    {
        // Sequence is a raw ushort; 0xFFFF must survive decode unchanged (no signedness clipping).
        var data = new byte[] { 0x2A, 0x02, 0xFF, 0xFF, 0x00, 0x01, 0x7E };

        var flaps = OscarUtils.DecodeFlaps(data);

        Assert.AreEqual(1, flaps.Length);
        Assert.AreEqual(ushort.MaxValue, flaps[0].Sequence);
        Assert.AreEqual(1, flaps[0].Data.Length);
        Assert.AreEqual(0x7E, flaps[0].Data[0]);
    }

    [TestMethod]
    public void ZeroPayloadFlapFollowedByFullFlap_BothDecode()
    {
        // Boundary: a zero-length payload advances readIdx by exactly 6 with no data copy, and the
        // next frame must still line up.
        var data = Concat(
            new byte[] { 0x2A, 0x05, 0x00, 0x09, 0x00, 0x00 },              // KeepAlive, empty
            new byte[] { 0x2A, 0x02, 0x00, 0x0A, 0x00, 0x03, 0x61, 0x62, 0x63 }); // Data "abc"

        var flaps = OscarUtils.DecodeFlaps(data);

        Assert.AreEqual(2, flaps.Length);
        Assert.AreEqual(0, flaps[0].Data.Length);
        Assert.AreEqual(3, flaps[1].Data.Length);
        Assert.AreEqual((byte)'c', flaps[1].Data[2]);
    }
}

[TestClass]
public class DecodeTlvsFramingAdversarialTests
{
    // GAP: "length that exactly reaches vs exceeds the buffer" - the first pass covered EXCEEDS and
    // the single ushort.MaxValue fill. These cover the exact-reach boundary in small, multi-TLV, and
    // opaque/nested shapes.

    [TestMethod]
    public void SingleTlv_ValueEndsExactlyAtBufferEnd_Decodes()
    {
        // type=1, len=2, exactly 2 value bytes and nothing trailing. readIdx lands on data.Length.
        var data = new byte[] { 0x00, 0x01, 0x00, 0x02, 0x41, 0x42 };

        var tlvs = OscarUtils.DecodeTlvs(data);

        Assert.AreEqual(1, tlvs.Length);
        Assert.AreEqual((ushort)1, tlvs[0].Type);
        Assert.AreEqual(2, tlvs[0].Value.Length);
        Assert.AreEqual((byte)'B', tlvs[0].Value[1]);
    }

    [TestMethod]
    public void TwoTlvs_SecondValueEndsExactlyAtBufferEnd_BothDecode()
    {
        var data = new byte[]
        {
            0x00, 0x01, 0x00, 0x02, 0x41, 0x42, // type=1 len=2 "AB"
            0x00, 0x02, 0x00, 0x01, 0x43        // type=2 len=1 "C" ends exactly at buffer end
        };

        var tlvs = OscarUtils.DecodeTlvs(data);

        Assert.AreEqual(2, tlvs.Length);
        Assert.AreEqual((ushort)2, tlvs[1].Type);
        Assert.AreEqual(1, tlvs[1].Value.Length);
        Assert.AreEqual((byte)'C', tlvs[1].Value[0]);
    }

    [TestMethod]
    public void NestedTlv_OuterValueIsOpaque_InnerNotAutoDecoded()
    {
        // A TLV whose value is itself an encoded TLV. The decoder treats the value as opaque bytes;
        // it does NOT recurse. Re-decoding the value yields the inner TLV.
        var inner = new Tlv(0x0009, "PW").Encode(); // type(2)+len(2)+"PW"(2) = 6 bytes
        var outer = new List<Tlv> { new Tlv(0x0005, inner) }.EncodeTlvs();

        var decoded = OscarUtils.DecodeTlvs(outer);

        Assert.AreEqual(1, decoded.Length);
        Assert.AreEqual((ushort)0x0005, decoded[0].Type);
        Assert.AreEqual(6, decoded[0].Value.Length);

        var reDecoded = OscarUtils.DecodeTlvs(decoded[0].Value);
        Assert.AreEqual(1, reDecoded.Length);
        Assert.AreEqual((ushort)0x0009, reDecoded[0].Type);
        Assert.AreEqual("PW", Encoding.ASCII.GetString(reDecoded[0].Value));
    }

    [TestMethod]
    public void ZeroLengthTlvFollowedByValuedTlv_BothDecode()
    {
        // A zero-length TLV advances readIdx by exactly 4 (no value slice); the next TLV must align.
        var data = new byte[]
        {
            0x00, 0x0A, 0x00, 0x00,             // type=10 len=0
            0x00, 0x0B, 0x00, 0x02, 0xDE, 0xAD  // type=11 len=2
        };

        var tlvs = OscarUtils.DecodeTlvs(data);

        Assert.AreEqual(2, tlvs.Length);
        Assert.AreEqual(0, tlvs[0].Value.Length);
        Assert.AreEqual((ushort)11, tlvs[1].Type);
        Assert.AreEqual(0xDE, tlvs[1].Value[0]);
    }

    [TestMethod]
    public void EncodeThenDecode_ManyTlvs_PreservesOrderAndTypes()
    {
        var list = new List<Tlv>
        {
            new Tlv(0x0001, "screenname"),
            new Tlv(0x0005, Array.Empty<byte>()),
            new Tlv(0x0006, (ushort)0x1234),
            new Tlv(0x000C, new byte[] { 0x00, 0xFF, 0x7F })
        };

        var decoded = OscarUtils.DecodeTlvs(list.EncodeTlvs());

        Assert.AreEqual(4, decoded.Length);
        Assert.AreEqual((ushort)0x0001, decoded[0].Type);
        Assert.AreEqual((ushort)0x0005, decoded[1].Type);
        Assert.AreEqual(0, decoded[1].Value.Length);
        Assert.AreEqual((ushort)0x1234, OscarUtils.ToUInt16(decoded[2].Value));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xFF, 0x7F }, decoded[3].Value);
    }
}

[TestClass]
public class GetTlvAndEncodeTlvsAdversarialTests
{
    // GAP: duplicate-type lookup semantics and empty-collection lookups were untested.

    [TestMethod]
    public void GetTlv_List_DuplicateTypes_ReturnsFirstMatch()
    {
        var first = new Tlv(0x0008, "first");
        var second = new Tlv(0x0008, "second");
        var list = new List<Tlv> { first, second };

        var result = list.GetTlv(0x0008);

        Assert.AreSame(first, result);
    }

    [TestMethod]
    public void GetTlv_Array_DuplicateTypes_ReturnsFirstMatch()
    {
        var first = new Tlv(0x0008, "first");
        var second = new Tlv(0x0008, "second");
        var arr = new[] { first, second };

        var result = arr.GetTlv(0x0008);

        Assert.AreSame(first, result);
    }

    [TestMethod]
    public void GetTlv_EmptyList_ReturnsNull()
    {
        Assert.IsNull(new List<Tlv>().GetTlv(0x0001));
    }

    [TestMethod]
    public void GetTlv_EmptyArray_ReturnsNull()
    {
        Assert.IsNull(Array.Empty<Tlv>().GetTlv(0x0001));
    }

    [TestMethod]
    public void EncodeTlvs_EmptyList_ReturnsEmptyArray()
    {
        var encoded = new List<Tlv>().EncodeTlvs();

        Assert.IsNotNull(encoded);
        Assert.AreEqual(0, encoded.Length);
    }
}

[TestClass]
public class FlapEncodeFramingAdversarialTests
{
    // Flap.Encode now rejects a payload that would overflow the 16-bit length field, instead of wrapping
    // the length to 0 while still appending the full payload (which silently desynced any FLAP reader).

    [TestMethod]
    public void Encode_PayloadExactly64KiB_IsRejected()
    {
        // 65536 bytes cannot fit the 16-bit length field, so Encode throws instead of wrapping to 0.
        var flap = new Flap(FlapFrameType.Data) { Sequence = 1, Data = new byte[65536] };

        Assert.ThrowsExactly<ApplicationException>(() => flap.Encode());
    }

    [TestMethod]
    public void Encode_OversizedPayload_ThrowsBeforeProducingDesyncableFrame()
    {
        // Because Encode rejects the oversized payload up front, no wrapped-length frame is ever emitted,
        // so the DecodeFlaps desync the wrap used to cause cannot happen.
        var flap = new Flap(FlapFrameType.Data) { Sequence = 1, Data = new byte[70000] };

        Assert.ThrowsExactly<ApplicationException>(() => flap.Encode());
    }

    [TestMethod]
    public void Encode_PayloadMax65535_LengthFieldIsFFFF_RoundTrips()
    {
        // The largest payload that does NOT overflow the length field must round-trip cleanly.
        var payload = new byte[65535];
        payload[0] = 0x11;
        payload[65534] = 0x22;

        var flap = new Flap(FlapFrameType.Data) { Sequence = 9, Data = payload };
        var encoded = flap.Encode();

        Assert.AreEqual(6 + 65535, encoded.Length);
        Assert.AreEqual(0xFF, encoded[4]);
        Assert.AreEqual(0xFF, encoded[5]);

        var decoded = OscarUtils.DecodeFlaps(encoded);
        Assert.AreEqual(1, decoded.Length);
        Assert.AreEqual(65535, decoded[0].Data.Length);
        Assert.AreEqual(0x11, decoded[0].Data[0]);
        Assert.AreEqual(0x22, decoded[0].Data[65534]);
    }
}

[TestClass]
public class SnacFramingAdversarialTests
{
    // GAP: the first pass covered NewReply inheritance and single-field overrides but not the
    // "0 means inherit" quirk (you cannot force a field to 0), max-value header encoding, or a full
    // Snac.Encode -> Flap.GetSnac integration round trip.

    [TestMethod]
    public void NewReply_FamilyZero_CannotOverride_InheritsOriginal()
    {
        var original = new Snac(0x0005, 0x0006, 0x0000, 42);

        var reply = original.NewReply(family: 0, subType: 0);

        // Passing 0 is indistinguishable from "not specified"; the original values are kept.
        Assert.AreEqual((ushort)0x0005, reply.Family);
        Assert.AreEqual((ushort)0x0006, reply.SubType);
    }

    [TestMethod]
    public void NewReply_FlagsZero_CannotClearInheritedFlags()
    {
        // A caller wanting a reply with flags == 0 cannot get it if the original carried flags: the
        // ternary treats 0 as "inherit", so the set bits leak into the reply.
        var original = new Snac(0x0001, 0x0002, 0x8000, 7);

        var reply = original.NewReply(flags: 0);

        Assert.AreEqual((ushort)0x8000, reply.Flags);
    }

    [TestMethod]
    public void NewReply_NonZeroOverrides_TakeEffect()
    {
        var original = new Snac(0x0001, 0x0002, 0x0001, 7);

        var reply = original.NewReply(family: 0x000A, subType: 0x0021, flags: 0x0004);

        Assert.AreEqual((ushort)0x000A, reply.Family);
        Assert.AreEqual((ushort)0x0021, reply.SubType);
        Assert.AreEqual((ushort)0x0004, reply.Flags);
        Assert.AreEqual((uint)7, reply.RequestID); // RequestID always inherited
    }

    [TestMethod]
    public void Encode_MaxHeaderValues_AllBytesAreFF()
    {
        var snac = new Snac(0xFFFF, 0xFFFF, 0xFFFF, 0xFFFFFFFF);

        var encoded = snac.Encode();

        Assert.AreEqual(10, encoded.Length);
        for (var i = 0; i < 10; i++)
        {
            Assert.AreEqual(0xFF, encoded[i], $"header byte {i} must be 0xFF");
        }
    }

    [TestMethod]
    public void EncodeThenFlapGetSnac_RoundTripsHeaderAndPayload()
    {
        var snac = new Snac(0x000A, 0x0021, 0x8000, 0xDEADBEEF);
        snac.WriteString("hi");

        var wire = snac.Encode(); // 10-byte header + "hi"
        var flap = new Flap(FlapFrameType.Data) { Data = wire };

        var parsed = flap.GetSnac();

        Assert.AreEqual((ushort)0x000A, parsed.Family);
        Assert.AreEqual((ushort)0x0021, parsed.SubType);
        Assert.AreEqual((ushort)0x8000, parsed.Flags);
        Assert.AreEqual(0xDEADBEEF, parsed.RequestID);
        Assert.AreEqual("hi", Encoding.ASCII.GetString(parsed.RawData));
    }

    [TestMethod]
    public void Encode_EmptyPayload_IsHeaderOnly()
    {
        var snac = new Snac(0x0001, 0x0002);

        var encoded = snac.Encode();

        Assert.AreEqual(10, encoded.Length);
    }
}

#pragma warning restore MSTEST0025