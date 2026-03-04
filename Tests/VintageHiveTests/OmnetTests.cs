// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.Omnet;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  OMNET Constants tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class OmnetConstantsTests
{
    [TestMethod]
    public void MessageName_AllKnownTypes()
    {
        Assert.AreEqual("HELLO", OmnetConstants.MessageName(OmnetConstants.MSG_HELLO));
        Assert.AreEqual("WELCOME", OmnetConstants.MessageName(OmnetConstants.MSG_WELCOME));
        Assert.AreEqual("LOCK_REQ", OmnetConstants.MessageName(OmnetConstants.MSG_LOCK_REQ));
        Assert.AreEqual("OBJECT_ADD", OmnetConstants.MessageName(OmnetConstants.MSG_OBJECT_ADD));
        Assert.AreEqual("OBJECT_DELETE", OmnetConstants.MessageName(OmnetConstants.MSG_OBJECT_DELETE));
        Assert.AreEqual("MORE_DATA", OmnetConstants.MessageName(OmnetConstants.MSG_MORE_DATA));
        Assert.AreEqual("WORKSET_NEW", OmnetConstants.MessageName(OmnetConstants.MSG_WORKSET_NEW));
    }

    [TestMethod]
    public void MessageName_Unknown_ReturnsGeneric()
    {
        Assert.IsTrue(OmnetConstants.MessageName(0xFFFF).Contains("FFFF"));
    }

    [TestMethod]
    public void IsJoinerMessage_CorrectForHelloWelcome()
    {
        Assert.IsTrue(OmnetConstants.IsJoinerMessage(OmnetConstants.MSG_HELLO));
        Assert.IsTrue(OmnetConstants.IsJoinerMessage(OmnetConstants.MSG_WELCOME));
        Assert.IsFalse(OmnetConstants.IsJoinerMessage(OmnetConstants.MSG_LOCK_REQ));
        Assert.IsFalse(OmnetConstants.IsJoinerMessage(OmnetConstants.MSG_OBJECT_ADD));
    }

    [TestMethod]
    public void IsLockMessage_CorrectRange()
    {
        Assert.IsTrue(OmnetConstants.IsLockMessage(OmnetConstants.MSG_LOCK_REQ));
        Assert.IsTrue(OmnetConstants.IsLockMessage(OmnetConstants.MSG_LOCK_GRANT));
        Assert.IsTrue(OmnetConstants.IsLockMessage(OmnetConstants.MSG_LOCK_DENY));
        Assert.IsTrue(OmnetConstants.IsLockMessage(OmnetConstants.MSG_UNLOCK));
        Assert.IsTrue(OmnetConstants.IsLockMessage(OmnetConstants.MSG_LOCK_NOTIFY));
        Assert.IsFalse(OmnetConstants.IsLockMessage(OmnetConstants.MSG_HELLO));
    }

    [TestMethod]
    public void HasObjectData_CorrectTypes()
    {
        Assert.IsTrue(OmnetConstants.HasObjectData(OmnetConstants.MSG_OBJECT_ADD));
        Assert.IsTrue(OmnetConstants.HasObjectData(OmnetConstants.MSG_OBJECT_REPLACE));
        Assert.IsTrue(OmnetConstants.HasObjectData(OmnetConstants.MSG_OBJECT_UPDATE));
        Assert.IsTrue(OmnetConstants.HasObjectData(OmnetConstants.MSG_OBJECT_CATCHUP));
        Assert.IsTrue(OmnetConstants.HasObjectData(OmnetConstants.MSG_MORE_DATA));
        Assert.IsFalse(OmnetConstants.HasObjectData(OmnetConstants.MSG_OBJECT_DELETE));
        Assert.IsFalse(OmnetConstants.HasObjectData(OmnetConstants.MSG_OBJECT_MOVE));
        Assert.IsFalse(OmnetConstants.HasObjectData(OmnetConstants.MSG_HELLO));
    }

    [TestMethod]
    public void WsGroupInfoStamp_IsOMWI()
    {
        // "OMWI" in LE: O=0x4F, M=0x4D, W=0x57, I=0x49
        // LE uint32: bytes are 4F 4D 57 49 → value = 0x49574D4F
        Assert.AreEqual(0x49574D4Fu, OmnetConstants.WSGROUP_INFO_STAMP);
    }
}

// ──────────────────────────────────────────────────────────
//  OMNET Joiner message tests (HELLO, WELCOME)
// ──────────────────────────────────────────────────────────

[TestClass]
public class OmnetJoinerTests
{
    [TestMethod]
    public void Hello_RoundTrip()
    {
        var data = OmnetCodec.EncodeHello(1001, OmnetConstants.CAPS_NO_COMPRESSION);
        Assert.AreEqual(OmnetConstants.JOINER_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_HELLO, msg.MessageType);
        Assert.AreEqual((ushort)1001, msg.Sender);
        Assert.AreEqual(OmnetConstants.CAPS_NO_COMPRESSION, msg.CompressionCaps);
    }

    [TestMethod]
    public void Welcome_RoundTrip()
    {
        var caps = OmnetConstants.CAPS_PKW_COMPRESSION | OmnetConstants.CAPS_NO_COMPRESSION;
        var data = OmnetCodec.EncodeWelcome(2000, caps);
        Assert.AreEqual(OmnetConstants.JOINER_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_WELCOME, msg.MessageType);
        Assert.AreEqual((ushort)2000, msg.Sender);
        Assert.AreEqual(caps, msg.CompressionCaps);
    }

    [TestMethod]
    public void Hello_IsDetectedAsOmnet()
    {
        var data = OmnetCodec.EncodeHello(1001, OmnetConstants.CAPS_NO_COMPRESSION);
        Assert.IsTrue(OmnetCodec.IsOmnetMessage(data));
    }

    [TestMethod]
    public void IsOmnetMessage_Null_ReturnsFalse()
    {
        Assert.IsFalse(OmnetCodec.IsOmnetMessage(null));
        Assert.IsFalse(OmnetCodec.IsOmnetMessage(Array.Empty<byte>()));
        Assert.IsFalse(OmnetCodec.IsOmnetMessage(new byte[3]));
    }

    [TestMethod]
    public void IsOmnetMessage_UnknownType_ReturnsFalse()
    {
        var data = new byte[12];
        data[2] = 0xFF;
        data[3] = 0xFF;
        Assert.IsFalse(OmnetCodec.IsOmnetMessage(data));
    }
}

// ──────────────────────────────────────────────────────────
//  OMNET Lock message tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class OmnetLockTests
{
    [TestMethod]
    public void LockReq_RoundTrip()
    {
        var data = OmnetCodec.EncodeLock(1001, OmnetConstants.MSG_LOCK_REQ,
            wsGroupId: 2, worksetId: 0);
        Assert.AreEqual(OmnetConstants.LOCK_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_LOCK_REQ, msg.MessageType);
        Assert.AreEqual((ushort)1001, msg.Sender);
        Assert.AreEqual((byte)2, msg.WsGroupId);
        Assert.AreEqual((byte)0, msg.WorksetId);
    }

    [TestMethod]
    public void LockGrant_WithCorrelator()
    {
        var data = OmnetCodec.EncodeLock(1001, OmnetConstants.MSG_LOCK_GRANT,
            wsGroupId: 2, worksetId: 1, correlator: 42);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_LOCK_GRANT, msg.MessageType);
        Assert.AreEqual((ushort)42, msg.LockCorrelator);
    }

    [TestMethod]
    public void AllLockTypes_Detected()
    {
        var types = new[]
        {
            OmnetConstants.MSG_LOCK_REQ,
            OmnetConstants.MSG_LOCK_GRANT,
            OmnetConstants.MSG_LOCK_DENY,
            OmnetConstants.MSG_UNLOCK,
            OmnetConstants.MSG_LOCK_NOTIFY
        };

        foreach (var t in types)
        {
            var data = OmnetCodec.EncodeLock(1001, t, 0, 0);
            Assert.IsTrue(OmnetCodec.IsOmnetMessage(data),
                $"Lock type 0x{t:X4} should be detected as OMNET");
        }
    }
}

// ──────────────────────────────────────────────────────────
//  OMNET WsGroup Send message tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class OmnetWsGroupSendTests
{
    [TestMethod]
    public void WsGroupSendReq_RoundTrip()
    {
        var objId = new OmnetObjectId { Sequence = 100, Creator = 1001 };
        var data = OmnetCodec.EncodeWsGroupSend(1001, OmnetConstants.MSG_WSGROUP_SEND_REQ,
            wsGroupId: 2, correlator: 7, objectId: objId, maxObjIdSeqUsed: 50);
        Assert.AreEqual(OmnetConstants.WSGROUP_SEND_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_WSGROUP_SEND_REQ, msg.MessageType);
        Assert.AreEqual((byte)2, msg.WsGroupId);
        Assert.AreEqual((ushort)7, msg.Correlator);
        Assert.AreEqual(100u, msg.ObjectId.Sequence);
        Assert.AreEqual((ushort)1001, msg.ObjectId.Creator);
        Assert.AreEqual(50u, msg.MaxObjIdSeqUsed);
    }

    [TestMethod]
    public void WsGroupSendComplete_RoundTrip()
    {
        var data = OmnetCodec.EncodeWsGroupSend(2000, OmnetConstants.MSG_WSGROUP_SEND_COMPLETE,
            wsGroupId: 0, correlator: 1);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_WSGROUP_SEND_COMPLETE, msg.MessageType);
        Assert.AreEqual((ushort)2000, msg.Sender);
    }
}

// ──────────────────────────────────────────────────────────
//  OMNET Operation message tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class OmnetOperationTests
{
    [TestMethod]
    public void ObjectAdd_WithData_RoundTrip()
    {
        var stamp = new OmnetSeqStamp { GenNumber = 1, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 42, Creator = 1001 };
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var data = OmnetCodec.EncodeObjectAdd(1001, wsGroupId: 2, worksetId: 0,
            position: OmnetConstants.POSITION_LAST, seqStamp: stamp, objectId: objId,
            objectData: payload);

        Assert.AreEqual(OmnetConstants.OBJECT_ADD_HEADER_SIZE + 4, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_OBJECT_ADD, msg.MessageType);
        Assert.AreEqual((ushort)1001, msg.Sender);
        Assert.AreEqual((byte)2, msg.WsGroupId);
        Assert.AreEqual((byte)0, msg.WorksetId);
        Assert.AreEqual(OmnetConstants.POSITION_LAST, msg.Position);
        Assert.AreEqual(1u, msg.SeqStamp.GenNumber);
        Assert.AreEqual((ushort)1001, msg.SeqStamp.NodeId);
        Assert.AreEqual(42u, msg.ObjectId.Sequence);
        Assert.AreEqual(4u, msg.TotalSize);
        Assert.IsNotNull(msg.Data);
        CollectionAssert.AreEqual(payload, msg.Data);
    }

    [TestMethod]
    public void ObjectAdd_EmptyData_RoundTrip()
    {
        var stamp = new OmnetSeqStamp { GenNumber = 0, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 1, Creator = 1001 };

        var data = OmnetCodec.EncodeObjectAdd(1001, 0, 0,
            OmnetConstants.POSITION_FIRST, stamp, objId, null);

        Assert.AreEqual(OmnetConstants.OBJECT_ADD_HEADER_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_OBJECT_ADD, msg.MessageType);
        Assert.IsNull(msg.Data);
    }

    [TestMethod]
    public void ObjectDelete_RoundTrip()
    {
        var stamp = new OmnetSeqStamp { GenNumber = 5, NodeId = 2000 };
        var objId = new OmnetObjectId { Sequence = 99, Creator = 1001 };

        var data = OmnetCodec.EncodeObjectDelete(2000, wsGroupId: 2, worksetId: 0,
            seqStamp: stamp, objectId: objId);

        Assert.AreEqual(OmnetConstants.OPERATION_HEADER_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_OBJECT_DELETE, msg.MessageType);
        Assert.AreEqual((ushort)2000, msg.Sender);
        Assert.AreEqual(5u, msg.SeqStamp.GenNumber);
        Assert.AreEqual(99u, msg.ObjectId.Sequence);
    }

    [TestMethod]
    public void ObjectReplace_WithData_RoundTrip()
    {
        var stamp = new OmnetSeqStamp { GenNumber = 3, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 10, Creator = 1001 };
        var payload = new byte[] { 0x01, 0x02, 0x03 };

        var data = OmnetCodec.EncodeObjectReplaceOrUpdate(1001,
            OmnetConstants.MSG_OBJECT_REPLACE, 2, 0, stamp, objId, payload);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_OBJECT_REPLACE, msg.MessageType);
        Assert.AreEqual(3u, msg.TotalSize);
        CollectionAssert.AreEqual(payload, msg.Data);
    }

    [TestMethod]
    public void ObjectUpdate_WithData_RoundTrip()
    {
        var stamp = new OmnetSeqStamp { GenNumber = 7, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 20, Creator = 1001 };
        var payload = new byte[] { 0xAA, 0xBB };

        var data = OmnetCodec.EncodeObjectReplaceOrUpdate(1001,
            OmnetConstants.MSG_OBJECT_UPDATE, 2, 0, stamp, objId, payload);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_OBJECT_UPDATE, msg.MessageType);
        CollectionAssert.AreEqual(payload, msg.Data);
    }

    [TestMethod]
    public void ObjectMove_RoundTrip()
    {
        var stamp = new OmnetSeqStamp { GenNumber = 2, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 5, Creator = 1001 };

        var data = OmnetCodec.EncodeObjectMove(1001, 2, 0,
            OmnetConstants.POSITION_FIRST, stamp, objId);

        Assert.AreEqual(OmnetConstants.OPERATION_HEADER_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_OBJECT_MOVE, msg.MessageType);
        Assert.AreEqual(OmnetConstants.POSITION_FIRST, msg.Position);
    }

    [TestMethod]
    public void WorksetNew_RoundTrip()
    {
        var stamp = new OmnetSeqStamp { GenNumber = 1, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 0, Creator = 1001 };

        var data = OmnetCodec.EncodeWorksetNew(1001, 0, 0, stamp, objId);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_WORKSET_NEW, msg.MessageType);
        Assert.AreEqual(1u, msg.SeqStamp.GenNumber);
    }

    [TestMethod]
    public void WorksetClear_RoundTrip()
    {
        var clearStamp = new OmnetSeqStamp { GenNumber = 10, NodeId = 2000 };

        var data = OmnetCodec.EncodeWorksetClear(2000, wsGroupId: 2, worksetId: 0,
            clearStamp: clearStamp);

        Assert.AreEqual(OmnetConstants.WORKSET_CLEAR_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_WORKSET_CLEAR, msg.MessageType);
        Assert.AreEqual(10u, msg.SeqStamp.GenNumber);
        Assert.AreEqual((ushort)2000, msg.SeqStamp.NodeId);
    }

    [TestMethod]
    public void MoreData_RoundTrip()
    {
        var payload = new byte[1024];
        new Random(42).NextBytes(payload);

        var data = OmnetCodec.EncodeMoreData(1001, payload);
        Assert.AreEqual(OmnetConstants.MORE_DATA_HEADER_SIZE + 1024, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_MORE_DATA, msg.MessageType);
        Assert.AreEqual((ushort)1001, msg.Sender);
        CollectionAssert.AreEqual(payload, msg.Data);
    }

    [TestMethod]
    public void MoreData_Empty_RoundTrip()
    {
        var data = OmnetCodec.EncodeMoreData(1001, null);
        Assert.AreEqual(OmnetConstants.MORE_DATA_HEADER_SIZE, data.Length);

        var msg = OmnetCodec.Decode(data);
        Assert.AreEqual(OmnetConstants.MSG_MORE_DATA, msg.MessageType);
        Assert.IsNull(msg.Data);
    }
}

// ──────────────────────────────────────────────────────────
//  WSGROUP_INFO tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class WsGroupInfoTests
{
    [TestMethod]
    public void WsGroupInfo_RoundTrip()
    {
        var info = new WsGroupInfo
        {
            ChannelId = 7,
            Creator = 1001,
            WsGroupId = 2,
            FunctionProfile = "SIASTD01",
            WsGroupName = "Whiteboard"
        };

        var data = info.Encode();
        Assert.AreEqual(64, data.Length);

        var decoded = WsGroupInfo.Decode(data);
        Assert.AreEqual((ushort)7, decoded.ChannelId);
        Assert.AreEqual((ushort)1001, decoded.Creator);
        Assert.AreEqual((byte)2, decoded.WsGroupId);
        Assert.AreEqual("SIASTD01", decoded.FunctionProfile);
        Assert.AreEqual("Whiteboard", decoded.WsGroupName);
    }

    [TestMethod]
    public void WsGroupInfo_EmptyStrings()
    {
        var info = new WsGroupInfo
        {
            ChannelId = 5,
            Creator = 2000,
            WsGroupId = 0,
            FunctionProfile = "",
            WsGroupName = ""
        };

        var data = info.Encode();
        var decoded = WsGroupInfo.Decode(data);
        Assert.AreEqual(string.Empty, decoded.FunctionProfile);
        Assert.AreEqual(string.Empty, decoded.WsGroupName);
    }

    [TestMethod]
    public void WsGroupInfo_StampIsOMWI()
    {
        var info = new WsGroupInfo
        {
            ChannelId = 1,
            Creator = 1,
            WsGroupId = 0,
            FunctionProfile = "TEST"
        };

        var data = info.Encode();

        // Offset 4: "OMWI" in bytes
        Assert.AreEqual((byte)'O', data[4]);
        Assert.AreEqual((byte)'M', data[5]);
        Assert.AreEqual((byte)'W', data[6]);
        Assert.AreEqual((byte)'I', data[7]);
    }

    [TestMethod]
    public void WsGroupInfo_InvalidStamp_Throws()
    {
        var info = new WsGroupInfo
        {
            ChannelId = 1, Creator = 1, WsGroupId = 0
        };

        var data = info.Encode();
        data[4] = 0xFF; // Corrupt the stamp

        Assert.ThrowsExactly<InvalidDataException>(() => WsGroupInfo.Decode(data));
    }

    [TestMethod]
    public void WsGroupInfo_TooShort_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => WsGroupInfo.Decode(new byte[10]));
    }

    [TestMethod]
    public void WsGroupInfo_ToString_Readable()
    {
        var info = new WsGroupInfo
        {
            ChannelId = 7,
            Creator = 1001,
            WsGroupId = 2,
            FunctionProfile = "SIASTD01",
            WsGroupName = "Whiteboard"
        };

        var str = info.ToString();
        Assert.IsTrue(str.Contains("2"));
        Assert.IsTrue(str.Contains("SIASTD01"));
        Assert.IsTrue(str.Contains("Whiteboard"));
    }
}

// ──────────────────────────────────────────────────────────
//  OMNET message over MCS integration tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class OmnetMcsIntegrationTests
{
    [TestMethod]
    public void OmnetHello_OverMcsSendData_RoundTrips()
    {
        var hello = OmnetCodec.EncodeHello(1001, OmnetConstants.CAPS_NO_COMPRESSION);

        // Wrap in MCS SendDataRequest
        var sendData = VintageHive.Proxy.NetMeeting.T120.McsCodec.EncodeSendDataRequest(
            1001, 7, VintageHive.Proxy.NetMeeting.T120.McsConstants.PRIORITY_TOP, hello);

        // Decode MCS
        var pdu = VintageHive.Proxy.NetMeeting.T120.McsCodec.DecodeDomainPdu(sendData);
        Assert.IsTrue(OmnetCodec.IsOmnetMessage(pdu.UserData));

        // Decode OMNET
        var msg = OmnetCodec.Decode(pdu.UserData);
        Assert.AreEqual(OmnetConstants.MSG_HELLO, msg.MessageType);
        Assert.AreEqual((ushort)1001, msg.Sender);
    }

    [TestMethod]
    public void OmnetObjectAdd_OverMcs_RoundTrips()
    {
        var stamp = new OmnetSeqStamp { GenNumber = 1, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 1, Creator = 1001 };
        var sipduData = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var objectAdd = OmnetCodec.EncodeObjectAdd(1001, 2, 0,
            OmnetConstants.POSITION_LAST, stamp, objId, sipduData);

        var sendData = VintageHive.Proxy.NetMeeting.T120.McsCodec.EncodeSendDataRequest(
            1001, 7, VintageHive.Proxy.NetMeeting.T120.McsConstants.PRIORITY_HIGH, objectAdd);

        var pdu = VintageHive.Proxy.NetMeeting.T120.McsCodec.DecodeDomainPdu(sendData);
        var msg = OmnetCodec.Decode(pdu.UserData);

        Assert.AreEqual(OmnetConstants.MSG_OBJECT_ADD, msg.MessageType);
        CollectionAssert.AreEqual(sipduData, msg.Data);
    }
}
