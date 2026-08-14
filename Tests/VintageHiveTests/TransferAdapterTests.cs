// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Proxy.Msn.FileTransfer;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Oscar.FileTransfer;
using VintageHive.Proxy.Transfer;
using VintageHive.Proxy.Yahoo.FileTransfer;

namespace Transfer;

// -- Yahoo: the HTTP filetransfer store ---------------------------------------

[TestClass]
public class YahooFileTransferStoreTests
{
    [TestInitialize]
    public void Setup()
    {
        RelayTestEnv.Ensure();
        YahooFileTransfers.Clear();
    }

    [TestMethod]
    public void Store_ThenTake_ReturnsThePayloadOnce()
    {
        var data = Encoding.ASCII.GetBytes("kitten pixels");

        var token = YahooFileTransfers.Store("alice", "bob", "kitten.jpg", data);

        var first = YahooFileTransfers.Take(token);

        Assert.IsNotNull(first, "The first fetch must return the payload");
        Assert.AreEqual("bob", first.To);
        Assert.AreEqual("kitten.jpg", first.FileName);
        CollectionAssert.AreEqual(data, first.Data);

        // Single-serve: the URL cannot be replayed, so a second fetch is a miss.
        Assert.IsNull(YahooFileTransfers.Take(token), "A downloaded file must not be re-fetchable");
    }

    [TestMethod]
    public void Take_UnknownToken_ReturnsNull()
    {
        Assert.IsNull(YahooFileTransfers.Take("no-such-token"));
    }

    [TestMethod]
    public void Token_ExpiresAfterTtl_AndCannotBeFetched()
    {
        var saved = YahooFileTransfers.Lifetime;

        try
        {
            YahooFileTransfers.Lifetime = TimeSpan.FromMilliseconds(1);

            var token = YahooFileTransfers.Store("alice", "bob", "f.bin", new byte[] { 1, 2, 3 });

            Thread.Sleep(20);

            Assert.IsNull(YahooFileTransfers.Take(token), "A token past its TTL must not resolve");
        }
        finally
        {
            YahooFileTransfers.Lifetime = saved;
        }
    }
}

// -- MSN: the msmsgsinvite parse + rewrite ------------------------------------

[TestClass]
public class MsnInviteTests
{
    const string FtInvite =
        "MIME-Version: 1.0\r\n" +
        "Content-Type: text/x-msmsgsinvite; charset=UTF-8\r\n" +
        "\r\n" +
        "Application-Name: File Transfer\r\n" +
        "Application-GUID: {5D3E02AB-6190-11d3-BBBB-00C04F795683}\r\n" +
        "Invitation-Command: ACCEPT\r\n" +
        "Invitation-Cookie: 1234\r\n" +
        "IP-Address: 192.168.1.50\r\n" +
        "Port: 6891\r\n" +
        "AuthCookie: 98765\r\n" +
        "Sender-Connect: TRUE\r\n" +
        "\r\n";

    // Same shape, a DIFFERENT application GUID (a voice clip): must NOT be treated as file transfer.
    const string OtherInvite =
        "MIME-Version: 1.0\r\n" +
        "Content-Type: text/x-msmsgsinvite; charset=UTF-8\r\n" +
        "\r\n" +
        "Application-GUID: {01234567-89AB-CDEF-0123-456789ABCDEF}\r\n" +
        "Invitation-Command: INVITE\r\n" +
        "Invitation-Cookie: 42\r\n" +
        "\r\n";

    [TestMethod]
    public void TryParse_PlainMessage_IsNotAnInvite()
    {
        Assert.IsFalse(MsnInvite.TryParse(Encoding.UTF8.GetBytes("hello there"), out _), "A plain IM must not parse as an invite");
    }

    [TestMethod]
    public void TryParse_FileTransfer_IsRecognised()
    {
        Assert.IsTrue(MsnInvite.TryParse(Encoding.UTF8.GetBytes(FtInvite), out var invite));
        Assert.IsTrue(invite.IsFileTransfer, "The file-transfer GUID must be recognised");
        Assert.AreEqual("ACCEPT", invite.Command);
        Assert.AreEqual("1234", invite.Cookie);
        Assert.IsTrue(invite.HasConnectivity);
    }

    [TestMethod]
    public void TryParse_OtherApplication_IsNotFileTransfer()
    {
        Assert.IsTrue(MsnInvite.TryParse(Encoding.UTF8.GetBytes(OtherInvite), out var invite));
        Assert.IsFalse(invite.IsFileTransfer, "A different Application-GUID must not be treated as file transfer");
    }

    [TestMethod]
    public void Rewrite_ChangesOnlyTheConnectivityFields()
    {
        MsnInvite.TryParse(Encoding.UTF8.GetBytes(FtInvite), out var invite);

        invite.SetField("IP-Address", "10.0.0.1");
        invite.SetField("Port", "1810");
        invite.SetField("AuthCookie", "relayticket");

        var rewritten = Encoding.UTF8.GetString(invite.Serialize());

        StringAssert.Contains(rewritten, "IP-Address: 10.0.0.1");
        StringAssert.Contains(rewritten, "Port: 1810");
        StringAssert.Contains(rewritten, "AuthCookie: relayticket");

        // Everything else is preserved.
        StringAssert.Contains(rewritten, "Application-GUID: {5D3E02AB-6190-11d3-BBBB-00C04F795683}");
        StringAssert.Contains(rewritten, "Invitation-Cookie: 1234");
        StringAssert.Contains(rewritten, "Sender-Connect: TRUE");
        Assert.IsFalse(rewritten.Contains("192.168.1.50"), "The original LAN address must be gone");
    }
}

// -- MSN: the MSNFTP relay termination + pump ---------------------------------

[TestClass]
public class MsnFtpRelayTests
{
    [TestInitialize]
    public void Setup()
    {
        RelayTestEnv.Ensure();
        Mind.Db!.ConfigSet(ConfigNames.RelayTransfers, true);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortLow, 43140);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortHigh, 43149);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Mind.Db!.ConfigSet(ConfigNames.RelayTransfers, false);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortLow, 1810);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortHigh, 1819);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Relay_TerminatesBothHandshakes_AndPumpsSenderToReceiver()
    {
        using var listener = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);

        Assert.IsNotNull(listener);

        const string ticket = "msnfitticket";

        var host = MsnFtpRelay.HostAsync(listener, ticket, 5, CancellationToken.None);

        var payload = Encoding.ASCII.GetBytes("hello");

        // The file-sender: connects, VER/USR, then pushes FIL + data (the data-ready side the relay classifies).
        var senderTask = Task.Run(async () =>
        {
            using var sender = Connect(listener.Port);
            var s = sender.GetStream();

            await WriteLine(s, "VER MSNFTP");
            await ExpectLine(s, "VER MSNFTP");
            await WriteLine(s, $"USR sender@hive.com {ticket}");
            await WriteLine(s, "FIL 5");
            await ExpectLine(s, "TFR");
            await s.WriteAsync(payload);
            await s.FlushAsync();
            await Task.Delay(200);
        });

        // The file-receiver: connects, VER/USR, then waits for FIL, sends TFR, reads the data.
        byte[] received = null;

        var receiverTask = Task.Run(async () =>
        {
            using var receiver = Connect(listener.Port);
            var r = receiver.GetStream();

            await WriteLine(r, "VER MSNFTP");
            await ExpectLine(r, "VER MSNFTP");
            await WriteLine(r, $"USR receiver@hive.com {ticket}");
            var fil = await ReadLine(r);
            Assert.IsTrue(fil.StartsWith("FIL"), $"Relay must announce the file to the receiver, got: {fil}");
            await WriteLine(r, "TFR");

            received = new byte[payload.Length];
            var offset = 0;
            while (offset < received.Length)
            {
                var read = await r.ReadAsync(received.AsMemory(offset));
                if (read <= 0) break;
                offset += read;
            }
        });

        await Task.WhenAll(senderTask, receiverTask);

        try { await host; } catch { }

        Assert.IsNotNull(received);
        CollectionAssert.AreEqual(payload, received, "The file bytes must be pumped from sender to receiver");
    }

    static TcpClient Connect(int port)
    {
        var c = new TcpClient();
        c.Connect(IPAddress.Loopback, port);
        return c;
    }

    static async Task WriteLine(Stream s, string line)
    {
        var b = Encoding.ASCII.GetBytes(line + "\r\n");
        await s.WriteAsync(b);
        await s.FlushAsync();
    }

    static async Task<string> ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        using var cts = new CancellationTokenSource(5000);
        while (true)
        {
            var read = await s.ReadAsync(one.AsMemory(0, 1), cts.Token);
            if (read <= 0 || one[0] == (byte)'\n') break;
            if (one[0] != (byte)'\r') sb.Append((char)one[0]);
        }
        return sb.ToString();
    }

    static async Task ExpectLine(Stream s, string expected)
    {
        var line = await ReadLine(s);
        Assert.AreEqual(expected, line);
    }
}

// -- AIM: the rendezvous parse + rewrite --------------------------------------

[TestClass]
public class OscarRendezvousTests
{
    static readonly byte[] SendFileCap =
    {
        0x09, 0x46, 0x13, 0x43, 0x4C, 0x7F, 0x11, 0xD1, 0x82, 0x22, 0x44, 0x45, 0x53, 0x54, 0x00, 0x00
    };

    static readonly byte[] OtherCap =
    {
        0x09, 0x46, 0x13, 0x49, 0x4C, 0x7F, 0x11, 0xD1, 0x82, 0x22, 0x44, 0x45, 0x53, 0x54, 0x00, 0x00
    };

    static byte[] BuildFragment(ushort type, byte[] cookie, byte[] cap, params Tlv[] tlvs)
    {
        using var ms = new MemoryStream();
        var t = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(t, type);
        ms.Write(t);
        ms.Write(cookie);
        ms.Write(cap);
        foreach (var tlv in tlvs)
        {
            ms.Write(tlv.Encode());
        }
        return ms.ToArray();
    }

    [TestMethod]
    public void TryParse_SendFilePropose_IsRecognised()
    {
        var cookie = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frag = BuildFragment(OscarRendezvous.MessageTypePropose, cookie, SendFileCap,
            new Tlv(0x03, new byte[] { 192, 168, 1, 5 }),
            new Tlv(0x05, new byte[] { 0x1A, 0xEB }));

        Assert.IsTrue(OscarRendezvous.TryParse(frag, out var rv));
        Assert.IsTrue(rv.IsSendFile);
        Assert.AreEqual(OscarRendezvous.MessageTypePropose, rv.MessageType);
        CollectionAssert.AreEqual(cookie, rv.Cookie);
    }

    [TestMethod]
    public void TryParse_OtherCapability_IsNotSendFile()
    {
        var frag = BuildFragment(OscarRendezvous.MessageTypePropose, new byte[8], OtherCap);

        Assert.IsTrue(OscarRendezvous.TryParse(frag, out var rv));
        Assert.IsFalse(rv.IsSendFile, "A non-SendFile rendezvous must be left untouched");
    }

    [TestMethod]
    public void PointAtRelay_RewritesConnectivity_SetsProxyFlag_PreservesCookie()
    {
        var cookie = new byte[] { 9, 9, 9, 9, 8, 8, 8, 8 };
        var frag = BuildFragment(OscarRendezvous.MessageTypePropose, cookie, SendFileCap,
            new Tlv(0x03, new byte[] { 192, 168, 1, 5 }),
            new Tlv(0x05, new byte[] { 0x1A, 0xEB }),
            new Tlv(0x16, new byte[] { 0x3F, 0x57, 0xFE, 0xFA }),
            new Tlv(0x17, new byte[] { 0xE5, 0x14 }));

        OscarRendezvous.TryParse(frag, out var rv);

        rv.PointAtRelay(IPAddress.Parse("10.9.8.7"), 1815);

        var rewritten = rv.Serialize();

        Assert.IsTrue(OscarRendezvous.TryParse(rewritten, out var reparsed));

        // Cookie and capability survive.
        CollectionAssert.AreEqual(cookie, reparsed.Cookie);
        Assert.IsTrue(reparsed.IsSendFile);

        var tlvs = OscarUtils.DecodeTlvs(rewritten[26..]);

        CollectionAssert.AreEqual(new byte[] { 10, 9, 8, 7 }, tlvs.GetTlv(0x03).Value, "RequesterIP must point at the relay");
        CollectionAssert.AreEqual(new byte[] { 0x07, 0x17 }, tlvs.GetTlv(0x05).Value, "Port must be the relay port (1815)");
        Assert.IsNotNull(tlvs.GetTlv(0x10), "The UseARS proxy flag must be set");

        // XOR-guarded copies rewritten in lockstep.
        var expectedIpXor = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(expectedIpXor, BinaryPrimitives.ReadUInt32BigEndian(new byte[] { 10, 9, 8, 7 }) ^ 0xFFFFFFFF);
        CollectionAssert.AreEqual(expectedIpXor, tlvs.GetTlv(0x16).Value, "IPXOR must track the rewritten IP");

        var expectedPortXor = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(expectedPortXor, (ushort)(1815 ^ 0xFFFF));
        CollectionAssert.AreEqual(expectedPortXor, tlvs.GetTlv(0x17).Value, "PortXOR must track the rewritten port");
    }
}

// -- AIM: the rendezvous-proxy session ----------------------------------------

[TestClass]
public class OscarArsProxyTests
{
    const ushort PeerProxyVersion = 0x044a;
    const ushort ProxyTypeCreate = 0x0002;
    const ushort ProxyTypeCreated = 0x0003;
    const ushort ProxyTypeJoin = 0x0004;
    const ushort ProxyTypeReady = 0x0005;

    [TestInitialize]
    public void Setup()
    {
        RelayTestEnv.Ensure();
        Mind.Db!.ConfigSet(ConfigNames.RelayTransfers, true);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortLow, 43160);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortHigh, 43169);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Mind.Db!.ConfigSet(ConfigNames.RelayTransfers, false);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortLow, 1810);
        Mind.Db!.ConfigSet(ConfigNames.RelayPortHigh, 1819);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Proxy_MatchesByCookie_AnswersHandshake_ThenSplices()
    {
        using var listener = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);

        Assert.IsNotNull(listener);

        var cookie = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };

        var host = OscarFileTransferRelay.HostAsync(listener, cookie, (ushort)listener.Port, IPAddress.Loopback, CancellationToken.None);

        byte[] received = null;
        var payload = Encoding.ASCII.GetBytes("oft-bytes");

        var initiatorTask = Task.Run(async () =>
        {
            using var initiator = Connect(listener.Port);
            var s = initiator.GetStream();

            await WriteFrame(s, ProxyTypeCreate, CreatePayload("initiator", cookie));
            var reply = await ReadFrame(s);
            Assert.AreEqual(ProxyTypeCreated, reply.Type, "A CREATE must be answered with CREATED");
            var ready = await ReadFrame(s);
            Assert.AreEqual(ProxyTypeReady, ready.Type);

            await s.WriteAsync(payload);
            await s.FlushAsync();
            await Task.Delay(200);
        });

        var joinerTask = Task.Run(async () =>
        {
            using var joiner = Connect(listener.Port);
            var s = joiner.GetStream();

            await WriteFrame(s, ProxyTypeJoin, JoinPayload("joiner", cookie));
            var ready = await ReadFrame(s);
            Assert.AreEqual(ProxyTypeReady, ready.Type);

            received = new byte[payload.Length];
            var offset = 0;
            while (offset < received.Length)
            {
                var read = await s.ReadAsync(received.AsMemory(offset));
                if (read <= 0) break;
                offset += read;
            }
        });

        await Task.WhenAll(initiatorTask, joinerTask);

        try { await host; } catch { }

        Assert.IsNotNull(received);
        CollectionAssert.AreEqual(payload, received, "OFT bytes must be spliced between the two proxy legs");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Proxy_RefusesAWrongCookie()
    {
        using var listener = TransferRelay.Instance.TryAllocateListener(IPAddress.Loopback);

        var cookie = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 };
        var host = OscarFileTransferRelay.HostAsync(listener, cookie, (ushort)listener.Port, IPAddress.Loopback, CancellationToken.None);

        using (var intruder = Connect(listener.Port))
        {
            var s = intruder.GetStream();
            await WriteFrame(s, ProxyTypeCreate, CreatePayload("intruder", new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }));

            // The proxy drops a wrong-cookie frame; the connection closes with no CREATED.
            var eof = await s.ReadAsync(new byte[1]);
            Assert.AreEqual(0, eof, "A wrong-cookie proxy frame must be refused");
        }

        // Tear the (still-waiting) host down by disposing the listener via the test's using scope end; give it a beat.
        listener.Dispose();
        try { await host; } catch { }
    }

    static byte[] CreatePayload(string screenName, byte[] cookie)
    {
        using var ms = new MemoryStream();
        var sn = Encoding.ASCII.GetBytes(screenName);
        ms.WriteByte((byte)sn.Length);
        ms.Write(sn);
        ms.Write(cookie);
        // capability block (2 type, 2 len=16, 16 bytes) - shape only; the proxy keys on the cookie.
        ms.Write(new byte[] { 0x00, 0x01, 0x00, 0x10 });
        ms.Write(new byte[16]);
        return ms.ToArray();
    }

    static byte[] JoinPayload(string screenName, byte[] cookie)
    {
        using var ms = new MemoryStream();
        var sn = Encoding.ASCII.GetBytes(screenName);
        ms.WriteByte((byte)sn.Length);
        ms.Write(sn);
        ms.Write(new byte[] { 0x00, 0x00 }); // PIN
        ms.Write(cookie);
        ms.Write(new byte[] { 0x00, 0x01, 0x00, 0x10 });
        ms.Write(new byte[16]);
        return ms.ToArray();
    }

    static TcpClient Connect(int port)
    {
        var c = new TcpClient();
        c.Connect(IPAddress.Loopback, port);
        return c;
    }

    static async Task WriteFrame(Stream s, ushort type, byte[] payload)
    {
        var body = new byte[10 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(0, 2), PeerProxyVersion);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(2, 2), type);
        payload.CopyTo(body, 10);

        var frame = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), (ushort)body.Length);
        body.CopyTo(frame, 2);

        await s.WriteAsync(frame);
        await s.FlushAsync();
    }

    static async Task<(ushort Type, byte[] Payload)> ReadFrame(Stream s)
    {
        using var cts = new CancellationTokenSource(5000);
        var len = await ReadExact(s, 2, cts.Token);
        var length = BinaryPrimitives.ReadUInt16BigEndian(len);
        var rest = await ReadExact(s, length, cts.Token);
        var type = BinaryPrimitives.ReadUInt16BigEndian(rest.AsSpan(2, 2));
        return (type, rest[10..]);
    }

    static async Task<byte[]> ReadExact(Stream s, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await s.ReadAsync(buf.AsMemory(offset, count - offset), ct);
            if (read <= 0) throw new IOException("closed");
            offset += read;
        }
        return buf;
    }
}
