// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting;
using VintageHive.Proxy.NetMeeting.H225;

namespace Adversarial2.H225;

// ----------------------------------------------------------
//  Adversarial tests for TPKT framing (RFC 1006) - ParsePayload
//  These exercise the PURE, synchronous byte[] parse path only.
//  The socket-based ReadAsync/WriteAsync paths are covered by the
//  happy-path suite and are intentionally NOT touched here.
// ----------------------------------------------------------

[TestClass]
public class TpktFrameAdversarialTests
{
    // --- Short / truncated headers -------------------------------------

    [TestMethod]
    public void ParsePayload_EmptyBuffer_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => TpktFrame.ParsePayload(Array.Empty<byte>()));
    }

    [TestMethod]
    public void ParsePayload_OneByte_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => TpktFrame.ParsePayload(new byte[] { 0x03 }));
    }

    [TestMethod]
    public void ParsePayload_ThreeBytes_JustUnderHeader_Throws()
    {
        // Version byte is valid, but there is no room for the 2-byte length field.
        Assert.ThrowsExactly<ArgumentException>(() => TpktFrame.ParsePayload(new byte[] { 0x03, 0x00, 0x00 }));
    }

    // --- Bad version byte ----------------------------------------------

    [TestMethod]
    public void ParsePayload_ZeroVersion_ThrowsInvalidData()
    {
        // A full 4-byte header, but version 0x00 instead of 0x03.
        var frame = new byte[] { 0x00, 0x00, 0x00, 0x04 };
        Assert.ThrowsExactly<InvalidDataException>(() => TpktFrame.ParsePayload(frame));
    }

    [TestMethod]
    public void ParsePayload_WrongVersion0x04_ThrowsInvalidData()
    {
        var frame = new byte[] { 0x04, 0x00, 0x00, 0x04 };
        Assert.ThrowsExactly<InvalidDataException>(() => TpktFrame.ParsePayload(frame));
    }

    [TestMethod]
    public void ParsePayload_HighVersion0xFF_ThrowsInvalidData()
    {
        var frame = new byte[] { 0xFF, 0x00, 0x00, 0x04 };
        Assert.ThrowsExactly<InvalidDataException>(() => TpktFrame.ParsePayload(frame));
    }

    [TestMethod]
    public void ParsePayload_VersionCheckedBeforeLength()
    {
        // Length field here is nonsense (declared 0), but version is wrong,
        // so the version check must fire first (InvalidData, not OverflowException).
        var frame = new byte[] { 0x02, 0x00, 0x00, 0x00 };
        Assert.ThrowsExactly<InvalidDataException>(() => TpktFrame.ParsePayload(frame));
    }

    // --- Declared length past the actual buffer ------------------------

    [TestMethod]
    public void ParsePayload_DeclaredLengthPastBuffer_Throws()
    {
        // Header claims a 100-byte frame, but only the 4-byte header is present.
        // ParsePayload allocates payloadLength (96) and Array.Copy over-reads the source.
        var frame = new byte[] { 0x03, 0x00, 0x00, 0x64 }; // totalLength = 100
        Assert.ThrowsExactly<ArgumentException>(() => TpktFrame.ParsePayload(frame));
    }

    [TestMethod]
    public void ParsePayload_DeclaredLengthMax_OneBytePayloadMissing_Throws()
    {
        // Declared totalLength = 0xFFFF (65535) => payloadLength 65531, but no payload bytes.
        // Allocation is bounded (2-byte length field caps it at ~64 KiB), Array.Copy then throws.
        var frame = new byte[] { 0x03, 0x00, 0xFF, 0xFF };
        Assert.ThrowsExactly<ArgumentException>(() => TpktFrame.ParsePayload(frame));
    }

    // --- Declared length shorter than the buffer (over-long buffer) -----

    [TestMethod]
    public void ParsePayload_DeclaredLengthShorterThanBuffer_ReturnsOnlyDeclared()
    {
        // Header declares total length 5 (=> 1 payload byte) but four trailing bytes follow.
        // Parser trusts the header and returns only the declared payload, silently dropping the rest.
        var frame = new byte[] { 0x03, 0x00, 0x00, 0x05, 0xAA, 0xBB, 0xCC, 0xDD };
        var payload = TpktFrame.ParsePayload(frame);

        Assert.AreEqual(1, payload.Length);
        Assert.AreEqual(0xAA, payload[0]);
    }

    // --- Zero / sub-header declared length ------------------------------

    [TestMethod]
    public void ParsePayload_ZeroDeclaredLength_Rejected()
    {
        // Declared totalLength = 0 => payloadLength = -4. The async ReadAsync path guards
        // this with an InvalidDataException; ParsePayload does NOT, so it dies allocating a
        // negative-size array (OverflowException). See bugsFound. We only assert that the
        // hostile frame is rejected (some exception), without endorsing the specific type.
        var frame = new byte[] { 0x03, 0x00, 0x00, 0x00 };
        AssertRejected(() => TpktFrame.ParsePayload(frame));
    }

    [TestMethod]
    public void ParsePayload_DeclaredLengthThree_SubHeader_Rejected()
    {
        // Declared totalLength = 3 (< 4-byte header) => payloadLength = -1. Same defect class.
        var frame = new byte[] { 0x03, 0x00, 0x00, 0x03 };
        AssertRejected(() => TpktFrame.ParsePayload(frame));
    }

    // --- Build guards ---------------------------------------------------

    [TestMethod]
    public void Build_PayloadTooLarge_WriteAsyncGuardMirrored()
    {
        // Build itself does not guard size, but the 2-byte length field silently wraps.
        // A payload of exactly MaxPayloadSize is the boundary that still fits.
        var frame = TpktFrame.Build(new byte[TpktFrame.MaxPayloadSize]);
        Assert.AreEqual(65535, frame.Length);
        Assert.AreEqual(65535, (frame[2] << 8) | frame[3]);
    }

    private static void AssertRejected(Action act)
    {
        var threw = false;

        try
        {
            act();
        }
        catch (Exception)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "Expected the malformed frame to be rejected with an exception.");
    }
}

// ----------------------------------------------------------
//  Adversarial tests for Q.931 message parsing (H.225.0 call signaling)
//  Pure byte[] -> Q931Message.Parse path. No sockets, no shared state.
// ----------------------------------------------------------

[TestClass]
public class Q931MessageAdversarialTests
{
    // --- Too short / truncated fixed header ----------------------------

    [TestMethod]
    public void Parse_EmptyBuffer_Throws()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Q931Message.Parse(Array.Empty<byte>()));
    }

    [TestMethod]
    public void Parse_ThreeBytes_Throws()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Q931Message.Parse(new byte[] { 0x08, 0x02, 0x00 }));
    }

    // --- Bad protocol discriminator ------------------------------------

    [TestMethod]
    public void Parse_WrongProtocolDiscriminator_Throws()
    {
        // First byte must be 0x08. 0x00 is a classic wrong value.
        var data = new byte[] { 0x00, 0x02, 0x00, 0x00 };
        Assert.ThrowsExactly<InvalidDataException>(() => Q931Message.Parse(data));
    }

    [TestMethod]
    public void Parse_HighProtocolDiscriminator_Throws()
    {
        var data = new byte[] { 0xFF, 0x02, 0x00, 0x00 };
        Assert.ThrowsExactly<InvalidDataException>(() => Q931Message.Parse(data));
    }

    // --- Call reference length boundaries ------------------------------

    [TestMethod]
    public void Parse_CallRefLengthZero_Throws()
    {
        // crLength must be 1 or 2. Zero is below the floor.
        var data = new byte[] { 0x08, 0x00, 0x00, 0x00 };
        Assert.ThrowsExactly<InvalidDataException>(() => Q931Message.Parse(data));
    }

    [TestMethod]
    public void Parse_CallRefLengthThree_Throws()
    {
        // crLength = 3 is above the ceiling of 2.
        var data = new byte[] { 0x08, 0x03, 0x00, 0x00, 0x00 };
        Assert.ThrowsExactly<InvalidDataException>(() => Q931Message.Parse(data));
    }

    [TestMethod]
    public void Parse_CallRefLengthMax_Throws()
    {
        var data = new byte[] { 0x08, 0xFF, 0x00, 0x00, 0x00 };
        Assert.ThrowsExactly<InvalidDataException>(() => Q931Message.Parse(data));
    }

    // --- Truncated before message type ---------------------------------

    [TestMethod]
    public void Parse_TruncatedBeforeMessageType_Throws()
    {
        // PD + crLength(2) + 2 CRV bytes = exactly 4 bytes, no message type byte.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00 };
        Assert.ThrowsExactly<InvalidDataException>(() => Q931Message.Parse(data));
    }

    // --- Minimal well-formed messages (boundary, not happy-path dup) ----

    [TestMethod]
    public void Parse_ShortestValidMessage_OneByteCallRef_NoIEs()
    {
        // PD, crLength=1, CRV=0x00, msgType=SETUP. No information elements at all.
        var data = new byte[] { 0x08, 0x01, 0x00, Q931Message.MSG_SETUP };
        var msg = Q931Message.Parse(data);

        Assert.AreEqual(Q931Message.MSG_SETUP, msg.MessageType);
        Assert.AreEqual(0, msg.CallReference);
        Assert.IsFalse(msg.CallReferenceFlag);
        Assert.AreEqual(0, msg.InformationElements.Count);
    }

    [TestMethod]
    public void Parse_OneByteCallRef_FlagBitStripped()
    {
        // crLength=1, CRV byte 0x80 => flag set, 7-bit value 0.
        var data = new byte[] { 0x08, 0x01, 0x80, Q931Message.MSG_CONNECT };
        var msg = Q931Message.Parse(data);

        Assert.IsTrue(msg.CallReferenceFlag);
        Assert.AreEqual(0, msg.CallReference);
    }

    [TestMethod]
    public void Parse_TwoByteCallRef_MaxValueAndFlag()
    {
        // crLength=2, CRV = 0xFF 0xFF => flag set, 15-bit value 0x7FFF (32767).
        var data = new byte[] { 0x08, 0x02, 0xFF, 0xFF, Q931Message.MSG_SETUP };
        var msg = Q931Message.Parse(data);

        Assert.IsTrue(msg.CallReferenceFlag);
        Assert.AreEqual(32767, msg.CallReference);
    }

    // --- Hostile IE length fields (best-effort truncation) --------------

    [TestMethod]
    public void Parse_StandardIE_LengthPastEnd_SilentlyDropped()
    {
        // Display IE (0x28) claims length 0xFF but no value bytes follow.
        // Parser breaks out best-effort and the IE is NOT added.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_DISPLAY, 0xFF };
        var msg = Q931Message.Parse(data);

        Assert.AreEqual(0, msg.InformationElements.Count);
        Assert.IsFalse(msg.InformationElements.ContainsKey(Q931Message.IE_DISPLAY));
    }

    [TestMethod]
    public void Parse_UserUserIE_TwoByteLengthPastEnd_SilentlyDropped()
    {
        // User-User IE (0x7E) with 2-byte length 0xFFFF but no payload.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_USER_USER, 0xFF, 0xFF };
        var msg = Q931Message.Parse(data);

        Assert.AreEqual(0, msg.InformationElements.Count);
    }

    [TestMethod]
    public void Parse_UserUserIE_TruncatedLengthField_SilentlyDropped()
    {
        // Only one of the two length bytes is present for the User-User IE.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_USER_USER, 0xFF };
        var msg = Q931Message.Parse(data);

        Assert.AreEqual(0, msg.InformationElements.Count);
    }

    [TestMethod]
    public void Parse_IETag_WithNoLengthByte_SilentlyDropped()
    {
        // A variable-length IE tag with nothing after it.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_DISPLAY };
        var msg = Q931Message.Parse(data);

        Assert.AreEqual(0, msg.InformationElements.Count);
    }

    [TestMethod]
    public void Parse_IE_LengthExactlyReachesEnd_Accepted()
    {
        // Boundary: Display IE length 1 with exactly one trailing byte. offset+len == data.Length.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_DISPLAY, 0x01, 0x41 };
        var msg = Q931Message.Parse(data);

        Assert.AreEqual(1, msg.InformationElements.Count);
        CollectionAssert.AreEqual(new byte[] { 0x41 }, msg.InformationElements[Q931Message.IE_DISPLAY]);
    }

    [TestMethod]
    public void Parse_ZeroLengthIE_PresentButEmpty()
    {
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_DISPLAY, 0x00 };
        var msg = Q931Message.Parse(data);

        Assert.IsTrue(msg.InformationElements.ContainsKey(Q931Message.IE_DISPLAY));
        Assert.AreEqual(0, msg.InformationElements[Q931Message.IE_DISPLAY].Length);
        Assert.AreEqual(string.Empty, msg.GetDisplay());
    }

    // --- Single-octet IEs and skipping ---------------------------------

    [TestMethod]
    public void Parse_SingleOctetIE_SkippedThenNextIEParsed()
    {
        // 0xA0 has bit 7 set and is not the User-User tag => treated as a single-octet IE and skipped.
        // The following Display IE must still parse normally.
        var data = new byte[]
        {
            0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP,
            0xA0,                                   // single-octet IE (skipped)
            Q931Message.IE_DISPLAY, 0x01, 0xAB      // variable IE that follows
        };

        var msg = Q931Message.Parse(data);

        Assert.IsFalse(msg.InformationElements.ContainsKey(0xA0));
        Assert.IsTrue(msg.InformationElements.ContainsKey(Q931Message.IE_DISPLAY));
        CollectionAssert.AreEqual(new byte[] { 0xAB }, msg.InformationElements[Q931Message.IE_DISPLAY]);
    }

    // --- Duplicate / repeated IE tags ----------------------------------

    [TestMethod]
    public void Parse_DuplicateIETags_LastWins()
    {
        // Two Display IEs with the same tag - the dictionary keeps the last.
        var data = new byte[]
        {
            0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP,
            Q931Message.IE_DISPLAY, 0x01, 0xAA,
            Q931Message.IE_DISPLAY, 0x01, 0xBB
        };

        var msg = Q931Message.Parse(data);

        Assert.AreEqual(1, msg.InformationElements.Count);
        CollectionAssert.AreEqual(new byte[] { 0xBB }, msg.InformationElements[Q931Message.IE_DISPLAY]);
    }

    // --- GetUuieData hostile inputs ------------------------------------

    [TestMethod]
    public void GetUuieData_WrongInnerProtocolDiscriminator_ReturnsNull()
    {
        // User-User IE present but its first (protocol-discriminator) byte is not 0x05.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_USER_USER, 0x00, 0x01, 0x99 };
        var msg = Q931Message.Parse(data);

        Assert.IsTrue(msg.InformationElements.ContainsKey(Q931Message.IE_USER_USER));
        Assert.IsNull(msg.GetUuieData());
    }

    [TestMethod]
    public void GetUuieData_EmptyUserUserIE_ReturnsNull()
    {
        // User-User IE with a zero-length value: uuie.Length < 1 => null (no index-out-of-range).
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_USER_USER, 0x00, 0x00 };
        var msg = Q931Message.Parse(data);

        Assert.IsTrue(msg.InformationElements.ContainsKey(Q931Message.IE_USER_USER));
        Assert.IsNull(msg.GetUuieData());
    }

    [TestMethod]
    public void GetUuieData_NotPresent_ReturnsNull()
    {
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP };
        var msg = Q931Message.Parse(data);

        Assert.IsNull(msg.GetUuieData());
    }

    // --- GetDisplay non-ASCII / control-char handling ------------------

    [TestMethod]
    public void GetDisplay_HighBytes_DecodedAsAsciiReplacement()
    {
        // Display IE carrying bytes > 0x7F. ASCII decoding maps each to '?'.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_DISPLAY, 0x02, 0xFF, 0x80 };
        var msg = Q931Message.Parse(data);

        Assert.AreEqual("??", msg.GetDisplay());
    }

    [TestMethod]
    public void GetDisplay_ControlChars_PassThroughUnsanitized()
    {
        // NUL, LF and ESC are valid 7-bit ASCII and are returned verbatim (no sanitization).
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, Q931Message.MSG_SETUP, Q931Message.IE_DISPLAY, 0x03, 0x00, 0x0A, 0x1B };
        var msg = Q931Message.Parse(data);

        var display = msg.GetDisplay();
        Assert.AreEqual(3, display.Length);
        Assert.AreEqual('\0', display[0]);
        Assert.AreEqual('\n', display[1]);
        Assert.AreEqual('\x1B', display[2]);
    }

    // --- Unknown message type is passed through untouched ---------------

    [TestMethod]
    public void Parse_UnknownMessageType_Preserved()
    {
        // Message type 0x99 is not a known constant; parser stores it verbatim.
        var data = new byte[] { 0x08, 0x02, 0x00, 0x00, 0x99 };
        var msg = Q931Message.Parse(data);

        Assert.AreEqual(0x99, msg.MessageType);
        Assert.IsTrue(Q931Message.MessageTypeName(0x99).Contains("Unknown"));
    }
}
