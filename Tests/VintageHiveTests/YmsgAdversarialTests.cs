// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using System.Linq;
using System.Text;
using VintageHive.Proxy.Yahoo;

namespace Adversarial.Ymsg;

// Adversarial coverage for the pure YMSG codec (VintageHive.Proxy.Yahoo.YmsgPacket): malformed magic,
// truncated headers, declared-length mismatches, oversized bodies, hostile 0xC0 0x80 separator layouts,
// non-numeric / signed / overflowing ASCII keys, non-UTF8 and non-ASCII bytes, and empty inputs.
// These exercise only Decode/Encode/HasMagic/BodyLength; no sockets, no DB, no shared static state.
[TestClass]
public class YmsgAdversarialTests
{
    private static readonly byte[] Sep = { 0xC0, 0x80 };

    // Builds the fixed 20-byte big-endian YMSG header. declaredLen is written into the length field but is
    // deliberately independent of any body we pass to Decode, so we can prove Decode ignores it.
    private static byte[] MakeHeader(ushort version = 0, ushort vendor = 0, ushort service = 0, uint status = 0, uint sessionId = 0, ushort declaredLen = 0)
    {
        var header = new byte[YmsgPacket.HeaderSize];

        Encoding.ASCII.GetBytes("YMSG").CopyTo(header, 0);

        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), version);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), vendor);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), declaredLen);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), service);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), status);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), sessionId);

        return header;
    }

    // Concatenates each raw token followed by the 0xC0 0x80 separator, matching the on-wire body framing.
    private static byte[] BuildBody(params byte[][] tokens)
    {
        using var ms = new MemoryStream();

        foreach (var token in tokens)
        {
            ms.Write(token, 0, token.Length);
            ms.Write(Sep, 0, Sep.Length);
        }

        return ms.ToArray();
    }

    private static byte[] A(string s)
    {
        return Encoding.ASCII.GetBytes(s);
    }

    // ---- HasMagic ----------------------------------------------------------------------------------

    [TestMethod]
    public void HasMagic_BuffersShorterThanFour_ReturnFalse()
    {
        Assert.IsFalse(YmsgPacket.HasMagic(Array.Empty<byte>()));
        Assert.IsFalse(YmsgPacket.HasMagic(new byte[] { 0x59 }));                   // "Y"
        Assert.IsFalse(YmsgPacket.HasMagic(new byte[] { 0x59, 0x4D }));             // "YM"
        Assert.IsFalse(YmsgPacket.HasMagic(new byte[] { 0x59, 0x4D, 0x53 }));       // "YMS"
    }

    [TestMethod]
    public void HasMagic_ExactAndTrailingGarbage_True()
    {
        Assert.IsTrue(YmsgPacket.HasMagic(A("YMSG")));
        Assert.IsTrue(YmsgPacket.HasMagic(A("YMSGtrailing-junk-ignored")));
    }

    [TestMethod]
    public void HasMagic_WrongOrLowercaseMagic_False()
    {
        Assert.IsFalse(YmsgPacket.HasMagic(A("ymsg")), "Magic check is case-sensitive");
        Assert.IsFalse(YmsgPacket.HasMagic(A("YMSX")));
        Assert.IsFalse(YmsgPacket.HasMagic(A("XMSG")));
        Assert.IsFalse(YmsgPacket.HasMagic(new byte[20]), "All-zero header has no magic");
    }

    // ---- BodyLength --------------------------------------------------------------------------------

    [TestMethod]
    public void BodyLength_ReadsUnsigned16BigEndian_IncludingMax()
    {
        Assert.AreEqual(0x1234, YmsgPacket.BodyLength(MakeHeader(declaredLen: 0x1234)));
        Assert.AreEqual(0xFFFF, YmsgPacket.BodyLength(MakeHeader(declaredLen: 0xFFFF)));
        Assert.AreEqual(0, YmsgPacket.BodyLength(MakeHeader(declaredLen: 0)));
    }

    [TestMethod]
    public void BodyLength_HeaderTooShortForLengthField_Throws()
    {
        // A hostile client that sends fewer than 10 bytes cannot yield a length field; the read must fault
        // rather than return garbage. (The socket layer reads a full 20 bytes before ever calling this.)
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => YmsgPacket.BodyLength(new byte[5]));
    }

    // ---- Decode: truncated / boundary headers ------------------------------------------------------

    [TestMethod]
    public void Decode_HeaderShorterThan20_Throws()
    {
        var truncated = MakeHeader(service: 0x0006)[..19];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => YmsgPacket.Decode(truncated, Array.Empty<byte>()));
    }

    [TestMethod]
    public void Decode_AllMaxHeaderValues_ReadWithoutOverflow()
    {
        var header = MakeHeader(version: 0xFFFF, vendor: 0xFFFF, service: 0xFFFF, status: 0xFFFFFFFF, sessionId: 0xFFFFFFFF, declaredLen: 0xFFFF);

        var packet = YmsgPacket.Decode(header, Array.Empty<byte>());

        Assert.AreEqual((ushort)0xFFFF, packet.Version);
        Assert.AreEqual((ushort)0xFFFF, packet.Vendor);
        Assert.AreEqual((YmsgService)0xFFFF, packet.Service);
        Assert.AreEqual(0xFFFFFFFFu, packet.Status);
        Assert.AreEqual(0xFFFFFFFFu, packet.SessionId);
        Assert.AreEqual(0, packet.Fields.Count);
    }

    [TestMethod]
    public void Decode_UnknownServiceCode_IsPreservedNotClamped()
    {
        var packet = YmsgPacket.Decode(MakeHeader(service: 0x7777), Array.Empty<byte>());

        // The codec does not validate the service against the known enum; the raw value survives the cast.
        Assert.AreEqual((YmsgService)0x7777, packet.Service);
    }

    // ---- Decode: body-length mismatch (parser trusts the caller-sized body) -------------------------

    [TestMethod]
    public void Decode_IgnoresDeclaredBodyLengthField_ParsesActualBuffer()
    {
        // Header lies: it claims a 0xFFFF-byte body, but Decode parses only the bytes actually handed to it.
        var header = MakeHeader(service: 0x0006, declaredLen: 0xFFFF);
        var body = BuildBody(A("5"), A("bob"));

        var packet = YmsgPacket.Decode(header, body);

        Assert.AreEqual("bob", packet.Get(5));
        Assert.AreEqual(1, packet.Fields.Count);
    }

    // ---- Decode: empty / degenerate bodies ---------------------------------------------------------

    [TestMethod]
    public void Decode_EmptyBody_ProducesNoFields()
    {
        var packet = YmsgPacket.Decode(MakeHeader(), Array.Empty<byte>());

        Assert.AreEqual(0, packet.Fields.Count);
        Assert.IsNull(packet.Get(0));
    }

    [TestMethod]
    public void Decode_SingleByteBodyWithNoSeparator_NoFields()
    {
        var packet = YmsgPacket.Decode(MakeHeader(), new byte[] { 0x35 }); // "5", never terminated

        Assert.AreEqual(0, packet.Fields.Count);
    }

    [TestMethod]
    public void Decode_TruncatedSeparatorFirstByteOnly_NoFields()
    {
        // Body ends on a lone 0xC0 (separator cut in half): no complete separator, so no token is emitted.
        var packet = YmsgPacket.Decode(MakeHeader(), new byte[] { 0x35, 0xC0 });

        Assert.AreEqual(0, packet.Fields.Count);
    }

    [TestMethod]
    public void Decode_KeyWithNoTrailingValueSeparator_KeyDropped()
    {
        // "5" + one separator = a single token; a key with no value is discarded (needs a key/value pair).
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("5")));

        Assert.AreEqual(0, packet.Fields.Count);
        Assert.IsNull(packet.Get(5));
    }

    [TestMethod]
    public void Decode_LoneSeparator_EmptyKeyDropped()
    {
        var packet = YmsgPacket.Decode(MakeHeader(), Sep.ToArray()); // one token: ""

        Assert.AreEqual(0, packet.Fields.Count);
    }

    [TestMethod]
    public void Decode_EmptyKeyEmptyValuePair_NonNumericKeyDropped()
    {
        // Two separators back-to-back => tokens ["", ""]; the empty key is not a number, so it is skipped.
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(Array.Empty<byte>(), Array.Empty<byte>()));

        Assert.AreEqual(0, packet.Fields.Count);
    }

    [TestMethod]
    public void Decode_KeyWithEmptyValue_IsKept()
    {
        // "5" SEP SEP => tokens ["5", ""]; a numeric key with an empty value is a valid pair.
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("5"), Array.Empty<byte>()));

        Assert.AreEqual(1, packet.Fields.Count);
        Assert.AreEqual(string.Empty, packet.Get(5));
    }

    // ---- Decode: hostile keys ----------------------------------------------------------------------

    [TestMethod]
    public void Decode_NonNumericKey_IsSilentlySkipped()
    {
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("abc"), A("value")));

        Assert.AreEqual(0, packet.Fields.Count);
        Assert.IsNull(packet.Get(0));
    }

    [TestMethod]
    public void Decode_NumericKeyOverflowingInt32_IsSkipped()
    {
        // 14 decimal digits overflow Int32; int.TryParse fails and the pair is dropped (no exception).
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("99999999999999"), A("x")));

        Assert.AreEqual(0, packet.Fields.Count);
    }

    [TestMethod]
    public void Decode_KeyWithSurroundingWhitespace_IsAcceptedByTryParse()
    {
        // int.TryParse's default NumberStyles.Integer tolerates leading/trailing whitespace, so a padded key
        // still lands in the field table. Documenting the actual (surprising) behavior, not endorsing it.
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("  5  "), A("v")));

        Assert.AreEqual("v", packet.Get(5));
    }

    [TestMethod]
    public void Decode_SignedKeys_AreAcceptedByTryParse()
    {
        // Leading sign is also permitted by NumberStyles.Integer, so negative/explicit-positive keys parse.
        var negative = YmsgPacket.Decode(MakeHeader(), BuildBody(A("-7"), A("v")));
        Assert.AreEqual("v", negative.Get(-7));

        var positive = YmsgPacket.Decode(MakeHeader(), BuildBody(A("+8"), A("w")));
        Assert.AreEqual("w", positive.Get(8));
    }

    [TestMethod]
    public void Decode_HexOrThousandsSeparatorKey_IsRejected()
    {
        Assert.AreEqual(0, YmsgPacket.Decode(MakeHeader(), BuildBody(A("0x10"), A("x"))).Fields.Count);
        Assert.AreEqual(0, YmsgPacket.Decode(MakeHeader(), BuildBody(A("1,000"), A("x"))).Fields.Count);
    }

    [TestMethod]
    public void Decode_NonAsciiKeyBytes_DecodeToPlaceholderAndAreRejected()
    {
        // 0xFF is not ASCII; Encoding.ASCII maps it to '?', which is not numeric, so the pair is dropped.
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(new byte[] { 0xFF }, A("x")));

        Assert.AreEqual(0, packet.Fields.Count);
    }

    // ---- Decode: hostile values --------------------------------------------------------------------

    [TestMethod]
    public void Decode_InvalidUtf8Value_ReplacedWithoutThrowing()
    {
        // Raw non-UTF8 bytes as a value must not throw; the default UTF-8 decoder substitutes U+FFFD.
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("14"), new byte[] { 0xFF, 0xFE, 0x80 }));

        var value = packet.Get(14);

        Assert.IsNotNull(value);
        Assert.IsTrue(value.Contains('�'), "Invalid UTF-8 must surface as the replacement character");
    }

    [TestMethod]
    public void Decode_ControlCharsInValue_ArePreservedVerbatim()
    {
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("14"), new byte[] { 0x00, 0x0A, 0x0D, 0x1B }));

        Assert.AreEqual("\0\n\r", packet.Get(14));
    }

    [TestMethod]
    public void Decode_ValueContainingEmbeddedSeparator_MisalignsIntoExtraToken()
    {
        // A hostile value that itself contains 0xC0 0x80 is split, shifting the pairing. tokens => [14,a,b];
        // (14,"a") pairs and the orphaned "b" token is dropped. This is the documented framing limitation.
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("14"), A("a"), A("b")));

        Assert.AreEqual("a", packet.Get(14));
        Assert.AreEqual(1, packet.Fields.Count);
    }

    [TestMethod]
    public void Decode_OddTokenCount_TrailingTokenDropped()
    {
        // tokens => [1, a, 2]; the final unpaired "2" key is ignored rather than paired with nothing.
        var packet = YmsgPacket.Decode(MakeHeader(), BuildBody(A("1"), A("a"), A("2")));

        Assert.AreEqual("a", packet.Get(1));
        Assert.IsNull(packet.Get(2));
        Assert.AreEqual(1, packet.Fields.Count);
    }

    // ---- Encode: length-field boundaries -----------------------------------------------------------

    [TestMethod]
    public void Encode_BodyOverSixteenBitLimit_Throws()
    {
        var packet = new YmsgPacket(YmsgService.Message, 0, 0).Add(1, new string('a', 70000));

        // The 16-bit length field cannot express >65535 bytes; the encoder must reject rather than truncate.
        Assert.ThrowsExactly<InvalidOperationException>(() => packet.Encode());
    }

    [TestMethod]
    public void Encode_LargeBodyWithinLimit_RoundTripsWithoutTruncation()
    {
        var value = new string('a', 60000);
        var bytes = new YmsgPacket(YmsgService.Message, 0, 0).Add(1, value).Encode();

        Assert.AreEqual(bytes.Length - YmsgPacket.HeaderSize, YmsgPacket.BodyLength(bytes));

        var decoded = YmsgPacket.Decode(bytes[..YmsgPacket.HeaderSize], bytes[YmsgPacket.HeaderSize..]);

        Assert.AreEqual(value, decoded.Get(1));
    }

    // ---- Encode: null / signed keys ----------------------------------------------------------------

    [TestMethod]
    public void Encode_NullValue_IsCoercedToEmptyString()
    {
        var bytes = new YmsgPacket(YmsgService.Message, 0, 0).Add(1, null).Encode();

        var decoded = YmsgPacket.Decode(bytes[..YmsgPacket.HeaderSize], bytes[YmsgPacket.HeaderSize..]);

        Assert.AreEqual(string.Empty, decoded.Get(1));
    }

    [TestMethod]
    public void Encode_NegativeKey_SurvivesRoundTrip()
    {
        // Encode stringifies the int key, and Decode's TryParse accepts the sign, so a negative key round-trips.
        var bytes = new YmsgPacket(YmsgService.Message, 0, 0).Add(-5, "v").Encode();

        var decoded = YmsgPacket.Decode(bytes[..YmsgPacket.HeaderSize], bytes[YmsgPacket.HeaderSize..]);

        Assert.AreEqual("v", decoded.Get(-5));
    }

    [TestMethod]
    public void Get_MissingKey_ReturnsNull()
    {
        var packet = new YmsgPacket();

        Assert.IsNull(packet.Get(999));
    }
}