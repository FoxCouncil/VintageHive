// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// OscarServer used to hand every FLAP channel-2 SNAC straight to its family handler without ever asking
// whether the session had signed on. The ICQ family (0x15) is the one that makes that bite: it answers
// CLI_FIND_BY_UIN and CLI_FULLINFO_REQUEST2 out of the profile store and needs no screen name to do it, so
// a socket that connected to 5190 and skipped sign-on entirely could read a member's nickname, real name,
// email, addresses and phone number.
//
// The obvious gate - "is ScreenName set?" - would NOT have been a fix. The client supplies that name in a
// TLV, and both CLI_AUTH_REQUEST and CLI_MD5_LOGIN assign it before anything is verified, so an attacker
// can hand the server any name it likes. The gate has to hang off a flag that is only set where a
// credential was actually checked, which is what IsAuthenticated is.

using System.Net;
using System.Net.Sockets;
using VintageHive.Network;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Oscar.Services;

namespace Adversarial5.OscarAuthGate;

[TestClass]
public class OscarAuthGateTests
{
    #region Harness

    // A connected socket pair with OscarServer.ProcessConnection pumping the server end, exactly as the
    // listener would drive it. Returns the client side to write hostile frames into.
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

    // Reads one FLAP, or returns null if the server stayed silent for the read timeout. Silence is the
    // interesting answer in most of these, so a timeout is a result rather than a failure.
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

    // A well-formed ICQ CLI_FIND_BY_UIN meta request: TLV 0x01 wrapping LEN + client UIN + request type +
    // sequence + (subtype + searched UIN). Same shape the ICQ parser tests build.
    private static byte[] FindByUinSnac(uint searchUin)
    {
        var tail = Cat(BitConverter.GetBytes(OscarIcqService.CLI_FIND_BY_UIN), BitConverter.GetBytes(searchUin));

        var body = Cat(BitConverter.GetBytes(1u), BitConverter.GetBytes(OscarIcqService.CLI_META_INFO_REQ), BitConverter.GetBytes((ushort)1), tail);

        var value = Cat(BitConverter.GetBytes((ushort)body.Length), body);

        var tlv = Cat(new byte[] { 0x00, 0x01, (byte)(value.Length >> 8), (byte)(value.Length & 0xFF) }, value);

        var snac = new Snac(OscarIcqService.FAMILY_ID, OscarIcqService.CLI_META_REQ);

        snac.Write(tlv);

        return snac.Encode();
    }

    #endregion

    // The headline one. A socket that never signed on asks for a member's ICQ profile and must get nothing
    // back. Before the gate the server answered with META_USER_FOUND_LAST carrying the stored profile.
    [TestMethod]
    [Timeout(20000)]
    public void AnUnauthenticatedSession_CannotReadAnIcqProfile()
    {
        Mail.MailTestEnv.Ensure();

        using var wire = new Wire();

        // The server opens with its hello FLAP; consume it so the next read is the answer to our probe.
        Assert.IsNotNull(ReadFlap(wire), "The server never sent its hello FLAP, so this test never reached the interesting part.");

        SendFlap(wire, FlapFrameType.Data, FindByUinSnac(1234));

        Assert.IsNull(ReadFlap(wire), "An unauthenticated socket got an answer to an ICQ profile lookup, which is the disclosure this gate exists to stop.");
    }

    // The other reachable pre-auth family, and the one whose reply is pure server state rather than a
    // parsed payload, so it is the cleanest probe that dispatch is genuinely refused rather than merely
    // failing to parse.
    [TestMethod]
    [Timeout(20000)]
    public void AnUnauthenticatedSession_CannotRequestTheServiceFamilyList()
    {
        Mail.MailTestEnv.Ensure();

        using var wire = new Wire();

        Assert.IsNotNull(ReadFlap(wire), "The server never sent its hello FLAP.");

        SendFlap(wire, FlapFrameType.Data, new Snac(OscarGenericServiceControls.FAMILY_ID, OscarGenericServiceControls.SRV_FAMILIES).Encode());

        Assert.IsNull(ReadFlap(wire), "An unauthenticated socket was served the family list, so non-auth SNACs are still being dispatched before sign-on.");
    }

    // The gate must not close the door on the exchange that does the authenticating. Family 0x17 has to keep
    // working pre-auth or nobody can ever sign on at all.
    [TestMethod]
    [Timeout(20000)]
    public void TheAuthorizationFamily_StillAnswersBeforeSignOn()
    {
        Mail.MailTestEnv.Ensure();

        using var wire = new Wire();

        Assert.IsNotNull(ReadFlap(wire), "The server never sent its hello FLAP.");

        var snac = new Snac(OscarAuthorizationService.FAMILY_ID, OscarAuthorizationService.CLI_AUTH_REQUEST);

        snac.Write(new Tlv(0x01, "someone").Encode());

        SendFlap(wire, FlapFrameType.Data, snac.Encode());

        var reply = ReadFlap(wire);

        Assert.IsNotNull(reply, "The auth-key request went unanswered, so the gate has locked out sign-on itself.");

        var replySnac = reply.GetSnac();

        Assert.AreEqual(OscarAuthorizationService.FAMILY_ID, replySnac.Family);
        Assert.AreEqual(OscarAuthorizationService.SRV_AUTH_KEY_RESPONSE, replySnac.SubType);
    }
}
