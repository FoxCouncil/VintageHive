// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using VintageHive.Proxy.NetMeeting;
using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.H245;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  H.245 Constants tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245ConstantsTests
{
    [TestMethod]
    public void MessageName_Request_MasterSlaveDetermination()
    {
        var name = H245Constants.MessageName(H245Constants.MSG_REQUEST,
            H245Constants.REQ_MASTER_SLAVE_DETERMINATION);
        Assert.AreEqual("MasterSlaveDetermination", name);
    }

    [TestMethod]
    public void MessageName_Response_OpenLogicalChannelAck()
    {
        var name = H245Constants.MessageName(H245Constants.MSG_RESPONSE,
            H245Constants.RSP_OPEN_LOGICAL_CHANNEL_ACK);
        Assert.AreEqual("OpenLogicalChannelAck", name);
    }

    [TestMethod]
    public void MessageName_Unknown_ReturnsGeneric()
    {
        var name = H245Constants.MessageName(H245Constants.MSG_COMMAND, 99);
        Assert.IsTrue(name.Contains("99"));
    }
}

// ──────────────────────────────────────────────────────────
//  MasterSlaveDetermination codec tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245MasterSlaveTests
{
    [TestMethod]
    public void MasterSlaveDetermination_EncodeDeocde_RoundTrip()
    {
        var msd = new MasterSlaveDetermination
        {
            TerminalType = 50,
            StatusDeterminationNumber = 12345678
        };

        var bytes = H245Codec.EncodeMasterSlaveDetermination(msd);
        Assert.IsTrue(bytes.Length > 0);

        var decoded = H245Codec.Decode(bytes);
        Assert.AreEqual(H245Constants.MSG_REQUEST, decoded.TopLevel);
        Assert.AreEqual(H245Constants.REQ_MASTER_SLAVE_DETERMINATION, decoded.SubIndex);
        Assert.IsNotNull(decoded.MasterSlaveDetermination);
        Assert.AreEqual(50, decoded.MasterSlaveDetermination.TerminalType);
        Assert.AreEqual(12345678, decoded.MasterSlaveDetermination.StatusDeterminationNumber);
    }

    [TestMethod]
    public void MasterSlaveDetermination_BoundaryValues()
    {
        var msd = new MasterSlaveDetermination
        {
            TerminalType = 255,
            StatusDeterminationNumber = 16777215
        };

        var bytes = H245Codec.EncodeMasterSlaveDetermination(msd);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(255, decoded.MasterSlaveDetermination.TerminalType);
        Assert.AreEqual(16777215, decoded.MasterSlaveDetermination.StatusDeterminationNumber);
    }

    [TestMethod]
    public void MasterSlaveDetermination_ZeroValues()
    {
        var msd = new MasterSlaveDetermination
        {
            TerminalType = 0,
            StatusDeterminationNumber = 0
        };

        var bytes = H245Codec.EncodeMasterSlaveDetermination(msd);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(0, decoded.MasterSlaveDetermination.TerminalType);
        Assert.AreEqual(0, decoded.MasterSlaveDetermination.StatusDeterminationNumber);
    }

    [TestMethod]
    public void MasterSlaveDeterminationAck_Master_RoundTrip()
    {
        var ack = new MasterSlaveDeterminationAck
        {
            Decision = H245Constants.MSD_DECISION_MASTER
        };

        var bytes = H245Codec.EncodeMasterSlaveDeterminationAck(ack);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_RESPONSE, decoded.TopLevel);
        Assert.AreEqual(H245Constants.RSP_MASTER_SLAVE_DETERMINATION_ACK, decoded.SubIndex);
        Assert.IsNotNull(decoded.MasterSlaveDeterminationAck);
        Assert.AreEqual(H245Constants.MSD_DECISION_MASTER, decoded.MasterSlaveDeterminationAck.Decision);
    }

    [TestMethod]
    public void MasterSlaveDeterminationAck_Slave_RoundTrip()
    {
        var ack = new MasterSlaveDeterminationAck
        {
            Decision = H245Constants.MSD_DECISION_SLAVE
        };

        var bytes = H245Codec.EncodeMasterSlaveDeterminationAck(ack);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSD_DECISION_SLAVE, decoded.MasterSlaveDeterminationAck.Decision);
    }

    [TestMethod]
    public void MasterSlaveDeterminationReject_RoundTrip()
    {
        var rej = new MasterSlaveDeterminationReject
        {
            Cause = H245Constants.MSD_REJECT_IDENTICAL
        };

        var bytes = H245Codec.EncodeMasterSlaveDeterminationReject(rej);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_RESPONSE, decoded.TopLevel);
        Assert.AreEqual(H245Constants.RSP_MASTER_SLAVE_DETERMINATION_REJECT, decoded.SubIndex);
        Assert.IsNotNull(decoded.MasterSlaveDeterminationReject);
        Assert.AreEqual(H245Constants.MSD_REJECT_IDENTICAL, decoded.MasterSlaveDeterminationReject.Cause);
    }
}

// ──────────────────────────────────────────────────────────
//  TerminalCapabilitySet codec tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245TerminalCapabilitySetTests
{
    [TestMethod]
    public void TerminalCapabilitySetAck_RoundTrip()
    {
        var ack = new TerminalCapabilitySetAck { SequenceNumber = 42 };

        var bytes = H245Codec.EncodeTerminalCapabilitySetAck(ack);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_RESPONSE, decoded.TopLevel);
        Assert.AreEqual(H245Constants.RSP_TERMINAL_CAPABILITY_SET_ACK, decoded.SubIndex);
        Assert.IsNotNull(decoded.TerminalCapabilitySetAck);
        Assert.AreEqual(42, decoded.TerminalCapabilitySetAck.SequenceNumber);
    }

    [TestMethod]
    public void TerminalCapabilitySetAck_BoundaryValues()
    {
        foreach (var seqNum in new[] { 0, 127, 255 })
        {
            var ack = new TerminalCapabilitySetAck { SequenceNumber = seqNum };
            var bytes = H245Codec.EncodeTerminalCapabilitySetAck(ack);
            var decoded = H245Codec.Decode(bytes);
            Assert.AreEqual(seqNum, decoded.TerminalCapabilitySetAck.SequenceNumber);
        }
    }

    [TestMethod]
    public void TerminalCapabilitySetReject_Unspecified_RoundTrip()
    {
        var rej = new TerminalCapabilitySetReject
        {
            SequenceNumber = 10,
            Cause = H245Constants.TCS_REJ_UNSPECIFIED
        };

        var bytes = H245Codec.EncodeTerminalCapabilitySetReject(rej);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_RESPONSE, decoded.TopLevel);
        Assert.AreEqual(H245Constants.RSP_TERMINAL_CAPABILITY_SET_REJECT, decoded.SubIndex);
        Assert.IsNotNull(decoded.TerminalCapabilitySetReject);
        Assert.AreEqual(10, decoded.TerminalCapabilitySetReject.SequenceNumber);
        Assert.AreEqual(H245Constants.TCS_REJ_UNSPECIFIED, decoded.TerminalCapabilitySetReject.Cause);
    }

    [TestMethod]
    public void TerminalCapabilitySetReject_TableEntryCapacityExceeded()
    {
        var rej = new TerminalCapabilitySetReject
        {
            SequenceNumber = 200,
            Cause = H245Constants.TCS_REJ_TABLE_ENTRY_CAPACITY_EXCEEDED
        };

        var bytes = H245Codec.EncodeTerminalCapabilitySetReject(rej);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(200, decoded.TerminalCapabilitySetReject.SequenceNumber);
        Assert.AreEqual(H245Constants.TCS_REJ_TABLE_ENTRY_CAPACITY_EXCEEDED,
            decoded.TerminalCapabilitySetReject.Cause);
    }
}

// ──────────────────────────────────────────────────────────
//  CloseLogicalChannel codec tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245CloseLogicalChannelTests
{
    [TestMethod]
    public void CloseLogicalChannel_UserSource_RoundTrip()
    {
        var clc = new CloseLogicalChannel
        {
            ForwardLogicalChannelNumber = 1001,
            Source = H245Constants.CLC_SOURCE_USER
        };

        var bytes = H245Codec.EncodeCloseLogicalChannel(clc);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_REQUEST, decoded.TopLevel);
        Assert.AreEqual(H245Constants.REQ_CLOSE_LOGICAL_CHANNEL, decoded.SubIndex);
        Assert.IsNotNull(decoded.CloseLogicalChannel);
        Assert.AreEqual(1001, decoded.CloseLogicalChannel.ForwardLogicalChannelNumber);
        Assert.AreEqual(H245Constants.CLC_SOURCE_USER, decoded.CloseLogicalChannel.Source);
    }

    [TestMethod]
    public void CloseLogicalChannel_LcseSource_RoundTrip()
    {
        var clc = new CloseLogicalChannel
        {
            ForwardLogicalChannelNumber = 65535,
            Source = H245Constants.CLC_SOURCE_LCSE
        };

        var bytes = H245Codec.EncodeCloseLogicalChannel(clc);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(65535, decoded.CloseLogicalChannel.ForwardLogicalChannelNumber);
        Assert.AreEqual(H245Constants.CLC_SOURCE_LCSE, decoded.CloseLogicalChannel.Source);
    }

    [TestMethod]
    public void CloseLogicalChannelAck_RoundTrip()
    {
        var ack = new CloseLogicalChannelAck { ForwardLogicalChannelNumber = 500 };

        var bytes = H245Codec.EncodeCloseLogicalChannelAck(ack);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_RESPONSE, decoded.TopLevel);
        Assert.AreEqual(H245Constants.RSP_CLOSE_LOGICAL_CHANNEL_ACK, decoded.SubIndex);
        Assert.IsNotNull(decoded.CloseLogicalChannelAck);
        Assert.AreEqual(500, decoded.CloseLogicalChannelAck.ForwardLogicalChannelNumber);
    }
}

// ──────────────────────────────────────────────────────────
//  OpenLogicalChannelReject codec tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245OpenLogicalChannelRejectTests
{
    [TestMethod]
    public void OpenLogicalChannelReject_Unspecified_RoundTrip()
    {
        var rej = new OpenLogicalChannelReject
        {
            ForwardLogicalChannelNumber = 100,
            Cause = H245Constants.OLC_REJ_UNSPECIFIED
        };

        var bytes = H245Codec.EncodeOpenLogicalChannelReject(rej);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_RESPONSE, decoded.TopLevel);
        Assert.AreEqual(H245Constants.RSP_OPEN_LOGICAL_CHANNEL_REJECT, decoded.SubIndex);
        Assert.IsNotNull(decoded.OpenLogicalChannelReject);
        Assert.AreEqual(100, decoded.OpenLogicalChannelReject.ForwardLogicalChannelNumber);
        Assert.AreEqual(H245Constants.OLC_REJ_UNSPECIFIED, decoded.OpenLogicalChannelReject.Cause);
    }

    [TestMethod]
    public void OpenLogicalChannelReject_DataTypeNotSupported()
    {
        var rej = new OpenLogicalChannelReject
        {
            ForwardLogicalChannelNumber = 2,
            Cause = H245Constants.OLC_REJ_DATA_TYPE_NOT_SUPPORTED
        };

        var bytes = H245Codec.EncodeOpenLogicalChannelReject(rej);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(2, decoded.OpenLogicalChannelReject.ForwardLogicalChannelNumber);
        Assert.AreEqual(H245Constants.OLC_REJ_DATA_TYPE_NOT_SUPPORTED,
            decoded.OpenLogicalChannelReject.Cause);
    }
}

// ──────────────────────────────────────────────────────────
//  RoundTripDelayRequest codec tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245RoundTripDelayTests
{
    [TestMethod]
    public void RoundTripDelayRequest_RoundTrip()
    {
        var req = new RoundTripDelayRequest { SequenceNumber = 77 };

        var bytes = H245Codec.EncodeRoundTripDelayRequest(req);
        var decoded = H245Codec.Decode(bytes);

        Assert.AreEqual(H245Constants.MSG_REQUEST, decoded.TopLevel);
        Assert.AreEqual(H245Constants.REQ_ROUND_TRIP_DELAY_REQUEST, decoded.SubIndex);
        Assert.IsNotNull(decoded.RoundTripDelayRequest);
        Assert.AreEqual(77, decoded.RoundTripDelayRequest.SequenceNumber);
    }

    [TestMethod]
    public void RoundTripDelayRequest_BoundaryValues()
    {
        foreach (var seqNum in new[] { 0, 128, 255 })
        {
            var bytes = H245Codec.EncodeRoundTripDelayRequest(
                new RoundTripDelayRequest { SequenceNumber = seqNum });
            var decoded = H245Codec.Decode(bytes);
            Assert.AreEqual(seqNum, decoded.RoundTripDelayRequest.SequenceNumber);
        }
    }
}

// ──────────────────────────────────────────────────────────
//  H245Message ToString test
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245MessageTests
{
    [TestMethod]
    public void ToString_ReturnsMessageName()
    {
        var msg = new H245Message
        {
            TopLevel = H245Constants.MSG_REQUEST,
            SubIndex = H245Constants.REQ_OPEN_LOGICAL_CHANNEL
        };

        Assert.AreEqual("OpenLogicalChannel", msg.ToString());
    }
}

// ──────────────────────────────────────────────────────────
//  TPKT + H.245 integration tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245TpktIntegrationTests
{
    [TestMethod]
    public async Task Tpkt_H245_MasterSlaveDetermination_TcpRoundTrip()
    {
        var msd = new MasterSlaveDetermination
        {
            TerminalType = 120,
            StatusDeterminationNumber = 9999999
        };

        var payload = H245Codec.EncodeMasterSlaveDetermination(msd);

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
            await TpktFrame.WriteAsync(senderStream, payload);
            sender.Client.Shutdown(SocketShutdown.Send);

            var received = await serverTask;
            Assert.IsNotNull(received);
            CollectionAssert.AreEqual(payload, received);

            var decoded = H245Codec.Decode(received);
            Assert.AreEqual(120, decoded.MasterSlaveDetermination.TerminalType);
            Assert.AreEqual(9999999, decoded.MasterSlaveDetermination.StatusDeterminationNumber);
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Tpkt_H245_MultipleMessages_TcpRoundTrip()
    {
        var msgs = new[]
        {
            H245Codec.EncodeMasterSlaveDetermination(new MasterSlaveDetermination
            {
                TerminalType = 50,
                StatusDeterminationNumber = 1234
            }),
            H245Codec.EncodeMasterSlaveDeterminationAck(new MasterSlaveDeterminationAck
            {
                Decision = H245Constants.MSD_DECISION_MASTER
            }),
            H245Codec.EncodeTerminalCapabilitySetAck(new TerminalCapabilitySetAck
            {
                SequenceNumber = 1
            })
        };

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var received = new List<byte[]>();

                for (var i = 0; i < 3; i++)
                {
                    var payload = await TpktFrame.ReadAsync(stream);
                    if (payload != null)
                    {
                        received.Add(payload);
                    }
                }

                return received;
            });

            using var sender = new TcpClient();
            await sender.ConnectAsync(IPAddress.Loopback, port);
            var senderStream = sender.GetStream();

            foreach (var msg in msgs)
            {
                await TpktFrame.WriteAsync(senderStream, msg);
            }

            sender.Client.Shutdown(SocketShutdown.Send);

            var results = await serverTask;
            Assert.AreEqual(3, results.Count);

            var decoded0 = H245Codec.Decode(results[0]);
            Assert.AreEqual(H245Constants.REQ_MASTER_SLAVE_DETERMINATION, decoded0.SubIndex);

            var decoded1 = H245Codec.Decode(results[1]);
            Assert.AreEqual(H245Constants.RSP_MASTER_SLAVE_DETERMINATION_ACK, decoded1.SubIndex);

            var decoded2 = H245Codec.Decode(results[2]);
            Assert.AreEqual(H245Constants.RSP_TERMINAL_CAPABILITY_SET_ACK, decoded2.SubIndex);
        }
        finally
        {
            listener.Stop();
        }
    }
}

// ──────────────────────────────────────────────────────────
//  H245Handler tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H245HandlerTests
{
    [TestMethod]
    public async Task Handler_ProxiesMessages_Bidirectionally()
    {
        // Set up the handler (acts as proxy between "caller" and "callee")
        var handlerPort = FindFreePort();
        var calleeListenerPort = FindFreePort();

        // Callee's H.245 listener
        var calleeListener = new TcpListener(IPAddress.Loopback, calleeListenerPort);
        calleeListener.Start();

        using var handler = new H245Handler(IPAddress.Loopback, handlerPort);
        var calleeEndpoint = new IPEndPoint(IPAddress.Loopback, calleeListenerPort);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start handler in background
        var handlerTask = handler.RunAsync(calleeEndpoint, cts.Token);

        try
        {
            // Callee accepts outbound from handler
            var calleeAcceptTask = calleeListener.AcceptTcpClientAsync();

            // Caller connects to handler's inbound port
            using var callerClient = new TcpClient();
            await callerClient.ConnectAsync(IPAddress.Loopback, handlerPort);
            var callerStream = callerClient.GetStream();

            using var calleeClient = await calleeAcceptTask;
            var calleeStream = calleeClient.GetStream();

            // Caller sends MasterSlaveDetermination
            var msdBytes = H245Codec.EncodeMasterSlaveDetermination(new MasterSlaveDetermination
            {
                TerminalType = 70,
                StatusDeterminationNumber = 555555
            });
            await TpktFrame.WriteAsync(callerStream, msdBytes);

            // Callee should receive it
            var received = await TpktFrame.ReadAsync(calleeStream);
            Assert.IsNotNull(received);
            var decoded = H245Codec.Decode(received);
            Assert.AreEqual(70, decoded.MasterSlaveDetermination.TerminalType);

            // Callee sends MasterSlaveDeterminationAck back
            var ackBytes = H245Codec.EncodeMasterSlaveDeterminationAck(new MasterSlaveDeterminationAck
            {
                Decision = H245Constants.MSD_DECISION_SLAVE
            });
            await TpktFrame.WriteAsync(calleeStream, ackBytes);

            // Caller should receive the ack
            var receivedAck = await TpktFrame.ReadAsync(callerStream);
            Assert.IsNotNull(receivedAck);
            var decodedAck = H245Codec.Decode(receivedAck);
            Assert.AreEqual(H245Constants.MSD_DECISION_SLAVE,
                decodedAck.MasterSlaveDeterminationAck.Decision);

            // Verify handler tracked the state
            Assert.IsTrue(handler.MessageCount >= 2);
            Assert.IsTrue(handler.MasterSlaveResolved);

            // Close connections to end the proxy
            callerClient.Client.Shutdown(SocketShutdown.Both);
            calleeClient.Client.Shutdown(SocketShutdown.Both);
        }
        finally
        {
            cts.Cancel();
            calleeListener.Stop();

            try { await handlerTask; } catch { }
        }
    }

    [TestMethod]
    public async Task Handler_TracksLogicalChannels()
    {
        var handlerPort = FindFreePort();
        var calleeListenerPort = FindFreePort();

        var calleeListener = new TcpListener(IPAddress.Loopback, calleeListenerPort);
        calleeListener.Start();

        using var handler = new H245Handler(IPAddress.Loopback, handlerPort);
        var calleeEndpoint = new IPEndPoint(IPAddress.Loopback, calleeListenerPort);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var handlerTask = handler.RunAsync(calleeEndpoint, cts.Token);

        try
        {
            var calleeAcceptTask = calleeListener.AcceptTcpClientAsync();

            using var callerClient = new TcpClient();
            await callerClient.ConnectAsync(IPAddress.Loopback, handlerPort);
            var callerStream = callerClient.GetStream();

            using var calleeClient = await calleeAcceptTask;
            var calleeStream = calleeClient.GetStream();

            // Caller sends CloseLogicalChannel (simpler to encode than full OLC)
            var clcBytes = H245Codec.EncodeCloseLogicalChannel(new CloseLogicalChannel
            {
                ForwardLogicalChannelNumber = 42,
                Source = H245Constants.CLC_SOURCE_USER
            });
            await TpktFrame.WriteAsync(callerStream, clcBytes);

            // Wait for callee to receive
            var received = await TpktFrame.ReadAsync(calleeStream);
            Assert.IsNotNull(received);

            // Callee sends CloseLogicalChannelAck
            var ackBytes = H245Codec.EncodeCloseLogicalChannelAck(new CloseLogicalChannelAck
            {
                ForwardLogicalChannelNumber = 42
            });
            await TpktFrame.WriteAsync(calleeStream, ackBytes);

            // Wait for caller to receive the ack
            var receivedAck = await TpktFrame.ReadAsync(callerStream);
            Assert.IsNotNull(receivedAck);

            // Allow a moment for the handler to process
            await Task.Delay(50);

            // Verify handler tracked the close
            Assert.IsTrue(handler.LogicalChannels.ContainsKey(42));
            Assert.AreEqual(LogicalChannelState.Closed, handler.LogicalChannels[42].State);

            callerClient.Client.Shutdown(SocketShutdown.Both);
            calleeClient.Client.Shutdown(SocketShutdown.Both);
        }
        finally
        {
            cts.Cancel();
            calleeListener.Stop();

            try { await handlerTask; } catch { }
        }
    }

    [TestMethod]
    public void LogicalChannelInfo_Properties()
    {
        var info = new LogicalChannelInfo
        {
            ChannelNumber = 100,
            SessionId = H245Constants.SESSION_AUDIO,
            DataType = H245Constants.DATA_TYPE_AUDIO_DATA,
            State = LogicalChannelState.Open,
            SenderMediaChannel = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 5000),
            SenderMediaControlChannel = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 5001),
            ReceiverMediaChannel = new IPEndPoint(IPAddress.Parse("192.168.1.20"), 6000),
            ReceiverMediaControlChannel = new IPEndPoint(IPAddress.Parse("192.168.1.20"), 6001)
        };

        Assert.AreEqual(100, info.ChannelNumber);
        Assert.AreEqual(H245Constants.SESSION_AUDIO, info.SessionId);
        Assert.AreEqual(LogicalChannelState.Open, info.State);
        Assert.AreEqual(5000, info.SenderMediaChannel.Port);
        Assert.AreEqual(6001, info.ReceiverMediaControlChannel.Port);
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
