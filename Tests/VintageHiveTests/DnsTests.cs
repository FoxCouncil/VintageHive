// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using VintageHive.Proxy.Dns;

namespace DnsProxyTests;

#region DNS Packet Builder Helper

/// <summary>
/// Builds raw DNS query packets for testing.
/// </summary>
internal static class DnsPacketBuilder
{
    /// <summary>
    /// Builds a standard DNS query packet.
    /// </summary>
    public static byte[] BuildQuery(ushort transactionId, string domainName, ushort qtype = 1, ushort qclass = 1)
    {
        var labels = EncodeDomainName(domainName);

        // Header (12 bytes) + encoded domain name + QTYPE (2) + QCLASS (2)
        var packet = new byte[12 + labels.Length + 4];

        // Transaction ID
        packet[0] = (byte)(transactionId >> 8);
        packet[1] = (byte)(transactionId & 0xFF);

        // Flags: standard query, recursion desired (0x0100)
        packet[2] = 0x01;
        packet[3] = 0x00;

        // QDCOUNT = 1
        packet[4] = 0x00;
        packet[5] = 0x01;

        // ANCOUNT, NSCOUNT, ARCOUNT = 0 (already zeroed)

        // Question section: encoded domain name
        Buffer.BlockCopy(labels, 0, packet, 12, labels.Length);

        // QTYPE
        var qtypeOffset = 12 + labels.Length;

        packet[qtypeOffset] = (byte)(qtype >> 8);
        packet[qtypeOffset + 1] = (byte)(qtype & 0xFF);

        // QCLASS
        packet[qtypeOffset + 2] = (byte)(qclass >> 8);
        packet[qtypeOffset + 3] = (byte)(qclass & 0xFF);

        return packet;
    }

    /// <summary>
    /// Encodes a domain name into DNS label format (e.g., "www.yahoo.com" → [3]www[5]yahoo[3]com[0]).
    /// </summary>
    public static byte[] EncodeDomainName(string name)
    {
        var parts = name.Split('.');
        var result = new List<byte>();

        foreach (var part in parts)
        {
            result.Add((byte)part.Length);
            result.AddRange(System.Text.Encoding.ASCII.GetBytes(part));
        }

        result.Add(0x00); // null terminator

        return result.ToArray();
    }
}

#endregion

#region DNS Response Parser Helper

/// <summary>
/// Parses raw DNS response packets for test assertions.
/// </summary>
internal class DnsResponse
{
    public ushort TransactionId { get; init; }
    public ushort Flags { get; init; }
    public ushort QuestionCount { get; init; }
    public ushort AnswerCount { get; init; }
    public ushort AuthorityCount { get; init; }
    public ushort AdditionalCount { get; init; }
    public IPAddress AnswerAddress { get; init; }
    public ushort AnswerType { get; init; }
    public ushort AnswerClass { get; init; }
    public uint AnswerTtl { get; init; }

    public bool IsResponse => (Flags & 0x8000) != 0;
    public bool IsAuthoritative => (Flags & 0x0400) != 0;
    public bool IsRecursionDesired => (Flags & 0x0100) != 0;
    public bool IsRecursionAvailable => (Flags & 0x0080) != 0;
    public int ResponseCode => Flags & 0x000F;

    public static DnsResponse Parse(byte[] data)
    {
        if (data.Length < 12)
        {
            return null;
        }

        var txId = (ushort)((data[0] << 8) | data[1]);
        var flags = (ushort)((data[2] << 8) | data[3]);
        var qdCount = (ushort)((data[4] << 8) | data[5]);
        var anCount = (ushort)((data[6] << 8) | data[7]);
        var nsCount = (ushort)((data[8] << 8) | data[9]);
        var arCount = (ushort)((data[10] << 8) | data[11]);

        IPAddress answerAddr = null;
        ushort answerType = 0;
        ushort answerClass = 0;
        uint answerTtl = 0;

        if (anCount > 0)
        {
            // Skip question section to find answer
            var offset = 12;

            // Skip domain name labels
            while (offset < data.Length && data[offset] != 0)
            {
                if ((data[offset] & 0xC0) == 0xC0)
                {
                    offset += 2;

                    break;
                }

                offset += data[offset] + 1;
            }

            if (offset < data.Length && data[offset] == 0)
            {
                offset++; // skip null terminator
            }

            offset += 4; // skip QTYPE + QCLASS

            // Now at answer RR
            if (offset + 12 <= data.Length)
            {
                // Skip name (could be pointer)
                if ((data[offset] & 0xC0) == 0xC0)
                {
                    offset += 2;
                }
                else
                {
                    while (offset < data.Length && data[offset] != 0)
                    {
                        offset += data[offset] + 1;
                    }

                    offset++;
                }

                if (offset + 10 <= data.Length)
                {
                    answerType = (ushort)((data[offset] << 8) | data[offset + 1]);
                    answerClass = (ushort)((data[offset + 2] << 8) | data[offset + 3]);
                    answerTtl = (uint)((data[offset + 4] << 24) | (data[offset + 5] << 16) | (data[offset + 6] << 8) | data[offset + 7]);
                    var rdLength = (ushort)((data[offset + 8] << 8) | data[offset + 9]);

                    offset += 10;

                    if (answerType == 1 && rdLength == 4 && offset + 4 <= data.Length)
                    {
                        answerAddr = new IPAddress(new ReadOnlySpan<byte>(data, offset, 4));
                    }
                }
            }
        }

        return new DnsResponse
        {
            TransactionId = txId,
            Flags = flags,
            QuestionCount = qdCount,
            AnswerCount = anCount,
            AuthorityCount = nsCount,
            AdditionalCount = arCount,
            AnswerAddress = answerAddr,
            AnswerType = answerType,
            AnswerClass = answerClass,
            AnswerTtl = answerTtl,
        };
    }
}

#endregion

#region Shared Test Helpers

internal static class DnsTestHelper
{
    /// <summary>
    /// Waits for the proxy to be ready by sending probe queries until one gets a response.
    /// </summary>
    public static async Task WaitForProxyAsync(IPEndPoint server, int timeoutMs = 3000)
    {
        using var probe = new UdpClient();

        var probeQuery = DnsPacketBuilder.BuildQuery(0x0000, "probe.test");

        using var cts = new CancellationTokenSource(timeoutMs);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                await probe.SendAsync(probeQuery, probeQuery.Length, server);

                using var innerCts = new CancellationTokenSource(200);

                await probe.ReceiveAsync(innerCts.Token);

                return; // Got a response, proxy is ready
            }
            catch (OperationCanceledException)
            {
                // Timeout on this attempt, try again
            }
            catch (SocketException)
            {
                // Port not ready yet, brief pause and retry
                await Task.Delay(50, cts.Token);
            }
        }
    }

    public static async Task<DnsResponse> QueryAsync(UdpClient client, IPEndPoint server, byte[] query, int timeoutMs = 3000)
    {
        await client.SendAsync(query, query.Length, server);

        using var cts = new CancellationTokenSource(timeoutMs);

        var result = await client.ReceiveAsync(cts.Token);

        return DnsResponse.Parse(result.Buffer);
    }
}

#endregion

#region Integration Tests

[TestClass]
public class DnsProxyIntegrationTests
{
    private static readonly IPAddress ServerIp = IPAddress.Parse("10.0.0.1");
    private static int _nextPort = 25353;

    private static int GetFreePort()
    {
        return Interlocked.Increment(ref _nextPort);
    }

    private static Task WaitForProxyAsync(IPEndPoint server) => DnsTestHelper.WaitForProxyAsync(server);

    private static Task<DnsResponse> QueryAsync(UdpClient client, IPEndPoint server, byte[] query, int timeoutMs = 3000)
        => DnsTestHelper.QueryAsync(client, server, query, timeoutMs);

    [TestMethod]
    [Timeout(10000)]
    public async Task ARecordQuery_ReturnsConfiguredIp()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();
            var query = DnsPacketBuilder.BuildQuery(0x1234, "www.yahoo.com", qtype: 1, qclass: 1);

            var response = await QueryAsync(client, server, query);

            Assert.IsNotNull(response, "Should receive a response");
            Assert.IsTrue(response.IsResponse, "QR bit should be set");
            Assert.AreEqual(0x1234, (int)response.TransactionId, "Transaction ID should be echoed");
            Assert.AreEqual(1, (int)response.QuestionCount, "Should echo 1 question");
            Assert.AreEqual(1, (int)response.AnswerCount, "Should have 1 answer");
            Assert.AreEqual(ServerIp, response.AnswerAddress, "Answer should be the configured IP");
            Assert.AreEqual(1, (int)response.AnswerType, "Answer type should be A (1)");
            Assert.AreEqual(1, (int)response.AnswerClass, "Answer class should be IN (1)");
            Assert.AreEqual(300u, response.AnswerTtl, "TTL should be 300 seconds");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ARecordQuery_ResponseFlagsCorrect()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();
            var query = DnsPacketBuilder.BuildQuery(0xABCD, "example.com");

            var response = await QueryAsync(client, server, query);

            Assert.IsTrue(response.IsResponse, "QR bit should be set (response)");
            Assert.IsTrue(response.IsAuthoritative, "AA bit should be set");
            Assert.IsTrue(response.IsRecursionDesired, "RD bit should be set");
            Assert.IsTrue(response.IsRecursionAvailable, "RA bit should be set");
            Assert.AreEqual(0, response.ResponseCode, "RCODE should be NOERROR (0)");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task AAAAQuery_ReturnsEmptyResponse()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            // AAAA = qtype 28
            var query = DnsPacketBuilder.BuildQuery(0x5678, "www.google.com", qtype: 28, qclass: 1);

            var response = await QueryAsync(client, server, query);

            Assert.IsNotNull(response, "Should receive a response");
            Assert.IsTrue(response.IsResponse, "QR bit should be set");
            Assert.AreEqual(0x5678, (int)response.TransactionId, "Transaction ID should be echoed");
            Assert.AreEqual(1, (int)response.QuestionCount, "Should echo 1 question");
            Assert.AreEqual(0, (int)response.AnswerCount, "Should have 0 answers for AAAA");
            Assert.AreEqual(0, response.ResponseCode, "RCODE should be NOERROR (0)");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task MXQuery_ReturnsEmptyResponse()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            // MX = qtype 15
            var query = DnsPacketBuilder.BuildQuery(0x9999, "hive.com", qtype: 15, qclass: 1);

            var response = await QueryAsync(client, server, query);

            Assert.IsNotNull(response, "Should receive a response");
            Assert.AreEqual(0, (int)response.AnswerCount, "Should have 0 answers for MX");
            Assert.AreEqual(0, response.ResponseCode, "RCODE should be NOERROR");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task TransactionId_PreservedAcrossQueries()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            // Send three queries with different transaction IDs
            ushort[] txIds = { 0x0001, 0x7FFF, 0xFFFF };

            foreach (var txId in txIds)
            {
                var query = DnsPacketBuilder.BuildQuery(txId, "test.example.com");
                var response = await QueryAsync(client, server, query);

                Assert.AreEqual(txId, response.TransactionId, $"Transaction ID 0x{txId:X4} should be preserved");
            }
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task MultipleSequentialQueries_AllSucceed()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            string[] domains = { "www.yahoo.com", "www.altavista.com", "www.geocities.com", "ftp.cdrom.com", "irc.efnet.org" };

            foreach (var domain in domains)
            {
                var query = DnsPacketBuilder.BuildQuery(0x1111, domain);
                var response = await QueryAsync(client, server, query);

                Assert.IsNotNull(response, $"Should receive response for {domain}");
                Assert.AreEqual(1, (int)response.AnswerCount, $"Should have 1 answer for {domain}");
                Assert.AreEqual(ServerIp, response.AnswerAddress, $"Answer should be server IP for {domain}");
            }
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task SingleLabelDomain_ReturnsARecord()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();
            var query = DnsPacketBuilder.BuildQuery(0x2222, "localhost");

            var response = await QueryAsync(client, server, query);

            Assert.IsNotNull(response, "Should handle single-label domain");
            Assert.AreEqual(1, (int)response.AnswerCount, "Should have 1 answer");
            Assert.AreEqual(ServerIp, response.AnswerAddress);
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task DeepSubdomain_ReturnsARecord()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();
            var query = DnsPacketBuilder.BuildQuery(0x3333, "a.b.c.d.e.example.com");

            var response = await QueryAsync(client, server, query);

            Assert.IsNotNull(response, "Should handle deeply nested subdomains");
            Assert.AreEqual(1, (int)response.AnswerCount, "Should have 1 answer");
            Assert.AreEqual(ServerIp, response.AnswerAddress);
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task EmptyResponse_DoesNotContainAnswerSection()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            // TXT = qtype 16
            var query = DnsPacketBuilder.BuildQuery(0x4444, "example.com", qtype: 16);

            await client.SendAsync(query, query.Length, server);

            using var cts = new CancellationTokenSource(3000);

            var result = await client.ReceiveAsync(cts.Token);
            var raw = result.Buffer;

            // Empty response should be exactly the question section size (no answer RR appended)
            var expectedLen = query.Length; // header + question only

            Assert.AreEqual(expectedLen, raw.Length, "Empty response should have no answer section bytes");

            // Verify ANCOUNT is 0
            var anCount = (raw[6] << 8) | raw[7];

            Assert.AreEqual(0, anCount, "ANCOUNT should be 0");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task AResponse_HasCorrectLength()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();
            var query = DnsPacketBuilder.BuildQuery(0x5555, "www.test.com");

            await client.SendAsync(query, query.Length, server);

            using var cts = new CancellationTokenSource(3000);

            var result = await client.ReceiveAsync(cts.Token);
            var raw = result.Buffer;

            // Response = question section + 16-byte answer RR
            var expectedLen = query.Length + 16;

            Assert.AreEqual(expectedLen, raw.Length, "A response should be question + 16 bytes for answer RR");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task EmptyResponse_FlagsAreCorrect()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            // PTR = qtype 12
            var query = DnsPacketBuilder.BuildQuery(0x6666, "1.0.0.10.in-addr.arpa", qtype: 12);

            var response = await QueryAsync(client, server, query);

            Assert.IsTrue(response.IsResponse, "QR bit should be set");
            Assert.IsFalse(response.IsAuthoritative, "AA bit should NOT be set for empty responses");
            Assert.IsTrue(response.IsRecursionDesired, "RD bit should be set");
            Assert.IsTrue(response.IsRecursionAvailable, "RA bit should be set");
            Assert.AreEqual(0, response.ResponseCode, "RCODE should be NOERROR");
            Assert.AreEqual(0, (int)response.AnswerCount);
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task AnswerRR_UsesCompressionPointer()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();
            var query = DnsPacketBuilder.BuildQuery(0x7777, "www.example.com");

            await client.SendAsync(query, query.Length, server);

            using var cts = new CancellationTokenSource(3000);

            var result = await client.ReceiveAsync(cts.Token);
            var raw = result.Buffer;

            // The answer RR name should be a compression pointer to offset 12 (0xC0 0x0C)
            var answerStart = query.Length; // answer begins right after question section

            Assert.AreEqual(0xC0, raw[answerStart], "Answer name should start with compression pointer 0xC0");
            Assert.AreEqual(0x0C, raw[answerStart + 1], "Compression pointer should reference offset 12 (0x0C)");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task AnswerRR_ContainsCorrectIpBytes()
    {
        var port = GetFreePort();
        var responseIp = IPAddress.Parse("192.168.1.42");
        var proxy = new DnsProxy(IPAddress.Loopback, port, responseIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();
            var query = DnsPacketBuilder.BuildQuery(0x8888, "ftp.cdrom.com");

            await client.SendAsync(query, query.Length, server);

            using var cts = new CancellationTokenSource(3000);

            var result = await client.ReceiveAsync(cts.Token);
            var raw = result.Buffer;

            // Last 4 bytes should be the IPv4 address
            var ipBytes = new byte[4];

            Buffer.BlockCopy(raw, raw.Length - 4, ipBytes, 0, 4);

            var responseAddr = new IPAddress(ipBytes);

            Assert.AreEqual(responseIp, responseAddr, "RDATA should contain the configured response IP");
        }
        finally
        {
            proxy.Stop();
        }
    }
}

#endregion

#region Packet Builder Tests

[TestClass]
public class DnsPacketBuilderTests
{
    [TestMethod]
    public void EncodeDomainName_SimpleThreeLabels()
    {
        var encoded = DnsPacketBuilder.EncodeDomainName("www.yahoo.com");

        // Expected: [3]www[5]yahoo[3]com[0]
        var expected = new byte[] { 3, (byte)'w', (byte)'w', (byte)'w', 5, (byte)'y', (byte)'a', (byte)'h', (byte)'o', (byte)'o', 3, (byte)'c', (byte)'o', (byte)'m', 0 };

        CollectionAssert.AreEqual(expected, encoded);
    }

    [TestMethod]
    public void EncodeDomainName_SingleLabel()
    {
        var encoded = DnsPacketBuilder.EncodeDomainName("localhost");

        // Expected: [9]localhost[0]
        Assert.AreEqual(9, encoded[0], "Label length should be 9");
        Assert.AreEqual(0, encoded[^1], "Should end with null terminator");
        Assert.AreEqual(11, encoded.Length, "Total length: 1 (len) + 9 (label) + 1 (null)");
    }

    [TestMethod]
    public void BuildQuery_HasCorrectHeader()
    {
        var query = DnsPacketBuilder.BuildQuery(0xBEEF, "test.com");

        // Transaction ID
        Assert.AreEqual(0xBE, query[0]);
        Assert.AreEqual(0xEF, query[1]);

        // Flags: RD=1 → 0x0100
        Assert.AreEqual(0x01, query[2]);
        Assert.AreEqual(0x00, query[3]);

        // QDCOUNT = 1
        Assert.AreEqual(0x00, query[4]);
        Assert.AreEqual(0x01, query[5]);

        // ANCOUNT, NSCOUNT, ARCOUNT = 0
        for (var i = 6; i < 12; i++)
        {
            Assert.AreEqual(0x00, query[i], $"Header byte {i} should be 0");
        }
    }

    [TestMethod]
    public void BuildQuery_HasCorrectLength()
    {
        var query = DnsPacketBuilder.BuildQuery(0x0001, "a.b.c");

        // Header (12) + [1]a[1]b[1]c[0] (7) + QTYPE (2) + QCLASS (2) = 23
        Assert.AreEqual(23, query.Length);
    }

    [TestMethod]
    public void BuildQuery_EncodesQtypeAndQclass()
    {
        var query = DnsPacketBuilder.BuildQuery(0x0001, "x.y", qtype: 28, qclass: 3);

        // Domain "x.y" encoded: [1]x[1]y[0] = 5 bytes. Start at offset 12, end at 17.
        // QTYPE at 17, QCLASS at 19
        var qtypeOffset = query.Length - 4;

        Assert.AreEqual(0x00, query[qtypeOffset], "QTYPE high byte");
        Assert.AreEqual(28, query[qtypeOffset + 1], "QTYPE low byte = 28 (AAAA)");
        Assert.AreEqual(0x00, query[qtypeOffset + 2], "QCLASS high byte");
        Assert.AreEqual(3, query[qtypeOffset + 3], "QCLASS low byte = 3 (CH)");
    }
}

#endregion

#region Malformed Input Tests

[TestClass]
public class DnsMalformedInputTests
{
    private static readonly IPAddress ServerIp = IPAddress.Parse("10.0.0.1");
    private static int _nextPort = 26353;

    private static int GetFreePort()
    {
        return Interlocked.Increment(ref _nextPort);
    }

    private static Task WaitForProxyAsync(IPEndPoint server) => DnsTestHelper.WaitForProxyAsync(server);

    [TestMethod]
    [Timeout(10000)]
    public async Task TooShortPacket_IsIgnored()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            // Send a packet shorter than DNS_HEADER_SIZE (12 bytes)
            var shortPacket = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00 };

            await client.SendAsync(shortPacket, shortPacket.Length, server);

            // Now send a valid query to prove the server is still alive
            var validQuery = DnsPacketBuilder.BuildQuery(0xAAAA, "alive.test.com");

            await client.SendAsync(validQuery, validQuery.Length, server);

            using var cts = new CancellationTokenSource(3000);

            var result = await client.ReceiveAsync(cts.Token);
            var response = DnsResponse.Parse(result.Buffer);

            Assert.AreEqual(0xAAAA, (int)response.TransactionId, "Server should still respond after ignoring short packet");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ZeroQuestionCount_IsIgnored()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            // Build a 12-byte header with QDCOUNT = 0
            var noQuestion = new byte[12];

            noQuestion[0] = 0xBB;
            noQuestion[1] = 0xBB;
            // Flags, QDCOUNT, etc. all zero

            await client.SendAsync(noQuestion, noQuestion.Length, server);

            // Send a valid query to prove the server is still alive
            var validQuery = DnsPacketBuilder.BuildQuery(0xCCCC, "alive2.test.com");

            await client.SendAsync(validQuery, validQuery.Length, server);

            using var cts = new CancellationTokenSource(3000);

            var result = await client.ReceiveAsync(cts.Token);
            var response = DnsResponse.Parse(result.Buffer);

            Assert.AreEqual(0xCCCC, (int)response.TransactionId, "Server should still respond after ignoring zero-question packet");
        }
        finally
        {
            proxy.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task TruncatedDomainName_IsIgnored()
    {
        var port = GetFreePort();
        var proxy = new DnsProxy(IPAddress.Loopback, port, ServerIp);

        proxy.Start();

        var server = new IPEndPoint(IPAddress.Loopback, port);

        await WaitForProxyAsync(server);

        try
        {
            using var client = new UdpClient();

            // Header (12 bytes) with QDCOUNT=1, then a truncated domain name
            // Label length says 10 but only 3 bytes follow
            var truncated = new byte[16];

            truncated[4] = 0x00;
            truncated[5] = 0x01; // QDCOUNT = 1
            truncated[12] = 10;  // label length = 10, but only 3 bytes remain
            truncated[13] = (byte)'a';
            truncated[14] = (byte)'b';
            truncated[15] = (byte)'c';

            await client.SendAsync(truncated, truncated.Length, server);

            // Verify server is still alive
            var validQuery = DnsPacketBuilder.BuildQuery(0xDDDD, "alive3.test.com");

            await client.SendAsync(validQuery, validQuery.Length, server);

            using var cts = new CancellationTokenSource(3000);

            var result = await client.ReceiveAsync(cts.Token);
            var response = DnsResponse.Parse(result.Buffer);

            Assert.AreEqual(0xDDDD, (int)response.TransactionId, "Server should still respond after ignoring truncated packet");
        }
        finally
        {
            proxy.Stop();
        }
    }
}

#endregion
