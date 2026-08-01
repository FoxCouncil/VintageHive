// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive;
using VintageHive.Network;
using VintageHive.Proxy.Chat;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Oscar.Services;
using VintageHive.Proxy.Yahoo;
using Yahoo;

namespace Chat;

[TestClass]
public class ChatServiceRegistryTests
{
    [TestCleanup]
    public void Cleanup()
    {
        ChatServiceRegistry.Unregister("RegHelper");
    }

    [TestMethod]
    public async Task TryHandle_UnregisteredName_ReturnsNull()
    {
        Assert.IsNull(await ChatServiceRegistry.TryHandleAsync("NobodyHome", "Yahoo", "alice", "hi"), "Null is the 'not a service' signal; anything else would swallow messages to unknown users.");
    }

    [TestMethod]
    public async Task TryHandle_MatchesCaseInsensitively_AndCarriesTheMessage()
    {
        ChatServiceMessage seen = null;

        ChatServiceRegistry.Register("RegHelper", msg =>
        {
            seen = msg;

            return Task.FromResult<IReadOnlyList<string>>(new[] { "one", "two" });
        });

        var replies = await ChatServiceRegistry.TryHandleAsync("rEgHeLpEr", "IRC", "alice", "hello there");

        Assert.IsNotNull(replies, "A differently-cased recipient must still match the registered name.");
        CollectionAssert.AreEqual(new[] { "one", "two" }, replies.ToList());

        Assert.AreEqual("alice", seen.SenderUsername);
        Assert.AreEqual("IRC", seen.Network);
        Assert.AreEqual("hello there", seen.Text);
    }

    [TestMethod]
    public async Task TryHandle_HandlerReturningNull_MeansHandledAndSilent()
    {
        ChatServiceRegistry.Register("RegHelper", _ => Task.FromResult<IReadOnlyList<string>>(null));

        var replies = await ChatServiceRegistry.TryHandleAsync("RegHelper", "Yahoo", "alice", "hi");

        Assert.IsNotNull(replies, "A null from the handler must not read as 'not a service'.");
        Assert.AreEqual(0, replies.Count);
    }

    [TestMethod]
    public async Task TryHandle_HandlerThrowing_MeansHandledAndSilent()
    {
        ChatServiceRegistry.Register("RegHelper", _ => throw new InvalidOperationException("embedder bug"));

        var replies = await ChatServiceRegistry.TryHandleAsync("RegHelper", "Yahoo", "alice", "hi");

        Assert.IsNotNull(replies, "An embedder fault must not fall through to the unknown-recipient drop, and must never surface to the member's session.");
        Assert.AreEqual(0, replies.Count);
    }
}

// The YMSG path, driven over the real loopback harness: the reply must come back as if from the service,
// cased the way the CLIENT wrote the name, one Message packet per line.
[TestClass]
public class ChatServiceYmsgTests
{
    [TestInitialize]
    public void Setup()
    {
        YmsgTestEnv.Ensure();

        YahooSessionRegistry.Sessions.Clear();

        // Stale offline rows from another test would arrive during LoginAsync's drain and skew the reads below.
        Mind.Db!.YahooDeleteOfflineMessages("alice");
        Mind.Db!.YahooDeleteOfflineMessages("bob");
    }

    [TestCleanup]
    public void Cleanup()
    {
        ChatServiceRegistry.Unregister("YahooHelper");
        ChatServiceRegistry.Unregister("SilentHelper");
        ChatServiceRegistry.Unregister("bob");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task MessageToAService_RepliesPerLine_UsingTheClientsCasing()
    {
        ChatServiceRegistry.Register("YahooHelper", msg => Task.FromResult<IReadOnlyList<string>>(new[] { $"you said: {msg.Text}", "anything else?" }));

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        // The IMVironment pair (63/64) matches what a real 5.x client stamps on every IM it composes.
        await alice.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "yahoohelper").Add(14, "help").Add(97, "1").Add(63, ";0").Add(64, "0"));

        var first = await alice.ReadAsync();

        Assert.AreEqual(YmsgService.Message, first.Service);
        Assert.AreEqual(YmsgStatus.LiveDelivery, first.Status, "YM 5.5 silently discards a live delivery whose header status is not 1, so this reply would never render.");
        Assert.AreEqual("yahoohelper", first.Get(4), "The reply must carry the name as the client sent it, or the IM window may not thread it.");
        Assert.AreEqual("alice", first.Get(5));
        Assert.AreEqual("you said: help", first.Get(14));
        Assert.AreEqual(";0", first.Get(63), "Replies must echo the asker's IMVironment pair; a delivery without it is what the 5.5 capture showed being discarded.");
        Assert.AreEqual("0", first.Get(64), "Replies must echo the asker's IMVironment pair; a delivery without it is what the 5.5 capture showed being discarded.");

        var second = await alice.ReadAsync();

        Assert.AreEqual("anything else?", second.Get(14), "Each returned line is its own Message packet.");
        Assert.AreEqual(YmsgStatus.LiveDelivery, second.Status, "Every line of a multi-line service reply is its own live delivery and needs status 1.");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AnEmptyReplyList_MeansHandledSayNothing()
    {
        var handled = false;

        ChatServiceRegistry.Register("SilentHelper", _ =>
        {
            handled = true;

            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        });

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        await alice.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "SilentHelper").Add(14, "shh"));

        // A ping after the message: if the service had queued any reply it would arrive first.
        await alice.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));

        var next = await alice.ReadAsync();

        Assert.IsTrue(handled, "The handler should have been invoked.");
        Assert.AreEqual(YmsgService.Ping, next.Service, "Nothing should have been delivered ahead of the ping echo.");
    }

    // A service must never shadow a member who is actually signed on; the live relay runs first.
    [TestMethod]
    [Timeout(15000)]
    public async Task ALiveMemberBeatsAServiceHoldingTheSameName()
    {
        var handled = false;

        ChatServiceRegistry.Register("bob", _ =>
        {
            handled = true;

            return Task.FromResult<IReadOnlyList<string>>(new[] { "I am not bob" });
        });

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var alice = new YmsgConn(server);

        await alice.LoginAsync("alice");

        using var bob = new YmsgConn(server);

        await bob.LoginAsync("bob");

        await alice.ReadAsync(); // drain bob's arrival LOGON

        await alice.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "bob").Add(14, "hi bob"));

        var delivered = await bob.ReadAsync();

        Assert.AreEqual("hi bob", delivered.Get(14), "The signed-on member must receive the message.");
        Assert.AreEqual(YmsgStatus.LiveDelivery, delivered.Status, "A member-to-member relay is a live delivery: status 1 or YM 5.5 drops it on the floor.");
        Assert.IsFalse(handled, "The service handler must not fire when the recipient is a live session.");
    }
}

[TestClass]
public class ChatServiceOscarTests
{
    [TestCleanup]
    public void Cleanup()
    {
        ChatServiceRegistry.Unregister("YiffCloud");
    }

    static byte[] MessageBlockTlvValue(ushort charset, byte[] textBytes)
    {
        var block = new List<byte> { 0x05, 0x01, 0x00, 0x01, 0x01 }; // caps fragment

        var blockLength = (ushort)(4 + textBytes.Length);

        block.AddRange(new byte[] { 0x01, 0x01, (byte)(blockLength >> 8), (byte)blockLength });
        block.AddRange(new byte[] { (byte)(charset >> 8), (byte)charset, 0x00, 0x00 });
        block.AddRange(textBytes);

        return block.ToArray();
    }

    [TestMethod]
    public void ExtractPlainText_StripsAimsHtmlWrapper()
    {
        var tlvs = new[] { new Tlv(0x02, MessageBlockTlvValue(0, Encoding.ASCII.GetBytes("<HTML><BODY BGCOLOR=\"#ffffff\"><FONT LANG=\"0\">help &amp; hello</FONT></BODY></HTML>"))) };

        Assert.AreEqual("help & hello", OscarIcbmService.ExtractPlainText(tlvs), "A handler should compare against what the member typed, not the client's HTML wrapper.");
    }

    [TestMethod]
    public void ExtractPlainText_DecodesUcs2AndLatin1Charsets()
    {
        var ucs2 = new[] { new Tlv(0x02, MessageBlockTlvValue(2, Encoding.BigEndianUnicode.GetBytes("héllo ☕"))) };

        Assert.AreEqual("héllo ☕", OscarIcbmService.ExtractPlainText(ucs2));

        var latin1 = new[] { new Tlv(0x02, MessageBlockTlvValue(3, Encoding.Latin1.GetBytes("café"))) };

        Assert.AreEqual("café", OscarIcbmService.ExtractPlainText(latin1));
    }

    [TestMethod]
    public void ExtractPlainText_ATruncatedFragment_DoesNotThrow()
    {
        // Fragment header claims 100 bytes but the value ends after 2.
        var tlvs = new[] { new Tlv(0x02, new byte[] { 0x01, 0x01, 0x00, 0x64, 0x00, 0x00 }) };

        Assert.AreEqual(string.Empty, OscarIcbmService.ExtractPlainText(tlvs));
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AnIcbmToAService_IsAckedAndAnsweredAsTheService()
    {
        YmsgTestEnv.Ensure(); // Mind.Db, so the non-service branches this test must NOT take cannot NRE first

        OscarServer.Sessions.Clear();

        ChatServiceRegistry.Register("YiffCloud", _ => Task.FromResult<IReadOnlyList<string>>(new[] { "Welcome to the cloud!" }));

        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var client = new TcpClient();

        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);

        using var server = await listener.AcceptTcpClientAsync();

        listener.Stop();

        try
        {
            var session = new OscarSession(new ListenerSocket { IsSecure = false, RawSocket = server.Client, Stream = server.GetStream() })
            {
                ScreenName = "alice",
            };

            // CLI_SEND_ICBM: cookie(8) + channel(2) + name length(1) + name + message TLV, with the name
            // cased differently from the registration to prove the reply echoes the client's casing.
            var snac = new Snac(OscarIcbmService.FAMILY_ID, OscarIcbmService.CLI_SEND_ICBM);

            snac.WriteUInt64(0x1122334455667788);
            snac.WriteUInt16(1);
            snac.WriteUInt8((byte)"yiffcloud".Length);
            snac.WriteString("yiffcloud");
            snac.Write(new Tlv(0x02, MessageBlockTlvValue(0, Encoding.ASCII.GetBytes("hello?"))).Encode());

            await new OscarIcbmService(null).ProcessSnac(session, snac);

            // Both the ACK and the reply ICBM are on the wire now; ProcessSnac awaited the sends.
            var buffer = new byte[8192];

            var read = 0;

            var stream = client.GetStream();

            stream.ReadTimeout = 5000;

            while (read < buffer.Length && (stream.DataAvailable || read == 0))
            {
                var got = stream.Read(buffer, read, buffer.Length - read);

                if (got <= 0)
                {
                    break;
                }

                read += got;
            }

            var wire = buffer[..read];

            Assert.IsTrue(ContainsSequence(wire, Encoding.ASCII.GetBytes("Welcome to the cloud!")), "The reply text should be on the wire in an ICBM to the sender.");
            Assert.IsTrue(ContainsSequence(wire, Encoding.ASCII.GetBytes("yiffcloud")), "The reply must be attributed to the name as the client wrote it.");
            Assert.IsFalse(ContainsSequence(wire, Encoding.ASCII.GetBytes("YiffCloud")), "The registered casing must not leak into the reply.");
        }
        finally
        {
            client.Dispose();
        }
    }

    static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}
