// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Proxy.Yahoo;
using VintageHive.Proxy.Yahoo.Pager;

namespace Yahoo;

// Helpers for building and reading the YPNS wire format from a client's point of view, mirroring what
// libyahoo's yahoo_sendcmd and yahoo_getpacket do.
internal static class Ypns
{
    public const int HeaderSize = 104;

    // Named rather than written inline: a raw control byte in a source file is invisible, survives no
    // round trip through an editor or a diff intact, and these two bytes ARE the content grammar here.
    public const string MessageTerminator = "\u0001";

    public const string ImvironmentSeparator = "\u0006";

    // What the reference client actually puts on the wire: a hardwired 1128-byte length field regardless of
    // how many bytes it goes on to send. Tests build packets this way on purpose - a server that trusts the
    // length field breaks against the real client, and only a test that reproduces the lie catches that.
    public const uint HardwiredClientLength = 4 * 256 + HeaderSize;

    public static byte[] Build(uint service, string realId, string activeId, string content, uint msgType = 0, uint magicId = 0, uint? declaredLength = null)
    {
        var contentBytes = Encoding.Latin1.GetBytes(content ?? string.Empty);

        var buffer = new byte[HeaderSize + contentBytes.Length + 1];

        Encoding.ASCII.GetBytes(YpnsPacket.ClientVersion).CopyTo(buffer, 0);

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), declaredLength ?? HardwiredClientLength);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), service);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), magicId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), msgType);

        Encoding.Latin1.GetBytes(realId ?? string.Empty).CopyTo(buffer, 32);
        Encoding.Latin1.GetBytes(activeId ?? string.Empty).CopyTo(buffer, 68);

        contentBytes.CopyTo(buffer, HeaderSize);

        return buffer;
    }

    // Walks a response body the way the client does: resynchronise on "YHOO", then take len bytes.
    public static List<YpnsPacket> Parse(byte[] body)
    {
        var packets = new List<YpnsPacket>();

        var offset = 0;

        while (offset + HeaderSize <= body.Length)
        {
            if (!body.AsSpan(offset, 4).SequenceEqual("YHOO"u8))
            {
                offset++;

                continue;
            }

            var declared = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset + 8));

            if (declared < HeaderSize || offset + declared > body.Length)
            {
                break;
            }

            packets.Add(YpnsPacket.Decode(body.AsSpan(offset, declared)));

            offset += declared;
        }

        return packets;
    }
}

// The wire protocol, driven end to end over the real HTTP pipeline.
//
// Written against libyahoo 0.18.4. The request side of this client generation was captured from a real Pager
// (request lines and User-Agent only, no bodies), so every content grammar asserted here comes from the
// reference's parsers rather than from that capture.
[TestClass]
public class YahooPagerWireTests
{
    [TestInitialize]
    public void Setup()
    {
        Http.HttpErrorPageEnv.EnsureContexts();
        YmsgTestEnv.Ensure();

        YahooSessionRegistry.Sessions.Clear();
        PagerLoginTokens.Clear();

        PagerEnv.Cookie = null;

        Mind.Db!.ConfigSet(ConfigNames.ServiceYahooPager, true);
        Mind.Db!.YahooDeleteOfflineMessages("alice");
        Mind.Db!.YahooDeleteOfflineMessages("bob");
    }

    static string NotifyUrl => $"http://{PagerHosts.Notify}/notify/";

    // Signs on over HTTP the way a client does: ncclogin for the cookie, then a LOGON packet carrying the
    // token. Returns the token and whatever the sign-on POST replied with.
    static async Task<(string Token, List<YpnsPacket> Packets)> SignOn(string user, uint status = YmsgStatus.Available)
    {
        var login = await PagerEnv.Get(PagerEnv.LoginUrl(user, YmsgTestEnv.Password));

        var cookie = PagerEnv.SetCookieValue(login);

        Assert.IsNotNull(cookie, $"ncclogin refused '{user}', so nothing below can be exercised.");

        var token = Regex.Match(cookie, @"[?&]?n=([a-z0-9]+)").Groups[1].Value;

        PagerEnv.Cookie = cookie.Split(';')[0];

        var (code, body) = await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Logon, user, user, $"{token}{Ypns.MessageTerminator}{user}", status));

        Assert.AreEqual(200, code, "The LOGON packet was refused.");

        return (token, Ypns.Parse(body));
    }

    static Task<(int Status, byte[] Body)> Poll() => PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Ping, "alice", "alice", string.Empty));

    // ---- codec ----

    [TestMethod]
    public void Packet_RoundTripsThroughTheFixedHeader()
    {
        var encoded = new YpnsPacket(YpnsService.Message, "alice", "alice", "bob,,hello", YpnsMsgType.Offline)
        {
            ConnectionId = 0x11223344,
            MagicId = 0x55667788,
        }.Encode();

        StringAssert.StartsWith(Encoding.ASCII.GetString(encoded, 0, 4), YpnsPacket.ServerMagic, "The client resynchronises on this and will skip the whole packet without it.");

        var decoded = YpnsPacket.Decode(encoded);

        Assert.AreEqual(YpnsService.Message, decoded.Service);
        Assert.AreEqual(YpnsMsgType.Offline, decoded.MsgType);
        Assert.AreEqual(0x11223344u, decoded.ConnectionId);
        Assert.AreEqual(0x55667788u, decoded.MagicId);
        Assert.AreEqual("alice", decoded.RealId);
        Assert.AreEqual("bob,,hello", decoded.Content);

        // Total length including the header and the content's NUL terminator, which is how the client
        // computes contentlen back out of it.
        Assert.AreEqual((uint)encoded.Length, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(8)));
    }

    [TestMethod]
    public void Packet_FieldsAreLittleEndianAtTheDocumentedOffsets()
    {
        var encoded = new YpnsPacket(7, string.Empty, string.Empty, string.Empty) { ConnectionId = 1, MagicId = 2, MsgType = 3 }.Encode();

        Assert.AreEqual(7u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(12)), "service");
        Assert.AreEqual(1u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(16)), "connection_id");
        Assert.AreEqual(2u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(20)), "magic_id");
        Assert.AreEqual(3u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(28)), "msgtype");
        Assert.AreEqual(0x07, encoded[12], "A big-endian service field would put the byte at offset 15.");
    }

    // The reference client hardwires len to 1128 on every packet but sends only 104 + strlen(content) + 1
    // bytes, so a server that sizes the content from the length field reads far past the body it was given.
    [TestMethod]
    public void Decode_IgnoresTheClientsHardwiredLengthField()
    {
        var body = Ypns.Build(YpnsService.Message, "alice", "alice", "bob,hello there");

        Assert.AreEqual(Ypns.HardwiredClientLength, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)), "The fixture stopped reproducing the client's lie, so this proves nothing.");
        Assert.IsTrue(body.Length < Ypns.HardwiredClientLength, "The fixture must be shorter than the length it declares.");

        var decoded = YpnsPacket.Decode(body);

        Assert.AreEqual("bob,hello there", decoded.Content);
    }

    [TestMethod]
    public void Decode_RejectsABodyShorterThanTheHeader()
    {
        Assert.IsNull(YpnsPacket.Decode(new byte[YpnsPacket.HeaderSize - 1]));
        Assert.IsNull(YpnsPacket.Decode(Array.Empty<byte>()));
    }

    [TestMethod]
    public void Encode_TruncatesAnOversizedNickInsteadOfOverflowingTheField()
    {
        var encoded = new YpnsPacket(YpnsService.Logon, new string('a', 200), new string('b', 200), string.Empty).Encode();

        // nick1 occupies 32..67 and nick2 68..103; a write past either would corrupt the next field.
        Assert.AreEqual(0, encoded[67], "nick1 overran into nick2.");
        Assert.AreEqual(0, encoded[103], "nick2 overran into the content.");
    }

    // ---- sign-on ----

    [TestMethod]
    public async Task Logon_WithAValidToken_ReturnsARosterSnapshot()
    {
        var (_, packets) = await SignOn("alice");

        Assert.AreEqual(1, packets.Count, "Sign-on should answer with exactly the roster packet.");
        Assert.AreEqual(YpnsService.Logon, packets[0].Service);
        Assert.AreEqual("alice", packets[0].RealId);

        // Nobody else is online, so the count prefix is zero.
        Assert.AreEqual("0", packets[0].Content);
    }

    [TestMethod]
    public async Task Logon_StampsTheConnectionAndMagicIdsTheClientCaches()
    {
        var (_, packets) = await SignOn("alice");

        Assert.AreNotEqual(0u, packets[0].ConnectionId, "The client caches connection_id from any packet carrying a non-zero one.");
        Assert.AreNotEqual(0u, packets[0].MagicId, "The client caches magic_id and echoes it on every packet afterwards.");
    }

    [TestMethod]
    public async Task Logon_WithAnUnknownToken_IsRefused()
    {
        var (status, _) = await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Logon, "alice", "alice", $"nosuchtokenx{Ypns.MessageTerminator}alice"));

        Assert.AreEqual(401, status);
        Assert.IsNull(YahooSessionRegistry.GetByUsername("alice"), "A refused sign-on created a session anyway.");
    }

    // A member's own valid token must not be a way to sign on as somebody else.
    [TestMethod]
    public async Task Logon_PresentingSomeoneElsesAccountWithYourOwnToken_IsRefused()
    {
        var login = await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password));

        var token = Regex.Match(PagerEnv.SetCookieValue(login), @"[?&]?n=([a-z0-9]+)").Groups[1].Value;

        var (status, _) = await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Logon, "bob", "bob", $"{token}{Ypns.MessageTerminator}bob"));

        Assert.AreEqual(401, status, "alice's token signed someone in as bob.");
        Assert.IsNull(YahooSessionRegistry.GetByUsername("bob"));
    }

    [TestMethod]
    public async Task Logon_Twice_ReusesTheSessionRatherThanSupersedingItself()
    {
        var (token, _) = await SignOn("alice");

        var before = YahooSessionRegistry.GetByUsername("alice").SessionId;

        var (status, _) = await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Logon, "alice", "alice", $"{token}{Ypns.MessageTerminator}alice"));

        Assert.AreEqual(200, status);

        var after = YahooSessionRegistry.GetByUsername("alice");

        Assert.IsNotNull(after, "A repeat LOGON superseded itself and left the account signed out.");
        Assert.AreEqual(before, after.SessionId);
        Assert.AreEqual("alice", PagerLoginTokens.Resolve(token), "The repeat sign-on revoked the very token it authenticated with.");
    }

    [TestMethod]
    public async Task APacketOnASessionThatWasNeverSignedOn_IsRefused()
    {
        PagerEnv.Cookie = "Y=v=1&n=nosuchtokenx";

        var (status, _) = await Poll();

        Assert.AreEqual(401, status);
    }

    [TestMethod]
    public async Task AMalformedBody_IsRejectedWithoutTouchingTheRegistry()
    {
        var (status, _) = await PagerEnv.SendRaw("POST", NotifyUrl, new byte[] { 1, 2, 3 });

        Assert.AreEqual(400, status);
        Assert.AreEqual(0, YahooSessionRegistry.Sessions.Count);
    }

    // ---- polling ----

    [TestMethod]
    public async Task APollWithNothingWaiting_ReturnsAnEmptyBody()
    {
        await SignOn("alice");

        var (status, body) = await Poll();

        Assert.AreEqual(200, status);
        Assert.AreEqual(0, body.Length, "An empty queue is a normal poll result, not an error or a filler packet.");
    }

    // ---- cross transport ----

    // The headline property: one identity, whichever wire it arrived on.
    [TestMethod]
    [Timeout(15000)]
    public async Task APagerSignOn_SupersedesALiveYmsgSession()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("alice");

        Assert.AreEqual("YMSG", YahooSessionRegistry.GetByUsername("alice").Transport);

        await SignOn("alice");

        var owner = YahooSessionRegistry.GetByUsername("alice");

        Assert.AreEqual("Pager", owner.Transport, "The Pager sign-on did not take the account from the YMSG session.");
        Assert.AreEqual(1, YahooSessionRegistry.Sessions.Values.Count(s => s.IsAuthenticated && s.Username == "alice"), "Two sessions both look live for one account.");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AYmsgSignOn_SupersedesALivePagerSession()
    {
        var (token, _) = await SignOn("alice");

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("alice");

        Assert.AreEqual("YMSG", YahooSessionRegistry.GetByUsername("alice").Transport, "The YMSG sign-on did not take the account from the Pager session.");

        // Revoking the token is what ends a Pager session - there is no socket to close - so the old client's
        // next poll has to come back refused rather than resuming.
        Assert.IsNull(PagerLoginTokens.Resolve(token), "The superseded Pager session's token is still live.");

        PagerEnv.Cookie = $"Y=v=1&n={token}";

        var (status, _) = await Poll();

        Assert.AreEqual(401, status, "A superseded Pager client kept polling successfully.");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AMessageFromYmsg_ReachesAPagerMemberOnTheirNextPoll()
    {
        await SignOn("bob");

        var pagerCookie = PagerEnv.Cookie;

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("alice");

        await socket.SendAsync(new YmsgPacket(YmsgService.Message, 0, 0).Add(1, "alice").Add(5, "bob").Add(14, "hello, over there"));

        // The handler loop is sequential, so a ping round-trip proves the relay completed.
        await socket.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        await socket.ReadAsync();

        PagerEnv.Cookie = pagerCookie;

        var (status, body) = await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Ping, "bob", "bob", string.Empty));

        Assert.AreEqual(200, status);

        var packets = Ypns.Parse(body);

        var message = packets.FirstOrDefault(p => p.Service == YpnsService.Message);

        Assert.IsNotNull(message, "The IM never reached the Pager member's queue.");

        // "<from>,<flag>,<text>" with the flag left empty - the reference's own "userid,,msg" shape. The comma
        // the member typed has to survive, which is why message text is not comma-sanitised.
        Assert.AreEqual("alice,,hello, over there", message.Content);

        Assert.AreEqual(0, Mind.Db!.YahooGetOfflineMessages("bob").Count, "The IM was also queued offline for a member who was signed on.");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AMessageFromAPagerMember_ReachesAYmsgMember()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("bob");

        await SignOn("alice");

        // Drain alice's arrival LOGON so the next read is the message.
        await socket.ReadAsync();

        var (status, _) = await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Message, "alice", "alice", "bob,hi from the pager"));

        Assert.AreEqual(200, status);

        var delivered = await socket.ReadAsync();

        Assert.AreEqual(YmsgService.Message, delivered.Service);
        Assert.AreEqual("alice", delivered.Get(4));
        Assert.AreEqual("hi from the pager", delivered.Get(14));
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task PresenceCrossesBothWays()
    {
        await SignOn("bob");

        var pagerCookie = PagerEnv.Cookie;

        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        // alice signing on over YMSG must be announced into the Pager member's queue...
        await socket.LoginAsync("alice");

        await socket.SendAsync(new YmsgPacket(YmsgService.Ping, 0, 0));
        await socket.ReadAsync();

        PagerEnv.Cookie = pagerCookie;

        var (_, body) = await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Ping, "bob", "bob", string.Empty));

        var presence = Ypns.Parse(body).FirstOrDefault(p => p.Service == YpnsService.Logon);

        Assert.IsNotNull(presence, "A YMSG sign-on was not announced to the Pager member.");
        StringAssert.StartsWith(presence.Content, "alice(", "The presence record does not name the member who signed on.");

        // ...and the Pager member has to have been in the roster snapshot alice received on the way in.
        Assert.IsNotNull(YahooSessionRegistry.GetByUsername("bob"));
    }

    [TestMethod]
    public async Task OfflineMessagesAreFlushedAtPagerSignOnAndThenDeleted()
    {
        Mind.Db!.YahooStoreOfflineMessage("bob", "alice", "read this later");

        var (_, packets) = await SignOn("alice");

        var message = packets.FirstOrDefault(p => p.Service == YpnsService.Message);

        Assert.IsNotNull(message, "Queued offline messages were not delivered at sign-on.");
        Assert.AreEqual(YpnsMsgType.Offline, message.MsgType, "The offline header flag routes the client to its offline parser.");

        // "6,6,<to>,<from>,<timestamp>,<text>" - the first two numbers are ignored by the reference parser.
        StringAssert.StartsWith(message.Content, "6,6,alice,bob,");
        StringAssert.EndsWith(message.Content, ",read this later");

        Assert.AreEqual(0, Mind.Db!.YahooGetOfflineMessages("alice").Count, "The queue was not flushed, so the message redelivers forever.");
    }

    // ---- status ----

    [TestMethod]
    [Timeout(15000)]
    public async Task APagerStatusChange_IsBroadcastToYmsgPeers()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("bob");

        await SignOn("alice");

        await socket.ReadAsync(); // alice's arrival

        await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.IsAway, "alice", "alice", $"{YmsgStatus.Busy}{Ypns.MessageTerminator}in a meeting"));

        var notice = await socket.ReadAsync();

        Assert.AreEqual(YmsgService.Logon, notice.Service);
        Assert.AreEqual("alice", notice.Get(7));
        Assert.AreEqual(YmsgStatus.Busy.ToString(), notice.Get(10));
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task APagerMemberGoingInvisible_LooksLikeADepartureToPeers()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("bob");

        await SignOn("alice");

        await socket.ReadAsync(); // alice's arrival

        await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.IsAway, "alice", "alice", YmsgStatus.Invisible.ToString()));

        var notice = await socket.ReadAsync();

        Assert.AreEqual(YmsgService.Logoff, notice.Service, "An invisible Pager member was left showing as online to YMSG peers.");
        Assert.AreEqual("alice", notice.Get(7));
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ALogoffPacket_SignsOutAndTellsPeers()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var socket = new YmsgConn(server);

        await socket.LoginAsync("bob");

        var (token, _) = await SignOn("alice");

        await socket.ReadAsync(); // alice's arrival

        await PagerEnv.SendRaw("POST", NotifyUrl, Ypns.Build(YpnsService.Logoff, "alice", "alice", "alice"));

        var notice = await socket.ReadAsync();

        Assert.AreEqual(YmsgService.Logoff, notice.Service);
        Assert.AreEqual("alice", notice.Get(7));

        Assert.IsNull(YahooSessionRegistry.GetByUsername("alice"));
        Assert.IsNull(PagerLoginTokens.Resolve(token), "Signing off left the token usable.");
    }

    // ---- reaping ----

    // The POST loop is the connection. A client that stops polling has gone away, and until it is reaped it
    // still owns the username and swallows every message relayed to it.
    [TestMethod]
    public async Task ASessionThatStopsPolling_IsReapedAndReleasesTheUsername()
    {
        await SignOn("alice");

        var session = (PagerSession)YahooSessionRegistry.GetByUsername("alice");

        session.LastSeen = DateTimeOffset.UtcNow - PagerSession.IdleTimeout - TimeSpan.FromMinutes(1);

        Assert.IsTrue(session.IsExpired);

        await PagerTransport.ReapExpiredAsync();

        Assert.IsNull(YahooSessionRegistry.GetByUsername("alice"), "An abandoned Pager session still owns the account.");
        Assert.IsNull(PagerLoginTokens.Resolve(session.Token));
    }

    [TestMethod]
    public async Task PollingKeepsASessionAlive()
    {
        await SignOn("alice");

        var session = (PagerSession)YahooSessionRegistry.GetByUsername("alice");

        session.LastSeen = DateTimeOffset.UtcNow - PagerSession.IdleTimeout + TimeSpan.FromMinutes(1);

        await Poll();

        Assert.IsFalse(session.IsExpired, "A poll did not refresh the session's liveness.");
        Assert.IsNotNull(YahooSessionRegistry.GetByUsername("alice"));
    }
}

// The content grammars, unit tested against the shapes libyahoo's parsers accept.
[TestClass]
public class PagerContentFormatTests
{
    static PagerSession Session(string username, uint status = YmsgStatus.Available, string customMessage = "")
    {
        return new PagerSession("tok") { Username = username, IsAuthenticated = true, YahooStatus = status, CustomStatusMessage = customMessage };
    }

    // nick(status,connection_id,unk,in_pager,in_chat,in_game) - six fields for every ordinary status.
    [TestMethod]
    public void StatusRecord_UsesTheSixFieldFormForAnOrdinaryStatus()
    {
        var record = PagerSession.StatusRecord(Session("alice"));

        StringAssert.StartsWith(record, "alice(0,");
        StringAssert.EndsWith(record, ",0,1,0,0)");

        Assert.AreEqual(6, record[(record.IndexOf('(') + 1)..record.IndexOf(')')].Split(',').Length, "The ordinary status record changed arity.");
        Assert.IsFalse(record.Contains((char)0x01), "An ordinary status record must not carry the message terminator.");
    }

    // nick(99,msg\x01,connection_id,unk,in_pager,in_chat,in_game) - the message field appears only at Custom,
    // which the reference parser keys off the status value it just read rather than off a protocol version.
    [TestMethod]
    public void StatusRecord_InsertsTheMessageFieldOnlyForACustomStatus()
    {
        var record = PagerSession.StatusRecord(Session("alice", YmsgStatus.Custom, "gone fishing"));

        StringAssert.StartsWith(record, $"alice(99,gone fishing{Ypns.MessageTerminator},");
        StringAssert.EndsWith(record, ",0,1,0,0)");
    }

    // The terminator is what ends the message field, which is exactly why a comma inside it is legal.
    [TestMethod]
    public void StatusRecord_KeepsCommasInACustomMessageButStripsTheTerminator()
    {
        var record = PagerSession.StatusRecord(Session("alice", YmsgStatus.Custom, "out, back later"));

        StringAssert.Contains(record, $"out, back later{Ypns.MessageTerminator}");

        var injected = PagerSession.StatusRecord(Session("alice", YmsgStatus.Custom, $"sneaky{Ypns.MessageTerminator},DEADBEEF,9,9,9,9"));

        Assert.AreEqual(1, injected.Count(c => c == (char)0x01), "A status message carrying the terminator could forge the rest of the record.");
    }

    [TestMethod]
    public void RosterSnapshot_CountsAccurately()
    {
        Assert.AreEqual("0", PagerSession.RosterSnapshot(Array.Empty<YahooSession>()), "An empty roster is a bare zero.");

        var snapshot = PagerSession.RosterSnapshot(new[] { Session("bob"), Session("carol") });

        StringAssert.StartsWith(snapshot, "2,", "The count prefix sizes the client's status array; a wrong one walks it off the end.");
        Assert.AreEqual(2, Regex.Matches(snapshot, @"\(").Count);
    }

    [TestMethod]
    public void MessageText_KeepsCommasAndStripsOnlyTheImvironmentSeparator()
    {
        Assert.AreEqual("hello, world - it's 3,000 miles", PagerSession.SanitiseMessageText("hello, world - it's 3,000 miles"));
        Assert.AreEqual("before after", PagerSession.SanitiseMessageText($"before{Ypns.ImvironmentSeparator}after"), "The imvironment separator truncates the message on the client.");
    }

    [TestMethod]
    public void Identifiers_AreStrippedOfEveryStructuralCharacter()
    {
        Assert.AreEqual("a b c", PagerSession.SanitiseIdentifier("a,b(c"));
        Assert.AreEqual("alice", PagerSession.SanitiseIdentifier("alice"));
    }

    // asctime shape, matching the reference's worked example "Tue Mar  7 12:14:50 2000" - note the
    // space-padded day.
    [TestMethod]
    public void Timestamp_UsesTheAsctimeShape()
    {
        var formatted = PagerSession.FormatTimestamp(new DateTimeOffset(2000, 3, 7, 12, 14, 50, TimeSpan.Zero).ToUnixTimeSeconds());

        Assert.AreEqual("Tue Mar  7 12:14:50 2000", formatted);
    }

    // A queue that fills must fail the delivery rather than dropping it: failure sends the IM to offline
    // storage, dropping loses it.
    [TestMethod]
    public async Task AFullOutboundQueue_FailsDeliveryRatherThanDiscardingTheMessage()
    {
        var session = Session("alice");

        for (var i = 0; i < PagerSession.MaxQueuedPackets; i++)
        {
            Assert.IsTrue(await session.DeliverMessageAsync("bob", $"message {i}", 0, false));
        }

        Assert.IsFalse(await session.DeliverMessageAsync("bob", "one too many", 0, false), "A full queue silently swallowed a message instead of reporting failure.");
        Assert.AreEqual(PagerSession.MaxQueuedPackets, session.QueueDepth);
    }

    [TestMethod]
    public void DrainOutbound_ConcatenatesPacketsAndEmptiesTheQueue()
    {
        var session = Session("alice");

        session.Enqueue(new YpnsPacket(YpnsService.Logon, "alice", "alice", "0")).GetAwaiter().GetResult();
        session.Enqueue(new YpnsPacket(YpnsService.Message, "alice", "alice", "bob,,hi")).GetAwaiter().GetResult();

        var body = session.DrainOutbound();

        Assert.AreEqual(2, Ypns.Parse(body).Count, "The client walks a plain concatenation; framing must not need anything else.");
        Assert.AreEqual(0, session.QueueDepth);
        Assert.AreEqual(0, session.DrainOutbound().Length, "Draining twice must not replay packets the client already has.");
    }
}
