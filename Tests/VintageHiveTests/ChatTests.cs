// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.NetMeeting;
using VintageHive.Proxy.NetMeeting.Chat;
using VintageHive.Proxy.NetMeeting.T120;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  ChatMessage codec tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class ChatMessageTests
{
    // ── Header constants ─────────────────────────────────

    [TestMethod]
    public void HeaderLength_Is0x08()
    {
        Assert.AreEqual(0x08, ChatMessage.HEADER_LENGTH);
    }

    [TestMethod]
    public void HeaderSize_Is8()
    {
        Assert.AreEqual(8, ChatMessage.HEADER_SIZE);
    }

    // ── Encode basic ─────────────────────────────────────

    [TestMethod]
    public void Encode_SimpleText_ProducesCorrectPacket()
    {
        var packet = ChatMessage.Encode("Hi");

        // Header: 0x08 + 7 zeros
        Assert.AreEqual(0x08, packet[0]);
        for (var i = 1; i < 8; i++)
        {
            Assert.AreEqual(0, packet[i], $"Header byte {i} should be zero");
        }

        // "Hi" = 2 chars × 2 bytes = 4 bytes text + 2 null = 6 bytes payload
        // Total: 8 header + 4 text + 2 null terminator = 14
        Assert.AreEqual(14, packet.Length);

        // 'H' = 0x0048 LE
        Assert.AreEqual(0x48, packet[8]);
        Assert.AreEqual(0x00, packet[9]);

        // 'i' = 0x0069 LE
        Assert.AreEqual(0x69, packet[10]);
        Assert.AreEqual(0x00, packet[11]);

        // Null terminator
        Assert.AreEqual(0x00, packet[12]);
        Assert.AreEqual(0x00, packet[13]);
    }

    [TestMethod]
    public void Encode_EmptyString_ProducesHeaderPlusNullTerminator()
    {
        var packet = ChatMessage.Encode("");

        Assert.AreEqual(8 + 2, packet.Length); // header + null terminator only
        Assert.AreEqual(0x08, packet[0]);

        // Null terminator
        Assert.AreEqual(0x00, packet[8]);
        Assert.AreEqual(0x00, packet[9]);
    }

    [TestMethod]
    public void Encode_NullText_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ChatMessage.Encode(null));
    }

    [TestMethod]
    public void Encode_LongText_ProducesCorrectLength()
    {
        var text = new string('A', 500);
        var packet = ChatMessage.Encode(text);

        // 8 header + 500×2 UTF-16LE + 2 null terminator
        Assert.AreEqual(8 + 1000 + 2, packet.Length);
    }

    [TestMethod]
    public void Encode_UnicodeText_ProducesUtf16LE()
    {
        // Japanese: "こんにちは"
        var text = "\u3053\u3093\u306B\u3061\u306F";
        var packet = ChatMessage.Encode(text);

        // 8 header + 5×2 UTF-16LE + 2 null terminator
        Assert.AreEqual(8 + 10 + 2, packet.Length);

        // First char こ = U+3053 → LE: 0x53, 0x30
        Assert.AreEqual(0x53, packet[8]);
        Assert.AreEqual(0x30, packet[9]);
    }

    // ── Decode basic ─────────────────────────────────────

    [TestMethod]
    public void Decode_ValidPacket_ReturnsText()
    {
        var packet = ChatMessage.Encode("Hello World");
        var text = ChatMessage.Decode(packet);

        Assert.AreEqual("Hello World", text);
    }

    [TestMethod]
    public void Decode_EmptyPacket_ReturnsNull()
    {
        Assert.IsNull(ChatMessage.Decode(null));
        Assert.IsNull(ChatMessage.Decode(Array.Empty<byte>()));
        Assert.IsNull(ChatMessage.Decode(new byte[4]));
    }

    [TestMethod]
    public void Decode_InvalidHeaderLength_ReturnsNull()
    {
        var packet = ChatMessage.Encode("test");
        packet[0] = 0x09; // Wrong header length

        Assert.IsNull(ChatMessage.Decode(packet));
    }

    [TestMethod]
    public void Decode_NonZeroReservedByte_ReturnsNull()
    {
        var packet = ChatMessage.Encode("test");
        packet[3] = 0xFF; // Non-zero reserved byte

        Assert.IsNull(ChatMessage.Decode(packet));
    }

    [TestMethod]
    public void Decode_HeaderOnly_ReturnsEmptyString()
    {
        // Just the 8-byte header, no text data
        var packet = new byte[] { 0x08, 0, 0, 0, 0, 0, 0, 0 };
        var text = ChatMessage.Decode(packet);

        Assert.AreEqual(string.Empty, text);
    }

    [TestMethod]
    public void Decode_NullTerminatorOnly_ReturnsEmptyString()
    {
        // Header + just a null terminator
        var packet = new byte[] { 0x08, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var text = ChatMessage.Decode(packet);

        Assert.AreEqual(string.Empty, text);
    }

    [TestMethod]
    public void Decode_UnicodeText_Correct()
    {
        var original = "\u3053\u3093\u306B\u3061\u306F"; // こんにちは
        var packet = ChatMessage.Encode(original);
        var decoded = ChatMessage.Decode(packet);

        Assert.AreEqual(original, decoded);
    }

    // ── Round-trip ───────────────────────────────────────

    [TestMethod]
    public void RoundTrip_SimpleAscii()
    {
        var text = "Hello, NetMeeting!";
        Assert.AreEqual(text, ChatMessage.Decode(ChatMessage.Encode(text)));
    }

    [TestMethod]
    public void RoundTrip_EmptyString()
    {
        Assert.AreEqual("", ChatMessage.Decode(ChatMessage.Encode("")));
    }

    [TestMethod]
    public void RoundTrip_MixedUnicode()
    {
        var text = "Hello 世界 🌍"; // ASCII + CJK + emoji (surrogate pair)
        Assert.AreEqual(text, ChatMessage.Decode(ChatMessage.Encode(text)));
    }

    [TestMethod]
    public void RoundTrip_SpecialCharacters()
    {
        var text = "Line1\r\nLine2\tTab";
        Assert.AreEqual(text, ChatMessage.Decode(ChatMessage.Encode(text)));
    }

    [TestMethod]
    public void RoundTrip_MaxLength()
    {
        // NetMeeting chat box allows up to ~32KB of text
        var text = new string('X', 16000);
        Assert.AreEqual(text, ChatMessage.Decode(ChatMessage.Encode(text)));
    }

    // ── IsChatPacket ─────────────────────────────────────

    [TestMethod]
    public void IsChatPacket_ValidPacket_ReturnsTrue()
    {
        var packet = ChatMessage.Encode("test");
        Assert.IsTrue(ChatMessage.IsChatPacket(packet));
    }

    [TestMethod]
    public void IsChatPacket_HeaderOnly_ReturnsTrue()
    {
        var packet = new byte[] { 0x08, 0, 0, 0, 0, 0, 0, 0 };
        Assert.IsTrue(ChatMessage.IsChatPacket(packet));
    }

    [TestMethod]
    public void IsChatPacket_Null_ReturnsFalse()
    {
        Assert.IsFalse(ChatMessage.IsChatPacket(null));
    }

    [TestMethod]
    public void IsChatPacket_TooShort_ReturnsFalse()
    {
        Assert.IsFalse(ChatMessage.IsChatPacket(new byte[7]));
    }

    [TestMethod]
    public void IsChatPacket_WrongHeaderLength_ReturnsFalse()
    {
        var packet = new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0 };
        Assert.IsFalse(ChatMessage.IsChatPacket(packet));
    }

    [TestMethod]
    public void IsChatPacket_NonZeroReserved_ReturnsFalse()
    {
        var packet = new byte[] { 0x08, 0, 0, 0, 0x01, 0, 0, 0 };
        Assert.IsFalse(ChatMessage.IsChatPacket(packet));
    }

    [TestMethod]
    public void IsChatPacket_McsPayload_NotChat_ReturnsFalse()
    {
        // Some other MCS payload that starts differently
        var payload = new byte[] { 0x64, 0x00, 0x01, 0x03, 0xEB, 0x70, 0x80, 0x00 };
        Assert.IsFalse(ChatMessage.IsChatPacket(payload));
    }

    // ── GetPacketLength ──────────────────────────────────

    [TestMethod]
    public void GetPacketLength_EmptyString()
    {
        Assert.AreEqual(10, ChatMessage.GetPacketLength("")); // 8 + 0 + 2
    }

    [TestMethod]
    public void GetPacketLength_SimpleText()
    {
        Assert.AreEqual(14, ChatMessage.GetPacketLength("Hi")); // 8 + 4 + 2
    }

    [TestMethod]
    public void GetPacketLength_MatchesActualEncode()
    {
        var texts = new[] { "", "A", "Hello", "Test 123", new string('Z', 100) };

        foreach (var text in texts)
        {
            var predicted = ChatMessage.GetPacketLength(text);
            var actual = ChatMessage.Encode(text).Length;
            Assert.AreEqual(actual, predicted, $"Mismatch for text '{text}'");
        }
    }

    [TestMethod]
    public void GetPacketLength_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ChatMessage.GetPacketLength(null));
    }

    // ── Wire format verification ─────────────────────────

    [TestMethod]
    public void WireFormat_MatchesMsMnprSpec()
    {
        // Verify exact byte sequence matches MS-MNPR section 2.2.3 example
        // Message "Hi" should produce:
        // 08 00 00 00 00 00 00 00  48 00 69 00 00 00
        var packet = ChatMessage.Encode("Hi");

        var expected = new byte[]
        {
            0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // Header
            0x48, 0x00, // 'H' UTF-16LE
            0x69, 0x00, // 'i' UTF-16LE
            0x00, 0x00  // null terminator
        };

        CollectionAssert.AreEqual(expected, packet);
    }

    [TestMethod]
    public void WireFormat_SingleChar()
    {
        var packet = ChatMessage.Encode("A");

        var expected = new byte[]
        {
            0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x41, 0x00, // 'A' UTF-16LE
            0x00, 0x00  // null terminator
        };

        CollectionAssert.AreEqual(expected, packet);
    }
}

// ──────────────────────────────────────────────────────────
//  Chat over MCS integration tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class ChatMcsIntegrationTests
{
    [TestMethod]
    public void ChatPacket_WrappedInMcsSendData_RoundTrips()
    {
        // Simulate the full wrap: ChatMessage → MCS SendDataRequest → unwrap → ChatMessage
        var chatText = "Hello from NetMeeting!";
        var chatPacket = ChatMessage.Encode(chatText);

        // Wrap in MCS SendDataRequest (initiator=1001, channel=7, priority=high)
        var sendDataReq = McsCodec.EncodeSendDataRequest(1001, 7, McsConstants.PRIORITY_HIGH, chatPacket);

        // Server would decode this and relay as SendDataIndication
        var indication = McsCodec.EncodeSendDataIndication(1001, 7, McsConstants.PRIORITY_HIGH, chatPacket);

        // Recipient decodes MCS SendDataIndication
        var pdu = McsCodec.DecodeDomainPdu(indication);
        Assert.AreEqual(McsConstants.DOMAIN_SEND_DATA_INDICATION, pdu.Type);
        Assert.AreEqual(1001, pdu.Initiator);
        Assert.AreEqual(7, pdu.ChannelId);

        // Recipient checks if it's a chat packet and decodes
        Assert.IsTrue(ChatMessage.IsChatPacket(pdu.UserData));
        var decoded = ChatMessage.Decode(pdu.UserData);
        Assert.AreEqual(chatText, decoded);
    }

    [TestMethod]
    public void ChatPacket_OverX224AndTpkt_RoundTrips()
    {
        var chatText = "Testing T.120 chat!";
        var chatPacket = ChatMessage.Encode(chatText);

        // Wrap in MCS SendDataRequest
        var sendData = McsCodec.EncodeSendDataRequest(1001, 5, McsConstants.PRIORITY_TOP, chatPacket);

        // Wrap in X.224 DT
        var x224Dt = X224Message.BuildDataTransfer(sendData);

        // Wrap in TPKT
        var tpkt = TpktFrame.Build(x224Dt);

        // Now unwrap the full stack
        var tpktPayload = tpkt[4..]; // Skip TPKT header (4 bytes)
        var x224Parsed = X224Message.Parse(tpktPayload);
        Assert.AreEqual(X224Message.TYPE_DT, x224Parsed.Type);
        Assert.IsTrue(x224Parsed.Eot);

        var mcsPdu = McsCodec.DecodeDomainPdu(x224Parsed.Data);
        Assert.AreEqual(McsConstants.DOMAIN_SEND_DATA_REQUEST, mcsPdu.Type);

        Assert.IsTrue(ChatMessage.IsChatPacket(mcsPdu.UserData));
        Assert.AreEqual(chatText, ChatMessage.Decode(mcsPdu.UserData));
    }

    [TestMethod]
    public void NonChatMcsPayload_IsNotDetectedAsChat()
    {
        // An MCS ErectDomainRequest is not a chat message
        var erectDomain = McsCodec.EncodeErectDomainRequest(0, 0);
        Assert.IsFalse(ChatMessage.IsChatPacket(erectDomain));
    }

    [TestMethod]
    public void ChatPacket_MultipleMessages_AllDistinct()
    {
        // Verify different messages produce different packets
        var messages = new[] { "Hello", "World", "Test 123", "" };
        var packets = messages.Select(m => ChatMessage.Encode(m)).ToArray();

        for (var i = 0; i < packets.Length; i++)
        {
            for (var j = i + 1; j < packets.Length; j++)
            {
                Assert.IsFalse(
                    packets[i].SequenceEqual(packets[j]),
                    $"Packets for '{messages[i]}' and '{messages[j]}' should differ");
            }
        }
    }

    [TestMethod]
    public void ChatPacket_PreservesAllPriorities()
    {
        // Chat messages can be sent at any MCS priority level
        var priorities = new[]
        {
            McsConstants.PRIORITY_TOP,
            McsConstants.PRIORITY_HIGH,
            McsConstants.PRIORITY_MEDIUM,
            McsConstants.PRIORITY_LOW
        };

        var chatPacket = ChatMessage.Encode("Priority test");

        foreach (var priority in priorities)
        {
            var sendData = McsCodec.EncodeSendDataRequest(1001, 7, priority, chatPacket);
            var pdu = McsCodec.DecodeDomainPdu(sendData);
            Assert.AreEqual(priority, pdu.DataPriority);
            Assert.AreEqual("Priority test", ChatMessage.Decode(pdu.UserData));
        }
    }
}
