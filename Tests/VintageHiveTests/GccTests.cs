// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using VintageHive.Proxy.NetMeeting;
using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.GCC;
using VintageHive.Proxy.NetMeeting.T120;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  GCC Constants tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class GccConstantsTests
{
    [TestMethod]
    public void T124Oid_HasExpectedValues()
    {
        CollectionAssert.AreEqual(new[] { 0, 0, 20, 124, 0, 1 }, GccConstants.T124_OID);
    }

    [TestMethod]
    public void ConnectPduName_AllKnownTypes()
    {
        Assert.AreEqual("ConferenceCreateRequest",
            GccConstants.ConnectPduName(GccConstants.CONNECT_CONFERENCE_CREATE_REQUEST));
        Assert.AreEqual("ConferenceCreateResponse",
            GccConstants.ConnectPduName(GccConstants.CONNECT_CONFERENCE_CREATE_RESPONSE));
        Assert.AreEqual("ConferenceJoinRequest",
            GccConstants.ConnectPduName(GccConstants.CONNECT_CONFERENCE_JOIN_REQUEST));
    }

    [TestMethod]
    public void ConnectPduName_Unknown_ReturnsGeneric()
    {
        Assert.IsTrue(GccConstants.ConnectPduName(99).Contains("99"));
    }

    [TestMethod]
    public void ResultName_AllKnownValues()
    {
        Assert.AreEqual("success", GccConstants.ResultName(GccConstants.RESULT_SUCCESS));
        Assert.AreEqual("userRejected", GccConstants.ResultName(GccConstants.RESULT_USER_REJECTED));
        Assert.AreEqual("resourcesNotAvailable",
            GccConstants.ResultName(GccConstants.RESULT_RESOURCES_NOT_AVAILABLE));
    }

    [TestMethod]
    public void H221Key_Microsoft_IsDuca()
    {
        var key = GccConstants.H221_KEY_MICROSOFT;
        Assert.AreEqual(4, key.Length);
        Assert.AreEqual((byte)'D', key[0]);
        Assert.AreEqual((byte)'u', key[1]);
        Assert.AreEqual((byte)'c', key[2]);
        Assert.AreEqual((byte)'a', key[3]);
    }
}

// ──────────────────────────────────────────────────────────
//  ConnectData wrapper tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class GccConnectDataTests
{
    [TestMethod]
    public void EncodeDecodeRoundTrip_WithOid()
    {
        var innerData = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var encoded = GccCodec.EncodeConnectData(GccConstants.T124_OID, innerData);
        var decoded = GccCodec.DecodeConnectData(encoded);

        CollectionAssert.AreEqual(GccConstants.T124_OID, decoded.Identifier);
        CollectionAssert.AreEqual(innerData, decoded.ConnectPdu);
    }

    [TestMethod]
    public void IsT124ConnectData_ValidData_ReturnsTrue()
    {
        var encoded = GccCodec.EncodeConnectData(GccConstants.T124_OID, new byte[] { 0x00 });
        Assert.IsTrue(GccCodec.IsT124ConnectData(encoded));
    }

    [TestMethod]
    public void IsT124ConnectData_NullOrShort_ReturnsFalse()
    {
        Assert.IsFalse(GccCodec.IsT124ConnectData(null));
        Assert.IsFalse(GccCodec.IsT124ConnectData(Array.Empty<byte>()));
        Assert.IsFalse(GccCodec.IsT124ConnectData(new byte[] { 0x00, 0x05 }));
    }

    [TestMethod]
    public void IsT124ConnectData_WrongOid_ReturnsFalse()
    {
        var encoded = GccCodec.EncodeConnectData(new[] { 1, 2, 3 }, new byte[] { 0x00 });
        Assert.IsFalse(GccCodec.IsT124ConnectData(encoded));
    }

    [TestMethod]
    public void ConnectData_EmptyPayload()
    {
        var encoded = GccCodec.EncodeConnectData(GccConstants.T124_OID, Array.Empty<byte>());
        var decoded = GccCodec.DecodeConnectData(encoded);

        Assert.AreEqual(0, decoded.ConnectPdu.Length);
    }

    [TestMethod]
    public void ConnectData_LargePayload()
    {
        var payload = new byte[4096];
        new Random(42).NextBytes(payload);

        var encoded = GccCodec.EncodeConnectData(GccConstants.T124_OID, payload);
        var decoded = GccCodec.DecodeConnectData(encoded);

        CollectionAssert.AreEqual(payload, decoded.ConnectPdu);
    }
}

// ──────────────────────────────────────────────────────────
//  SimpleNumericString tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class GccNumericStringTests
{
    [TestMethod]
    public void WriteReadRoundTrip_SingleDigit()
    {
        var enc = new PerEncoder();
        GccCodec.WriteSimpleNumericString(enc, "1", 1, 255);
        var data = enc.ToArray();

        var dec = new PerDecoder(data);
        var result = GccCodec.ReadSimpleNumericString(dec, 1, 255);

        Assert.AreEqual("1", result);
    }

    [TestMethod]
    public void WriteReadRoundTrip_MultipleDigits()
    {
        var enc = new PerEncoder();
        GccCodec.WriteSimpleNumericString(enc, "123456789", 1, 255);
        var data = enc.ToArray();

        var dec = new PerDecoder(data);
        var result = GccCodec.ReadSimpleNumericString(dec, 1, 255);

        Assert.AreEqual("123456789", result);
    }

    [TestMethod]
    public void WriteReadRoundTrip_AllDigits()
    {
        var enc = new PerEncoder();
        GccCodec.WriteSimpleNumericString(enc, "0123456789", 1, 255);
        var data = enc.ToArray();

        var dec = new PerDecoder(data);
        var result = GccCodec.ReadSimpleNumericString(dec, 1, 255);

        Assert.AreEqual("0123456789", result);
    }

    [TestMethod]
    public void WriteReadRoundTrip_MaxLength()
    {
        var digits = new string('5', 255);
        var enc = new PerEncoder();
        GccCodec.WriteSimpleNumericString(enc, digits, 1, 255);
        var data = enc.ToArray();

        var dec = new PerDecoder(data);
        var result = GccCodec.ReadSimpleNumericString(dec, 1, 255);

        Assert.AreEqual(digits, result);
    }
}

// ──────────────────────────────────────────────────────────
//  ConferenceCreateRequest encode/decode tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class GccConferenceCreateRequestTests
{
    [TestMethod]
    public void MinimalRequest_EncodeDecodeRoundTrip()
    {
        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "1",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC
        };

        var encoded = GccCodec.EncodeConferenceCreateRequest(request);
        var decoded = GccCodec.DecodeConferenceCreateRequest(encoded);

        Assert.AreEqual("1", decoded.ConferenceNameNumeric);
        Assert.IsNull(decoded.ConferenceNameText);
        Assert.IsFalse(decoded.LockedConference);
        Assert.IsFalse(decoded.ListedConference);
        Assert.IsFalse(decoded.ConducibleConference);
        Assert.AreEqual(GccConstants.TERMINATION_AUTOMATIC, decoded.TerminationMethod);
        Assert.IsNull(decoded.UserData);
    }

    [TestMethod]
    public void RequestWithUserData_EncodeDecodeRoundTrip()
    {
        var userData = new[]
        {
            new GccUserDataBlock
            {
                H221Key = GccConstants.H221_KEY_MICROSOFT,
                Value = new byte[] { 0x01, 0x02, 0x03, 0x04 }
            }
        };

        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "42",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC,
            UserData = userData
        };

        var encoded = GccCodec.EncodeConferenceCreateRequest(request);
        var decoded = GccCodec.DecodeConferenceCreateRequest(encoded);

        Assert.AreEqual("42", decoded.ConferenceNameNumeric);
        Assert.IsNotNull(decoded.UserData);
        Assert.AreEqual(1, decoded.UserData.Length);
        Assert.IsTrue(decoded.UserData[0].IsH221);
        CollectionAssert.AreEqual(GccConstants.H221_KEY_MICROSOFT, decoded.UserData[0].H221Key);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0x04 }, decoded.UserData[0].Value);
    }

    [TestMethod]
    public void RequestWithMultipleUserDataBlocks()
    {
        var userData = new[]
        {
            new GccUserDataBlock
            {
                H221Key = GccConstants.H221_KEY_MICROSOFT,
                Value = new byte[] { 0xAA }
            },
            new GccUserDataBlock
            {
                H221Key = new byte[] { 0x4D, 0x53, 0x46, 0x54 }, // "MSFT"
                Value = new byte[] { 0xBB, 0xCC }
            }
        };

        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "1",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC,
            UserData = userData
        };

        var encoded = GccCodec.EncodeConferenceCreateRequest(request);
        var decoded = GccCodec.DecodeConferenceCreateRequest(encoded);

        Assert.AreEqual(2, decoded.UserData.Length);
        CollectionAssert.AreEqual(new byte[] { 0xAA }, decoded.UserData[0].Value);
        CollectionAssert.AreEqual(new byte[] { 0xBB, 0xCC }, decoded.UserData[1].Value);
    }

    [TestMethod]
    public void Request_ManualTermination()
    {
        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "1",
            LockedConference = true,
            ListedConference = true,
            ConducibleConference = true,
            TerminationMethod = GccConstants.TERMINATION_MANUAL
        };

        var encoded = GccCodec.EncodeConferenceCreateRequest(request);
        var decoded = GccCodec.DecodeConferenceCreateRequest(encoded);

        Assert.IsTrue(decoded.LockedConference);
        Assert.IsTrue(decoded.ListedConference);
        Assert.IsTrue(decoded.ConducibleConference);
        Assert.AreEqual(GccConstants.TERMINATION_MANUAL, decoded.TerminationMethod);
    }

    [TestMethod]
    public void Request_LongConferenceName()
    {
        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "9876543210",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC
        };

        var encoded = GccCodec.EncodeConferenceCreateRequest(request);
        var decoded = GccCodec.DecodeConferenceCreateRequest(encoded);

        Assert.AreEqual("9876543210", decoded.ConferenceNameNumeric);
    }

    [TestMethod]
    public void Request_UserDataBlockWithObjectKey()
    {
        var userData = new[]
        {
            new GccUserDataBlock
            {
                ObjectKey = new[] { 1, 2, 840, 113556 },
                Value = new byte[] { 0xFF }
            }
        };

        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "1",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC,
            UserData = userData
        };

        var encoded = GccCodec.EncodeConferenceCreateRequest(request);
        var decoded = GccCodec.DecodeConferenceCreateRequest(encoded);

        Assert.AreEqual(1, decoded.UserData.Length);
        Assert.IsFalse(decoded.UserData[0].IsH221);
        CollectionAssert.AreEqual(new[] { 1, 2, 840, 113556 }, decoded.UserData[0].ObjectKey);
        CollectionAssert.AreEqual(new byte[] { 0xFF }, decoded.UserData[0].Value);
    }

    [TestMethod]
    public void Request_UserDataBlockWithNullValue()
    {
        var userData = new[]
        {
            new GccUserDataBlock
            {
                H221Key = GccConstants.H221_KEY_MICROSOFT,
                Value = null
            }
        };

        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "1",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC,
            UserData = userData
        };

        var encoded = GccCodec.EncodeConferenceCreateRequest(request);
        var decoded = GccCodec.DecodeConferenceCreateRequest(encoded);

        Assert.AreEqual(1, decoded.UserData.Length);
        Assert.IsNull(decoded.UserData[0].Value);
    }
}

// ──────────────────────────────────────────────────────────
//  ConferenceCreateResponse encode/decode tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class GccConferenceCreateResponseTests
{
    [TestMethod]
    public void Response_Success_EncodeDecodeRoundTrip()
    {
        var response = new ConferenceCreateResponse
        {
            NodeId = 1001,
            Tag = 1,
            Result = GccConstants.RESULT_SUCCESS
        };

        var encoded = GccCodec.EncodeConferenceCreateResponse(response);
        var decoded = GccCodec.DecodeConferenceCreateResponse(encoded);

        Assert.AreEqual(1001, decoded.NodeId);
        Assert.AreEqual(1, decoded.Tag);
        Assert.AreEqual(GccConstants.RESULT_SUCCESS, decoded.Result);
        Assert.IsNull(decoded.UserData);
    }

    [TestMethod]
    public void Response_WithUserData_EncodeDecodeRoundTrip()
    {
        var userData = new[]
        {
            new GccUserDataBlock
            {
                H221Key = GccConstants.H221_KEY_MICROSOFT,
                Value = new byte[] { 0xDE, 0xAD }
            }
        };

        var response = new ConferenceCreateResponse
        {
            NodeId = 31219,
            Tag = 1,
            Result = GccConstants.RESULT_SUCCESS,
            UserData = userData
        };

        var encoded = GccCodec.EncodeConferenceCreateResponse(response);
        var decoded = GccCodec.DecodeConferenceCreateResponse(encoded);

        Assert.AreEqual(31219, decoded.NodeId);
        Assert.AreEqual(1, decoded.Tag);
        Assert.AreEqual(GccConstants.RESULT_SUCCESS, decoded.Result);
        Assert.IsNotNull(decoded.UserData);
        Assert.AreEqual(1, decoded.UserData.Length);
        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD }, decoded.UserData[0].Value);
    }

    [TestMethod]
    public void Response_Failure()
    {
        var response = new ConferenceCreateResponse
        {
            NodeId = 1001,
            Tag = 0,
            Result = GccConstants.RESULT_USER_REJECTED
        };

        var encoded = GccCodec.EncodeConferenceCreateResponse(response);
        var decoded = GccCodec.DecodeConferenceCreateResponse(encoded);

        Assert.AreEqual(GccConstants.RESULT_USER_REJECTED, decoded.Result);
    }

    [TestMethod]
    public void Response_MaxNodeId()
    {
        var response = new ConferenceCreateResponse
        {
            NodeId = 65535,
            Tag = 999,
            Result = GccConstants.RESULT_SUCCESS
        };

        var encoded = GccCodec.EncodeConferenceCreateResponse(response);
        var decoded = GccCodec.DecodeConferenceCreateResponse(encoded);

        Assert.AreEqual(65535, decoded.NodeId);
        Assert.AreEqual(999, decoded.Tag);
    }

    [TestMethod]
    public void Response_MinNodeId()
    {
        var response = new ConferenceCreateResponse
        {
            NodeId = 1001,
            Tag = 0,
            Result = GccConstants.RESULT_SUCCESS
        };

        var encoded = GccCodec.EncodeConferenceCreateResponse(response);
        var decoded = GccCodec.DecodeConferenceCreateResponse(encoded);

        Assert.AreEqual(1001, decoded.NodeId);
    }
}

// ──────────────────────────────────────────────────────────
//  Full GCC pipeline integration tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class GccPipelineTests
{
    [TestMethod]
    public void FullPipeline_RequestThroughMcsUserData()
    {
        // Build a CCR with user data
        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "1",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC,
            UserData = new[]
            {
                new GccUserDataBlock
                {
                    H221Key = GccConstants.H221_KEY_MICROSOFT,
                    Value = new byte[] { 0x01, 0x00, 0x08, 0x00 }
                }
            }
        };

        // Encode as MCS userData
        var mcsUserData = GccCodec.EncodeConferenceCreateRequest(request);

        // Verify it's detectable
        Assert.IsTrue(GccCodec.IsT124ConnectData(mcsUserData));

        // Wrap in MCS Connect-Initial
        var mcsInitial = McsCodec.EncodeConnectInitial(new McsConnectInitial
        {
            CallingDomainParameters = McsDomainParameters.Default,
            MinimumDomainParameters = McsDomainParameters.Default,
            MaximumDomainParameters = McsDomainParameters.Default,
            UserData = mcsUserData
        });

        // Decode MCS
        var decodedMcs = McsCodec.DecodeConnectInitial(mcsInitial);

        // Decode GCC from MCS userData
        var decodedGcc = GccCodec.DecodeConferenceCreateRequest(decodedMcs.UserData);

        Assert.AreEqual("1", decodedGcc.ConferenceNameNumeric);
        Assert.AreEqual(1, decodedGcc.UserData.Length);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x00, 0x08, 0x00 },
            decodedGcc.UserData[0].Value);
    }

    [TestMethod]
    public void FullPipeline_ResponseThroughMcsUserData()
    {
        var response = new ConferenceCreateResponse
        {
            NodeId = 1001,
            Tag = 1,
            Result = GccConstants.RESULT_SUCCESS,
            UserData = new[]
            {
                new GccUserDataBlock
                {
                    H221Key = GccConstants.H221_KEY_MICROSOFT,
                    Value = new byte[] { 0x02, 0x00, 0x0C, 0x00 }
                }
            }
        };

        var mcsUserData = GccCodec.EncodeConferenceCreateResponse(response);

        // Wrap in MCS Connect-Response
        var mcsResponse = McsCodec.EncodeConnectResponse(new McsConnectResponse
        {
            Result = McsConstants.RESULT_SUCCESSFUL,
            CalledConnectId = 0,
            DomainParameters = McsDomainParameters.Default,
            UserData = mcsUserData
        });

        // Decode MCS
        var decodedMcs = McsCodec.DecodeConnectResponse(mcsResponse);

        // Decode GCC
        var decodedGcc = GccCodec.DecodeConferenceCreateResponse(decodedMcs.UserData);

        Assert.AreEqual(1001, decodedGcc.NodeId);
        Assert.AreEqual(GccConstants.RESULT_SUCCESS, decodedGcc.Result);
        Assert.AreEqual(1, decodedGcc.UserData.Length);
    }

    [TestMethod]
    public void FullPipeline_OverX224AndTpkt()
    {
        // GCC → MCS → X.224 DT → TPKT — the complete stack
        var request = new ConferenceCreateRequest
        {
            ConferenceNameNumeric = "1",
            LockedConference = false,
            ListedConference = false,
            ConducibleConference = false,
            TerminationMethod = GccConstants.TERMINATION_AUTOMATIC
        };

        var gccData = GccCodec.EncodeConferenceCreateRequest(request);

        var mcsData = McsCodec.EncodeConnectInitial(new McsConnectInitial
        {
            CallingDomainParameters = McsDomainParameters.Default,
            MinimumDomainParameters = McsDomainParameters.Default,
            MaximumDomainParameters = McsDomainParameters.Default,
            UserData = gccData
        });

        var x224Data = X224Message.BuildDataTransfer(mcsData);
        var tpktData = TpktFrame.Build(x224Data);

        // Now unwind the entire stack
        var tpktPayload = TpktFrame.ParsePayload(tpktData);
        var x224Msg = X224Message.Parse(tpktPayload);
        Assert.AreEqual(X224Message.TYPE_DT, x224Msg.Type);

        Assert.IsTrue(McsCodec.IsConnectInitial(x224Msg.Data));
        var mcsMsg = McsCodec.DecodeConnectInitial(x224Msg.Data);

        Assert.IsTrue(GccCodec.IsT124ConnectData(mcsMsg.UserData));
        var gccMsg = GccCodec.DecodeConferenceCreateRequest(mcsMsg.UserData);

        Assert.AreEqual("1", gccMsg.ConferenceNameNumeric);
    }
}

// ──────────────────────────────────────────────────────────
//  T120Server GCC integration tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class T120ServerGccTests
{
    [TestMethod]
    public async Task Server_GccRequest_ReturnsGccResponse()
    {
        var port = GetAvailablePort();
        var server = new T120Server(IPAddress.Loopback, port);
        server.Start();

        await Task.Delay(100);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            // X.224 handshake
            await DoX224Handshake(stream);

            // Build GCC ConferenceCreateRequest
            var gccRequest = GccCodec.EncodeConferenceCreateRequest(new ConferenceCreateRequest
            {
                ConferenceNameNumeric = "1",
                LockedConference = false,
                ListedConference = false,
                ConducibleConference = false,
                TerminationMethod = GccConstants.TERMINATION_AUTOMATIC,
                UserData = new[]
                {
                    new GccUserDataBlock
                    {
                        H221Key = GccConstants.H221_KEY_MICROSOFT,
                        Value = new byte[] { 0x01, 0x02 }
                    }
                }
            });

            // Send MCS Connect-Initial with GCC userData
            var ci = McsCodec.EncodeConnectInitial(new McsConnectInitial
            {
                CallingDomainParameters = McsDomainParameters.Default,
                MinimumDomainParameters = McsDomainParameters.Default,
                MaximumDomainParameters = McsDomainParameters.Default,
                UserData = gccRequest
            });

            var dt = X224Message.BuildDataTransfer(ci);
            await TpktFrame.WriteAsync(stream, dt);

            // Read Connect-Response
            var respPayload = await TpktFrame.ReadAsync(stream);
            Assert.IsNotNull(respPayload);

            var x224 = X224Message.Parse(respPayload);
            Assert.AreEqual(X224Message.TYPE_DT, x224.Type);

            var mcsResp = McsCodec.DecodeConnectResponse(x224.Data);
            Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, mcsResp.Result);

            // Decode GCC response from MCS userData
            Assert.IsTrue(GccCodec.IsT124ConnectData(mcsResp.UserData));
            var gccResp = GccCodec.DecodeConferenceCreateResponse(mcsResp.UserData);

            Assert.AreEqual(GccConstants.RESULT_SUCCESS, gccResp.Result);
            Assert.IsTrue(gccResp.NodeId >= 1001);
            Assert.AreEqual(1, gccResp.Tag);

            // Server should echo back the user data blocks
            Assert.IsNotNull(gccResp.UserData);
            Assert.AreEqual(1, gccResp.UserData.Length);
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x02 }, gccResp.UserData[0].Value);
        }
        finally
        {
            server.IsListening = false;
        }
    }

    [TestMethod]
    public async Task Server_NonGccUserData_StillWorks()
    {
        var port = GetAvailablePort();
        var server = new T120Server(IPAddress.Loopback, port);
        server.Start();

        await Task.Delay(100);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            await DoX224Handshake(stream);

            // Send MCS Connect-Initial with non-GCC userData (just raw bytes)
            var ci = McsCodec.EncodeConnectInitial(new McsConnectInitial
            {
                CallingDomainParameters = McsDomainParameters.Default,
                MinimumDomainParameters = McsDomainParameters.Default,
                MaximumDomainParameters = McsDomainParameters.Default,
                UserData = new byte[] { 0xFF, 0xFE, 0xFD } // Not GCC
            });

            var dt = X224Message.BuildDataTransfer(ci);
            await TpktFrame.WriteAsync(stream, dt);

            // Should still get a valid MCS Connect-Response
            var respPayload = await TpktFrame.ReadAsync(stream);
            Assert.IsNotNull(respPayload);

            var x224 = X224Message.Parse(respPayload);
            var mcsResp = McsCodec.DecodeConnectResponse(x224.Data);
            Assert.AreEqual(McsConstants.RESULT_SUCCESSFUL, mcsResp.Result);

            // userData should be the raw echo since it wasn't GCC
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFE, 0xFD }, mcsResp.UserData);
        }
        finally
        {
            server.IsListening = false;
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
}
