// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using VintageHive.Proxy.NetMeeting;
using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.T120;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  X.224 PDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class X224MessageTests
{
    [TestMethod]
    public void Parse_ConnectionRequest_ValidFields()
    {
        var pdu = X224Message.BuildConnectionRequest(srcRef: 0x1234, classOptions: 0x00);
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_CR, parsed.Type);
        Assert.AreEqual((ushort)0, parsed.DstRef);
        Assert.AreEqual((ushort)0x1234, parsed.SrcRef);
        Assert.AreEqual((byte)0x00, parsed.ClassOptions);
    }

    [TestMethod]
    public void Parse_ConnectionConfirm_ValidFields()
    {
        var pdu = X224Message.BuildConnectionConfirm(dstRef: 0xABCD, srcRef: 0x5678);
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_CC, parsed.Type);
        Assert.AreEqual((ushort)0xABCD, parsed.DstRef);
        Assert.AreEqual((ushort)0x5678, parsed.SrcRef);
        Assert.AreEqual((byte)0x00, parsed.ClassOptions);
    }

    [TestMethod]
    public void Parse_DataTransfer_WithUserData()
    {
        var userData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var pdu = X224Message.BuildDataTransfer(userData);
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_DT, parsed.Type);
        Assert.IsTrue(parsed.Eot);
        CollectionAssert.AreEqual(userData, parsed.Data);
    }

    [TestMethod]
    public void Parse_DataTransfer_NoEot()
    {
        var pdu = X224Message.BuildDataTransfer(new byte[] { 0xFF }, eot: false);
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_DT, parsed.Type);
        Assert.IsFalse(parsed.Eot);
    }

    [TestMethod]
    public void Parse_DataTransfer_EmptyData()
    {
        var pdu = X224Message.BuildDataTransfer(Array.Empty<byte>());
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_DT, parsed.Type);
        Assert.IsTrue(parsed.Eot);
        Assert.IsNull(parsed.Data); // No data after header
    }

    [TestMethod]
    public void Parse_DisconnectRequest_ValidFields()
    {
        var pdu = X224Message.BuildDisconnectRequest(dstRef: 0x1111, srcRef: 0x2222, reason: 3);
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_DR, parsed.Type);
        Assert.AreEqual((ushort)0x1111, parsed.DstRef);
        Assert.AreEqual((ushort)0x2222, parsed.SrcRef);
        Assert.AreEqual((byte)3, parsed.ClassOptions); // Reason stored in ClassOptions
    }

    [TestMethod]
    public void Parse_ConnectionRequest_WithCookie()
    {
        var cookie = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var pdu = X224Message.BuildConnectionRequest(srcRef: 1, cookie: cookie);
        var parsed = X224Message.Parse(pdu);

        Assert.AreEqual(X224Message.TYPE_CR, parsed.Type);
        Assert.AreEqual((ushort)1, parsed.SrcRef);
        Assert.IsNotNull(parsed.Data);
        CollectionAssert.AreEqual(cookie, parsed.Data);
    }

    [TestMethod]
    public void Parse_TooShort_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            X224Message.Parse(new byte[] { 0x01 }));
    }

    [TestMethod]
    public void Parse_UnknownType_Throws()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            X224Message.Parse(new byte[] { 6, 0x30, 0, 0, 0, 0, 0 }));
    }

    [TestMethod]
    public void TypeName_AllTypes()
    {
        Assert.AreEqual("ConnectionRequest", X224Message.TypeName(X224Message.TYPE_CR));
        Assert.AreEqual("ConnectionConfirm", X224Message.TypeName(X224Message.TYPE_CC));
        Assert.AreEqual("DataTransfer", X224Message.TypeName(X224Message.TYPE_DT));
        Assert.AreEqual("DisconnectRequest", X224Message.TypeName(X224Message.TYPE_DR));
        Assert.IsTrue(X224Message.TypeName(0x70).Contains("0x70"));
    }

    [TestMethod]
    public void BuildAndParse_RoundTrip_CR()
    {
        var original = X224Message.BuildConnectionRequest(srcRef: 42);
        var parsed = X224Message.Parse(original);
        var rebuilt = X224Message.BuildConnectionRequest(srcRef: parsed.SrcRef);

        CollectionAssert.AreEqual(original, rebuilt);
    }

    [TestMethod]
    public void BuildAndParse_RoundTrip_CC()
    {
        var original = X224Message.BuildConnectionConfirm(dstRef: 100, srcRef: 200);
        var parsed = X224Message.Parse(original);
        var rebuilt = X224Message.BuildConnectionConfirm(dstRef: parsed.DstRef, srcRef: parsed.SrcRef);

        CollectionAssert.AreEqual(original, rebuilt);
    }
}

// ──────────────────────────────────────────────────────────
//  MCS Constants tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class McsConstantsTests
{
    [TestMethod]
    public void DomainPduName_KnownTypes()
    {
        Assert.AreEqual("ErectDomainRequest", McsConstants.DomainPduName(McsConstants.DOMAIN_ERECT_DOMAIN_REQUEST));
        Assert.AreEqual("AttachUserRequest", McsConstants.DomainPduName(McsConstants.DOMAIN_ATTACH_USER_REQUEST));
        Assert.AreEqual("AttachUserConfirm", McsConstants.DomainPduName(McsConstants.DOMAIN_ATTACH_USER_CONFIRM));
        Assert.AreEqual("ChannelJoinRequest", McsConstants.DomainPduName(McsConstants.DOMAIN_CHANNEL_JOIN_REQUEST));
        Assert.AreEqual("ChannelJoinConfirm", McsConstants.DomainPduName(McsConstants.DOMAIN_CHANNEL_JOIN_CONFIRM));
        Assert.AreEqual("SendDataRequest", McsConstants.DomainPduName(McsConstants.DOMAIN_SEND_DATA_REQUEST));
        Assert.AreEqual("SendDataIndication", McsConstants.DomainPduName(McsConstants.DOMAIN_SEND_DATA_INDICATION));
        Assert.AreEqual("DisconnectProviderUltimatum",
            McsConstants.DomainPduName(McsConstants.DOMAIN_DISCONNECT_PROVIDER_ULTIMATUM));
    }

    [TestMethod]
    public void DomainPduName_Unknown_ReturnsGeneric()
    {
        var name = McsConstants.DomainPduName(99);
        Assert.IsTrue(name.Contains("99"));
    }

    [TestMethod]
    public void DefaultDomainParameters_HasExpectedValues()
    {
        var dp = McsDomainParameters.Default;

        Assert.AreEqual(34, dp.MaxChannelIds);
        Assert.AreEqual(2, dp.MaxUserIds);
        Assert.AreEqual(0, dp.MaxTokenIds);
        Assert.AreEqual(1, dp.NumPriorities);
        Assert.AreEqual(0, dp.MinThroughput);
        Assert.AreEqual(1, dp.MaxHeight);
        Assert.AreEqual(65535, dp.MaxMcsPduSize);
        Assert.AreEqual(2, dp.ProtocolVersion);
    }
}

// ──────────────────────────────────────────────────────────
//  MCS Connect-Initial / Connect-Response (BER) tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class McsConnectTests
{
    [TestMethod]
    public void ConnectInitial_EncodeDecodeRoundTrip()
    {
        var ci = new McsConnectInitial
        {
            CallingDomainParameters = McsDomainParameters.Default,
            MinimumDomainParameters = new McsDomainParameters
            {
                MaxChannelIds = 1,
                MaxUserIds = 1,
                MaxTokenIds = 0,
                NumPriorities = 1,
                MinThroughput = 0,
                MaxHeight = 1,
                MaxMcsPduSize = 1024,
                ProtocolVersion = 2
            },
            MaximumDomainParameters = new McsDomainParameters
            {
                MaxChannelIds = 100,
                MaxUserIds = 100,
                MaxTokenIds = 100,
                NumPriorities = 4,
                MinThroughput = 0,
                MaxHeight = 4,
                MaxMcsPduSize = 65535,
                ProtocolVersion = 2
            },
            UserData = new byte[] { 0x00, 0x05, 0x00, 0x14, 0x7C, 0x00, 0x01 }
        };

        var encoded = McsCodec.EncodeConnectInitial(ci);
        Assert.IsTrue(McsCodec.IsConnectInitial(encoded));

        var decoded = McsCodec.DecodeConnectInitial(encoded);
        Assert.AreEqual(ci.CallingDomainParameters.MaxChannelIds, decoded.CallingDomainParameters.MaxChannelIds);
        Assert.AreEqual(ci.CallingDomainParameters.MaxUserIds, decoded.CallingDomainParameters.MaxUserIds);
        Assert.AreEqual(ci.CallingDomainParameters.MaxMcsPduSize, decoded.CallingDomainParameters.MaxMcsPduSize);
        Assert.AreEqual(ci.MinimumDomainParameters.MaxChannelIds, decoded.MinimumDomainParameters.MaxChannelIds);
        Assert.AreEqual(ci.MaximumDomainParameters.MaxChannelIds, decoded.MaximumDomainParameters.MaxChannelIds);
        CollectionAssert.AreEqual(ci.UserData, decoded.UserData);
    }

    [TestMethod]
    public void ConnectResponse_EncodeDecodeRoundTrip()
    {
        var cr = new McsConnectResponse
        {
            Result = McsConstants.RESULT_SUCCESSFUL,
            CalledConnectId = 0,
            DomainParameters = McsDomainParameters.Default,
            UserData = new byte[] { 0x01, 0x02, 0x03 }
        };

        var encoded = McsCodec.EncodeConnectResponse(cr);
        Assert.IsTrue(McsCodec.IsConnectResponse(encoded));

        var decoded = McsCodec.DecodeConnectResponse(encoded);
        Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, decoded.Result);
        Assert.AreEqual(0, decoded.CalledConnectId);
        Assert.AreEqual(cr.DomainParameters.MaxChannelIds, decoded.DomainParameters.MaxChannelIds);
        Assert.AreEqual(cr.DomainParameters.MaxMcsPduSize, decoded.DomainParameters.MaxMcsPduSize);
        CollectionAssert.AreEqual(cr.UserData, decoded.UserData);
    }

    [TestMethod]
    public void IsConnectInitial_RejectsResponse()
    {
        var cr = McsCodec.EncodeConnectResponse(new McsConnectResponse
        {
            Result = McsConstants.RESULT_SUCCESSFUL,
            DomainParameters = McsDomainParameters.Default
        });

        Assert.IsFalse(McsCodec.IsConnectInitial(cr));
    }

    [TestMethod]
    public void IsConnectResponse_RejectsInitial()
    {
        var ci = McsCodec.EncodeConnectInitial(new McsConnectInitial
        {
            CallingDomainParameters = McsDomainParameters.Default
        });

        Assert.IsFalse(McsCodec.IsConnectResponse(ci));
    }

    [TestMethod]
    public void IsConnectInitial_NullOrShort_ReturnsFalse()
    {
        Assert.IsFalse(McsCodec.IsConnectInitial(null!));
        Assert.IsFalse(McsCodec.IsConnectInitial(new byte[] { 0x7F }));
        Assert.IsFalse(McsCodec.IsConnectInitial(Array.Empty<byte>()));
    }

    [TestMethod]
    public void ConnectInitial_EmptyUserData_RoundTrip()
    {
        var ci = new McsConnectInitial
        {
            CallingDomainParameters = McsDomainParameters.Default,
            MinimumDomainParameters = McsDomainParameters.Default,
            MaximumDomainParameters = McsDomainParameters.Default,
            UserData = Array.Empty<byte>()
        };

        var encoded = McsCodec.EncodeConnectInitial(ci);
        var decoded = McsCodec.DecodeConnectInitial(encoded);

        Assert.AreEqual(0, decoded.UserData.Length);
    }

    [TestMethod]
    public void ConnectResponse_FailureResult_RoundTrip()
    {
        var cr = new McsConnectResponse
        {
            Result = McsConstants.RESULT_USER_REJECTED,
            CalledConnectId = 42,
            DomainParameters = McsDomainParameters.Default,
            UserData = Array.Empty<byte>()
        };

        var encoded = McsCodec.EncodeConnectResponse(cr);
        var decoded = McsCodec.DecodeConnectResponse(encoded);

        Assert.AreEqual(McsConstants.RESULT_USER_REJECTED, decoded.Result);
        Assert.AreEqual(42, decoded.CalledConnectId);
    }
}

// ──────────────────────────────────────────────────────────
//  MCS Domain PDU (PER) tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class McsDomainPduTests
{
    [TestMethod]
    public void ErectDomainRequest_EncodeDecodeRoundTrip()
    {
        var encoded = McsCodec.EncodeErectDomainRequest(subHeight: 1, subInterval: 0);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_ERECT_DOMAIN_REQUEST, decoded.Type);
        Assert.AreEqual(1, decoded.SubHeight);
        Assert.AreEqual(0, decoded.SubInterval);
    }

    [TestMethod]
    public void AttachUserRequest_EncodeDecodeRoundTrip()
    {
        var encoded = McsCodec.EncodeAttachUserRequest();
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_ATTACH_USER_REQUEST, decoded.Type);
    }

    [TestMethod]
    public void AttachUserConfirm_Successful_WithInitiator()
    {
        var encoded = McsCodec.EncodeAttachUserConfirm(McsConstants.RESULT_SUCCESSFUL, initiator: 1001);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_ATTACH_USER_CONFIRM, decoded.Type);
        Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, decoded.Result);
        Assert.AreEqual(1001, decoded.Initiator);
    }

    [TestMethod]
    public void AttachUserConfirm_NoInitiator()
    {
        var encoded = McsCodec.EncodeAttachUserConfirm(McsConstants.RESULT_SUCCESSFUL, initiator: 0);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_ATTACH_USER_CONFIRM, decoded.Type);
        Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, decoded.Result);
        Assert.AreEqual(0, decoded.Initiator);
    }

    [TestMethod]
    public void ChannelJoinRequest_EncodeDecodeRoundTrip()
    {
        var encoded = McsCodec.EncodeChannelJoinRequest(userId: 1001, channelId: 7);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_CHANNEL_JOIN_REQUEST, decoded.Type);
        Assert.AreEqual(1001, decoded.UserId);
        Assert.AreEqual(7, decoded.ChannelId);
    }

    [TestMethod]
    public void ChannelJoinConfirm_EncodeDecodeRoundTrip()
    {
        var encoded = McsCodec.EncodeChannelJoinConfirm(
            McsConstants.RESULT_SUCCESSFUL, userId: 1001, channelId: 7);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_CHANNEL_JOIN_CONFIRM, decoded.Type);
        Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, decoded.Result);
        Assert.AreEqual(1001, decoded.UserId);
        Assert.AreEqual(7, decoded.ChannelId);
    }

    [TestMethod]
    public void SendDataRequest_EncodeDecodeRoundTrip()
    {
        var userData = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var encoded = McsCodec.EncodeSendDataRequest(
            initiator: 1001, channelId: 7,
            priority: McsConstants.PRIORITY_HIGH, userData: userData);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_SEND_DATA_REQUEST, decoded.Type);
        Assert.AreEqual(1001, decoded.Initiator);
        Assert.AreEqual(7, decoded.ChannelId);
        Assert.AreEqual(McsConstants.PRIORITY_HIGH, decoded.DataPriority);
        CollectionAssert.AreEqual(userData, decoded.UserData);
    }

    [TestMethod]
    public void SendDataIndication_EncodeDecodeRoundTrip()
    {
        var userData = new byte[] { 0x01, 0x02, 0x03 };
        var encoded = McsCodec.EncodeSendDataIndication(
            initiator: 1001, channelId: 5,
            priority: McsConstants.PRIORITY_LOW, userData: userData);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_SEND_DATA_INDICATION, decoded.Type);
        Assert.AreEqual(1001, decoded.Initiator);
        Assert.AreEqual(5, decoded.ChannelId);
        Assert.AreEqual(McsConstants.PRIORITY_LOW, decoded.DataPriority);
        CollectionAssert.AreEqual(userData, decoded.UserData);
    }

    [TestMethod]
    public void DisconnectProviderUltimatum_EncodeDecodeRoundTrip()
    {
        var encoded = McsCodec.EncodeDisconnectProviderUltimatum(reason: 1);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(McsConstants.DOMAIN_DISCONNECT_PROVIDER_ULTIMATUM, decoded.Type);
        Assert.AreEqual(1, decoded.Result); // Reason stored in Result
    }

    [TestMethod]
    public void SendDataRequest_LargePayload()
    {
        var userData = new byte[4096];
        new Random(42).NextBytes(userData);

        var encoded = McsCodec.EncodeSendDataRequest(
            initiator: 1001, channelId: 7,
            priority: McsConstants.PRIORITY_MEDIUM, userData: userData);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        CollectionAssert.AreEqual(userData, decoded.UserData);
    }

    [TestMethod]
    public void ChannelJoinRequest_BoundaryValues()
    {
        // Min userId (1), max channelId (65535)
        var encoded = McsCodec.EncodeChannelJoinRequest(userId: 1, channelId: 65535);
        var decoded = McsCodec.DecodeDomainPdu(encoded);

        Assert.AreEqual(1, decoded.UserId);
        Assert.AreEqual(65535, decoded.ChannelId);
    }

    [TestMethod]
    public void McsDomainPdu_ToString_ReturnsName()
    {
        var pdu = new McsDomainPdu { Type = McsConstants.DOMAIN_SEND_DATA_REQUEST };
        Assert.AreEqual("SendDataRequest", pdu.ToString());
    }
}

// ──────────────────────────────────────────────────────────
//  BER length encoding tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class McsBerHelperTests
{
    [TestMethod]
    public void BerLength_ShortForm_RoundTrip()
    {
        var stream = new MemoryStream();
        McsCodec.WriteBerLength(stream, 50);
        var data = stream.ToArray();

        Assert.AreEqual(1, data.Length);
        Assert.AreEqual(50, data[0]);

        var offset = 0;
        var length = McsCodec.ReadBerLength(data, ref offset);
        Assert.AreEqual(50, length);
        Assert.AreEqual(1, offset);
    }

    [TestMethod]
    public void BerLength_OneByteLong_RoundTrip()
    {
        var stream = new MemoryStream();
        McsCodec.WriteBerLength(stream, 200);
        var data = stream.ToArray();

        Assert.AreEqual(2, data.Length);
        Assert.AreEqual(0x81, data[0]);
        Assert.AreEqual(200, data[1]);

        var offset = 0;
        var length = McsCodec.ReadBerLength(data, ref offset);
        Assert.AreEqual(200, length);
    }

    [TestMethod]
    public void BerLength_TwoByteLong_RoundTrip()
    {
        var stream = new MemoryStream();
        McsCodec.WriteBerLength(stream, 1000);
        var data = stream.ToArray();

        Assert.AreEqual(3, data.Length);
        Assert.AreEqual(0x82, data[0]);

        var offset = 0;
        var length = McsCodec.ReadBerLength(data, ref offset);
        Assert.AreEqual(1000, length);
    }
}

// ──────────────────────────────────────────────────────────
//  X.224 + TPKT integration tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class X224TpktIntegrationTests
{
    [TestMethod]
    public void TpktWrappedX224DT_RoundTrip()
    {
        var userData = new byte[] { 0xAA, 0xBB, 0xCC };
        var dt = X224Message.BuildDataTransfer(userData);
        var tpkt = TpktFrame.Build(dt);

        // Parse TPKT
        var payload = TpktFrame.ParsePayload(tpkt);
        // Parse X.224
        var x224 = X224Message.Parse(payload);

        Assert.AreEqual(X224Message.TYPE_DT, x224.Type);
        Assert.IsTrue(x224.Eot);
        CollectionAssert.AreEqual(userData, x224.Data);
    }

    [TestMethod]
    public void TpktWrappedX224CR_CC_Handshake()
    {
        // Client sends CR
        var cr = X224Message.BuildConnectionRequest(srcRef: 0x0042);
        var crFrame = TpktFrame.Build(cr);

        // Parse CR
        var crPayload = TpktFrame.ParsePayload(crFrame);
        var crParsed = X224Message.Parse(crPayload);

        Assert.AreEqual(X224Message.TYPE_CR, crParsed.Type);
        Assert.AreEqual((ushort)0x0042, crParsed.SrcRef);

        // Server responds with CC
        var cc = X224Message.BuildConnectionConfirm(
            dstRef: crParsed.SrcRef,
            srcRef: 0x0001);
        var ccFrame = TpktFrame.Build(cc);

        var ccPayload = TpktFrame.ParsePayload(ccFrame);
        var ccParsed = X224Message.Parse(ccPayload);

        Assert.AreEqual(X224Message.TYPE_CC, ccParsed.Type);
        Assert.AreEqual((ushort)0x0042, ccParsed.DstRef);
        Assert.AreEqual((ushort)0x0001, ccParsed.SrcRef);
    }

    [TestMethod]
    public void McsConnectInitial_OverX224DT_OverTpkt()
    {
        // Build MCS Connect-Initial
        var ci = McsCodec.EncodeConnectInitial(new McsConnectInitial
        {
            CallingDomainParameters = McsDomainParameters.Default,
            MinimumDomainParameters = McsDomainParameters.Default,
            MaximumDomainParameters = McsDomainParameters.Default,
            UserData = new byte[] { 0x00, 0x05 }
        });

        // Wrap in X.224 DT
        var dt = X224Message.BuildDataTransfer(ci);

        // Wrap in TPKT
        var tpkt = TpktFrame.Build(dt);

        // Unwrap all layers
        var tpktPayload = TpktFrame.ParsePayload(tpkt);
        var x224 = X224Message.Parse(tpktPayload);
        Assert.AreEqual(X224Message.TYPE_DT, x224.Type);

        Assert.IsTrue(McsCodec.IsConnectInitial(x224.Data));
        var decoded = McsCodec.DecodeConnectInitial(x224.Data);

        Assert.AreEqual(34, decoded.CallingDomainParameters.MaxChannelIds);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x05 }, decoded.UserData);
    }
}

// ──────────────────────────────────────────────────────────
//  T120Server TCP integration tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class T120ServerTests
{
    [TestMethod]
    public async Task Server_X224Handshake_SendsCC()
    {
        // Start server on ephemeral port
        var server = new T120Server(IPAddress.Loopback, 0);

        // Use reflection to find the actual port after binding
        // Actually, the Listener base class binds on Start(). For testing,
        // we'll do a direct TCP test with a known port.
        var port = GetAvailablePort();
        var testServer = new T120Server(IPAddress.Loopback, port);
        testServer.Start();

        await Task.Delay(100); // Let server start

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            // Send X.224 CR
            var cr = X224Message.BuildConnectionRequest(srcRef: 0x0001);
            await TpktFrame.WriteAsync(stream, cr);

            // Read X.224 CC
            var ccPayload = await TpktFrame.ReadAsync(stream);
            Assert.IsNotNull(ccPayload);

            var cc = X224Message.Parse(ccPayload);
            Assert.AreEqual(X224Message.TYPE_CC, cc.Type);
            Assert.AreEqual((ushort)0x0001, cc.DstRef);
        }
        finally
        {
            testServer.IsListening = false;
        }
    }

    [TestMethod]
    public async Task Server_McsConnectPhase_SendsResponse()
    {
        var port = GetAvailablePort();
        var testServer = new T120Server(IPAddress.Loopback, port);
        testServer.Start();

        await Task.Delay(100);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            // X.224 handshake
            var cr = X224Message.BuildConnectionRequest(srcRef: 1);
            await TpktFrame.WriteAsync(stream, cr);
            await TpktFrame.ReadAsync(stream); // CC

            // Send MCS Connect-Initial in X.224 DT
            var ci = McsCodec.EncodeConnectInitial(new McsConnectInitial
            {
                CallingDomainParameters = McsDomainParameters.Default,
                MinimumDomainParameters = McsDomainParameters.Default,
                MaximumDomainParameters = McsDomainParameters.Default,
                UserData = new byte[] { 0x00 }
            });

            var dt = X224Message.BuildDataTransfer(ci);
            await TpktFrame.WriteAsync(stream, dt);

            // Read Connect-Response in X.224 DT
            var respPayload = await TpktFrame.ReadAsync(stream);
            Assert.IsNotNull(respPayload);

            var x224 = X224Message.Parse(respPayload);
            Assert.AreEqual(X224Message.TYPE_DT, x224.Type);
            Assert.IsTrue(McsCodec.IsConnectResponse(x224.Data));

            var cr2 = McsCodec.DecodeConnectResponse(x224.Data);
            Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, cr2.Result);
        }
        finally
        {
            testServer.IsListening = false;
        }
    }

    [TestMethod]
    public async Task Server_AttachUser_AssignsUserId()
    {
        var port = GetAvailablePort();
        var testServer = new T120Server(IPAddress.Loopback, port);
        testServer.Start();

        await Task.Delay(100);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            // Full handshake
            await DoX224Handshake(stream);
            await DoMcsConnect(stream);

            // ErectDomainRequest
            var edr = McsCodec.EncodeErectDomainRequest();
            await WriteX224Data(stream, edr);

            // AttachUserRequest
            var aur = McsCodec.EncodeAttachUserRequest();
            await WriteX224Data(stream, aur);

            // Read AttachUserConfirm
            var aucData = await ReadX224Data(stream);
            Assert.IsNotNull(aucData);

            var auc = McsCodec.DecodeDomainPdu(aucData);
            Assert.AreEqual(McsConstants.DOMAIN_ATTACH_USER_CONFIRM, auc.Type);
            Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, auc.Result);
            Assert.IsTrue(auc.Initiator > 1000);
        }
        finally
        {
            testServer.IsListening = false;
        }
    }

    [TestMethod]
    public async Task Server_ChannelJoin_Succeeds()
    {
        var port = GetAvailablePort();
        var testServer = new T120Server(IPAddress.Loopback, port);
        testServer.Start();

        await Task.Delay(100);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            await DoX224Handshake(stream);
            await DoMcsConnect(stream);

            // ErectDomain + AttachUser
            await WriteX224Data(stream, McsCodec.EncodeErectDomainRequest());
            await WriteX224Data(stream, McsCodec.EncodeAttachUserRequest());
            var aucData = await ReadX224Data(stream);
            var auc = McsCodec.DecodeDomainPdu(aucData);
            var userId = auc.Initiator;

            // Join user's own channel
            var cjr = McsCodec.EncodeChannelJoinRequest(userId, userId);
            await WriteX224Data(stream, cjr);

            // Read ChannelJoinConfirm
            var cjcData = await ReadX224Data(stream);
            var cjc = McsCodec.DecodeDomainPdu(cjcData);

            Assert.AreEqual(McsConstants.DOMAIN_CHANNEL_JOIN_CONFIRM, cjc.Type);
            Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, cjc.Result);
            Assert.AreEqual(userId, cjc.ChannelId);
        }
        finally
        {
            testServer.IsListening = false;
        }
    }

    [TestMethod]
    public async Task Server_MultipleUsers_IncrementingIds()
    {
        var port = GetAvailablePort();
        var testServer = new T120Server(IPAddress.Loopback, port);
        testServer.Start();

        await Task.Delay(100);

        try
        {
            // Connect first client
            using var client1 = new TcpClient();
            await client1.ConnectAsync(IPAddress.Loopback, port);
            var stream1 = client1.GetStream();
            await DoX224Handshake(stream1);
            await DoMcsConnect(stream1);
            await WriteX224Data(stream1, McsCodec.EncodeErectDomainRequest());
            await WriteX224Data(stream1, McsCodec.EncodeAttachUserRequest());
            var auc1 = McsCodec.DecodeDomainPdu(await ReadX224Data(stream1));

            // Connect second client
            using var client2 = new TcpClient();
            await client2.ConnectAsync(IPAddress.Loopback, port);
            var stream2 = client2.GetStream();
            await DoX224Handshake(stream2);
            await DoMcsConnect(stream2);
            await WriteX224Data(stream2, McsCodec.EncodeErectDomainRequest());
            await WriteX224Data(stream2, McsCodec.EncodeAttachUserRequest());
            var auc2 = McsCodec.DecodeDomainPdu(await ReadX224Data(stream2));

            // User IDs should be different and incrementing
            Assert.AreNotEqual(auc1.Initiator, auc2.Initiator);
            Assert.AreEqual(auc1.Initiator + 1, auc2.Initiator);
        }
        finally
        {
            testServer.IsListening = false;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task DoX224Handshake(NetworkStream stream)
    {
        var cr = X224Message.BuildConnectionRequest(srcRef: 1);
        await TpktFrame.WriteAsync(stream, cr);
        await TpktFrame.ReadAsync(stream); // CC
    }

    private static async Task DoMcsConnect(NetworkStream stream)
    {
        var ci = McsCodec.EncodeConnectInitial(new McsConnectInitial
        {
            CallingDomainParameters = McsDomainParameters.Default,
            MinimumDomainParameters = McsDomainParameters.Default,
            MaximumDomainParameters = McsDomainParameters.Default,
            UserData = Array.Empty<byte>()
        });

        var dt = X224Message.BuildDataTransfer(ci);
        await TpktFrame.WriteAsync(stream, dt);
        await TpktFrame.ReadAsync(stream); // Connect-Response
    }

    private static async Task WriteX224Data(NetworkStream stream, byte[] data)
    {
        var dt = X224Message.BuildDataTransfer(data);
        await TpktFrame.WriteAsync(stream, dt);
    }

    private static async Task<byte[]> ReadX224Data(NetworkStream stream)
    {
        var payload = await TpktFrame.ReadAsync(stream);
        if (payload == null)
        {
            return null!;
        }

        var x224 = X224Message.Parse(payload);
        return x224.Data;
    }
}
