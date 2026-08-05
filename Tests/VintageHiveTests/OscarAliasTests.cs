// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Numeric OSCAR alias: one account may also answer an ICQ-style sign-on under a configured number,
// without becoming a second user. The contract under test:
//
//   - The feature is inert until an operator sets an alias, and the username path always wins - an
//     account whose USERNAME is numeric keeps signing on exactly as before, and no alias may shadow it.
//   - Sign-on by alias authenticates against the OWNER's password but the session's wire identity stays
//     the presented number: contacts, ICBMs and the offline store all see and route to the number.
//   - Containment: the alias is only an OSCAR sign-on name. Mail, IRC and finger all gate on the user
//     table (UserExistsByUsername / UserFetch), which the alias is deliberately not in.
//
// Wire tests drive OscarServer.ProcessConnection over a loopback socket pair, same harness as the
// auth-gate tests: channel-1 sign-on hands back a cookie on a FLAP, and redeeming that cookie on a
// fresh connection brings up an authenticated BOS session.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Oscar.Services;

namespace OscarAlias;

[TestClass]
public class OscarAliasTests
{
    private const string OwnerName = "alsowner";

    private const string OwnerAlias = "740001";

    private const string SenderName = "alsend";

    private const string NumericUser = "740100";

    private const string OtherName = "alsother";

    private const string Password = "pass1234";

    private const string WrongPassword = "wrong123";

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        Mail.MailTestEnv.Ensure();

        foreach (var user in new[] { OwnerName, SenderName, NumericUser, OtherName })
        {
            if (!Mind.Db.UserExistsByUsername(user))
            {
                Assert.IsTrue(Mind.Db.UserCreate(user, Password), $"could not create test user {user}");
            }
        }

        // The SQLite file outlives the test process, so the alias is (re)asserted rather than assumed fresh.
        Assert.IsTrue(Mind.Db.UserSetAlias(OwnerName, OwnerAlias), "could not attach the owner's alias");
    }

    #region Harness

    // A connected socket pair with OscarServer.ProcessConnection pumping the server end, exactly as the
    // listener would drive it. Returns the client side to write frames into.
    private sealed class Wire : IDisposable
    {
        public TcpClient Client { get; }

        public NetworkStream Stream { get; }

        private readonly TcpClient _serverSide;

        private readonly Task _pump;

        public Wire()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            Client = new TcpClient();
            Client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);

            _serverSide = listener.AcceptTcpClient();

            listener.Stop();

            Stream = Client.GetStream();
            Stream.ReadTimeout = 2000;

            var server = new OscarServer(IPAddress.Loopback, 0);

            _pump = server.ProcessConnection(new ListenerSocket
            {
                IsSecure = false,
                RawSocket = _serverSide.Client,
                Stream = _serverSide.GetStream(),
            });
        }

        public void Dispose()
        {
            try { Client.Close(); } catch { }
            try { _serverSide.Close(); } catch { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private static void SendFlap(Wire wire, FlapFrameType type, byte[] payload)
    {
        var flap = new Flap(type) { Data = payload };

        var bytes = flap.Encode();

        wire.Stream.Write(bytes, 0, bytes.Length);
        wire.Stream.Flush();
    }

    // Reads one FLAP, or returns null if the server stayed silent for the read timeout.
    private static Flap ReadFlap(Wire wire)
    {
        try
        {
            var header = ReadExactly(wire, 6);

            if (header == null || header[0] != (byte)'*')
            {
                return null;
            }

            var length = (ushort)((header[4] << 8) | header[5]);

            var data = length == 0 ? Array.Empty<byte>() : ReadExactly(wire, length);

            if (data == null)
            {
                return null;
            }

            return new Flap((FlapFrameType)header[1]) { Data = data };
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static byte[] ReadExactly(Wire wire, int count)
    {
        var buffer = new byte[count];

        var read = 0;

        while (read < count)
        {
            var got = wire.Stream.Read(buffer, read, count - read);

            if (got <= 0)
            {
                return null;
            }

            read += got;
        }

        return buffer;
    }

    private static byte[] Cat(params byte[][] parts)
    {
        var list = new List<byte>();

        foreach (var part in parts)
        {
            list.AddRange(part);
        }

        return list.ToArray();
    }

    // Channel-1 (roasted password) sign-on. Success and refusal both come back as a SignOff FLAP full of
    // TLVs, so the caller asserts on cookie (0x06) versus error (0x08).
    private static Tlv[] ChannelOneSignOn(Wire wire, string name, string password)
    {
        Assert.IsNotNull(ReadFlap(wire), "The server never sent its hello FLAP.");

        var tlvs = Cat(
            new Tlv(0x01, name).Encode(),
            new Tlv(0x02, OscarUtils.RoastPassword(password)).Encode(),
            new Tlv(0x03, nameof(OscarAliasTests)).Encode());

        SendFlap(wire, FlapFrameType.SignOn, Cat(new byte[] { 0x00, 0x00, 0x00, 0x01 }, tlvs));

        var reply = ReadFlap(wire);

        Assert.IsNotNull(reply, $"The server dropped the connection without answering the channel-1 sign-on for {name}.");

        return OscarUtils.DecodeTlvs(reply.Data);
    }

    // Redeems a sign-on cookie on a fresh connection, consuming the SRV_FAMILIES push so the next read
    // is test traffic. The returned wire is an authenticated BOS session.
    private static Wire ConnectBos(string cookie)
    {
        var wire = new Wire();

        Assert.IsNotNull(ReadFlap(wire), "The server never sent its hello FLAP.");

        SendFlap(wire, FlapFrameType.SignOn, Cat(new byte[] { 0x00, 0x00, 0x00, 0x01 }, new Tlv(0x06, cookie).Encode()));

        Assert.IsNotNull(ReadFlap(wire), "Cookie auth got no reply, so the BOS session never came up.");

        return wire;
    }

    private static Wire SignOnToBos(string name, string password)
    {
        Tlv[] tlvs;

        using (var authWire = new Wire())
        {
            tlvs = ChannelOneSignOn(authWire, name, password);
        }

        var cookieTlv = tlvs.GetTlv(0x06);

        Assert.IsNotNull(cookieTlv, $"Channel-1 sign-on for {name} returned no cookie, so there is nothing to redeem for a BOS session.");

        return ConnectBos(Encoding.ASCII.GetString(cookieTlv.Value));
    }

    private static byte[] IcbmSnac(string to, string message)
    {
        var snac = new Snac(OscarIcbmService.FAMILY_ID, OscarIcbmService.CLI_SEND_ICBM);

        snac.WriteUInt64(0x1122334455667788UL); // message id cookie
        snac.WriteUInt16(1);                    // channel 1
        snac.WriteUInt8((byte)to.Length);
        snac.WriteString(to);
        snac.Write(new Tlv(0x02, message).Encode());

        return snac.Encode();
    }

    #endregion

    // The regression that matters most: an account whose USERNAME is a number keeps signing on through
    // the username path, untouched by the alias machinery existing (and other aliases being configured).
    [TestMethod]
    [Timeout(20000)]
    public void AnExistingNumericUsername_StillSignsOnNormally()
    {
        using var wire = new Wire();

        var tlvs = ChannelOneSignOn(wire, NumericUser, Password);

        Assert.IsNull(tlvs.GetTlv(0x08), "A numeric USERNAME was refused sign-on; the alias feature must not touch the username path.");
        Assert.IsNotNull(tlvs.GetTlv(0x06), "A numeric USERNAME signed on but got no cookie.");
        Assert.AreEqual(NumericUser, tlvs.GetTlv(0x01).Value.ToASCII());
    }

    [TestMethod]
    [Timeout(20000)]
    public void SignOnByAlias_WithTheOwnersPassword_SucceedsAndReportsTheNumber()
    {
        using var wire = new Wire();

        var tlvs = ChannelOneSignOn(wire, OwnerAlias, Password);

        Assert.IsNull(tlvs.GetTlv(0x08), "Sign-on by a configured alias with the owner's password was refused.");
        Assert.IsNotNull(tlvs.GetTlv(0x06), "Alias sign-on succeeded but handed back no cookie.");
        Assert.AreEqual(OwnerAlias, tlvs.GetTlv(0x01).Value.ToASCII(), "The session must report the NUMBER as its identity, not the owner's username.");
    }

    // MD5 login is the other auth path. The challenge key is the presented name echoed back, so the
    // client hashes with the number; the server must verify that against the OWNER's stored password,
    // report the number as the identity, and show the OWNER's mailbox as the account email - the alias
    // is not a mailbox.
    [TestMethod]
    [Timeout(20000)]
    public void Md5SignOnByAlias_ReportsTheNumberAndTheOwnersMailbox()
    {
        using var wire = new Wire();

        Assert.IsNotNull(ReadFlap(wire), "The server never sent its hello FLAP.");

        SendFlap(wire, FlapFrameType.SignOn, new byte[] { 0x00, 0x00, 0x00, 0x01 });

        var authReq = new Snac(OscarAuthorizationService.FAMILY_ID, OscarAuthorizationService.CLI_AUTH_REQUEST);

        authReq.Write(new Tlv(0x01, OwnerAlias).Encode());

        SendFlap(wire, FlapFrameType.Data, authReq.Encode());

        Assert.IsNotNull(ReadFlap(wire), "The auth-key request went unanswered.");

        var hash = MD5.HashData(Encoding.ASCII.GetBytes(OwnerAlias + Password + "AOL Instant Messenger (SM)"));

        var login = new Snac(OscarAuthorizationService.FAMILY_ID, OscarAuthorizationService.CLI_MD5_LOGIN);

        login.Write(new Tlv(0x01, OwnerAlias).Encode());
        login.Write(new Tlv(0x03, nameof(OscarAliasTests)).Encode());
        login.Write(new Tlv(0x25, hash).Encode());

        SendFlap(wire, FlapFrameType.Data, login.Encode());

        var reply = ReadFlap(wire);

        Assert.IsNotNull(reply, "The MD5 login went unanswered.");

        var replySnac = reply.GetSnac();

        Assert.AreEqual(OscarAuthorizationService.SRV_LOGIN_REPLY, replySnac.SubType);

        var tlvs = OscarUtils.DecodeTlvs(replySnac.RawData);

        Assert.IsNull(tlvs.GetTlv(0x08), "MD5 sign-on by a configured alias with the owner's password was refused.");
        Assert.IsNotNull(tlvs.GetTlv(0x06), "MD5 alias sign-on succeeded but handed back no cookie.");
        Assert.AreEqual(OwnerAlias, tlvs.GetTlv(0x01).Value.ToASCII(), "The session must report the NUMBER as its identity, not the owner's username.");
        Assert.AreEqual($"{OwnerName}@{MailDomains.Primary}", tlvs.GetTlv(0x11).Value.ToASCII(), "The account email must be the OWNER's mailbox; the alias is not a mailbox.");
    }

    [TestMethod]
    [Timeout(20000)]
    public void SignOnByAlias_WithTheWrongPassword_IsRefused()
    {
        using var wire = new Wire();

        var tlvs = ChannelOneSignOn(wire, OwnerAlias, WrongPassword);

        Assert.IsNotNull(tlvs.GetTlv(0x08), "Alias sign-on with the wrong password was not answered with an auth error.");
        Assert.IsNull(tlvs.GetTlv(0x06), "Alias sign-on with the wrong password handed back a cookie.");
    }

    // A number that is neither a username nor a configured alias stays exactly as refused as it is today.
    [TestMethod]
    [Timeout(20000)]
    public void AnUnknownNumber_IsStillRefused()
    {
        using var wire = new Wire();

        var tlvs = ChannelOneSignOn(wire, "749999", Password);

        Assert.IsNotNull(tlvs.GetTlv(0x08), "An unresolvable number was not refused.");
        Assert.IsNull(tlvs.GetTlv(0x06), "An unresolvable number was handed a cookie.");
    }

    [TestMethod]
    public void AliasCollisionsAndMalformedAliases_AreRefused()
    {
        Assert.IsFalse(Mind.Db.UserSetAlias(OtherName, NumericUser), "An alias equal to an existing USERNAME must be refused: the username path wins sign-on, so it could only ever shadow.");
        Assert.IsFalse(Mind.Db.UserSetAlias(OtherName, OwnerAlias), "An alias already owned by another user must be refused.");
        Assert.IsFalse(Mind.Db.UserSetAlias(OtherName, "74x001"), "A non-numeric alias must be refused.");
        Assert.IsFalse(Mind.Db.UserSetAlias(OtherName, "12"), "An alias shorter than the 3-character username floor must be refused.");
        Assert.IsFalse(Mind.Db.UserSetAlias(OtherName, "123456789"), "An alias longer than the 8-character username ceiling must be refused.");
        Assert.IsFalse(Mind.Db.UserSetAlias("nosuchu", "740999"), "An alias for a user that does not exist must be refused.");
    }

    [TestMethod]
    public void AnAlias_CanBeReassignedAndCleared()
    {
        Assert.IsTrue(Mind.Db.UserSetAlias(OtherName, "740002"));
        Assert.AreEqual(OtherName, Mind.Db.UserResolveAlias("740002"));
        Assert.AreEqual(OtherName, Mind.Db.UserFetchByAlias("740002").Username);

        // Reassigning frees the old number; an account holds at most one alias.
        Assert.IsTrue(Mind.Db.UserSetAlias(OtherName, "740003"));
        Assert.IsNull(Mind.Db.UserResolveAlias("740002"));
        Assert.AreEqual(OtherName, Mind.Db.UserResolveAlias("740003"));

        // Re-setting the same alias is a no-op, not a refusal.
        Assert.IsTrue(Mind.Db.UserSetAlias(OtherName, "740003"));

        Assert.IsTrue(Mind.Db.UserClearAlias(OtherName));
        Assert.IsNull(Mind.Db.UserResolveAlias("740003"));
        Assert.IsNull(Mind.Db.UserFetchByAlias("740003"));
    }

    // Live routing follows the wire identity: an ICBM addressed to the number reaches the session that
    // signed on with it, attributed to the sender, and the sender gets its ACK.
    [TestMethod]
    [Timeout(30000)]
    public void AnIcbmAddressedToTheNumber_ReachesTheAliasSession()
    {
        using var receiver = SignOnToBos(OwnerAlias, Password);
        using var sender = SignOnToBos(SenderName, Password);

        SendFlap(sender, FlapFrameType.Data, IcbmSnac(OwnerAlias, "hello number"));

        var flap = ReadFlap(receiver);

        Assert.IsNotNull(flap, "The ICBM addressed to the number never reached the alias session.");

        var snac = flap.GetSnac();

        Assert.AreEqual(OscarIcbmService.FAMILY_ID, snac.Family);
        Assert.AreEqual(OscarIcbmService.SRV_CLIENT_ICBM, snac.SubType);

        var raw = snac.RawData;

        Assert.AreEqual(SenderName, Encoding.ASCII.GetString(raw, 11, raw[10]), "The delivered ICBM is not attributed to the sender.");

        var ack = ReadFlap(sender);

        Assert.IsNotNull(ack, "The sender never got an ACK for a delivered ICBM.");
        Assert.AreEqual(OscarIcbmService.SRV_MSG_ACK, ack.GetSnac().SubType);
    }

    // With the alias offline, a channel-1 ICBM to the number is stored (keyed on the number, which is the
    // name the owning session signs on with and flushes) instead of bouncing "not logged on".
    [TestMethod]
    [Timeout(30000)]
    public void AnIcbmToTheOfflineNumber_IsStoredForLaterDelivery()
    {
        Mind.Db.OscarDeleteOfflineMessages(OwnerAlias);

        using var sender = SignOnToBos(SenderName, Password);

        SendFlap(sender, FlapFrameType.Data, IcbmSnac(OwnerAlias, "stored for later"));

        var ack = ReadFlap(sender);

        Assert.IsNotNull(ack, "Sending to the offline number got no reply at all.");
        Assert.AreEqual(OscarIcbmService.SRV_MSG_ACK, ack.GetSnac().SubType, "Expected the offline-store ACK; an error SNAC means the alias was refused as a recipient.");

        var stored = Mind.Db.OscarGetOfflineMessages(OwnerAlias);

        try
        {
            Assert.AreEqual(1, stored.Count, "The ICBM to the offline number was not stored.");
            Assert.AreEqual(SenderName, stored[0].FromScreenName);
            Assert.AreEqual(OwnerAlias, stored[0].ToScreenName);
        }
        finally
        {
            Mind.Db.OscarDeleteOfflineMessages(OwnerAlias);
        }
    }

    // Containment: mail (SMTP/POP3/IMAP), IRC and finger all gate on the user table - either
    // UserExistsByUsername or a credentialed UserFetch, after MailDomains.TryResolveLogin peels any
    // hosted domain off a mail-style login. The alias lives in its own table precisely so that every one
    // of those surfaces keeps refusing the number, alias configured or not.
    [TestMethod]
    public void TheAlias_IsNotAMailboxAnIrcNickOrAFingerTarget()
    {
        Assert.IsTrue(MailDomains.TryResolveLogin($"{OwnerAlias}@{MailDomains.Primary}", out var localPart, out _));
        Assert.AreEqual(OwnerAlias, localPart);
        Assert.IsFalse(Mind.Db.UserExistsByUsername(localPart), "The alias resolved as a mail login local part backed by a user row.");

        Assert.IsFalse(Mind.Db.UserExistsByUsername(OwnerAlias), "The alias must not exist as a username - that lookup is the gate for IRC nick auth and finger targets.");
        Assert.IsNull(Mind.Db.UserFetch(OwnerAlias), "The alias must not fetch a user row.");
        Assert.IsNull(Mind.Db.UserFetch(OwnerAlias, Password), "The alias plus the owner's password must not authenticate anywhere outside OSCAR sign-on.");
    }
}
