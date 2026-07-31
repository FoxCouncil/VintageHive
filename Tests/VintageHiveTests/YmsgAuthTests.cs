// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive;
using VintageHive.Proxy.Yahoo;

namespace Yahoo;

// Regression cover for YMSG login verification.
//
// HandleAuthRespAsync used to accept ANY crypt response for a username that existed in the user table, so anyone
// who could reach port 5050 could sign in as any member and act as them - presence, IM, everything keyed off
// session.Username. The comment justifying it claimed parity with OSCAR, but OscarServer.ProcessChannelOneAuth
// has always compared the roasted password, so OSCAR was the counter-example.
//
// The load-bearing assertion in here is that a wrong password never reaches service 0x55 (List). Everything else
// guards a way that could regress into accepting something unverified.
[TestClass]
public class YmsgAuthTests
{
    [TestInitialize]
    public void Setup()
    {
        YmsgTestEnv.Ensure();
        YmsgServer.Sessions.Clear();
    }

    private static async Task<YmsgPacket> AttemptLogin(string username, string password)
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var conn = new YmsgConn(server);

        await conn.SendAsync(new YmsgPacket(YmsgService.Verify, 0, 0));
        await conn.ReadAsync();

        await conn.SendAsync(new YmsgPacket(YmsgService.Auth, 0, 0).Add(1, username));

        var challenge = await conn.ReadAsync();

        var seed = challenge.Get(94);

        var response = YmsgCrypt.ComputeAuthResponse(seed, password);

        Assert.IsNotNull(response, "The server's own challenge must be computable by a real client");

        await conn.SendAsync(new YmsgPacket(YmsgService.AuthResp, 0, 0).Add(0, username).Add(6, response.Resp6).Add(96, response.Resp96));

        return await conn.ReadAsync();
    }

    private static async Task<YmsgPacket> AttemptLoginRaw(string username, string resp6, string resp96)
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var conn = new YmsgConn(server);

        await conn.SendAsync(new YmsgPacket(YmsgService.Verify, 0, 0));
        await conn.ReadAsync();

        await conn.SendAsync(new YmsgPacket(YmsgService.Auth, 0, 0).Add(1, username));
        await conn.ReadAsync();

        var authResp = new YmsgPacket(YmsgService.AuthResp, 0, 0).Add(0, username);

        if (resp6 != null)
        {
            authResp.Add(6, resp6);
        }

        if (resp96 != null)
        {
            authResp.Add(96, resp96);
        }

        await conn.SendAsync(authResp);

        return await conn.ReadAsync();
    }

    // The original report's probe, as a test: existing account, wrong password, must not sign in.
    [TestMethod]
    [Timeout(15000)]
    public async Task Login_ExistingUserWrongPassword_DoesNotReachList()
    {
        var reply = await AttemptLogin("alice", "TOTALLY-WRONG-PASSWORD");

        Assert.AreNotEqual(YmsgService.List, reply.Service, "A wrong password signed in - this is the impersonation bug.");
        Assert.AreEqual(YmsgService.AuthResp, reply.Service);
        Assert.AreEqual(YmsgStatus.LoginError, reply.Status);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Login_ExistingUserCorrectPassword_ReachesList()
    {
        var reply = await AttemptLogin("alice", YmsgTestEnv.Password);

        Assert.AreEqual(YmsgService.List, reply.Service, "The correct password must still sign in");
        Assert.AreEqual(0u, reply.Status);
    }

    // A near-miss must fail too: this catches a comparison that only checks length or a prefix.
    [TestMethod]
    [Timeout(15000)]
    public async Task Login_PasswordOffByOneCharacter_IsRefused()
    {
        var reply = await AttemptLogin("alice", YmsgTestEnv.Password + "x");

        Assert.AreEqual(YmsgStatus.LoginError, reply.Status);

        reply = await AttemptLogin("alice", YmsgTestEnv.Password.ToUpperInvariant());

        Assert.AreEqual(YmsgStatus.LoginError, reply.Status, "Password comparison must be case sensitive");
    }

    // Junk in both fields is exactly what the original probe sent, and what every test in this suite used to send.
    [TestMethod]
    [Timeout(15000)]
    public async Task Login_JunkCryptResponse_IsRefused()
    {
        var reply = await AttemptLoginRaw("alice", "x", "y");

        Assert.AreEqual(YmsgStatus.LoginError, reply.Status);
    }

    // Omitting the response entirely must not be read as "nothing to check, therefore fine".
    [TestMethod]
    [Timeout(15000)]
    public async Task Login_MissingCryptResponseFields_IsRefused()
    {
        Assert.AreEqual(YmsgStatus.LoginError, (await AttemptLoginRaw("alice", null, null)).Status);
        Assert.AreEqual(YmsgStatus.LoginError, (await AttemptLoginRaw("alice", "", "")).Status);
    }

    // Only one of the two responses being right must not be enough.
    [TestMethod]
    [Timeout(15000)]
    public async Task Login_OnlyOneResponseFieldCorrect_IsRefused()
    {
        var server = new YmsgServer(IPAddress.Loopback, 0);

        using var conn = new YmsgConn(server);

        await conn.SendAsync(new YmsgPacket(YmsgService.Verify, 0, 0));
        await conn.ReadAsync();

        await conn.SendAsync(new YmsgPacket(YmsgService.Auth, 0, 0).Add(1, "alice"));

        var seed = (await conn.ReadAsync()).Get(94);

        var correct = YmsgCrypt.ComputeAuthResponse(seed, YmsgTestEnv.Password);

        await conn.SendAsync(new YmsgPacket(YmsgService.AuthResp, 0, 0).Add(0, "alice").Add(6, correct.Resp6).Add(96, "wrong"));

        var reply = await conn.ReadAsync();

        Assert.AreEqual(YmsgStatus.LoginError, reply.Status, "field 96 must be checked, not just field 6");
    }

    // A response valid for one challenge must not be replayable against a different one.
    [TestMethod]
    [Timeout(15000)]
    public async Task Login_ResponseFromADifferentChallenge_IsRefused()
    {
        var otherSeed = YmsgCrypt.MakeChallenge();

        var stale = YmsgCrypt.ComputeAuthResponse(otherSeed, YmsgTestEnv.Password);

        var reply = await AttemptLoginRaw("alice", stale.Resp6, stale.Resp96);

        Assert.AreEqual(YmsgStatus.LoginError, reply.Status, "A response computed for another challenge must not authenticate");
    }

    // Fox's report asked for this to stay exactly as it was, so it is pinned rather than left implicit.
    [TestMethod]
    [Timeout(15000)]
    public async Task Login_UnknownUser_StillRefusedWithBadUsernameCode()
    {
        var reply = await AttemptLoginRaw("nobody-at-all", "x", "y");

        Assert.AreEqual(YmsgService.AuthResp, reply.Service);
        Assert.AreEqual(YmsgStatus.LoginError, reply.Status);
        Assert.AreEqual("3", reply.Get(66), "Unknown account must keep reporting 3 (bad username)");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Login_BadPassword_ReportsBadPasswordCode()
    {
        var reply = await AttemptLogin("alice", "TOTALLY-WRONG-PASSWORD");

        Assert.AreEqual("13", reply.Get(66), "A bad password should not masquerade as a bad username");
    }

    // The old challenge was "c={sessionId:X8}$vintagehive$", which a real client cannot parse at all - it would
    // spin forever on the '=' because its parser does not advance on unknown characters. Now that the response is
    // actually verified, an unusable challenge would lock every real client out, so the format is pinned.
    [TestMethod]
    public void MakeChallenge_IsProcessableByARealClient()
    {
        for (var i = 0; i < 25; i++)
        {
            var seed = YmsgServer.MakeChallenge();

            Assert.IsFalse(string.IsNullOrEmpty(seed));

            foreach (var c in seed)
            {
                // Parentheses are the one thing outside the lookup alphabets a client accepts: its parser skips
                // them explicitly, and real server seeds carried them to look like arithmetic.
                Assert.IsTrue(
                    YmsgCrypt.ChallengeLookup.Contains(c) || YmsgCrypt.OperandLookup.Contains(c) || c == '(' || c == ')',
                    $"Challenge contains '{c}', which is outside both client lookup alphabets and would hang the client");
            }

            Assert.IsNotNull(YmsgCrypt.PrepareChallenge(seed), "Every issued challenge must be derivable by a client");
        }
    }

    [TestMethod]
    public void MakeChallenge_IsDifferentEachTime()
    {
        var seeds = new HashSet<string>();

        for (var i = 0; i < 25; i++)
        {
            seeds.Add(YmsgServer.MakeChallenge());
        }

        // The old implementation derived the seed from the session id alone. A per-connection random challenge is
        // what stops a captured response from being replayed against a later session.
        Assert.IsTrue(seeds.Count > 20, $"Challenges should be unpredictable per connection, got {seeds.Count} distinct out of 25");
    }
}
