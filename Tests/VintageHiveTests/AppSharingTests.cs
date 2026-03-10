// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.AppSharing;
using VintageHive.Proxy.NetMeeting.T120;

#pragma warning disable MSTEST0025

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  AppSharing Constants tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class AppSharingConstantsTests
{
    [TestMethod]
    public void S20Types_HaveExpectedValues()
    {
        Assert.AreEqual(0x0031, AppSharingConstants.S20_CREATE);
        Assert.AreEqual(0x0032, AppSharingConstants.S20_JOIN);
        Assert.AreEqual(0x0033, AppSharingConstants.S20_RESPOND);
        Assert.AreEqual(0x0034, AppSharingConstants.S20_DELETE);
        Assert.AreEqual(0x0035, AppSharingConstants.S20_LEAVE);
        Assert.AreEqual(0x0036, AppSharingConstants.S20_END);
        Assert.AreEqual(0x0037, AppSharingConstants.S20_DATA);
        Assert.AreEqual(0x0038, AppSharingConstants.S20_COLLISION);
    }

    [TestMethod]
    public void PacketTypeName_AllKnownTypes()
    {
        Assert.AreEqual("S20_CREATE", AppSharingConstants.PacketTypeName(AppSharingConstants.S20_CREATE));
        Assert.AreEqual("S20_JOIN", AppSharingConstants.PacketTypeName(AppSharingConstants.S20_JOIN));
        Assert.AreEqual("S20_RESPOND", AppSharingConstants.PacketTypeName(AppSharingConstants.S20_RESPOND));
        Assert.AreEqual("S20_DELETE", AppSharingConstants.PacketTypeName(AppSharingConstants.S20_DELETE));
        Assert.AreEqual("S20_LEAVE", AppSharingConstants.PacketTypeName(AppSharingConstants.S20_LEAVE));
        Assert.AreEqual("S20_END", AppSharingConstants.PacketTypeName(AppSharingConstants.S20_END));
        Assert.AreEqual("S20_DATA", AppSharingConstants.PacketTypeName(AppSharingConstants.S20_DATA));
        Assert.AreEqual("S20_COLLISION", AppSharingConstants.PacketTypeName(AppSharingConstants.S20_COLLISION));
    }

    [TestMethod]
    public void PacketTypeName_Unknown_ReturnsGeneric()
    {
        Assert.IsTrue(AppSharingConstants.PacketTypeName(0x0099).Contains("0099"));
    }

    [TestMethod]
    public void DatatypeName_AllKnownTypes()
    {
        Assert.AreEqual("Update", AppSharingConstants.DatatypeName(AppSharingConstants.DT_UP));
        Assert.AreEqual("FontHandler", AppSharingConstants.DatatypeName(AppSharingConstants.DT_FH));
        Assert.AreEqual("ConfirmActive", AppSharingConstants.DatatypeName(AppSharingConstants.DT_CA));
        Assert.AreEqual("HostEntity", AppSharingConstants.DatatypeName(AppSharingConstants.DT_HET));
        Assert.AreEqual("DemandActive", AppSharingConstants.DatatypeName(AppSharingConstants.DT_DA));
    }

    [TestMethod]
    public void StreamName_AllKnownTypes()
    {
        Assert.AreEqual("Updates", AppSharingConstants.StreamName(AppSharingConstants.STREAM_UPDATES));
        Assert.AreEqual("Misc", AppSharingConstants.StreamName(AppSharingConstants.STREAM_MISC));
        Assert.AreEqual("Input", AppSharingConstants.StreamName(AppSharingConstants.STREAM_INPUT));
    }

    [TestMethod]
    public void CapsName_AllKnownTypes()
    {
        Assert.AreEqual("General", AppSharingConstants.CapsName(AppSharingConstants.CAPS_GENERAL));
        Assert.AreEqual("Bitmap", AppSharingConstants.CapsName(AppSharingConstants.CAPS_BITMAP));
        Assert.AreEqual("Order", AppSharingConstants.CapsName(AppSharingConstants.CAPS_ORDER));
        Assert.AreEqual("BitmapCache", AppSharingConstants.CapsName(AppSharingConstants.CAPS_BMPCACHE));
        Assert.AreEqual("Control", AppSharingConstants.CapsName(AppSharingConstants.CAPS_CONTROL));
        Assert.AreEqual("Activation", AppSharingConstants.CapsName(AppSharingConstants.CAPS_ACTIVATION));
        Assert.AreEqual("Pointer", AppSharingConstants.CapsName(AppSharingConstants.CAPS_POINTER));
    }

    [TestMethod]
    public void IsS20Packet_KnownTypes_ReturnsTrue()
    {
        Assert.IsTrue(AppSharingConstants.IsS20Packet(AppSharingConstants.S20_CREATE));
        Assert.IsTrue(AppSharingConstants.IsS20Packet(AppSharingConstants.S20_DATA));
        Assert.IsTrue(AppSharingConstants.IsS20Packet(AppSharingConstants.S20_COLLISION));
    }

    [TestMethod]
    public void IsS20Packet_Unknown_ReturnsFalse()
    {
        Assert.IsFalse(AppSharingConstants.IsS20Packet(0x0000));
        Assert.IsFalse(AppSharingConstants.IsS20Packet(0x0030));
        Assert.IsFalse(AppSharingConstants.IsS20Packet(0x0039));
    }

    [TestMethod]
    public void IsControlPacket_Excludes_DATA()
    {
        Assert.IsTrue(AppSharingConstants.IsControlPacket(AppSharingConstants.S20_CREATE));
        Assert.IsTrue(AppSharingConstants.IsControlPacket(AppSharingConstants.S20_LEAVE));
        Assert.IsFalse(AppSharingConstants.IsControlPacket(AppSharingConstants.S20_DATA));
    }

    [TestMethod]
    public void CpcallcapsSize_Is204()
    {
        Assert.AreEqual(204, AppSharingConstants.CPCALLCAPS_SIZE);
    }
}

// ──────────────────────────────────────────────────────────
//  S20_CREATE tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class S20CreateTests
{
    [TestMethod]
    public void Create_RoundTrip()
    {
        var pdu = new S20CreatePacket
        {
            User = 1001,
            Correlator = 0xAABBCCDD
        };

        var data = S20Codec.EncodeCreate(pdu);
        Assert.IsTrue(data.Length > 0);

        var msg = S20Codec.Decode(data);
        Assert.AreEqual(AppSharingConstants.S20_CREATE, msg.Type);
        Assert.IsNotNull(msg.Create);
        Assert.AreEqual(1001, msg.Create.User);
        Assert.AreEqual(0xAABBCCDDu, msg.Create.Correlator);
    }

    [TestMethod]
    public void Create_WithDefaultCaps_Has204ByteCaps()
    {
        var pdu = new S20CreatePacket
        {
            User = 1001,
            Correlator = 1
        };

        var data = S20Codec.EncodeCreate(pdu);
        // Total: 2 (length) + 2 (type) + 2 (user) + 4 (correlator) + 204 (caps) = 214
        Assert.AreEqual(214, data.Length);

        var msg = S20Codec.Decode(data);
        Assert.IsNotNull(msg.Create.Capabilities);
        Assert.AreEqual(204, msg.Create.Capabilities.Length);
    }

    [TestMethod]
    public void Create_CustomCaps_RoundTrip()
    {
        var customCaps = new byte[204];
        customCaps[0] = 0xCC;  // TotalLength low byte
        customCaps[1] = 0x00;
        customCaps[2] = 0x07;  // NumCapabilities low
        customCaps[3] = 0x00;  // NumCapabilities high
        customCaps[203] = 0xFF; // Marker at end

        var pdu = new S20CreatePacket
        {
            User = 2000,
            Correlator = 42,
            Capabilities = customCaps
        };

        var data = S20Codec.EncodeCreate(pdu);
        var msg = S20Codec.Decode(data);

        Assert.AreEqual(0xCC, msg.Create.Capabilities[0]);
        Assert.AreEqual(0xFF, msg.Create.Capabilities[203]);
    }

    [TestMethod]
    public void Create_WireFormat_LengthField()
    {
        var pdu = new S20CreatePacket { User = 1001, Correlator = 1 };
        var data = S20Codec.EncodeCreate(pdu);

        // Length field at offset 0-1 = body length (excludes 2-byte length prefix)
        var bodyLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0));
        Assert.AreEqual(data.Length - 2, bodyLen);

        // Type at offset 2-3
        var type = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        Assert.AreEqual(AppSharingConstants.S20_CREATE, type);
    }
}

// ──────────────────────────────────────────────────────────
//  S20_JOIN tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class S20JoinTests
{
    [TestMethod]
    public void Join_RoundTrip()
    {
        var pdu = new S20JoinPacket
        {
            User = 1002,
            Correlator = 0x12345678
        };

        var data = S20Codec.EncodeJoin(pdu);
        var msg = S20Codec.Decode(data);

        Assert.AreEqual(AppSharingConstants.S20_JOIN, msg.Type);
        Assert.AreEqual(1002, msg.Join.User);
        Assert.AreEqual(0x12345678u, msg.Join.Correlator);
    }

    [TestMethod]
    public void Join_WithCaps_SameSize_AsCreate()
    {
        var create = S20Codec.EncodeCreate(new S20CreatePacket { User = 1, Correlator = 1 });
        var join = S20Codec.EncodeJoin(new S20JoinPacket { User = 1, Correlator = 1 });

        Assert.AreEqual(create.Length, join.Length);
    }
}

// ──────────────────────────────────────────────────────────
//  S20_RESPOND tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class S20RespondTests
{
    [TestMethod]
    public void Respond_Positive_RoundTrip()
    {
        var pdu = new S20RespondPacket
        {
            User = 1001,
            Correlator = 0xAABBCCDD,
            Result = AppSharingConstants.RESPOND_POSITIVE
        };

        var data = S20Codec.EncodeRespond(pdu);
        Assert.AreEqual(14, data.Length); // 2+2+2+4+4

        var msg = S20Codec.Decode(data);
        Assert.AreEqual(AppSharingConstants.S20_RESPOND, msg.Type);
        Assert.AreEqual(1001, msg.Respond.User);
        Assert.AreEqual(0xAABBCCDDu, msg.Respond.Correlator);
        Assert.AreEqual(AppSharingConstants.RESPOND_POSITIVE, msg.Respond.Result);
    }

    [TestMethod]
    public void Respond_Negative_RoundTrip()
    {
        var pdu = new S20RespondPacket
        {
            User = 1002,
            Correlator = 42,
            Result = AppSharingConstants.RESPOND_NEGATIVE
        };

        var data = S20Codec.EncodeRespond(pdu);
        var msg = S20Codec.Decode(data);

        Assert.AreEqual(AppSharingConstants.RESPOND_NEGATIVE, msg.Respond.Result);
    }
}

// ──────────────────────────────────────────────────────────
//  S20_DELETE / LEAVE / END / COLLISION tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class S20SimpleControlTests
{
    [TestMethod]
    public void Delete_RoundTrip()
    {
        var data = S20Codec.EncodeDelete(new S20DeletePacket { User = 1001, Correlator = 99 });

        Assert.AreEqual(10, data.Length); // 2+2+2+4

        var msg = S20Codec.Decode(data);
        Assert.AreEqual(AppSharingConstants.S20_DELETE, msg.Type);
        Assert.AreEqual(1001, msg.Delete.User);
        Assert.AreEqual(99u, msg.Delete.Correlator);
    }

    [TestMethod]
    public void Leave_RoundTrip()
    {
        var data = S20Codec.EncodeLeave(new S20LeavePacket { User = 1002, Correlator = 50 });
        var msg = S20Codec.Decode(data);

        Assert.AreEqual(AppSharingConstants.S20_LEAVE, msg.Type);
        Assert.AreEqual(1002, msg.Leave.User);
        Assert.AreEqual(50u, msg.Leave.Correlator);
    }

    [TestMethod]
    public void End_RoundTrip()
    {
        var data = S20Codec.EncodeEnd(new S20EndPacket { User = 1001, Correlator = 1 });
        var msg = S20Codec.Decode(data);

        Assert.AreEqual(AppSharingConstants.S20_END, msg.Type);
        Assert.AreEqual(1001, msg.End.User);
        Assert.AreEqual(1u, msg.End.Correlator);
    }

    [TestMethod]
    public void Collision_RoundTrip()
    {
        var data = S20Codec.EncodeCollision(new S20CollisionPacket { User = 2000, Correlator = 0xDEAD });
        var msg = S20Codec.Decode(data);

        Assert.AreEqual(AppSharingConstants.S20_COLLISION, msg.Type);
        Assert.AreEqual(2000, msg.Collision.User);
        Assert.AreEqual(0xDEADu, msg.Collision.Correlator);
    }

    [TestMethod]
    public void AllSimpleControl_SameSize()
    {
        var del = S20Codec.EncodeDelete(new S20DeletePacket { User = 1, Correlator = 1 });
        var leave = S20Codec.EncodeLeave(new S20LeavePacket { User = 1, Correlator = 1 });
        var end = S20Codec.EncodeEnd(new S20EndPacket { User = 1, Correlator = 1 });
        var collision = S20Codec.EncodeCollision(new S20CollisionPacket { User = 1, Correlator = 1 });

        Assert.AreEqual(10, del.Length);
        Assert.AreEqual(10, leave.Length);
        Assert.AreEqual(10, end.Length);
        Assert.AreEqual(10, collision.Length);
    }
}

// ──────────────────────────────────────────────────────────
//  S20_DATA tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class S20DataTests
{
    [TestMethod]
    public void Data_RoundTrip()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        var pdu = new S20DataPacket
        {
            User = 1001,
            Correlator = 0xCAFEBABE,
            AckId = 0,
            Stream = AppSharingConstants.STREAM_UPDATES,
            Datatype = AppSharingConstants.DT_UP,
            CompressionType = AppSharingConstants.COMPRESS_NONE,
            Data = payload
        };

        var data = S20Codec.EncodeData(pdu);
        Assert.AreEqual(AppSharingConstants.S20_DATA_HEADER_SIZE + payload.Length, data.Length);

        var msg = S20Codec.Decode(data);
        Assert.AreEqual(AppSharingConstants.S20_DATA, msg.Type);
        Assert.IsNotNull(msg.DataPacket);
        Assert.AreEqual(1001, msg.DataPacket.User);
        Assert.AreEqual(0xCAFEBABEu, msg.DataPacket.Correlator);
        Assert.AreEqual((byte)0, msg.DataPacket.AckId);
        Assert.AreEqual(AppSharingConstants.STREAM_UPDATES, msg.DataPacket.Stream);
        Assert.AreEqual(AppSharingConstants.DT_UP, msg.DataPacket.Datatype);
        Assert.AreEqual(AppSharingConstants.COMPRESS_NONE, msg.DataPacket.CompressionType);
        CollectionAssert.AreEqual(payload, msg.DataPacket.Data);
    }

    [TestMethod]
    public void Data_EmptyPayload()
    {
        var pdu = new S20DataPacket
        {
            User = 1002,
            Correlator = 1,
            Stream = AppSharingConstants.STREAM_MISC,
            Datatype = AppSharingConstants.DT_SYNC,
            CompressionType = AppSharingConstants.COMPRESS_NONE,
            Data = Array.Empty<byte>()
        };

        var data = S20Codec.EncodeData(pdu);
        Assert.AreEqual(AppSharingConstants.S20_DATA_HEADER_SIZE, data.Length);

        var msg = S20Codec.Decode(data);
        Assert.AreEqual(0, msg.DataPacket.Data.Length);
    }

    [TestMethod]
    public void Data_NoLengthPrefix()
    {
        var pdu = new S20DataPacket
        {
            User = 1001,
            Correlator = 1,
            Stream = AppSharingConstants.STREAM_UPDATES,
            Datatype = AppSharingConstants.DT_UP,
            CompressionType = AppSharingConstants.COMPRESS_NONE,
            Data = new byte[] { 0xFF }
        };

        var data = S20Codec.EncodeData(pdu);

        // First two bytes should be the S20_DATA type value, NOT a length
        var firstWord = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0));
        Assert.AreEqual(AppSharingConstants.S20_DATA, firstWord);
    }

    [TestMethod]
    public void Data_AllDatatypes_RoundTrip()
    {
        byte[] datatypes = {
            AppSharingConstants.DT_UP, AppSharingConstants.DT_FH,
            AppSharingConstants.DT_CA, AppSharingConstants.DT_HET,
            AppSharingConstants.DT_SWL, AppSharingConstants.DT_AV,
            AppSharingConstants.DT_CM, AppSharingConstants.DT_BC,
            AppSharingConstants.DT_SYNC, AppSharingConstants.DT_CTRL,
            AppSharingConstants.DT_INPUT, AppSharingConstants.DT_DA
        };

        foreach (var dt in datatypes)
        {
            var pdu = new S20DataPacket
            {
                User = 1001,
                Correlator = 1,
                Stream = AppSharingConstants.STREAM_MISC,
                Datatype = dt,
                CompressionType = AppSharingConstants.COMPRESS_NONE,
                Data = new byte[] { 0x42 }
            };

            var data = S20Codec.EncodeData(pdu);
            var msg = S20Codec.Decode(data);
            Assert.AreEqual(dt, msg.DataPacket.Datatype, $"Datatype 0x{dt:X2} failed round-trip");
        }
    }

    [TestMethod]
    public void Data_AllStreams_RoundTrip()
    {
        byte[] streams = {
            AppSharingConstants.STREAM_UPDATES,
            AppSharingConstants.STREAM_MISC,
            AppSharingConstants.STREAM_INPUT
        };

        foreach (var stream in streams)
        {
            var pdu = new S20DataPacket
            {
                User = 1001,
                Correlator = 1,
                Stream = stream,
                Datatype = AppSharingConstants.DT_UP,
                CompressionType = AppSharingConstants.COMPRESS_NONE,
                Data = new byte[] { 0x00 }
            };

            var data = S20Codec.EncodeData(pdu);
            var msg = S20Codec.Decode(data);
            Assert.AreEqual(stream, msg.DataPacket.Stream);
        }
    }

    [TestMethod]
    public void Data_WithCompression_Flag()
    {
        var pdu = new S20DataPacket
        {
            User = 1001,
            Correlator = 1,
            Stream = AppSharingConstants.STREAM_UPDATES,
            Datatype = AppSharingConstants.DT_UP,
            CompressionType = AppSharingConstants.COMPRESS_PKZIP,
            Data = new byte[] { 0x78, 0x9C, 0x01, 0x02 }
        };

        var data = S20Codec.EncodeData(pdu);
        var msg = S20Codec.Decode(data);

        Assert.AreEqual(AppSharingConstants.COMPRESS_PKZIP, msg.DataPacket.CompressionType);
    }
}

// ──────────────────────────────────────────────────────────
//  CPCALLCAPS tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class CpCallCapsTests
{
    [TestMethod]
    public void EncodeDefault_Returns204Bytes()
    {
        var caps = CpCallCaps.EncodeDefault();
        Assert.AreEqual(204, caps.Length);
    }

    [TestMethod]
    public void EncodeDefault_Has7CapSets()
    {
        var caps = CpCallCaps.EncodeDefault();
        var parsed = CpCallCaps.Decode(caps);

        Assert.AreEqual(7, parsed.NumCapabilities);
    }

    [TestMethod]
    public void EncodeDefault_ContainsAllCapTypes()
    {
        var caps = CpCallCaps.EncodeDefault();
        var parsed = CpCallCaps.Decode(caps);

        Assert.IsNotNull(parsed.GetCapabilitySet(AppSharingConstants.CAPS_GENERAL));
        Assert.IsNotNull(parsed.GetCapabilitySet(AppSharingConstants.CAPS_BITMAP));
        Assert.IsNotNull(parsed.GetCapabilitySet(AppSharingConstants.CAPS_ORDER));
        Assert.IsNotNull(parsed.GetCapabilitySet(AppSharingConstants.CAPS_BMPCACHE));
        Assert.IsNotNull(parsed.GetCapabilitySet(AppSharingConstants.CAPS_CONTROL));
        Assert.IsNotNull(parsed.GetCapabilitySet(AppSharingConstants.CAPS_ACTIVATION));
        Assert.IsNotNull(parsed.GetCapabilitySet(AppSharingConstants.CAPS_POINTER));
    }

    [TestMethod]
    public void EncodeDefault_CapSetSizes()
    {
        var parsed = CpCallCaps.Decode(CpCallCaps.EncodeDefault());

        Assert.AreEqual(24, parsed.GetCapabilitySet(AppSharingConstants.CAPS_GENERAL).Length);
        Assert.AreEqual(28, parsed.GetCapabilitySet(AppSharingConstants.CAPS_BITMAP).Length);
        Assert.AreEqual(84, parsed.GetCapabilitySet(AppSharingConstants.CAPS_ORDER).Length);
        Assert.AreEqual(40, parsed.GetCapabilitySet(AppSharingConstants.CAPS_BMPCACHE).Length);
        Assert.AreEqual(12, parsed.GetCapabilitySet(AppSharingConstants.CAPS_CONTROL).Length);
        Assert.AreEqual(4, parsed.GetCapabilitySet(AppSharingConstants.CAPS_ACTIVATION).Length);
        Assert.AreEqual(8, parsed.GetCapabilitySet(AppSharingConstants.CAPS_POINTER).Length);
    }

    [TestMethod]
    public void Decode_TooShort_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CpCallCaps.Decode(new byte[100]));
    }

    [TestMethod]
    public void Decode_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CpCallCaps.Decode(null!));
    }

    [TestMethod]
    public void GetCapabilitySet_Unknown_ReturnsNull()
    {
        var parsed = CpCallCaps.Decode(CpCallCaps.EncodeDefault());
        Assert.IsNull(parsed.GetCapabilitySet(0x9999));
    }

    [TestMethod]
    public void ToString_ShowsSetsAndSize()
    {
        var parsed = CpCallCaps.Decode(CpCallCaps.EncodeDefault());
        var str = parsed.ToString();
        Assert.IsTrue(str.Contains("7"));
        Assert.IsTrue(str.Contains("204"));
    }
}

// ──────────────────────────────────────────────────────────
//  S20 packet detection tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class S20DetectionTests
{
    [TestMethod]
    public void PeekPacketType_Create()
    {
        var data = S20Codec.EncodeCreate(new S20CreatePacket { User = 1, Correlator = 1 });
        Assert.AreEqual(AppSharingConstants.S20_CREATE, S20Codec.PeekPacketType(data));
    }

    [TestMethod]
    public void PeekPacketType_Data()
    {
        var data = S20Codec.EncodeData(new S20DataPacket
        {
            User = 1,
            Correlator = 1,
            Stream = AppSharingConstants.STREAM_UPDATES,
            Datatype = AppSharingConstants.DT_UP,
            Data = new byte[] { 0x00 }
        });

        Assert.AreEqual(AppSharingConstants.S20_DATA, S20Codec.PeekPacketType(data));
    }

    [TestMethod]
    public void PeekPacketType_Null_ReturnsZero()
    {
        Assert.AreEqual(0, S20Codec.PeekPacketType(null!));
        Assert.AreEqual(0, S20Codec.PeekPacketType(new byte[0]));
        Assert.AreEqual(0, S20Codec.PeekPacketType(new byte[2]));
    }

    [TestMethod]
    public void IsS20Packet_ValidPackets()
    {
        var create = S20Codec.EncodeCreate(new S20CreatePacket { User = 1, Correlator = 1 });
        Assert.IsTrue(S20Codec.IsS20Packet(create));

        var data = S20Codec.EncodeData(new S20DataPacket
        {
            User = 1, Correlator = 1,
            Stream = 1, Datatype = 1, Data = Array.Empty<byte>()
        });
        Assert.IsTrue(S20Codec.IsS20Packet(data));
    }

    [TestMethod]
    public void IsS20Packet_Invalid_ReturnsFalse()
    {
        Assert.IsFalse(S20Codec.IsS20Packet(null!));
        Assert.IsFalse(S20Codec.IsS20Packet(new byte[] { 0x00, 0x00, 0x00, 0x00 }));
    }

    [TestMethod]
    public void Decode_TooShort_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => S20Codec.Decode(null!));
        Assert.ThrowsExactly<ArgumentException>(() => S20Codec.Decode(new byte[2]));
    }
}

// ──────────────────────────────────────────────────────────
//  S20 session sequence simulation
// ──────────────────────────────────────────────────────────

[TestClass]
public class AppSharingSessionTests
{
    [TestMethod]
    public void FullSession_CreateJoinRespondDataLeaveEnd()
    {
        // 1. Host sends CREATE with capabilities
        var create = S20Codec.EncodeCreate(new S20CreatePacket
        {
            User = 1001,
            Correlator = 0x00000001
        });

        var createMsg = S20Codec.Decode(create);
        Assert.AreEqual(AppSharingConstants.S20_CREATE, createMsg.Type);
        Assert.AreEqual(1001, createMsg.Create.User);

        // 2. Viewer sends JOIN with capabilities
        var join = S20Codec.EncodeJoin(new S20JoinPacket
        {
            User = 1002,
            Correlator = 0x00000001
        });

        var joinMsg = S20Codec.Decode(join);
        Assert.AreEqual(AppSharingConstants.S20_JOIN, joinMsg.Type);
        Assert.AreEqual(1002, joinMsg.Join.User);

        // 3. Host sends RESPOND (positive)
        var respond = S20Codec.EncodeRespond(new S20RespondPacket
        {
            User = 1001,
            Correlator = 0x00000001,
            Result = AppSharingConstants.RESPOND_POSITIVE
        });

        var respondMsg = S20Codec.Decode(respond);
        Assert.AreEqual(AppSharingConstants.RESPOND_POSITIVE, respondMsg.Respond.Result);

        // 4. Host sends screen update data
        var screenData = new byte[256];
        new Random(42).NextBytes(screenData);

        var update = S20Codec.EncodeData(new S20DataPacket
        {
            User = 1001,
            Correlator = 0x00000001,
            AckId = 0,
            Stream = AppSharingConstants.STREAM_UPDATES,
            Datatype = AppSharingConstants.DT_UP,
            CompressionType = AppSharingConstants.COMPRESS_NONE,
            Data = screenData
        });

        var updateMsg = S20Codec.Decode(update);
        Assert.AreEqual(AppSharingConstants.DT_UP, updateMsg.DataPacket.Datatype);
        CollectionAssert.AreEqual(screenData, updateMsg.DataPacket.Data);

        // 5. Viewer sends LEAVE
        var leave = S20Codec.EncodeLeave(new S20LeavePacket
        {
            User = 1002,
            Correlator = 0x00000001
        });

        var leaveMsg = S20Codec.Decode(leave);
        Assert.AreEqual(AppSharingConstants.S20_LEAVE, leaveMsg.Type);

        // 6. Host sends END
        var end = S20Codec.EncodeEnd(new S20EndPacket
        {
            User = 1001,
            Correlator = 0x00000001
        });

        var endMsg = S20Codec.Decode(end);
        Assert.AreEqual(AppSharingConstants.S20_END, endMsg.Type);
    }

    [TestMethod]
    public void CollisionScenario_TwoSimultaneousCreates()
    {
        // Two hosts try to create sessions simultaneously
        var create1 = S20Codec.EncodeCreate(new S20CreatePacket
        {
            User = 1001,
            Correlator = 0x00000001
        });

        var create2 = S20Codec.EncodeCreate(new S20CreatePacket
        {
            User = 1002,
            Correlator = 0x00000002
        });

        // Both decode fine
        var msg1 = S20Codec.Decode(create1);
        var msg2 = S20Codec.Decode(create2);
        Assert.AreEqual(1001, msg1.Create.User);
        Assert.AreEqual(1002, msg2.Create.User);

        // Lower-numbered user sends COLLISION
        var collision = S20Codec.EncodeCollision(new S20CollisionPacket
        {
            User = 1001,
            Correlator = 0x00000002
        });

        var collisionMsg = S20Codec.Decode(collision);
        Assert.AreEqual(AppSharingConstants.S20_COLLISION, collisionMsg.Type);
        Assert.AreEqual(1001, collisionMsg.Collision.User);
    }

    [TestMethod]
    public void HostDelete_RemovesViewer()
    {
        // Host forcibly removes a viewer
        var delete = S20Codec.EncodeDelete(new S20DeletePacket
        {
            User = 1001,
            Correlator = 0x00000001
        });

        var deleteMsg = S20Codec.Decode(delete);
        Assert.AreEqual(AppSharingConstants.S20_DELETE, deleteMsg.Type);
        Assert.AreEqual(1001, deleteMsg.Delete.User);
    }

    [TestMethod]
    public void S20Data_OverMcs_FullStack()
    {
        // S20_DATA wrapped in MCS SendData
        var screenPayload = new byte[] { 0x10, 0x20, 0x30, 0x40 };

        var s20Data = S20Codec.EncodeData(new S20DataPacket
        {
            User = 1001,
            Correlator = 0x00000001,
            AckId = 0,
            Stream = AppSharingConstants.STREAM_UPDATES,
            Datatype = AppSharingConstants.DT_UP,
            CompressionType = AppSharingConstants.COMPRESS_NONE,
            Data = screenPayload
        });

        // Wrap in MCS SendData
        var mcsData = McsCodec.EncodeSendDataRequest(1001, 5000,
            McsConstants.PRIORITY_HIGH, s20Data);

        // Decode MCS layer
        var mcsPdu = McsCodec.DecodeDomainPdu(mcsData);
        Assert.IsNotNull(mcsPdu.UserData);

        // Decode S20 layer
        var s20Msg = S20Codec.Decode(mcsPdu.UserData);
        Assert.AreEqual(AppSharingConstants.S20_DATA, s20Msg.Type);
        Assert.AreEqual(AppSharingConstants.DT_UP, s20Msg.DataPacket.Datatype);
        CollectionAssert.AreEqual(screenPayload, s20Msg.DataPacket.Data);
    }

    [TestMethod]
    public void S20Create_OverMcs_FullStack()
    {
        var s20Create = S20Codec.EncodeCreate(new S20CreatePacket
        {
            User = 1001,
            Correlator = 0x00000001
        });

        // Wrap in MCS SendData
        var mcsData = McsCodec.EncodeSendDataRequest(1001, 5000,
            McsConstants.PRIORITY_HIGH, s20Create);

        // Decode MCS layer
        var mcsPdu = McsCodec.DecodeDomainPdu(mcsData);

        // Decode S20 layer
        var s20Msg = S20Codec.Decode(mcsPdu.UserData);
        Assert.AreEqual(AppSharingConstants.S20_CREATE, s20Msg.Type);
        Assert.AreEqual(1001, s20Msg.Create.User);
        Assert.IsNotNull(s20Msg.Create.Capabilities);
    }

    [TestMethod]
    public void DemandActive_CapabilitiesExchange()
    {
        // DemandActive PDU carries capabilities — sent as S20_DATA with DT_DA
        var capData = CpCallCaps.EncodeDefault();

        var demandActive = S20Codec.EncodeData(new S20DataPacket
        {
            User = 1001,
            Correlator = 0x00000001,
            AckId = 0,
            Stream = AppSharingConstants.STREAM_MISC,
            Datatype = AppSharingConstants.DT_DA,
            CompressionType = AppSharingConstants.COMPRESS_NONE,
            Data = capData
        });

        var msg = S20Codec.Decode(demandActive);
        Assert.AreEqual(AppSharingConstants.DT_DA, msg.DataPacket.Datatype);
        Assert.AreEqual(204, msg.DataPacket.Data.Length);

        // Parse the embedded capabilities
        var caps = CpCallCaps.Decode(msg.DataPacket.Data);
        Assert.AreEqual(7, caps.NumCapabilities);
    }
}

#pragma warning restore MSTEST0025
