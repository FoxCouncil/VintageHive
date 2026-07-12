// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Reflection;
using VintageHive.Proxy.Dns;

namespace Adversarial.Dns;

// Adversarial, pure-parser tests for VintageHive.Proxy.Dns.DnsProxy.ParseDomainName.
//
// The DNS name parser is a private static method (byte[] data, ref int offset) -> string. It is the
// single most attacker-reachable piece of the DNS proxy: it runs on raw UDP bytes before any
// validation. These tests reach it directly via reflection so they exercise ONLY the parser/codec
// logic - no UdpClient, no sockets, no shared static state, no Mind.Db. The existing DnsTests.cs
// drives the same code through real UDP sockets; this file deliberately does NOT duplicate that and
// instead hammers the byte-level parsing contract: compression-pointer loops, out-of-range pointers,
// truncated pointers, over-long labels, labels running past the packet end, reserved label-type bits,
// oversized names, and non-ASCII / control-char / injection bytes.
//
// The observed contract, verified against the real code, is: the parser NEVER throws and NEVER hangs
// on hostile input - every malformed shape returns null (rejected), and only well-formed, terminated,
// non-empty names return a string. Each test asserts that actually-observed behavior.
[TestClass]
public class DnsAdversarialTests
{
    // Invokes the real private static DnsProxy.ParseDomainName(byte[], ref int). Unwraps
    // TargetInvocationException so a genuine parser throw surfaces as itself and fails the test
    // (that would be a bug: hostile input must never throw out of the parser).
    private static (string? result, int newOffset) ParseName(byte[] data, int offset)
    {
        var method = typeof(DnsProxy).GetMethod("ParseDomainName", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method, "ParseDomainName(byte[], ref int) not found via reflection - signature changed?");

        var args = new object[] { data, offset };

        object? raw;

        try
        {
            raw = method.Invoke(null, args);
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }

        return ((string?)raw, (int)args[1]!);
    }

    #region Empty / boundary buffers

    [TestMethod]
    public void EmptyBuffer_ReturnsNull()
    {
        var (result, _) = ParseName(Array.Empty<byte>(), 0);

        Assert.IsNull(result, "An empty buffer has no name and must be rejected");
    }

    [TestMethod]
    public void OffsetAtEnd_ReturnsNull()
    {
        var data = new byte[] { 3, (byte)'a', (byte)'b', (byte)'c', 0 };

        var (result, _) = ParseName(data, data.Length);

        Assert.IsNull(result, "Starting exactly at the end leaves nothing to parse -> null");
    }

    [TestMethod]
    public void OffsetPastEnd_ReturnsNull()
    {
        var data = new byte[] { 3, (byte)'a', (byte)'b', (byte)'c', 0 };

        // Hostile offset beyond the buffer: the while-guard (current < data.Length) must short-circuit
        // before any indexing, so no IndexOutOfRangeException.
        var (result, _) = ParseName(data, data.Length + 32);

        Assert.IsNull(result, "An offset past the end must be rejected, not throw");
    }

    [TestMethod]
    public void RootNameOnly_ReturnsNull_AndAdvancesOffset()
    {
        // A lone 0x00 is the DNS root (an empty label set). Observed behavior: the parser requires at
        // least one label, so the root name is REJECTED (null), but the offset is still advanced past
        // the terminator. Documented, not assumed.
        var data = new byte[] { 0x00 };

        var (result, newOffset) = ParseName(data, 0);

        Assert.IsNull(result, "Root/empty name yields null under this parser");
        Assert.AreEqual(1, newOffset, "Offset still advances past the null terminator");
    }

    #endregion

    #region Well-formed baselines (direct, not socket duplicates)

    [TestMethod]
    public void ValidSingleLabel_Parses_AndReportsOffsetPastTerminator()
    {
        var data = new byte[] { 3, (byte)'a', (byte)'b', (byte)'c', 0 };

        var (result, newOffset) = ParseName(data, 0);

        Assert.AreEqual("abc", result);
        Assert.AreEqual(5, newOffset, "Offset must land just past the null terminator so the caller reads QTYPE next");
    }

    [TestMethod]
    public void MaxLabelLength63_Parses()
    {
        // 63 is the RFC 1035 max single-label length (0x3F, still below the 0xC0 pointer bits).
        var label = new byte[63];

        for (var i = 0; i < 63; i++)
        {
            label[i] = (byte)'x';
        }

        var data = new byte[1 + 63 + 1];

        data[0] = 63;

        Buffer.BlockCopy(label, 0, data, 1, 63);

        data[^1] = 0x00;

        var (result, _) = ParseName(data, 0);

        Assert.AreEqual(new string('x', 63), result, "A full 63-byte label is valid and must round-trip");
    }

    #endregion

    #region Truncated / over-long labels

    [TestMethod]
    public void LabelLengthPastEnd_ReturnsNull()
    {
        // Length byte claims 10 but only 3 bytes follow.
        var data = new byte[] { 10, (byte)'a', (byte)'b', (byte)'c' };

        var (result, _) = ParseName(data, 0);

        Assert.IsNull(result, "A label length that runs past the buffer must be rejected, not over-read");
    }

    [TestMethod]
    public void LabelFillsBufferWithNoTerminator_ReturnsNull()
    {
        // Label consumes every remaining byte, so there is no room for a null terminator.
        var data = new byte[] { 3, (byte)'a', (byte)'b', (byte)'c' };

        var (result, _) = ParseName(data, 0);

        Assert.IsNull(result, "An unterminated name (label reaches EOF) must be rejected");
    }

    [TestMethod]
    public void ReservedLabelType0x80_WithShortBuffer_ReturnsNull()
    {
        // 0x80 has the 10xxxxxx reserved bits. Only 0xC0 (11xxxxxx) is treated as a pointer, so 0x80 is
        // interpreted as a literal length of 128. With a short buffer that over-runs -> rejected.
        var data = new byte[] { 0x80, (byte)'a', (byte)'b' };

        var (result, _) = ParseName(data, 0);

        Assert.IsNull(result, "0x80 label byte becomes length 128, over-runs the short buffer -> null");
    }

    [TestMethod]
    public void ReservedLabelType0x40_TreatedAsLiteralLength64()
    {
        // 0x40 (01xxxxxx reserved) is NOT the 0xC0 pointer mask, so the parser treats it as a plain
        // length of 64. Given enough bytes it parses as a 64-char label. This documents that the parser
        // does not special-case the reserved 01/10 label types.
        var data = new byte[1 + 64 + 1];

        data[0] = 0x40; // length 64

        for (var i = 0; i < 64; i++)
        {
            data[1 + i] = (byte)'z';
        }

        data[^1] = 0x00;

        var (result, _) = ParseName(data, 0);

        Assert.AreEqual(new string('z', 64), result, "Reserved 0x40 label is read as a literal 64-byte label");
    }

    [TestMethod]
    public void OverLongName_Exceeds255_ReturnsNull()
    {
        // Four 63-byte labels = 4 * 64 = 256 > 255, tripping the RFC 1035 total-length cap.
        var data = new byte[4 * 64 + 1];

        var pos = 0;

        for (var l = 0; l < 4; l++)
        {
            data[pos++] = 63;

            for (var i = 0; i < 63; i++)
            {
                data[pos++] = (byte)'a';
            }
        }

        data[pos] = 0x00; // terminator (never reached - length cap fires first)

        var (result, _) = ParseName(data, 0);

        Assert.IsNull(result, "A name exceeding 255 bytes must be rejected");
    }

    #endregion

    #region Compression pointers (loops, out-of-range, truncated)

    [TestMethod]
    public void SelfReferentialPointer_ReturnsNull()
    {
        // 0xC0 0x00 at offset 0 points to offset 0 (itself). Must be rejected by the pointer >= current
        // guard, never followed into infinite recursion.
        var data = new byte[] { 0xC0, 0x00 };

        var (result, _) = ParseName(data, 0);

        Assert.IsNull(result, "A self-referential pointer must be rejected");
    }

    [TestMethod]
    public void ForwardPointer_ReturnsNull()
    {
        // Pointer at offset 0 aims forward to offset 4. Forward jumps are rejected (pointer >= current).
        var data = new byte[8];

        data[0] = 0xC0;
        data[1] = 0x04;
        data[4] = 3;
        data[5] = (byte)'a';
        data[6] = (byte)'b';
        data[7] = (byte)'c';

        var (result, _) = ParseName(data, 0);

        Assert.IsNull(result, "A forward compression pointer must be rejected");
    }

    [TestMethod]
    public void PointerTargetPastBuffer_ReturnsNull()
    {
        // Pointer value 0x00FF far exceeds the 2-byte buffer. current (0) <= 255, so pointer >= current
        // rejects it. No out-of-range indexing occurs.
        var data = new byte[] { 0xC0, 0xFF };

        var (result, _) = ParseName(data, 0);

        Assert.IsNull(result, "A pointer past the end of the buffer must be rejected, not indexed");
    }

    [TestMethod]
    public void TruncatedPointer_MissingSecondByte_ReturnsNull()
    {
        // A lone 0xC0 with no following low byte: current + 1 >= data.Length guard must catch it.
        var data = new byte[] { 0xC0 };

        var (result, _) = ParseName(data, 0);

        Assert.IsNull(result, "A pointer missing its second byte must be rejected, not over-read");
    }

    [TestMethod]
    public void LegitimateBackwardPointer_Parses()
    {
        // A real backward compression pointer: a null-terminated label lives at offset 0, and the name
        // to parse (starting at offset 6) is a single pointer back to it. Verifies the happy compression
        // path AND that the reported offset lands just past the 2-byte pointer (not past the target name).
        var data = new byte[8];

        data[0] = 3;
        data[1] = (byte)'a';
        data[2] = (byte)'b';
        data[3] = (byte)'c';
        data[4] = 0x00; // terminator for the target label
        // offset 5 unused
        data[6] = 0xC0;
        data[7] = 0x00; // pointer back to offset 0

        var (result, newOffset) = ParseName(data, 6);

        Assert.AreEqual("abc", result, "A backward pointer to a valid label must resolve");
        Assert.AreEqual(8, newOffset, "Offset must advance past the 2-byte pointer, not the target");
    }

    [TestMethod]
    public void NonDecreasingPointerChain_ReturnsNull()
    {
        // First pointer jumps back to offset 4 (minPointer = 4). At offset 4 a 2-byte label advances the
        // cursor to 7, then a second pointer aims back to offset 6. That target (6) is < current (7) but
        // >= the earlier minPointer (4) - a revisit-risk cycle that the monotonic-decrease guard rejects.
        var data = new byte[16];

        data[12] = 0xC0;
        data[13] = 0x04; // pointer -> offset 4

        data[4] = 2;            // label length 2 at offset 4
        data[5] = (byte)'h';
        data[6] = (byte)'i';
        data[7] = 0xC0;         // second pointer at offset 7
        data[8] = 0x06;         // -> offset 6, which is >= minPointer (4): must be rejected

        var (result, _) = ParseName(data, 12);

        Assert.IsNull(result, "A pointer chain that does not strictly decrease must be rejected");
    }

    #endregion

    #region Non-ASCII / control / injection bytes

    [TestMethod]
    public void HighBitBinaryLabel_DecodesWithoutThrowing()
    {
        // Bytes above 0x7F are illegal in ASCII text. Encoding.ASCII maps them to '?' rather than
        // throwing. Hostile binary in a label must not crash the parser.
        var data = new byte[] { 3, 0xFF, 0x80, 0xC1, 0x00 };

        // Note: 0xC1 here is label DATA (mid-label), not a length/pointer byte, so it is decoded, not
        // interpreted as a compression pointer.
        var (result, _) = ParseName(data, 0);

        Assert.IsNotNull(result, "A binary label must decode (lossily) rather than throw");
        Assert.AreEqual(3, result.Length, "All three label bytes are decoded to characters");
        Assert.AreEqual("???", result, "ASCII decoder replaces high-bit bytes with '?'");
    }

    [TestMethod]
    public void ControlCharsInLabel_DecodedLiterally()
    {
        // Control characters (0x01, 0x1F) are passed through verbatim by the ASCII decoder - the parser
        // performs no sanitization. Documents that hostile control bytes survive into the parsed name.
        var data = new byte[] { 2, 0x01, 0x1F, 0x00 };

        var (result, _) = ParseName(data, 0);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Length);
        Assert.AreEqual((char)0x01, result[0], "Control byte 0x01 is preserved");
        Assert.AreEqual((char)0x1F, result[1], "Control byte 0x1F is preserved");
    }

    [TestMethod]
    public void DotInsideLabel_ProducesAmbiguousName()
    {
        // A literal '.' (0x2E) inside a single label is decoded verbatim, so the joined name contains a
        // dot that did NOT come from a label boundary. The parser does no de-ambiguation - documented so
        // callers know embedded separators are possible.
        var data = new byte[] { 3, (byte)'a', (byte)'.', (byte)'b', 0x00 };

        var (result, _) = ParseName(data, 0);

        Assert.AreEqual("a.b", result, "An embedded dot is indistinguishable from a label separator once joined");
    }

    [TestMethod]
    public void MultipleLabelsWithEmbeddedDots_JoinWithDots()
    {
        // Two labels, one containing a dot, join as "a.b" + "." + "c" = "a.b.c" - the same string a
        // three-label name would produce. Confirms the injection-collision surface directly.
        var data = new byte[] { 3, (byte)'a', (byte)'.', (byte)'b', 1, (byte)'c', 0x00 };

        var (result, _) = ParseName(data, 0);

        Assert.AreEqual("a.b.c", result);
    }

    #endregion
}