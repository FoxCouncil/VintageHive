// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using VintageHive.Proxy.NetMeeting;
using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.H225;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  TPKT framing tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class TpktFrameTests
{
    [TestMethod]
    public void Build_And_ParsePayload_RoundTrip()
    {
        var payload = new byte[] { 0x08, 0x02, 0x00, 0x01, 0x05 };
        var frame = TpktFrame.Build(payload);

        Assert.AreEqual(0x03, frame[0]); // Version
        Assert.AreEqual(0x00, frame[1]); // Reserved
        Assert.AreEqual(9, (frame[2] << 8) | frame[3]); // Total length = 4 + 5

        var parsed = TpktFrame.ParsePayload(frame);
        CollectionAssert.AreEqual(payload, parsed);
    }

    [TestMethod]
    public void Build_EmptyPayload()
    {
        var frame = TpktFrame.Build(Array.Empty<byte>());
        Assert.AreEqual(4, frame.Length);
        Assert.AreEqual(4, (frame[2] << 8) | frame[3]);

        var parsed = TpktFrame.ParsePayload(frame);
        Assert.AreEqual(0, parsed.Length);
    }

    [TestMethod]
    public async Task ReadAsync_WriteAsync_RoundTrip()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                return await TpktFrame.ReadAsync(stream);
            });

            using var sender = new TcpClient();
            await sender.ConnectAsync(IPAddress.Loopback, port);
            var senderStream = sender.GetStream();

            var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            await TpktFrame.WriteAsync(senderStream, payload);

            var received = await serverTask;
            CollectionAssert.AreEqual(payload, received);
        }
        finally
        {
            listener.Stop();
        }
    }
}

// ──────────────────────────────────────────────────────────
//  Q.931 message tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class Q931MessageTests
{
    [TestMethod]
    public void Parse_Build_RoundTrip()
    {
        var msg = new Q931Message
        {
            MessageType = Q931Message.MSG_SETUP,
            CallReference = 42,
            CallReferenceFlag = false
        };

        msg.SetDisplay("TestCall");
        msg.SetUuieData(new byte[] { 0x01, 0x02, 0x03 });

        var bytes = msg.Build();
        var parsed = Q931Message.Parse(bytes);

        Assert.AreEqual(Q931Message.MSG_SETUP, parsed.MessageType);
        Assert.AreEqual(42, parsed.CallReference);
        Assert.IsFalse(parsed.CallReferenceFlag);
        Assert.AreEqual("TestCall", parsed.GetDisplay());

        var uuie = parsed.GetUuieData();
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03 }, uuie);
    }

    [TestMethod]
    public void Parse_ProtocolDiscriminator()
    {
        var msg = new Q931Message
        {
            MessageType = Q931Message.MSG_CONNECT,
            CallReference = 1
        };

        var bytes = msg.Build();

        Assert.AreEqual(Q931Message.ProtocolDiscriminator, bytes[0]);
        Assert.AreEqual(0x02, bytes[1]); // Call ref length = 2
    }

    [TestMethod]
    public void CallReference_Flag()
    {
        var msg = new Q931Message
        {
            MessageType = Q931Message.MSG_ALERTING,
            CallReference = 100,
            CallReferenceFlag = true
        };

        var bytes = msg.Build();
        var parsed = Q931Message.Parse(bytes);

        Assert.AreEqual(100, parsed.CallReference);
        Assert.IsTrue(parsed.CallReferenceFlag);
    }

    [TestMethod]
    public void CallReference_MaxValue()
    {
        var msg = new Q931Message
        {
            MessageType = Q931Message.MSG_SETUP,
            CallReference = 32767 // Max 15-bit value
        };

        var parsed = Q931Message.Parse(msg.Build());
        Assert.AreEqual(32767, parsed.CallReference);
    }

    [TestMethod]
    public void UserUser_IE_TwoByteLength()
    {
        // Create a large UUIE payload to test 2-byte length encoding
        var largeUuie = new byte[300];
        for (var i = 0; i < largeUuie.Length; i++)
        {
            largeUuie[i] = (byte)(i & 0xFF);
        }

        var msg = new Q931Message
        {
            MessageType = Q931Message.MSG_SETUP,
            CallReference = 1
        };

        msg.SetUuieData(largeUuie);

        var bytes = msg.Build();
        var parsed = Q931Message.Parse(bytes);
        var recovered = parsed.GetUuieData();

        CollectionAssert.AreEqual(largeUuie, recovered);
    }

    [TestMethod]
    public void IEs_InAscendingTagOrder()
    {
        var msg = new Q931Message
        {
            MessageType = Q931Message.MSG_SETUP,
            CallReference = 1
        };

        // Add IEs in reverse order
        msg.SetUuieData(new byte[] { 0xAA }); // 0x7E
        msg.SetDisplay("Test");               // 0x28
        msg.InformationElements[Q931Message.IE_BEARER_CAPABILITY] = new byte[] { 0x01 }; // 0x04

        var bytes = msg.Build();

        // Find IE positions — they must be in ascending order
        // Skip: 0x08 (PD) + 0x02 (CR len) + 2 (CR) + 1 (msg type) = 6 bytes header
        var offset = 6;
        var ieTags = new List<byte>();

        while (offset < bytes.Length)
        {
            ieTags.Add(bytes[offset]);

            if (bytes[offset] == Q931Message.IE_USER_USER)
            {
                var len = (bytes[offset + 1] << 8) | bytes[offset + 2];
                offset += 3 + len;
            }
            else
            {
                var len = bytes[offset + 1];
                offset += 2 + len;
            }
        }

        // Verify ascending order
        for (var i = 1; i < ieTags.Count; i++)
        {
            Assert.IsTrue(ieTags[i] > ieTags[i - 1],
                $"IE 0x{ieTags[i]:X2} should be after 0x{ieTags[i - 1]:X2}");
        }
    }

    [TestMethod]
    public void Factory_CreateSetup()
    {
        var msg = Q931Message.CreateSetup(42, new byte[] { 0x01 });
        Assert.AreEqual(Q931Message.MSG_SETUP, msg.MessageType);
        Assert.AreEqual(42, msg.CallReference);
        Assert.IsFalse(msg.CallReferenceFlag);
    }

    [TestMethod]
    public void Factory_CreateReleaseComplete()
    {
        var msg = Q931Message.CreateReleaseComplete(10, true, new byte[] { 0x01 });
        Assert.AreEqual(Q931Message.MSG_RELEASE_COMPLETE, msg.MessageType);
        Assert.IsTrue(msg.CallReferenceFlag);
    }

    [TestMethod]
    public void MessageTypeName_AllTypes()
    {
        Assert.AreEqual("Setup", Q931Message.MessageTypeName(Q931Message.MSG_SETUP));
        Assert.AreEqual("Connect", Q931Message.MessageTypeName(Q931Message.MSG_CONNECT));
        Assert.AreEqual("ReleaseComplete", Q931Message.MessageTypeName(Q931Message.MSG_RELEASE_COMPLETE));
        Assert.AreEqual("Facility", Q931Message.MessageTypeName(Q931Message.MSG_FACILITY));
    }
}

// ──────────────────────────────────────────────────────────
//  H.225.0 UUIE codec tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H225CallCodecSetupTests
{
    [TestMethod]
    public void Setup_RoundTrip()
    {
        var confId = new byte[16];
        confId[0] = 0xAB;
        confId[15] = 0xCD;

        var setup = new SetupUuie
        {
            ProtocolIdentifier = H225Constants.ProtocolOid,
            SourceAliases = new[] { "Alice" },
            DestinationAliases = new[] { "Bob" },
            DestCallSignalAddress = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 1720),
            ActiveMC = false,
            ConferenceId = confId,
            ConferenceGoal = H225CallCodec.GOAL_CREATE,
            CallType = 0 // pointToPoint
        };

        var encoded = H225CallCodec.EncodeSetup(setup);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.AreEqual(H225CallCodec.BODY_SETUP, decoded.BodyType);
        Assert.IsNotNull(decoded.Setup);
        Assert.AreEqual("Alice", decoded.Setup.SourceAliases[0]);
        Assert.AreEqual("Bob", decoded.Setup.DestinationAliases[0]);
        Assert.AreEqual(1720, decoded.Setup.DestCallSignalAddress.Port);
        Assert.IsFalse(decoded.Setup.ActiveMC);
        Assert.AreEqual(0xAB, decoded.Setup.ConferenceId[0]);
        Assert.AreEqual(0xCD, decoded.Setup.ConferenceId[15]);
        Assert.AreEqual(H225CallCodec.GOAL_CREATE, decoded.Setup.ConferenceGoal);
    }

    [TestMethod]
    public void Setup_WithH245Address()
    {
        var setup = new SetupUuie
        {
            H245Address = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 9000),
            SourceAliases = new[] { "Caller" },
            ConferenceId = new byte[16],
            ConferenceGoal = H225CallCodec.GOAL_CREATE,
            CallType = 0
        };

        var encoded = H225CallCodec.EncodeSetup(setup);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.IsNotNull(decoded.Setup.H245Address);
        Assert.AreEqual(9000, decoded.Setup.H245Address.Port);
    }

    [TestMethod]
    public void Setup_MinimalFields()
    {
        var setup = new SetupUuie
        {
            ConferenceId = new byte[16],
            ConferenceGoal = H225CallCodec.GOAL_CREATE,
            CallType = 0
        };

        var encoded = H225CallCodec.EncodeSetup(setup);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.IsNull(decoded.Setup.SourceAliases);
        Assert.IsNull(decoded.Setup.DestinationAliases);
        Assert.IsNull(decoded.Setup.H245Address);
        Assert.IsNull(decoded.Setup.DestCallSignalAddress);
    }
}

[TestClass]
public class H225CallCodecConnectTests
{
    [TestMethod]
    public void Connect_RoundTrip()
    {
        var confId = new byte[16];
        confId[0] = 0x42;

        var conn = new ConnectUuie
        {
            ProtocolIdentifier = H225Constants.ProtocolOid,
            H245Address = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 8245),
            ConferenceId = confId
        };

        var encoded = H225CallCodec.EncodeConnect(conn);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.AreEqual(H225CallCodec.BODY_CONNECT, decoded.BodyType);
        Assert.IsNotNull(decoded.Connect);
        Assert.AreEqual(8245, decoded.Connect.H245Address.Port);
        Assert.AreEqual(0x42, decoded.Connect.ConferenceId[0]);
    }

    [TestMethod]
    public void Connect_NoH245Address()
    {
        var conn = new ConnectUuie
        {
            ConferenceId = new byte[16]
        };

        var encoded = H225CallCodec.EncodeConnect(conn);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.IsNull(decoded.Connect.H245Address);
    }
}

[TestClass]
public class H225CallCodecOtherTests
{
    [TestMethod]
    public void CallProceeding_RoundTrip()
    {
        var cp = new CallProceedingUuie
        {
            H245Address = new IPEndPoint(IPAddress.Loopback, 7777)
        };

        var encoded = H225CallCodec.EncodeCallProceeding(cp);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.AreEqual(H225CallCodec.BODY_CALL_PROCEEDING, decoded.BodyType);
        Assert.AreEqual(7777, decoded.CallProceeding.H245Address.Port);
    }

    [TestMethod]
    public void Alerting_RoundTrip()
    {
        var alerting = new AlertingUuie
        {
            H245Address = new IPEndPoint(IPAddress.Parse("172.16.0.1"), 5555)
        };

        var encoded = H225CallCodec.EncodeAlerting(alerting);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.AreEqual(H225CallCodec.BODY_ALERTING, decoded.BodyType);
        Assert.AreEqual(5555, decoded.Alerting.H245Address.Port);
    }

    [TestMethod]
    public void ReleaseComplete_WithReason()
    {
        var rc = new ReleaseCompleteUuie
        {
            Reason = H225CallCodec.REL_UNREACHABLE_DEST
        };

        var encoded = H225CallCodec.EncodeReleaseComplete(rc);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.AreEqual(H225CallCodec.BODY_RELEASE_COMPLETE, decoded.BodyType);
        Assert.AreEqual(H225CallCodec.REL_UNREACHABLE_DEST, decoded.ReleaseComplete.Reason);
    }

    [TestMethod]
    public void ReleaseComplete_NoReason()
    {
        var rc = new ReleaseCompleteUuie();

        var encoded = H225CallCodec.EncodeReleaseComplete(rc);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.IsNull(decoded.ReleaseComplete.Reason);
    }

    [TestMethod]
    public void Facility_RoundTrip()
    {
        var fac = new FacilityUuie
        {
            AlternativeAddress = new IPEndPoint(IPAddress.Parse("10.0.0.5"), 1720),
            Reason = H225CallCodec.FAC_CALL_FORWARDED
        };

        var encoded = H225CallCodec.EncodeFacility(fac);
        var decoded = H225CallCodec.Decode(encoded);

        Assert.AreEqual(H225CallCodec.BODY_FACILITY, decoded.BodyType);
        Assert.AreEqual(1720, decoded.Facility.AlternativeAddress.Port);
        Assert.AreEqual(H225CallCodec.FAC_CALL_FORWARDED, decoded.Facility.Reason);
    }
}

// ──────────────────────────────────────────────────────────
//  Q.931 + UUIE integration (full message round-trip)
// ──────────────────────────────────────────────────────────

[TestClass]
public class Q931UuieIntegrationTests
{
    [TestMethod]
    public void Q931Setup_WithUuie_FullRoundTrip()
    {
        // Build a complete Q.931 Setup with H.225.0 UUIE inside
        var setup = new SetupUuie
        {
            SourceAliases = new[] { "Caller" },
            DestinationAliases = new[] { "Callee" },
            ConferenceId = new byte[16],
            ConferenceGoal = H225CallCodec.GOAL_CREATE,
            CallType = 0
        };

        var uuieBytes = H225CallCodec.EncodeSetup(setup);
        var q931 = Q931Message.CreateSetup(42, uuieBytes);

        // Build raw Q.931 bytes
        var rawQ931 = q931.Build();

        // Wrap in TPKT
        var tpkt = TpktFrame.Build(rawQ931);

        // Parse back
        var recoveredQ931Bytes = TpktFrame.ParsePayload(tpkt);
        var recoveredQ931 = Q931Message.Parse(recoveredQ931Bytes);

        Assert.AreEqual(Q931Message.MSG_SETUP, recoveredQ931.MessageType);
        Assert.AreEqual(42, recoveredQ931.CallReference);

        // Extract and decode UUIE
        var recoveredUuie = recoveredQ931.GetUuieData();
        Assert.IsNotNull(recoveredUuie);

        var decoded = H225CallCodec.Decode(recoveredUuie);
        Assert.AreEqual(H225CallCodec.BODY_SETUP, decoded.BodyType);
        Assert.AreEqual("Caller", decoded.Setup.SourceAliases[0]);
        Assert.AreEqual("Callee", decoded.Setup.DestinationAliases[0]);
    }

    [TestMethod]
    public void Q931Connect_WithH245Address()
    {
        var conn = new ConnectUuie
        {
            H245Address = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 9245),
            ConferenceId = new byte[16]
        };

        var uuieBytes = H225CallCodec.EncodeConnect(conn);
        var q931 = Q931Message.CreateConnect(100, uuieBytes);
        var raw = q931.Build();

        var parsed = Q931Message.Parse(raw);
        var decoded = H225CallCodec.Decode(parsed.GetUuieData());

        Assert.AreEqual(H225CallCodec.BODY_CONNECT, decoded.BodyType);
        Assert.AreEqual(9245, decoded.Connect.H245Address.Port);
    }
}

// ──────────────────────────────────────────────────────────
//  H323Call model tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H323CallTests
{
    [TestMethod]
    public void Call_InitialState()
    {
        var call = new H323Call();
        Assert.AreEqual(H323CallState.Idle, call.State);
        Assert.IsTrue(call.CallId > 0);
        Assert.IsNull(call.ConnectedAt);
        Assert.IsNull(call.ReleasedAt);
    }

    [TestMethod]
    public void Call_UniqueIds()
    {
        var call1 = new H323Call();
        var call2 = new H323Call();
        Assert.AreNotEqual(call1.CallId, call2.CallId);
    }

    [TestMethod]
    public void Call_ToString()
    {
        var call = new H323Call
        {
            CallerAliases = new[] { "Alice" },
            CalleeAliases = new[] { "Bob" },
            CallReference = 42
        };

        call.State = H323CallState.Setup;
        call.State = H323CallState.Connected;

        var str = call.ToString();
        Assert.IsTrue(str.Contains("Alice"));
        Assert.IsTrue(str.Contains("Bob"));
        Assert.IsTrue(str.Contains("Connected"));
    }
}

// ──────────────────────────────────────────────────────────
//  H323Call state enforcement tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H323CallStateTests
{
    [TestMethod]
    public void ValidFullPath_Idle_To_Released()
    {
        var call = new H323Call();
        Assert.AreEqual(H323CallState.Idle, call.State);

        call.State = H323CallState.Setup;
        Assert.AreEqual(H323CallState.Setup, call.State);

        call.State = H323CallState.Proceeding;
        Assert.AreEqual(H323CallState.Proceeding, call.State);

        call.State = H323CallState.Alerting;
        Assert.AreEqual(H323CallState.Alerting, call.State);

        call.State = H323CallState.Connected;
        Assert.AreEqual(H323CallState.Connected, call.State);

        call.State = H323CallState.Released;
        Assert.AreEqual(H323CallState.Released, call.State);
    }

    [TestMethod]
    public void FastConnect_Setup_To_Connected()
    {
        var call = new H323Call();
        call.State = H323CallState.Setup;
        call.State = H323CallState.Connected;
        Assert.AreEqual(H323CallState.Connected, call.State);
    }

    [TestMethod]
    public void InvalidTransition_Idle_To_Connected_Ignored()
    {
        var call = new H323Call();
        call.State = H323CallState.Connected;
        Assert.AreEqual(H323CallState.Idle, call.State); // should remain Idle
    }

    [TestMethod]
    public void Released_To_Released_Idempotent()
    {
        var call = new H323Call();
        call.State = H323CallState.Setup;
        call.State = H323CallState.Released;
        Assert.AreEqual(H323CallState.Released, call.State);

        // Setting Released again should not throw or change anything
        call.State = H323CallState.Released;
        Assert.AreEqual(H323CallState.Released, call.State);
    }

    [TestMethod]
    public void IsValidTransition_AllPaths()
    {
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Idle, H323CallState.Setup));
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Setup, H323CallState.Proceeding));
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Setup, H323CallState.Alerting));
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Setup, H323CallState.Connected));
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Setup, H323CallState.Released));
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Proceeding, H323CallState.Connected));
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Alerting, H323CallState.Released));
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Connected, H323CallState.Released));
        Assert.IsTrue(H323Call.IsValidTransition(H323CallState.Released, H323CallState.Released));

        Assert.IsFalse(H323Call.IsValidTransition(H323CallState.Idle, H323CallState.Connected));
        Assert.IsFalse(H323Call.IsValidTransition(H323CallState.Connected, H323CallState.Setup));
        Assert.IsFalse(H323Call.IsValidTransition(H323CallState.Released, H323CallState.Idle));
    }
}

// ──────────────────────────────────────────────────────────
//  H323Server integration tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H323ServerTests
{
    private static readonly IPAddress Loopback = IPAddress.Loopback;

    [TestMethod]
    public async Task Server_Setup_NoCallee_Returns_ReleaseComplete()
    {
        var registry = new RasRegistry();
        var port = FindFreePort();
        var server = new H323Server(Loopback, port, registry);

        try
        {
            server.Start();

            // Give the listener a moment to bind
            await Task.Delay(200);

            using var client = new TcpClient();
            await client.ConnectAsync(Loopback, port);
            var stream = client.GetStream();

            // Send a Setup targeting a non-existent callee
            var setup = new SetupUuie
            {
                DestinationAliases = new[] { "NobodyHere" },
                ConferenceId = new byte[16],
                ConferenceGoal = H225CallCodec.GOAL_CREATE,
                CallType = 0
            };

            var uuie = H225CallCodec.EncodeSetup(setup);
            var q931 = Q931Message.CreateSetup(1, uuie);
            await TpktFrame.WriteAsync(stream, q931.Build());

            // Should get CallProceeding first, then ReleaseComplete
            // Actually, callee lookup happens before CallProceeding in our impl
            // No wait — we send CallProceeding, THEN fail on connect.
            // But if callee not found at all, we skip CallProceeding.
            // Let me read the response:

            var response1 = await TpktFrame.ReadAsync(stream);
            Assert.IsNotNull(response1);

            var resp1 = Q931Message.Parse(response1);

            // It could be either CallProceeding (if callee found but connect failed)
            // or ReleaseComplete (if callee not found)
            Assert.AreEqual(Q931Message.MSG_RELEASE_COMPLETE, resp1.MessageType);

            // Verify the UUIE has unreachable destination reason
            var respUuie = H225CallCodec.Decode(resp1.GetUuieData());
            Assert.AreEqual(H225CallCodec.BODY_RELEASE_COMPLETE, respUuie.BodyType);
            Assert.AreEqual(H225CallCodec.REL_UNREACHABLE_DEST, respUuie.ReleaseComplete.Reason);
        }
        finally
        {
            server.IsListening = false;
        }
    }

    [TestMethod]
    public async Task Server_Setup_CalleeRegistered_SendsCallProceeding()
    {
        var registry = new RasRegistry();

        // Start a mock "callee" TCP listener
        var calleeListener = new TcpListener(Loopback, 0);
        calleeListener.Start();
        var calleePort = ((IPEndPoint)calleeListener.LocalEndpoint).Port;

        try
        {
            // Register the callee in the RAS registry
            var calleeEpId = registry.GenerateEndpointId();
            registry.Register(new RasEndpoint
            {
                EndpointId = calleeEpId,
                CallSignalAddresses = new[] { new IPEndPoint(Loopback, calleePort) },
                Aliases = new[] { "Bob" },
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });

            var serverPort = FindFreePort();
            var server = new H323Server(Loopback, serverPort, registry);
            server.Start();
            await Task.Delay(200);

            try
            {
                // Accept callee connection in background
                var calleeTask = Task.Run(async () =>
                {
                    using var calleeClient = await calleeListener.AcceptTcpClientAsync();
                    var calleeStream = calleeClient.GetStream();

                    // Should receive forwarded Setup
                    var setupPayload = await TpktFrame.ReadAsync(calleeStream);
                    var setupMsg = Q931Message.Parse(setupPayload);
                    Assert.AreEqual(Q931Message.MSG_SETUP, setupMsg.MessageType);

                    // Send Connect back
                    var connUuie = H225CallCodec.EncodeConnect(new ConnectUuie
                    {
                        ConferenceId = new byte[16],
                        H245Address = new IPEndPoint(Loopback, 9999)
                    });

                    var connMsg = Q931Message.CreateConnect(setupMsg.CallReference, connUuie);
                    await TpktFrame.WriteAsync(calleeStream, connMsg.Build());

                    // Wait a moment then disconnect
                    await Task.Delay(200);
                });

                // Connect as caller
                using var caller = new TcpClient();
                await caller.ConnectAsync(Loopback, serverPort);
                var callerStream = caller.GetStream();

                // Send Setup
                var setup = new SetupUuie
                {
                    SourceAliases = new[] { "Alice" },
                    DestinationAliases = new[] { "Bob" },
                    ConferenceId = new byte[16],
                    ConferenceGoal = H225CallCodec.GOAL_CREATE,
                    CallType = 0
                };

                var uuie = H225CallCodec.EncodeSetup(setup);
                var q931Setup = Q931Message.CreateSetup(1, uuie);
                await TpktFrame.WriteAsync(callerStream, q931Setup.Build());

                // Should receive CallProceeding
                var cpPayload = await TpktFrame.ReadAsync(callerStream);
                Assert.IsNotNull(cpPayload);
                var cpMsg = Q931Message.Parse(cpPayload);
                Assert.AreEqual(Q931Message.MSG_CALL_PROCEEDING, cpMsg.MessageType);

                // Should receive Connect (forwarded from callee)
                var connPayload = await TpktFrame.ReadAsync(callerStream);
                Assert.IsNotNull(connPayload);
                var connResp = Q931Message.Parse(connPayload);
                Assert.AreEqual(Q931Message.MSG_CONNECT, connResp.MessageType);

                await calleeTask;
            }
            finally
            {
                server.IsListening = false;
            }
        }
        finally
        {
            calleeListener.Stop();
        }
    }

    /// <summary>
    /// Find a free TCP port by binding to port 0 and releasing.
    /// </summary>
    private static int FindFreePort()
    {
        var listener = new TcpListener(Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
