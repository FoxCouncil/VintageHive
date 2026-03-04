// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  BerTag Tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class BerTagTests
{
    [TestMethod]
    public void UniversalPrimitiveTags_ParseCorrectly()
    {
        var boolTag = new BerTag(0x01);
        Assert.AreEqual(BerTag.CLASS_UNIVERSAL, boolTag.Class);
        Assert.IsFalse(boolTag.Constructed);
        Assert.AreEqual(1, boolTag.Number);
        Assert.IsTrue(boolTag.IsUniversal);

        var intTag = new BerTag(0x02);
        Assert.AreEqual(2, intTag.Number);
        Assert.IsFalse(intTag.Constructed);

        var enumTag = new BerTag(0x0A);
        Assert.AreEqual(10, enumTag.Number);
    }

    [TestMethod]
    public void UniversalConstructedTags_ParseCorrectly()
    {
        var seqTag = new BerTag(0x30);
        Assert.AreEqual(BerTag.CLASS_UNIVERSAL, seqTag.Class);
        Assert.IsTrue(seqTag.Constructed);
        Assert.AreEqual(16, seqTag.Number);

        var setTag = new BerTag(0x31);
        Assert.IsTrue(setTag.Constructed);
        Assert.AreEqual(17, setTag.Number);
    }

    [TestMethod]
    public void ApplicationConstructedTags_ParseCorrectly()
    {
        // LDAP BindRequest = APPLICATION 0 CONSTRUCTED = 0x60
        var bindReq = new BerTag(0x60);
        Assert.AreEqual(BerTag.CLASS_APPLICATION, bindReq.Class);
        Assert.IsTrue(bindReq.Constructed);
        Assert.AreEqual(0, bindReq.Number);
        Assert.IsTrue(bindReq.IsApplication);

        // LDAP SearchRequest = APPLICATION 3 CONSTRUCTED = 0x63
        var searchReq = new BerTag(0x63);
        Assert.AreEqual(3, searchReq.Number);
        Assert.IsTrue(searchReq.IsApplication);
    }

    [TestMethod]
    public void ApplicationPrimitiveTags_ParseCorrectly()
    {
        // LDAP UnbindRequest = APPLICATION 2 PRIMITIVE = 0x42
        var unbind = new BerTag(0x42);
        Assert.AreEqual(BerTag.CLASS_APPLICATION, unbind.Class);
        Assert.IsFalse(unbind.Constructed);
        Assert.AreEqual(2, unbind.Number);

        // LDAP DelRequest = APPLICATION 10 PRIMITIVE = 0x4A
        var del = new BerTag(0x4A);
        Assert.AreEqual(10, del.Number);
        Assert.IsFalse(del.Constructed);
    }

    [TestMethod]
    public void ContextSpecificTags_ParseCorrectly()
    {
        // Context primitive [0] = 0x80 (LDAP simple auth)
        var ctx0 = new BerTag(0x80);
        Assert.AreEqual(BerTag.CLASS_CONTEXT, ctx0.Class);
        Assert.IsFalse(ctx0.Constructed);
        Assert.AreEqual(0, ctx0.Number);
        Assert.IsTrue(ctx0.IsContext);

        // Context constructed [0] = 0xA0 (LDAP AND filter)
        var ctxA0 = new BerTag(0xA0);
        Assert.AreEqual(BerTag.CLASS_CONTEXT, ctxA0.Class);
        Assert.IsTrue(ctxA0.Constructed);
        Assert.AreEqual(0, ctxA0.Number);

        // Context primitive [7] = 0x87 (LDAP present filter)
        var ctx7 = new BerTag(0x87);
        Assert.AreEqual(7, ctx7.Number);
        Assert.IsFalse(ctx7.Constructed);
    }

    [TestMethod]
    public void Application_GeneratesCorrectTagBytes()
    {
        Assert.AreEqual(0x60, BerTag.Application(0, constructed: true));
        Assert.AreEqual(0x61, BerTag.Application(1, constructed: true));
        Assert.AreEqual(0x42, BerTag.Application(2, constructed: false));
        Assert.AreEqual(0x63, BerTag.Application(3, constructed: true));
        Assert.AreEqual(0x4A, BerTag.Application(10, constructed: false));
    }

    [TestMethod]
    public void Context_GeneratesCorrectTagBytes()
    {
        Assert.AreEqual(0x80, BerTag.Context(0, constructed: false));
        Assert.AreEqual(0xA0, BerTag.Context(0, constructed: true));
        Assert.AreEqual(0x87, BerTag.Context(7, constructed: false));
        Assert.AreEqual(0xA3, BerTag.Context(3, constructed: true));
    }

    [TestMethod]
    public void Is_MatchesRawByte()
    {
        var tag = new BerTag(0x30);
        Assert.IsTrue(tag.Is(BerTag.SEQUENCE));
        Assert.IsFalse(tag.Is(BerTag.SET));
    }

    [TestMethod]
    public void Constructor_FromComponents_RoundTrips()
    {
        var tag = new BerTag(BerTag.CLASS_APPLICATION, true, 3);
        Assert.AreEqual(0x63, tag.RawByte);
        Assert.AreEqual(BerTag.CLASS_APPLICATION, tag.Class);
        Assert.IsTrue(tag.Constructed);
        Assert.AreEqual(3, tag.Number);
    }

    [TestMethod]
    public void MultiByteConstructor_PreservesFields()
    {
        // First byte: Context Constructed with 0x1F marker = 0xBF
        var tag = new BerTag(0xBF, 42);
        Assert.AreEqual(BerTag.CLASS_CONTEXT, tag.Class);
        Assert.IsTrue(tag.Constructed);
        Assert.AreEqual(42, tag.Number);
    }

    [TestMethod]
    public void Constructor_FromComponents_HighTagNumber_SetsLowBitsTo1F()
    {
        var tag = new BerTag(BerTag.CLASS_APPLICATION, true, 31);
        Assert.AreEqual(0x7F, tag.RawByte); // All low bits set for multi-byte indicator
        Assert.AreEqual(31, tag.Number);
    }
}

// ──────────────────────────────────────────────────────────
//  BerEncoder Tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class BerEncoderTests
{
    [TestMethod]
    public void WriteBoolean_True()
    {
        var enc = new BerEncoder();
        enc.WriteBoolean(true);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x01, 0xFF }, enc.ToArray());
    }

    [TestMethod]
    public void WriteBoolean_False()
    {
        var enc = new BerEncoder();
        enc.WriteBoolean(false);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x01, 0x00 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_Zero()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(0);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0x00 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_One()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(1);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0x01 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_127()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(127);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0x7F }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_128_NeedsPadByte()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(128);
        // 128 = 0x80, but 0x80 alone is -128 in two's complement, so we need 0x00 0x80
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x02, 0x00, 0x80 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_255()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(255);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x02, 0x00, 0xFF }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_256()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(256);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x02, 0x01, 0x00 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_Negative1()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(-1);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0xFF }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_Negative128()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(-128);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0x80 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_Negative129()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(-129);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x02, 0xFF, 0x7F }, enc.ToArray());
    }

    [TestMethod]
    public void WriteInteger_Large_50000()
    {
        var enc = new BerEncoder();
        enc.WriteInteger(50000);
        // 50000 = 0xC350, high bit set so needs pad: 0x00 0xC3 0x50
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x03, 0x00, 0xC3, 0x50 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteOctetString_Empty()
    {
        var enc = new BerEncoder();
        enc.WriteOctetString(Array.Empty<byte>());
        CollectionAssert.AreEqual(new byte[] { 0x04, 0x00 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteString_Hello()
    {
        var enc = new BerEncoder();
        enc.WriteString("Hello!");
        CollectionAssert.AreEqual(
            new byte[] { 0x04, 0x06, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21 },
            enc.ToArray()
        );
    }

    [TestMethod]
    public void WriteEnumerated_Success()
    {
        var enc = new BerEncoder();
        enc.WriteEnumerated(0);
        CollectionAssert.AreEqual(new byte[] { 0x0A, 0x01, 0x00 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteNull()
    {
        var enc = new BerEncoder();
        enc.WriteNull();
        CollectionAssert.AreEqual(new byte[] { 0x05, 0x00 }, enc.ToArray());
    }

    [TestMethod]
    public void WriteSequence_WithChildren()
    {
        var enc = new BerEncoder();
        enc.WriteSequence(seq =>
        {
            seq.WriteInteger(1);
            seq.WriteString("test");
        });

        var bytes = enc.ToArray();
        Assert.AreEqual(BerTag.SEQUENCE, bytes[0]);
        // INTEGER 1: 02 01 01 (3 bytes)
        // OCTET STRING "test": 04 04 74 65 73 74 (6 bytes)
        // Total contents: 9 bytes
        Assert.AreEqual(9, bytes[1]); // length
        Assert.AreEqual(11, bytes.Length); // tag + length + 9
    }

    [TestMethod]
    public void WriteSequence_Nested()
    {
        var enc = new BerEncoder();
        enc.WriteSequence(outer =>
        {
            outer.WriteSequence(inner =>
            {
                inner.WriteInteger(42);
            });
        });

        var bytes = enc.ToArray();
        // Inner: 30 03 02 01 2A (5 bytes)
        // Outer: 30 05 ... (7 bytes total)
        Assert.AreEqual(BerTag.SEQUENCE, bytes[0]);
        Assert.AreEqual(5, bytes[1]);
        Assert.AreEqual(BerTag.SEQUENCE, bytes[2]);
        Assert.AreEqual(3, bytes[3]);
        Assert.AreEqual(BerTag.INTEGER, bytes[4]);
        Assert.AreEqual(1, bytes[5]);
        Assert.AreEqual(42, bytes[6]);
    }

    [TestMethod]
    public void WriteApplicationConstructed_LdapBindRequest()
    {
        var enc = new BerEncoder();
        enc.WriteApplicationConstructed(0, bind =>
        {
            bind.WriteInteger(3); // version
        });

        var bytes = enc.ToArray();
        Assert.AreEqual(0x60, bytes[0]); // APPLICATION CONSTRUCTED 0
        Assert.AreEqual(3, bytes[1]);    // length
        Assert.AreEqual(0x02, bytes[2]); // INTEGER tag
    }

    [TestMethod]
    public void WriteContextPrimitive_SimpleAuth()
    {
        var enc = new BerEncoder();
        enc.WriteContextPrimitive(0, System.Text.Encoding.UTF8.GetBytes("secret"));

        var bytes = enc.ToArray();
        Assert.AreEqual(0x80, bytes[0]); // CONTEXT PRIMITIVE [0]
        Assert.AreEqual(6, bytes[1]);    // "secret" length
    }

    [TestMethod]
    public void WriteTag_MultiByte_Tag31()
    {
        var enc = new BerEncoder();
        enc.WriteTag(0xBF, 31); // Context Constructed [31]
        var bytes = enc.ToArray();
        Assert.AreEqual(0xBF, bytes[0]);
        Assert.AreEqual(0x1F, bytes[1]); // 31 in base-128
    }

    [TestMethod]
    public void WriteTag_MultiByte_Tag128()
    {
        var enc = new BerEncoder();
        enc.WriteTag(0xBF, 128); // Context Constructed [128]
        var bytes = enc.ToArray();
        Assert.AreEqual(0xBF, bytes[0]);
        Assert.AreEqual(0x81, bytes[1]); // continuation: 1 * 128
        Assert.AreEqual(0x00, bytes[2]); // remainder: 0
    }

    [TestMethod]
    public void WriteTag_MultiByte_SmallNumber_UsesShortForm()
    {
        var enc = new BerEncoder();
        enc.WriteTag(0xBF, 5); // Should write as single-byte: (0xBF & 0xE0) | 5 = 0xA5
        var bytes = enc.ToArray();
        Assert.AreEqual(1, bytes.Length);
        Assert.AreEqual(0xA5, bytes[0]);
    }

    [TestMethod]
    public void WriteLength_ShortForm()
    {
        var enc = new BerEncoder();
        enc.WriteLength(0);
        enc.WriteLength(127);
        var bytes = enc.ToArray();
        Assert.AreEqual(0x00, bytes[0]);
        Assert.AreEqual(0x7F, bytes[1]);
    }

    [TestMethod]
    public void WriteLength_LongForm_OneByte()
    {
        var enc = new BerEncoder();
        enc.WriteLength(128);
        var bytes = enc.ToArray();
        Assert.AreEqual(0x81, bytes[0]);
        Assert.AreEqual(128, bytes[1]);
    }

    [TestMethod]
    public void WriteLength_LongForm_TwoBytes()
    {
        var enc = new BerEncoder();
        enc.WriteLength(256);
        var bytes = enc.ToArray();
        Assert.AreEqual(0x82, bytes[0]);
        Assert.AreEqual(0x01, bytes[1]);
        Assert.AreEqual(0x00, bytes[2]);
    }

    [TestMethod]
    public void LdapBindResponse_MatchesSpecExample()
    {
        // Build: 30 0C 02 01 01 61 07 0A 01 00 04 00 04 00
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(1); // messageID
            msg.WriteApplicationConstructed(1, resp => // BindResponse
            {
                resp.WriteEnumerated(0);          // resultCode: success
                resp.WriteString("");             // matchedDN
                resp.WriteString("");             // diagnosticMessage
            });
        });

        var expected = new byte[] { 0x30, 0x0C, 0x02, 0x01, 0x01, 0x61, 0x07, 0x0A, 0x01, 0x00, 0x04, 0x00, 0x04, 0x00 };
        CollectionAssert.AreEqual(expected, enc.ToArray());
    }
}

// ──────────────────────────────────────────────────────────
//  BerDecoder Tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class BerDecoderTests
{
    [TestMethod]
    public void ReadBoolean_True()
    {
        var dec = new BerDecoder(new byte[] { 0x01, 0x01, 0xFF });
        Assert.IsTrue(dec.ReadBoolean());
        Assert.IsFalse(dec.HasData);
    }

    [TestMethod]
    public void ReadBoolean_False()
    {
        var dec = new BerDecoder(new byte[] { 0x01, 0x01, 0x00 });
        Assert.IsFalse(dec.ReadBoolean());
    }

    [TestMethod]
    public void ReadInteger_Zero()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x01, 0x00 });
        Assert.AreEqual(0, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadInteger_127()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x01, 0x7F });
        Assert.AreEqual(127, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadInteger_128_PadByte()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x02, 0x00, 0x80 });
        Assert.AreEqual(128, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadInteger_255()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x02, 0x00, 0xFF });
        Assert.AreEqual(255, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadInteger_256()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x02, 0x01, 0x00 });
        Assert.AreEqual(256, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadInteger_Negative1()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x01, 0xFF });
        Assert.AreEqual(-1, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadInteger_Negative128()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x01, 0x80 });
        Assert.AreEqual(-128, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadInteger_Negative129()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x02, 0xFF, 0x7F });
        Assert.AreEqual(-129, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadInteger_50000()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x03, 0x00, 0xC3, 0x50 });
        Assert.AreEqual(50000, dec.ReadInteger());
    }

    [TestMethod]
    public void ReadOctetString_Empty()
    {
        var dec = new BerDecoder(new byte[] { 0x04, 0x00 });
        CollectionAssert.AreEqual(Array.Empty<byte>(), dec.ReadOctetString());
    }

    [TestMethod]
    public void ReadString_Hello()
    {
        var dec = new BerDecoder(new byte[] { 0x04, 0x06, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21 });
        Assert.AreEqual("Hello!", dec.ReadString());
    }

    [TestMethod]
    public void ReadEnumerated()
    {
        var dec = new BerDecoder(new byte[] { 0x0A, 0x01, 0x02 });
        Assert.AreEqual(2, dec.ReadEnumerated());
    }

    [TestMethod]
    public void ReadNull()
    {
        var dec = new BerDecoder(new byte[] { 0x05, 0x00 });
        dec.ReadNull(); // should not throw
        Assert.IsFalse(dec.HasData);
    }

    [TestMethod]
    public void ReadSequence_ReturnsSubDecoder()
    {
        // SEQUENCE { INTEGER 1, OCTET STRING "AB" }
        var data = new byte[] { 0x30, 0x07, 0x02, 0x01, 0x01, 0x04, 0x02, 0x41, 0x42 };
        var dec = new BerDecoder(data);
        var seq = dec.ReadSequence();

        Assert.AreEqual(1, seq.ReadInteger());
        Assert.AreEqual("AB", seq.ReadString());
        Assert.IsFalse(seq.HasData);
        Assert.IsFalse(dec.HasData);
    }

    [TestMethod]
    public void ReadTag_And_PeekTag()
    {
        var dec = new BerDecoder(new byte[] { 0x30, 0x00 });

        var peeked = dec.PeekTag();
        Assert.AreEqual(0, dec.Position);
        Assert.IsTrue(peeked.Is(BerTag.SEQUENCE));

        var read = dec.ReadTag();
        Assert.AreEqual(1, dec.Position);
        Assert.IsTrue(read.Is(BerTag.SEQUENCE));
    }

    [TestMethod]
    public void IsNextTag_CorrectlyIdentifies()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x01, 0x05 });
        Assert.IsTrue(dec.IsNextTag(BerTag.INTEGER));
        Assert.IsFalse(dec.IsNextTag(BerTag.SEQUENCE));
    }

    [TestMethod]
    public void ReadLength_ShortForm()
    {
        var dec = new BerDecoder(new byte[] { 0x00, 0x7F });
        Assert.AreEqual(0, dec.ReadLength());
        Assert.AreEqual(127, dec.ReadLength());
    }

    [TestMethod]
    public void ReadLength_LongForm()
    {
        // 0x81 0x80 = 128
        var dec = new BerDecoder(new byte[] { 0x81, 0x80 });
        Assert.AreEqual(128, dec.ReadLength());

        // 0x82 0x01 0x00 = 256
        var dec2 = new BerDecoder(new byte[] { 0x82, 0x01, 0x00 });
        Assert.AreEqual(256, dec2.ReadLength());
    }

    [TestMethod]
    public void ReadApplicationConstructed()
    {
        // APPLICATION 1 (BindResponse): 61 03 0A 01 00
        var data = new byte[] { 0x61, 0x03, 0x0A, 0x01, 0x00 };
        var dec = new BerDecoder(data);
        var body = dec.ReadApplicationConstructed(1);

        Assert.AreEqual(0, body.ReadEnumerated());
        Assert.IsFalse(body.HasData);
    }

    [TestMethod]
    public void ReadContextPrimitive()
    {
        // CONTEXT [0] "secret": 80 06 73 65 63 72 65 74
        var data = new byte[] { 0x80, 0x06, 0x73, 0x65, 0x63, 0x72, 0x65, 0x74 };
        var dec = new BerDecoder(data);
        var value = dec.ReadContextPrimitive(0);
        Assert.AreEqual("secret", System.Text.Encoding.UTF8.GetString(value));
    }

    [TestMethod]
    public void ReadContextConstructed()
    {
        // CONTEXT CONSTRUCTED [0] { INTEGER 5 }: A0 03 02 01 05
        var data = new byte[] { 0xA0, 0x03, 0x02, 0x01, 0x05 };
        var dec = new BerDecoder(data);
        var inner = dec.ReadContextConstructed(0);
        Assert.AreEqual(5, inner.ReadInteger());
    }

    [TestMethod]
    public void Skip_AdvancesPosition()
    {
        // INTEGER 42, BOOLEAN TRUE
        var data = new byte[] { 0x02, 0x01, 0x2A, 0x01, 0x01, 0xFF };
        var dec = new BerDecoder(data);
        dec.Skip(); // skip the INTEGER
        Assert.IsTrue(dec.ReadBoolean());
    }

    [TestMethod]
    public void ReadRawTlv_CapturesFullElement()
    {
        var data = new byte[] { 0x02, 0x01, 0x2A, 0x01, 0x01, 0xFF };
        var dec = new BerDecoder(data);
        var tlv = dec.ReadRawTlv();
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0x2A }, tlv);
        Assert.IsTrue(dec.HasData); // BOOLEAN still remaining
    }

    [TestMethod]
    public void ReadTag_MultiByteTag31_SingleContinuationByte()
    {
        // Tag 31: first byte = 0x1F marker, then 0x1F (31 in base-128, no continuation)
        // Context Constructed [31] = 0xBF 0x1F
        var dec = new BerDecoder(new byte[] { 0xBF, 0x1F, 0x01, 0x00 });
        var tag = dec.ReadTag();
        Assert.AreEqual(BerTag.CLASS_CONTEXT, tag.Class);
        Assert.IsTrue(tag.Constructed);
        Assert.AreEqual(31, tag.Number);
    }

    [TestMethod]
    public void ReadTag_MultiByteTag128_MultipleContinuationBytes()
    {
        // Tag 128: 0xBF 0x81 0x00 (128 in base-128 = 0x81 0x00)
        var dec = new BerDecoder(new byte[] { 0xBF, 0x81, 0x00, 0x01, 0x00 });
        var tag = dec.ReadTag();
        Assert.AreEqual(128, tag.Number);
    }

    [TestMethod]
    public void ReadTag_SingleByte_StillWorks()
    {
        // Verify single-byte tags (number < 31) still decode correctly
        var dec = new BerDecoder(new byte[] { 0x30 }); // SEQUENCE
        var tag = dec.ReadTag();
        Assert.AreEqual(BerTag.SEQUENCE, tag.RawByte);
        Assert.AreEqual(16, tag.Number);
    }

    [TestMethod]
    public void ReadInteger_WrongTag_Throws()
    {
        var dec = new BerDecoder(new byte[] { 0x04, 0x01, 0x00 }); // OCTET STRING, not INTEGER
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadInteger());
    }

    [TestMethod]
    public void ReadBeyondEnd_Throws()
    {
        var dec = new BerDecoder(new byte[] { 0x02, 0x01, 0x00 });
        dec.ReadInteger();
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadInteger());
    }

    [TestMethod]
    public void IndefiniteLength_Throws()
    {
        var dec = new BerDecoder(new byte[] { 0x30, 0x80, 0x00, 0x00 });
        dec.ReadTag();
        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadLength());
    }

    [TestMethod]
    public void LdapBindResponse_ParsesCorrectly()
    {
        // 30 0C 02 01 01 61 07 0A 01 00 04 00 04 00
        var data = new byte[] { 0x30, 0x0C, 0x02, 0x01, 0x01, 0x61, 0x07, 0x0A, 0x01, 0x00, 0x04, 0x00, 0x04, 0x00 };
        var dec = new BerDecoder(data);
        var msg = dec.ReadSequence();

        Assert.AreEqual(1, msg.ReadInteger()); // messageID

        var resp = msg.ReadApplicationConstructed(1); // BindResponse
        Assert.AreEqual(0, resp.ReadEnumerated());    // success
        Assert.AreEqual("", resp.ReadString());       // matchedDN
        Assert.AreEqual("", resp.ReadString());       // diagnosticMessage
        Assert.IsFalse(resp.HasData);
        Assert.IsFalse(msg.HasData);
    }
}

// ──────────────────────────────────────────────────────────
//  Encoder ↔ Decoder Round-Trip Tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class BerRoundTripTests
{
    [TestMethod]
    public void Integer_RoundTrips()
    {
        int[] values = { 0, 1, -1, 127, 128, 255, 256, -128, -129, 32767, -32768, 50000, -50000, int.MaxValue, int.MinValue };
        foreach (var expected in values)
        {
            var enc = new BerEncoder();
            enc.WriteInteger(expected);
            var dec = new BerDecoder(enc.ToArray());
            var actual = dec.ReadInteger();
            Assert.AreEqual(expected, actual, $"Round-trip failed for {expected}");
        }
    }

    [TestMethod]
    public void Long_RoundTrips()
    {
        long[] values = { 0, 1, -1, long.MaxValue, long.MinValue, (long)int.MaxValue + 1, (long)int.MinValue - 1 };
        foreach (var expected in values)
        {
            var enc = new BerEncoder();
            enc.WriteLong(expected);
            var dec = new BerDecoder(enc.ToArray());
            var actual = dec.ReadLong();
            Assert.AreEqual(expected, actual, $"Round-trip failed for {expected}");
        }
    }

    [TestMethod]
    public void Boolean_RoundTrips()
    {
        foreach (var expected in new[] { true, false })
        {
            var enc = new BerEncoder();
            enc.WriteBoolean(expected);
            var dec = new BerDecoder(enc.ToArray());
            Assert.AreEqual(expected, dec.ReadBoolean());
        }
    }

    [TestMethod]
    public void OctetString_RoundTrips()
    {
        byte[][] values = { Array.Empty<byte>(), new byte[] { 0x00 }, new byte[] { 0xFF, 0xFE, 0xFD }, new byte[256] };
        foreach (var expected in values)
        {
            var enc = new BerEncoder();
            enc.WriteOctetString(expected);
            var dec = new BerDecoder(enc.ToArray());
            CollectionAssert.AreEqual(expected, dec.ReadOctetString());
        }
    }

    [TestMethod]
    public void String_RoundTrips()
    {
        string[] values = { "", "Hello", "dc=example,dc=com", "Unicode: \u00E9\u00F1\u00FC" };
        foreach (var expected in values)
        {
            var enc = new BerEncoder();
            enc.WriteString(expected);
            var dec = new BerDecoder(enc.ToArray());
            Assert.AreEqual(expected, dec.ReadString());
        }
    }

    [TestMethod]
    public void Enumerated_RoundTrips()
    {
        int[] values = { 0, 1, 2, 80, -1 };
        foreach (var expected in values)
        {
            var enc = new BerEncoder();
            enc.WriteEnumerated(expected);
            var dec = new BerDecoder(enc.ToArray());
            Assert.AreEqual(expected, dec.ReadEnumerated());
        }
    }

    [TestMethod]
    public void NestedSequence_RoundTrips()
    {
        var enc = new BerEncoder();
        enc.WriteSequence(outer =>
        {
            outer.WriteInteger(42);
            outer.WriteSequence(inner =>
            {
                inner.WriteString("nested");
                inner.WriteBoolean(true);
            });
            outer.WriteEnumerated(7);
        });

        var dec = new BerDecoder(enc.ToArray());
        var outer = dec.ReadSequence();

        Assert.AreEqual(42, outer.ReadInteger());

        var inner = outer.ReadSequence();
        Assert.AreEqual("nested", inner.ReadString());
        Assert.IsTrue(inner.ReadBoolean());
        Assert.IsFalse(inner.HasData);

        Assert.AreEqual(7, outer.ReadEnumerated());
        Assert.IsFalse(outer.HasData);
    }

    [TestMethod]
    public void LdapBindRequest_RoundTrip()
    {
        // Encode a BindRequest: SEQUENCE { messageId=1, BindRequest { version=3, name="", simple="" } }
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(1);
            msg.WriteApplicationConstructed(0, bind =>
            {
                bind.WriteInteger(3);
                bind.WriteString("");
                bind.WriteContextPrimitive(0, Array.Empty<byte>());
            });
        });

        // Decode it back
        var dec = new BerDecoder(enc.ToArray());
        var msg = dec.ReadSequence();

        Assert.AreEqual(1, msg.ReadInteger());

        var bind = msg.ReadApplicationConstructed(0);
        Assert.AreEqual(3, bind.ReadInteger());
        Assert.AreEqual("", bind.ReadString());

        var auth = bind.ReadContextPrimitive(0);
        Assert.AreEqual(0, auth.Length);

        Assert.IsFalse(bind.HasData);
        Assert.IsFalse(msg.HasData);
    }

    [TestMethod]
    public void LdapSearchResultDone_RoundTrip()
    {
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(2);
            msg.WriteApplicationConstructed(5, done =>
            {
                done.WriteEnumerated(0);   // success
                done.WriteString("");      // matchedDN
                done.WriteString("");      // diagnosticMessage
            });
        });

        var dec = new BerDecoder(enc.ToArray());
        var msg = dec.ReadSequence();

        Assert.AreEqual(2, msg.ReadInteger());

        var done = msg.ReadApplicationConstructed(5);
        Assert.AreEqual(0, done.ReadEnumerated());
        Assert.AreEqual("", done.ReadString());
        Assert.AreEqual("", done.ReadString());
    }

    [TestMethod]
    public void LargeOctetString_LongFormLength()
    {
        var largeData = new byte[300];
        for (var i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i & 0xFF);
        }

        var enc = new BerEncoder();
        enc.WriteOctetString(largeData);
        var bytes = enc.ToArray();

        // Length 300 = 0x012C, encoded as 0x82 0x01 0x2C (long form, 2 bytes)
        Assert.AreEqual(BerTag.OCTET_STRING, bytes[0]);
        Assert.AreEqual(0x82, bytes[1]);
        Assert.AreEqual(0x01, bytes[2]);
        Assert.AreEqual(0x2C, bytes[3]);

        var dec = new BerDecoder(bytes);
        CollectionAssert.AreEqual(largeData, dec.ReadOctetString());
    }
}
