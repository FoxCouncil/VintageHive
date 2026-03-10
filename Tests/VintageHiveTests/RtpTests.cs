// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using VintageHive.Proxy.NetMeeting.Rtp;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  RTP Constants tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class RtpConstantsTests
{
    [TestMethod]
    public void PayloadTypeName_Audio()
    {
        Assert.AreEqual("G.711µ", RtpConstants.PayloadTypeName(RtpConstants.PT_PCMU));
        Assert.AreEqual("G.723.1", RtpConstants.PayloadTypeName(RtpConstants.PT_G723));
        Assert.AreEqual("G.711A", RtpConstants.PayloadTypeName(RtpConstants.PT_PCMA));
        Assert.AreEqual("G.729", RtpConstants.PayloadTypeName(RtpConstants.PT_G729));
    }

    [TestMethod]
    public void PayloadTypeName_Video()
    {
        Assert.AreEqual("H.261", RtpConstants.PayloadTypeName(RtpConstants.PT_H261));
        Assert.AreEqual("H.263", RtpConstants.PayloadTypeName(RtpConstants.PT_H263));
    }

    [TestMethod]
    public void PayloadTypeName_Dynamic()
    {
        Assert.AreEqual("PT96", RtpConstants.PayloadTypeName(96));
        Assert.AreEqual("PT127", RtpConstants.PayloadTypeName(127));
    }

    [TestMethod]
    public void RtcpTypeName_AllStandard()
    {
        Assert.AreEqual("SR", RtpConstants.RtcpTypeName(RtpConstants.RTCP_SR));
        Assert.AreEqual("RR", RtpConstants.RtcpTypeName(RtpConstants.RTCP_RR));
        Assert.AreEqual("SDES", RtpConstants.RtcpTypeName(RtpConstants.RTCP_SDES));
        Assert.AreEqual("BYE", RtpConstants.RtcpTypeName(RtpConstants.RTCP_BYE));
        Assert.AreEqual("APP", RtpConstants.RtcpTypeName(RtpConstants.RTCP_APP));
    }

    [TestMethod]
    public void Constants_PortRange()
    {
        Assert.AreEqual(49152, RtpConstants.RELAY_PORT_RANGE_START);
        Assert.AreEqual(65534, RtpConstants.RELAY_PORT_RANGE_END);
        Assert.AreEqual(0, RtpConstants.RELAY_PORT_RANGE_START % 2); // Even start
        Assert.AreEqual(0, RtpConstants.RELAY_PORT_RANGE_END % 2);   // Even end
    }
}

// ──────────────────────────────────────────────────────────
//  RTP header parser tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class RtpHeaderTests
{
    [TestMethod]
    public void TryParse_ValidPacket()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var packet = RtpHeader.Build(
            payloadType: RtpConstants.PT_PCMU,
            sequenceNumber: 1234,
            timestamp: 160000,
            ssrc: 0x12345678,
            payload: payload);

        Assert.IsTrue(RtpHeader.TryParse(packet, packet.Length, out var header));
        Assert.AreEqual(2, header.Version);
        Assert.IsFalse(header.Padding);
        Assert.IsFalse(header.Extension);
        Assert.AreEqual(0, header.CsrcCount);
        Assert.IsFalse(header.Marker);
        Assert.AreEqual(RtpConstants.PT_PCMU, header.PayloadType);
        Assert.AreEqual(1234, header.SequenceNumber);
        Assert.AreEqual(160000u, header.Timestamp);
        Assert.AreEqual(0x12345678u, header.Ssrc);
        Assert.AreEqual(12, header.HeaderSize);
    }

    [TestMethod]
    public void TryParse_MarkerBit()
    {
        var packet = RtpHeader.Build(
            payloadType: RtpConstants.PT_H261,
            sequenceNumber: 42,
            timestamp: 90000,
            ssrc: 0xAABBCCDD,
            payload: new byte[10],
            marker: true);

        Assert.IsTrue(RtpHeader.TryParse(packet, packet.Length, out var header));
        Assert.IsTrue(header.Marker);
        Assert.AreEqual(RtpConstants.PT_H261, header.PayloadType);
    }

    [TestMethod]
    public void TryParse_MaxSequenceNumber()
    {
        var packet = RtpHeader.Build(
            payloadType: 0,
            sequenceNumber: 65535,
            timestamp: uint.MaxValue,
            ssrc: uint.MaxValue,
            payload: Array.Empty<byte>());

        Assert.IsTrue(RtpHeader.TryParse(packet, packet.Length, out var header));
        Assert.AreEqual(65535, header.SequenceNumber);
        Assert.AreEqual(uint.MaxValue, header.Timestamp);
        Assert.AreEqual(uint.MaxValue, header.Ssrc);
    }

    [TestMethod]
    public void TryParse_TooShort_ReturnsFalse()
    {
        var data = new byte[11]; // Less than 12-byte minimum
        Assert.IsFalse(RtpHeader.TryParse(data, data.Length, out _));
    }

    [TestMethod]
    public void TryParse_WrongVersion_ReturnsFalse()
    {
        var data = new byte[12];
        data[0] = 0x00; // Version 0 instead of 2
        Assert.IsFalse(RtpHeader.TryParse(data, data.Length, out _));

        data[0] = 0x40; // Version 1
        Assert.IsFalse(RtpHeader.TryParse(data, data.Length, out _));
    }

    [TestMethod]
    public void TryParse_WithCsrc_TooShort_ReturnsFalse()
    {
        var data = new byte[12];
        data[0] = 0x81; // V=2, CC=1 — needs 16 bytes but only 12 provided
        Assert.IsFalse(RtpHeader.TryParse(data, data.Length, out _));
    }

    [TestMethod]
    public void Build_CreatesCorrectLayout()
    {
        var packet = RtpHeader.Build(
            payloadType: 4,
            sequenceNumber: 0x0102,
            timestamp: 0x03040506,
            ssrc: 0x0708090A,
            payload: new byte[] { 0xFF });

        // V=2, P=0, X=0, CC=0 → 0x80
        Assert.AreEqual(0x80, packet[0]);
        // M=0, PT=4 → 0x04
        Assert.AreEqual(0x04, packet[1]);
        // Seq = 0x0102
        Assert.AreEqual(0x01, packet[2]);
        Assert.AreEqual(0x02, packet[3]);
        // Timestamp = 0x03040506
        Assert.AreEqual(0x03, packet[4]);
        Assert.AreEqual(0x04, packet[5]);
        Assert.AreEqual(0x05, packet[6]);
        Assert.AreEqual(0x06, packet[7]);
        // SSRC = 0x0708090A
        Assert.AreEqual(0x07, packet[8]);
        Assert.AreEqual(0x08, packet[9]);
        Assert.AreEqual(0x09, packet[10]);
        Assert.AreEqual(0x0A, packet[11]);
        // Payload
        Assert.AreEqual(0xFF, packet[12]);
        Assert.AreEqual(13, packet.Length);
    }
}

// ──────────────────────────────────────────────────────────
//  RTCP header parser tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class RtcpHeaderTests
{
    [TestMethod]
    public void TryParse_SenderReport()
    {
        var sr = RtcpHeader.BuildSenderReport(
            ssrc: 0x11223344,
            ntpHi: 1000, ntpLo: 2000,
            rtpTimestamp: 3000,
            packetCount: 100,
            octetCount: 16000);

        Assert.IsTrue(RtcpHeader.TryParse(sr, sr.Length, out var header));
        Assert.AreEqual(2, header.Version);
        Assert.IsFalse(header.Padding);
        Assert.AreEqual(0, header.Count);
        Assert.AreEqual(RtpConstants.RTCP_SR, header.PacketType);
        Assert.AreEqual(6, header.Length); // (28/4)-1
        Assert.AreEqual(0x11223344u, header.Ssrc);
    }

    [TestMethod]
    public void TryParse_TooShort_ReturnsFalse()
    {
        var data = new byte[7]; // Less than 8-byte minimum
        Assert.IsFalse(RtcpHeader.TryParse(data, data.Length, out _));
    }

    [TestMethod]
    public void TryParse_WrongVersion_ReturnsFalse()
    {
        var data = new byte[8];
        data[0] = 0x00; // Version 0
        data[1] = 200;  // SR type
        Assert.IsFalse(RtcpHeader.TryParse(data, data.Length, out _));
    }

    [TestMethod]
    public void TryParse_InvalidPacketType_ReturnsFalse()
    {
        var data = new byte[8];
        data[0] = 0x80; // V=2
        data[1] = 100;  // Not an RTCP type
        Assert.IsFalse(RtcpHeader.TryParse(data, data.Length, out _));
    }

    [TestMethod]
    public void BuildSenderReport_CorrectLayout()
    {
        var sr = RtcpHeader.BuildSenderReport(
            ssrc: 0xAABBCCDD,
            ntpHi: 0x11111111, ntpLo: 0x22222222,
            rtpTimestamp: 0x33333333,
            packetCount: 0x44444444,
            octetCount: 0x55555555);

        Assert.AreEqual(28, sr.Length);
        // V=2, P=0, RC=0
        Assert.AreEqual(0x80, sr[0]);
        // PT=200
        Assert.AreEqual(200, sr[1]);
    }
}

// ──────────────────────────────────────────────────────────
//  RTP Relay tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class RtpRelayTests
{
    [TestMethod]
    public async Task Relay_ForwardsPackets_AtoB()
    {
        var relayPort = FindFreeEvenPort();

        using var relay = new RtpRelay(IPAddress.Loopback, relayPort, "test-RTP");

        // Set up "endpoint A" and "endpoint B" UDP clients
        using var clientA = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var clientB = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var portA = ((IPEndPoint)clientA.Client.LocalEndPoint!).Port;
        var portB = ((IPEndPoint)clientB.Client.LocalEndPoint!).Port;

        relay.SetEndpoints(
            new IPEndPoint(IPAddress.Loopback, portA),
            new IPEndPoint(IPAddress.Loopback, portB));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var relayTask = relay.RunAsync(cts.Token);

        try
        {
            // A sends to relay
            var testData = RtpHeader.Build(RtpConstants.PT_PCMU, 1, 160, 0x1234, new byte[] { 0xAA, 0xBB });
            var relayEndpoint = new IPEndPoint(IPAddress.Loopback, relayPort);
            await clientA.SendAsync(testData, testData.Length, relayEndpoint);

            // B should receive the forwarded packet
            var receiveTask = clientB.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(2000));
            Assert.AreEqual(receiveTask, completed, "B should receive the forwarded packet");

            var received = await receiveTask;
            CollectionAssert.AreEqual(testData, received.Buffer);

            // Allow relay task continuation to increment counters
            await Task.Delay(50);

            Assert.AreEqual(1, relay.PacketsAtoB);
            Assert.AreEqual(testData.Length, relay.BytesAtoB);
        }
        finally
        {
            cts.Cancel();
            try { await relayTask; } catch { }
        }
    }

    [TestMethod]
    public async Task Relay_ForwardsPackets_BtoA()
    {
        var relayPort = FindFreeEvenPort();

        using var relay = new RtpRelay(IPAddress.Loopback, relayPort, "test-RTP");

        using var clientA = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var clientB = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var portA = ((IPEndPoint)clientA.Client.LocalEndPoint!).Port;
        var portB = ((IPEndPoint)clientB.Client.LocalEndPoint!).Port;

        relay.SetEndpoints(
            new IPEndPoint(IPAddress.Loopback, portA),
            new IPEndPoint(IPAddress.Loopback, portB));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var relayTask = relay.RunAsync(cts.Token);

        try
        {
            // B sends to relay
            var testData = new byte[] { 0x11, 0x22, 0x33 };
            var relayEndpoint = new IPEndPoint(IPAddress.Loopback, relayPort);
            await clientB.SendAsync(testData, testData.Length, relayEndpoint);

            // A should receive the forwarded packet
            var receiveTask = clientA.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(2000));
            Assert.AreEqual(receiveTask, completed, "A should receive the forwarded packet");

            var received = await receiveTask;
            CollectionAssert.AreEqual(testData, received.Buffer);

            // Allow relay task continuation to increment counters
            await Task.Delay(50);

            Assert.AreEqual(1, relay.PacketsBtoA);
            Assert.AreEqual(testData.Length, relay.BytesBtoA);
        }
        finally
        {
            cts.Cancel();
            try { await relayTask; } catch { }
        }
    }

    [TestMethod]
    public async Task Relay_Bidirectional_MultiplePackets()
    {
        var relayPort = FindFreeEvenPort();

        using var relay = new RtpRelay(IPAddress.Loopback, relayPort, "test-bidir");

        using var clientA = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var clientB = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var portA = ((IPEndPoint)clientA.Client.LocalEndPoint!).Port;
        var portB = ((IPEndPoint)clientB.Client.LocalEndPoint!).Port;

        relay.SetEndpoints(
            new IPEndPoint(IPAddress.Loopback, portA),
            new IPEndPoint(IPAddress.Loopback, portB));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var relayTask = relay.RunAsync(cts.Token);

        var relayEndpoint = new IPEndPoint(IPAddress.Loopback, relayPort);

        try
        {
            // Send 5 packets A→B, 3 packets B→A
            for (var i = 0; i < 5; i++)
            {
                var data = new byte[] { (byte)i };
                await clientA.SendAsync(data, data.Length, relayEndpoint);

                var bReceive = await clientB.ReceiveAsync();
                Assert.AreEqual((byte)i, bReceive.Buffer[0]);
            }

            for (var i = 0; i < 3; i++)
            {
                var data = new byte[] { (byte)(0x80 | i) };
                await clientB.SendAsync(data, data.Length, relayEndpoint);

                var aReceive = await clientA.ReceiveAsync();
                Assert.AreEqual((byte)(0x80 | i), aReceive.Buffer[0]);
            }

            Assert.AreEqual(5, relay.PacketsAtoB);
            Assert.AreEqual(3, relay.PacketsBtoA);
            Assert.AreEqual(8, relay.TotalPackets);
        }
        finally
        {
            cts.Cancel();
            try { await relayTask; } catch { }
        }
    }

    [TestMethod]
    public void Relay_Properties_InitialState()
    {
        var relayPort = FindFreeEvenPort();
        using var relay = new RtpRelay(IPAddress.Loopback, relayPort, "test");

        Assert.AreEqual(relayPort, relay.LocalPort);
        Assert.IsNull(relay.EndpointA);
        Assert.IsNull(relay.EndpointB);
        Assert.AreEqual(0, relay.PacketsAtoB);
        Assert.AreEqual(0, relay.PacketsBtoA);
        Assert.AreEqual(0, relay.TotalPackets);
        Assert.AreEqual(0, relay.TotalBytes);
        Assert.IsNull(relay.StartedAt);
        Assert.IsNull(relay.StoppedAt);
    }

    [TestMethod]
    public void Relay_SetEndpoints()
    {
        var relayPort = FindFreeEvenPort();
        using var relay = new RtpRelay(IPAddress.Loopback, relayPort, "test");

        var epA = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 5000);
        var epB = new IPEndPoint(IPAddress.Parse("192.168.1.20"), 6000);

        relay.SetEndpoints(epA, epB);

        Assert.AreEqual(epA, relay.EndpointA);
        Assert.AreEqual(epB, relay.EndpointB);
    }

    private static int FindFreeEvenPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Close();
        return port % 2 == 0 ? port : port + 1;
    }
}

// ──────────────────────────────────────────────────────────
//  RTP Relay Manager tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class RtpRelayManagerTests
{
    [TestMethod]
    public void CreateRelay_AllocatesEvenOddPorts()
    {
        using var manager = new RtpRelayManager(IPAddress.Loopback);

        var pair = manager.CreateRelay(100, "audio");

        Assert.AreEqual(100, pair.ChannelNumber);
        Assert.IsNotNull(pair.RtpRelay);
        Assert.IsNotNull(pair.RtcpRelay);
        Assert.AreEqual(0, pair.LocalRtpPort % 2);     // Even
        Assert.AreEqual(1, pair.LocalRtcpPort % 2);     // Odd
        Assert.AreEqual(pair.LocalRtpPort + 1, pair.LocalRtcpPort);

        pair.RtpRelay.Dispose();
        pair.RtcpRelay.Dispose();
    }

    [TestMethod]
    public void CreateRelay_MultipleChannels_DifferentPorts()
    {
        using var manager = new RtpRelayManager(IPAddress.Loopback);

        var pair1 = manager.CreateRelay(1, "audio");
        var pair2 = manager.CreateRelay(2, "video");

        Assert.AreNotEqual(pair1.LocalRtpPort, pair2.LocalRtpPort);
        Assert.AreNotEqual(pair1.LocalRtcpPort, pair2.LocalRtcpPort);

        pair1.RtpRelay.Dispose();
        pair1.RtcpRelay.Dispose();
        pair2.RtpRelay.Dispose();
        pair2.RtcpRelay.Dispose();
    }

    [TestMethod]
    public void GetLocalEndpoints()
    {
        using var manager = new RtpRelayManager(IPAddress.Loopback);

        var pair = manager.CreateRelay(42, "test");

        var rtpEp = manager.GetLocalRtpEndpoint(42);
        var rtcpEp = manager.GetLocalRtcpEndpoint(42);

        Assert.IsNotNull(rtpEp);
        Assert.IsNotNull(rtcpEp);
        Assert.AreEqual(pair.LocalRtpPort, rtpEp.Port);
        Assert.AreEqual(pair.LocalRtcpPort, rtcpEp.Port);

        // Non-existent channel
        Assert.IsNull(manager.GetLocalRtpEndpoint(999));
        Assert.IsNull(manager.GetLocalRtcpEndpoint(999));

        pair.RtpRelay.Dispose();
        pair.RtcpRelay.Dispose();
    }

    [TestMethod]
    public void GetStatistics_InitialZero()
    {
        using var manager = new RtpRelayManager(IPAddress.Loopback);

        var stats = manager.GetStatistics();
        Assert.AreEqual(0, stats.ActiveChannels);
        Assert.AreEqual(0, stats.TotalRtpPackets);
        Assert.AreEqual(0, stats.TotalRtpBytes);
        Assert.AreEqual(0, stats.TotalRtcpPackets);
        Assert.AreEqual(0, stats.TotalRtcpBytes);
    }

    [TestMethod]
    public void GetStatistics_WithActiveRelays()
    {
        using var manager = new RtpRelayManager(IPAddress.Loopback);

        var pair1 = manager.CreateRelay(1, "audio");
        var pair2 = manager.CreateRelay(2, "video");

        var stats = manager.GetStatistics();
        Assert.AreEqual(2, stats.ActiveChannels);

        pair1.RtpRelay.Dispose();
        pair1.RtcpRelay.Dispose();
        pair2.RtpRelay.Dispose();
        pair2.RtcpRelay.Dispose();
    }

    [TestMethod]
    public async Task StopRelayAsync_RemovesRelay()
    {
        using var manager = new RtpRelayManager(IPAddress.Loopback);

        manager.CreateRelay(10, "test");
        Assert.AreEqual(1, manager.GetStatistics().ActiveChannels);

        await manager.StopRelayAsync(10);
        Assert.AreEqual(0, manager.GetStatistics().ActiveChannels);
        Assert.IsNull(manager.GetLocalRtpEndpoint(10));
    }

    [TestMethod]
    public async Task StopAllAsync_RemovesAllRelays()
    {
        using var manager = new RtpRelayManager(IPAddress.Loopback);

        manager.CreateRelay(1, "a");
        manager.CreateRelay(2, "b");
        manager.CreateRelay(3, "c");

        Assert.AreEqual(3, manager.GetStatistics().ActiveChannels);

        await manager.StopAllAsync();
        Assert.AreEqual(0, manager.GetStatistics().ActiveChannels);
    }

    [TestMethod]
    public void AllocateEvenPort_ReturnsEvenNumber()
    {
        var port = RtpRelayManager.AllocateEvenPort();
        Assert.AreEqual(0, port % 2, $"Port {port} should be even");
        Assert.IsTrue(port > 0);
    }

    [TestMethod]
    public async Task FullRelay_CreateStartStop()
    {
        using var manager = new RtpRelayManager(IPAddress.Loopback);

        var pair = manager.CreateRelay(100, "audio");

        // Create test endpoints
        using var clientA = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var clientB = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var portA = ((IPEndPoint)clientA.Client.LocalEndPoint!).Port;
        var portB = ((IPEndPoint)clientB.Client.LocalEndPoint!).Port;

        manager.StartRelay(100,
            new IPEndPoint(IPAddress.Loopback, portA),
            new IPEndPoint(IPAddress.Loopback, portA + 1),
            new IPEndPoint(IPAddress.Loopback, portB),
            new IPEndPoint(IPAddress.Loopback, portB + 1));

        // Send a packet A → relay → B
        var testData = RtpHeader.Build(RtpConstants.PT_G723, 1, 0, 0x5678, new byte[20]);
        await clientA.SendAsync(testData, testData.Length,
            new IPEndPoint(IPAddress.Loopback, pair.LocalRtpPort));

        // B receives
        var receiveTask = clientB.ReceiveAsync();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(2000));
        Assert.AreEqual(receiveTask, completed);

        var received = await receiveTask;
        CollectionAssert.AreEqual(testData, received.Buffer);

        // Stop
        await manager.StopRelayAsync(100);
        Assert.AreEqual(0, manager.GetStatistics().ActiveChannels);
    }
}
