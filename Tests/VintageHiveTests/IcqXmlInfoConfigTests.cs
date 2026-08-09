// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// The ICQ CLI_REQ_XML_INFO service-locator answers are config-overridable for whitelabel deployments.
// The compatibility contract these tests pin down:
//
//   - Unset config: BannersIP emits the built-in api.hive.com template byte for byte, and DataFilesIP /
//     ChannelsIP stay UNANSWERED (they fall to the unknown-key branch) - no new default reply appears.
//   - Set config: each key emits the configured value in the same 0x0A + <value>...</value> frame.
//   - %d/%s in a configured value are the CLIENT's substitution tokens and must survive verbatim.

using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Oscar.Services;

namespace IcqXmlInfoConfig;

[TestClass]
public class IcqXmlInfoConfigTests
{
    #region Harness

    private sealed class Wire : IDisposable
    {
        public OscarSession Session { get; }

        public NetworkStream ClientStream { get; }

        private readonly TcpClient _client;

        private readonly TcpClient _server;

        public Wire()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            _client = new TcpClient();
            _client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);

            _server = listener.AcceptTcpClient();

            listener.Stop();

            ClientStream = _client.GetStream();
            ClientStream.ReadTimeout = 2000;

            Session = new OscarSession(new ListenerSocket { IsSecure = false, RawSocket = _server.Client, Stream = _server.GetStream() });
        }

        public void Dispose()
        {
            try { _client.Close(); } catch { }
            try { _server.Close(); } catch { }
        }
    }

    private static byte[] Cat(params byte[][] parts)
    {
        var list = new List<byte>();

        foreach (var p in parts)
        {
            list.AddRange(p);
        }

        return list.ToArray();
    }

    // A well-formed CLI_REQ_XML_INFO meta request for the given key, framed as TLV 0x01 the way
    // IcqUserMetaRequest decodes it: LE chunk size + UIN + request type + sequence + subtype +
    // LE string length (counting the trailing NUL) + key + NUL, inside a network-order TLV header.
    private static Snac XmlInfoRequest(string xmlKey)
    {
        var keyBytes = Encoding.ASCII.GetBytes(xmlKey);

        var body = Cat(
            BitConverter.GetBytes((uint)778899),
            BitConverter.GetBytes(OscarIcqService.CLI_META_INFO_REQ),
            BitConverter.GetBytes((ushort)0x0002),
            BitConverter.GetBytes(OscarIcqService.CLI_REQ_XML_INFO),
            BitConverter.GetBytes((ushort)(keyBytes.Length + 1)),
            keyBytes,
            new byte[] { 0x00 });

        var value = Cat(BitConverter.GetBytes((ushort)body.Length), body);

        var raw = Cat(new byte[] { 0x00, 0x01, (byte)(value.Length >> 8), (byte)(value.Length & 0xFF) }, value);

        var snac = new Snac(OscarIcqService.FAMILY_ID, OscarIcqService.CLI_META_REQ);

        snac.Write(raw);

        return snac;
    }

    // The exact reply payload fragment the service frames for an XML answer: the 0x0A success byte
    // followed by the ICQ string (LE length counting the NUL, ASCII bytes, NUL terminator).
    private static byte[] ExpectedAnswerBytes(string url)
    {
        var text = Encoding.ASCII.GetBytes($"<value>{url}</value>");

        return Cat(new byte[] { 0x0A }, BitConverter.GetBytes((ushort)(text.Length + 1)), text, new byte[] { 0x00 });
    }

    private static byte[] ReadExactly(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);

            if (read <= 0)
            {
                return null;
            }

            offset += read;
        }

        return buffer;
    }

    // Reads one FLAP off the client end and returns its payload (the encoded SNAC).
    private static byte[] ReadFlapPayload(Wire wire)
    {
        var header = ReadExactly(wire.ClientStream, 6);

        Assert.IsNotNull(header, "expected a FLAP header");
        Assert.AreEqual((byte)0x2A, header[0], "FLAP frames start with 0x2A");

        var length = (ushort)((header[4] << 8) | header[5]);

        return length == 0 ? Array.Empty<byte>() : ReadExactly(wire.ClientStream, length);
    }

    private static int IndexOfSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;

                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    // Every test states all three settings up front and restores them after, so suite ordering can
    // never leak a configured URL into an unset-behavior assertion.
    private static void SetConfig(string banners, string dataFiles, string channels)
    {
        AuditFixes.AuditTestDb.Ensure();

        Mind.Db!.ConfigSet(ConfigNames.IcqBannersUrl, banners);
        Mind.Db.ConfigSet(ConfigNames.IcqDataFilesUrl, dataFiles);
        Mind.Db.ConfigSet(ConfigNames.IcqChannelsUrl, channels);
    }

    private static void ResetConfig()
    {
        SetConfig("", "", "");
    }

    private static async Task<byte[]> RunAndReadReply(string xmlKey)
    {
        using var wire = new Wire();

        var service = new OscarIcqService(new OscarServer(IPAddress.Loopback, 0));

        await service.ProcessSnac(wire.Session, XmlInfoRequest(xmlKey));

        var payload = ReadFlapPayload(wire);

        Assert.IsNotNull(payload);
        Assert.AreEqual(OscarIcqService.FAMILY_ID, (ushort)((payload[0] << 8) | payload[1]), "reply must be family 0x15");
        Assert.AreEqual(OscarIcqService.SRV_META_REPLY, (ushort)((payload[2] << 8) | payload[3]), "reply must be SRV_META_REPLY");

        return payload;
    }

    private static async Task AssertUnanswered(string xmlKey)
    {
        using var wire = new Wire();

        var service = new OscarIcqService(new OscarServer(IPAddress.Loopback, 0));

        await service.ProcessSnac(wire.Session, XmlInfoRequest(xmlKey));

        // ProcessSnac has fully completed, so any reply would already be in flight on loopback; the
        // short grace period only covers kernel buffer delivery.
        await Task.Delay(150);

        Assert.IsFalse(wire.ClientStream.DataAvailable, $"{xmlKey} must stay unanswered when its URL is not configured");
    }

    #endregion

    #region Unset config - today's behavior, byte for byte

    [TestMethod]
    [Timeout(10000)]
    public async Task BannersIp_UnsetConfig_EmitsBuiltInApiTemplate()
    {
        ResetConfig();

        var payload = await RunAndReadReply(OscarIcqService.XML_REQ_BANNERIP);

        // Deliberately a hardcoded literal, not HiveDomains.Api: this pins the exact pre-config bytes.
        var expected = ExpectedAnswerBytes("http://api.hive.com/api/icq2000a/%d/%s.cb");

        Assert.IsTrue(IndexOfSequence(payload, expected) >= 0, "unset BannersIP config must reproduce the built-in api.hive.com answer byte for byte");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task DataFilesIp_UnsetConfig_StaysUnanswered()
    {
        ResetConfig();

        await AssertUnanswered(OscarIcqService.XML_REQ_DATAFILESIP);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ChannelsIp_UnsetConfig_StaysUnanswered()
    {
        ResetConfig();

        await AssertUnanswered(OscarIcqService.XML_REQ_CHANNELSIP);
    }

    #endregion

    #region Set config - each key answers with its configured value

    [TestMethod]
    [Timeout(10000)]
    public async Task BannersIp_Configured_EmitsConfiguredValueVerbatim()
    {
        SetConfig("http://icq.example.org/banners/%d/%s.cb", "", "");

        try
        {
            var payload = await RunAndReadReply(OscarIcqService.XML_REQ_BANNERIP);

            Assert.IsTrue(IndexOfSequence(payload, ExpectedAnswerBytes("http://icq.example.org/banners/%d/%s.cb")) >= 0, "configured BannersIP URL must go out verbatim, %d/%s included");
        }
        finally
        {
            ResetConfig();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task DataFilesIp_Configured_EmitsConfiguredValueVerbatim()
    {
        SetConfig("", "http://icq.example.org/data/%d/%s.dat", "");

        try
        {
            var payload = await RunAndReadReply(OscarIcqService.XML_REQ_DATAFILESIP);

            Assert.IsTrue(IndexOfSequence(payload, ExpectedAnswerBytes("http://icq.example.org/data/%d/%s.dat")) >= 0, "configured DataFilesIP URL must go out verbatim, %d/%s included");
        }
        finally
        {
            ResetConfig();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ChannelsIp_Configured_EmitsConfiguredValueVerbatim()
    {
        SetConfig("", "", "http://icq.example.org/channels.xml");

        try
        {
            var payload = await RunAndReadReply(OscarIcqService.XML_REQ_CHANNELSIP);

            Assert.IsTrue(IndexOfSequence(payload, ExpectedAnswerBytes("http://icq.example.org/channels.xml")) >= 0, "configured ChannelsIP URL must be answered");
        }
        finally
        {
            ResetConfig();
        }
    }

    #endregion
}
