// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.H225;
using VintageHive.Proxy.NetMeeting.H245;

namespace Adversarial6.H245;

// ============================================================
//  ADVERSARIAL H.245 MultimediaSystemControlMessage decode tests
//
//  Pure entry point under attack: H245Codec.Decode(byte[]) -> H245Message.
//  The codec drives PerDecoder (APER, X.691) over attacker-controlled bytes.
//  We never open a socket or touch the async H245Handler proxy loop; every
//  test feeds bytes straight into the static decoder.
//
//  Message bytes are built with the internal PerEncoder so the framing bits
//  line up exactly with what the decoder expects; for out-of-range / malformed
//  cases we drop to raw WriteBit / WriteBits to place invalid values.
// ============================================================

[TestClass]
public class H245DecodeEmptyAndTruncatedTests
{
    [TestMethod]
    public void Decode_EmptyBuffer_ThrowsInvalidOperation()
    {
        // Top-level CHOICE needs 2 bits; a zero-length buffer has none.
        Assert.ThrowsExactly<InvalidOperationException>(() => H245Codec.Decode(System.Array.Empty<byte>()));
    }

    [TestMethod]
    public void Decode_Null_ThrowsNullReference()
    {
        // PerDecoder ctor dereferences data.Length with no null guard.
        // Not reachable from the handler (TpktFrame yields non-null payloads),
        // documented here as current behavior only.
        Assert.ThrowsExactly<NullReferenceException>(() => H245Codec.Decode(null));
    }

    [TestMethod]
    public void Decode_TruncatedMasterSlaveDetermination_ThrowsInvalidOperation()
    {
        // 0x02 = 0000 0010:
        //   bits 0-1  = 00      -> top-level request
        //   bit  2    = 0       -> RequestMessage not-extension
        //   bits 3-6  = 0001    -> sub-index 1 = MasterSlaveDetermination
        //   bit  7    = 0       -> SEQUENCE extension bit (no extensions)
        // terminalType INTEGER(0..255) then aligns to next octet and needs a
        // whole byte that is not present -> underflow.
        Assert.ThrowsExactly<InvalidOperationException>(() => H245Codec.Decode(new byte[] { 0x02 }));
    }

    [TestMethod]
    public void Decode_TruncatedTerminalCapabilitySetReject_ThrowsInvalidOperation()
    {
        // response + subIndex 4 (TCS reject) header only; the mandatory
        // sequenceNumber octet and cause bits are missing.
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.RSP_TERMINAL_CAPABILITY_SET_REJECT, H245Constants.RSP_ROOT_COUNT, extensible: true);
        // Stop before the SEQUENCE body: decode must underflow reading seqNum/cause.
        var bytes = enc.ToArray();

        Assert.ThrowsExactly<InvalidOperationException>(() => H245Codec.Decode(bytes));
    }
}

[TestClass]
public class H245DecodeTopLevelChoiceTests
{
    [TestMethod]
    public void Decode_CommandTopLevel_ReturnsGenericWithRawPayload()
    {
        // 0x80 = 1000 0000 -> top-level bits 10 = command(2). The codec does not
        // decode commands; it returns a passthrough envelope holding the raw bytes.
        var input = new byte[] { 0x80 };
        var msg = H245Codec.Decode(input);

        Assert.AreEqual(H245Constants.MSG_COMMAND, msg.TopLevel);
        Assert.AreEqual(-1, msg.SubIndex);
        Assert.IsNotNull(msg.RawPayload);
        CollectionAssert.AreEqual(input, msg.RawPayload);
        Assert.IsNull(msg.MasterSlaveDetermination);
        Assert.IsNull(msg.OpenLogicalChannel);
    }

    [TestMethod]
    public void Decode_IndicationTopLevel_ReturnsGenericWithRawPayload()
    {
        // 0xC0 = 1100 0000 -> top-level bits 11 = indication(3).
        var input = new byte[] { 0xC0 };
        var msg = H245Codec.Decode(input);

        Assert.AreEqual(H245Constants.MSG_INDICATION, msg.TopLevel);
        Assert.AreEqual(-1, msg.SubIndex);
        CollectionAssert.AreEqual(input, msg.RawPayload);
    }

    [TestMethod]
    public void Decode_TopLevelIsAlwaysInRange_TwoBitsCannotOverflow()
    {
        // The top-level CHOICE reads exactly 2 bits (4 root alternatives), so no
        // byte value can produce an out-of-range top-level index. Sweep all 256
        // single-byte inputs: none throws for the top-level read itself, and each
        // maps to one of the 4 documented top-level codes.
        for (var b = 0; b < 256; b++)
        {
            H245Message msg = null;

            try
            {
                msg = H245Codec.Decode(new byte[] { (byte)b, 0x00, 0x00, 0x00, 0x00 });
            }
            catch
            {
                // Deeper field reads may still throw; that is not what this test asserts.
                continue;
            }

            Assert.IsTrue(msg.TopLevel >= 0 && msg.TopLevel <= 3, $"TopLevel out of range for byte {b}");
        }
    }
}

[TestClass]
public class H245DecodeChoiceIndexEdgeTests
{
    [TestMethod]
    public void Decode_RequestRootIndexOutOfRange_AcceptedAsGeneric()
    {
        // RequestMessage is an 11-alternative extensible CHOICE. With the extension
        // bit clear the index is a 4-bit field (0..15) but only 0..10 are valid.
        // Feed index 15: a conformant decoder would reject, this one silently
        // returns a generic envelope. Lenient, but benign (all typed fields null).
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        enc.WriteBit(false);        // not an extension
        enc.WriteBits(15, 4);       // invalid root index 15
        var bytes = enc.ToArray();

        var msg = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_REQUEST, msg.TopLevel);
        Assert.AreEqual(15, msg.SubIndex);
        Assert.IsNull(msg.MasterSlaveDetermination);
        Assert.IsNull(msg.OpenLogicalChannel);
    }

    [TestMethod]
    public void Decode_ResponseRootIndexOutOfRange_AcceptedAsGeneric()
    {
        // ResponseMessage: 15-alternative extensible CHOICE -> 4-bit field, only
        // 0..14 valid. Index 15 is invalid yet accepted as a generic envelope.
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteBit(false);
        enc.WriteBits(15, 4);
        var bytes = enc.ToArray();

        var msg = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_RESPONSE, msg.TopLevel);
        Assert.AreEqual(15, msg.SubIndex);
        Assert.IsNull(msg.MasterSlaveDeterminationAck);
    }

    [TestMethod]
    public void Decode_RequestExtensionMarkerSet_ReturnsGenericExtensionIndex()
    {
        // Extension bit set on the RequestMessage CHOICE: the sub-index comes from
        // a normally-small number, not the root table. Index 5 in extension space
        // is unhandled -> generic envelope, no throw.
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        enc.WriteBit(true);                 // extension marker
        enc.WriteNormallySmallNumber(5);    // extension index 5
        var bytes = enc.ToArray();

        var msg = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_REQUEST, msg.TopLevel);
        Assert.AreEqual(5, msg.SubIndex);
        Assert.IsNull(msg.RoundTripDelayRequest);
    }

    [TestMethod]
    public void Decode_ResponseNonStandard_ReturnsGeneric()
    {
        // 0x40 = 0100 0000 -> response(1), ext bit 0, index 0000 = non-standard(0),
        // which is not deeply decoded -> generic envelope.
        var msg = H245Codec.Decode(new byte[] { 0x40 });

        Assert.AreEqual(H245Constants.MSG_RESPONSE, msg.TopLevel);
        Assert.AreEqual(H245Constants.RSP_NON_STANDARD, msg.SubIndex);
        Assert.IsNull(msg.MasterSlaveDeterminationAck);
    }
}

[TestClass]
public class H245DecodeExtensionRobustnessTests
{
    [TestMethod]
    public void Decode_MasterSlaveDetermination_ExtensionBitSet_MandatoryFieldsIntact()
    {
        // SEQUENCE extension bit set but trailing extension block malformed/absent.
        // TryReadExtensionAdditions is best-effort and must not corrupt the already
        // decoded mandatory fields.
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.REQ_MASTER_SLAVE_DETERMINATION, H245Constants.REQ_ROOT_COUNT, extensible: true);
        enc.WriteExtensionBit(true);        // SEQUENCE has extensions
        enc.WriteConstrainedWholeNumber(111, H245Constants.TERMINAL_TYPE_MIN, H245Constants.TERMINAL_TYPE_MAX);
        enc.WriteConstrainedWholeNumber(12345, H245Constants.STATUS_DETERMINATION_MIN, H245Constants.STATUS_DETERMINATION_MAX);
        // No extension-additions block written; decoder reads padding/EOF best-effort.
        var bytes = enc.ToArray();

        var msg = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.REQ_MASTER_SLAVE_DETERMINATION, msg.SubIndex);
        Assert.IsNotNull(msg.MasterSlaveDetermination);
        Assert.AreEqual(111, msg.MasterSlaveDetermination.TerminalType);
        Assert.AreEqual(12345, msg.MasterSlaveDetermination.StatusDeterminationNumber);
    }

    [TestMethod]
    public void Decode_TryReadExtensionAdditions_SwallowsTruncatedTail()
    {
        // Explicitly claim one present extension addition but provide no open-type
        // body. ReadOpenType underflows; TryReadExtensionAdditions swallows the
        // InvalidOperationException so decode still succeeds.
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.REQ_ROUND_TRIP_DELAY_REQUEST, H245Constants.REQ_ROOT_COUNT, extensible: true);
        enc.WriteExtensionBit(true);        // hasExt
        enc.WriteConstrainedWholeNumber(9, H245Constants.RTD_SEQ_MIN, H245Constants.RTD_SEQ_MAX);
        enc.WriteNormallySmallNumber(0);    // extension count - 1 = 0 -> 1 addition
        enc.WriteBit(true);                 // present[0] = true -> tries to read an open type
        var bytes = enc.ToArray();

        var msg = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.REQ_ROUND_TRIP_DELAY_REQUEST, msg.SubIndex);
        Assert.IsNotNull(msg.RoundTripDelayRequest);
        Assert.AreEqual(9, msg.RoundTripDelayRequest.SequenceNumber);
    }

    [TestMethod]
    public void Decode_ExtensionCountOverflow_IsRejectedNotOverflowed()
    {
        // ReadExtensionAdditions bounds the extension count (count < 1 || count > BitsRemaining) BEFORE
        // allocating, so an int.MaxValue normally-small value (which +1 overflows to int.MinValue) is
        // rejected with an InvalidOperationException the best-effort guard catches - never an
        // OverflowException or a multi-gigabyte allocation.
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H245Constants.MSG_RESPONSE, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.RSP_MASTER_SLAVE_DETERMINATION_REJECT, H245Constants.RSP_ROOT_COUNT, extensible: true);
        enc.WriteExtensionBit(true);                    // MSD-Reject SEQUENCE hasExt
        enc.WriteChoiceIndex(H245Constants.MSD_REJECT_IDENTICAL, H245Constants.MSD_REJECT_ROOT_COUNT); // cause (0 bits)
        enc.WriteNormallySmallNumber(int.MaxValue);     // extension count-1 = 2147483647 -> +1 overflows
        var bytes = enc.ToArray();

        try
        {
            H245Codec.Decode(bytes);
        }
        catch (OverflowException)
        {
            Assert.Fail("The extension-count guard must prevent an OverflowException");
        }
        catch (OutOfMemoryException)
        {
            Assert.Fail("The extension-count guard must prevent an out-of-memory allocation");
        }
        catch (InvalidOperationException)
        {
            // Acceptable: a strict decode may surface the rejected count as an underflow / invalid op.
        }
    }
}

[TestClass]
public class H245DecodeOpenLogicalChannelTests
{
    private static byte[] BuildOlcHeader(PerEncoder enc, int lcn)
    {
        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.REQ_OPEN_LOGICAL_CHANNEL, H245Constants.REQ_ROOT_COUNT, extensible: true);
        enc.WriteExtensionBit(false);                   // OLC hasExt
        enc.WriteOptionalBitmap(false);                 // reverseLogicalChannelParameters absent
        enc.WriteConstrainedWholeNumber(lcn, H245Constants.LCN_MIN, H245Constants.LCN_MAX);
        enc.WriteExtensionBit(false);                   // forwardLogicalChannelParameters hasExt
        enc.WriteOptionalBitmap(false);                 // portNumber absent
        return null;
    }

    private static void WriteH2250(PerEncoder enc, int sessionId, IPEndPoint media, IPEndPoint mediaControl)
    {
        enc.WriteChoiceIndex(0, 3, extensible: true);   // multiplexParameters = h2250LogicalChannelParameters
        enc.WriteExtensionBit(false);                   // H2250 hasExt
        // 6 optional flags: nonStandard, associatedSessionID, mediaChannel,
        // mediaGuaranteedDelivery, mediaControlChannel, mediaControlGuaranteedDelivery
        enc.WriteOptionalBitmap(false, false, media != null, false, mediaControl != null, false);
        enc.WriteConstrainedWholeNumber(sessionId, 0, 255);

        if (media != null)
        {
            H225Types.WriteTransportAddress(enc, media);
        }

        if (mediaControl != null)
        {
            H225Types.WriteTransportAddress(enc, mediaControl);
        }
    }

    [TestMethod]
    public void Decode_OpenLogicalChannel_NullData_Baseline_DecodesCleanly()
    {
        // A well-formed OLC with nullData (whose contents are genuinely empty, so
        // the SkipDataTypeContents no-op is correct here) must decode faithfully.
        var enc = new PerEncoder();
        BuildOlcHeader(enc, 7);
        enc.WriteChoiceIndex(H245Constants.DATA_TYPE_NULL_DATA, H245Constants.DATA_TYPE_ROOT_COUNT, extensible: true);
        WriteH2250(enc, H245Constants.SESSION_AUDIO, null, null);
        var bytes = enc.ToArray();

        var msg = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.REQ_OPEN_LOGICAL_CHANNEL, msg.SubIndex);
        Assert.IsNotNull(msg.OpenLogicalChannel);
        Assert.AreEqual(7, msg.OpenLogicalChannel.ForwardLogicalChannelNumber);
        Assert.AreEqual(H245Constants.DATA_TYPE_NULL_DATA, msg.OpenLogicalChannel.DataType);
        Assert.AreEqual(H245Constants.SESSION_AUDIO, msg.OpenLogicalChannel.SessionId);
        Assert.IsNull(msg.OpenLogicalChannel.MediaChannel);
        Assert.IsNull(msg.OpenLogicalChannel.MediaControlChannel);
    }

    [TestMethod]
    public void Decode_OpenLogicalChannel_NullData_WithTransportAddresses_ParsesEndpoints()
    {
        var media = new IPEndPoint(IPAddress.Parse("10.1.2.3"), 5004);
        var mediaCtrl = new IPEndPoint(IPAddress.Parse("10.1.2.3"), 5005);

        var enc = new PerEncoder();
        BuildOlcHeader(enc, 1);
        enc.WriteChoiceIndex(H245Constants.DATA_TYPE_NULL_DATA, H245Constants.DATA_TYPE_ROOT_COUNT, extensible: true);
        WriteH2250(enc, H245Constants.SESSION_VIDEO, media, mediaCtrl);
        var bytes = enc.ToArray();

        var msg = H245Codec.Decode(bytes);

        Assert.IsNotNull(msg.OpenLogicalChannel);
        Assert.AreEqual(H245Constants.SESSION_VIDEO, msg.OpenLogicalChannel.SessionId);
        Assert.IsNotNull(msg.OpenLogicalChannel.MediaChannel);
        Assert.AreEqual(5004, msg.OpenLogicalChannel.MediaChannel.Port);
        Assert.AreEqual("10.1.2.3", msg.OpenLogicalChannel.MediaChannel.Address.ToString());
        Assert.IsNotNull(msg.OpenLogicalChannel.MediaControlChannel);
        Assert.AreEqual(5005, msg.OpenLogicalChannel.MediaControlChannel.Port);
    }

    [TestMethod]
    public void Decode_OpenLogicalChannel_AudioData_CannotBeParsedFaithfully()
    {
        // BUG: SkipDataTypeContents -> SkipCapabilityChoice is an empty no-op, so a
        // real audioData OLC (which carries an AudioCapability CHOICE before the
        // multiplexParameters) is misaligned: the decoder reads the capability bits
        // as the multiplexParameters CHOICE. With the bytes below the misread lands
        // on a bogus length octet and Decode throws NotSupportedException ("PER
        // fragmentation is not supported") - exactly the media the proxy is meant
        // to relay. We assert only that a faithful round-trip is impossible so the
        // test stays valid whether the misparse throws or yields wrong endpoints.
        var media = new IPEndPoint(IPAddress.Parse("192.168.0.50"), 49170);
        var mediaCtrl = new IPEndPoint(IPAddress.Parse("192.168.0.50"), 49171);

        var enc = new PerEncoder();
        BuildOlcHeader(enc, 101);
        enc.WriteChoiceIndex(H245Constants.DATA_TYPE_AUDIO_DATA, H245Constants.DATA_TYPE_ROOT_COUNT, extensible: true);
        // AudioCapability CHOICE (extensible, ~14 root): g711Ulaw64k with a value.
        enc.WriteChoiceIndex(3, 14, extensible: true);
        enc.WriteConstrainedWholeNumber(20, 1, 256);    // maxAl-sduAudioFrames
        WriteH2250(enc, H245Constants.SESSION_AUDIO, media, mediaCtrl);
        var bytes = enc.ToArray();

        Exception thrown = null;
        OpenLogicalChannel olc = null;

        try
        {
            olc = H245Codec.Decode(bytes).OpenLogicalChannel;
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        var faithful = thrown == null
            && olc != null
            && olc.SessionId == H245Constants.SESSION_AUDIO
            && olc.MediaChannel != null
            && olc.MediaChannel.Port == 49170
            && olc.MediaControlChannel != null
            && olc.MediaControlChannel.Port == 49171;

        Assert.IsFalse(faithful, "audioData OLC unexpectedly round-tripped; the SkipCapabilityChoice no-op may have been fixed");
    }
}

[TestClass]
public class H245DecodeBoundedLengthTests
{
    [TestMethod]
    public void Decode_TerminalCapabilitySet_OversizedOidLength_ThrowsBoundedNotOom()
    {
        // TerminalCapabilitySet reads an OBJECT IDENTIFIER whose length is an
        // unconstrained length determinant. Declaring 100 octets with none present
        // must fail the ReadOctets bounds check (InvalidOperationException) rather
        // than attempt an oversized allocation. This confirms the declared-length
        // path is buffer-bounded (length determinant caps at 16383, so count*8
        // cannot overflow int).
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H245Constants.MSG_REQUEST, H245Constants.MSG_ROOT_COUNT);
        enc.WriteChoiceIndex(H245Constants.REQ_TERMINAL_CAPABILITY_SET, H245Constants.REQ_ROOT_COUNT, extensible: true);
        enc.WriteExtensionBit(false);                   // TCS hasExt
        enc.WriteOptionalBitmap(false, false, false);   // multiplexCapability/table/descriptors absent
        enc.WriteConstrainedWholeNumber(1, H245Constants.TCS_SEQ_MIN, H245Constants.TCS_SEQ_MAX);
        enc.WriteLengthDeterminant(100);                // OID claims 100 octets, none follow
        var bytes = enc.ToArray();

        Assert.ThrowsExactly<InvalidOperationException>(() => H245Codec.Decode(bytes));
    }
}
