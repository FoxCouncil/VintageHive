// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// The notification loop used to route SYN, CHG and XFR with no check that USR had ever succeeded. SYN is the
// damaging one: HandleSynAsync answers with OtherAccounts(session.Account), and on an unauthenticated session
// that account is still null, so the "everyone except me" filter excludes nobody and the reply is an LST line
// naming every registered member. VER then SYN, with no password anywhere, enumerated the whole hive.

using System.Net;
using Msn;
using VintageHive.Proxy.Msn;

namespace Adversarial5.MsnAuthGate;

[TestClass]
public class MsnAuthGateTests
{
    private static MsnServer NewServer() => new(IPAddress.Loopback, 0);

    // Walks the version handshake only, deliberately stopping short of USR, so the session is live but has
    // never proved anything.
    private static async Task OpenUnauthenticatedAsync(MsnConn conn)
    {
        await conn.SendAsync("VER 1 MSNP7 MSNP6 CVR0");

        var ver = await conn.ReadLineAsync();

        Assert.IsTrue(ver.StartsWith("VER 1 MSNP7"), $"Unexpected VER reply: {ver}");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task AnUnauthenticatedSession_CannotSynTheContactList()
    {
        MsnTestEnv.Ensure();

        using var conn = new MsnConn(NewServer());

        await OpenUnauthenticatedAsync(conn);

        await conn.SendAsync("SYN 2");

        var reply = await conn.ReadLineAsync();

        StringAssert.StartsWith(reply, "911", $"SYN was answered on an unauthenticated session with '{reply}'.");
        Assert.IsFalse(reply.Contains("LST"), "The contact list was served to a session that never signed on.");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task AnUnauthenticatedSession_CannotChangePresence()
    {
        MsnTestEnv.Ensure();

        using var conn = new MsnConn(NewServer());

        await OpenUnauthenticatedAsync(conn);

        await conn.SendAsync("CHG 2 NLN");

        StringAssert.StartsWith(await conn.ReadLineAsync(), "911", "CHG was accepted before sign-on, so an anonymous socket can publish presence.");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task AnUnauthenticatedSession_CannotRequestASwitchboard()
    {
        MsnTestEnv.Ensure();

        using var conn = new MsnConn(NewServer());

        await OpenUnauthenticatedAsync(conn);

        await conn.SendAsync("XFR 2 SB");

        StringAssert.StartsWith(await conn.ReadLineAsync(), "911", "XFR issued a switchboard ticket to a session that never signed on.");
    }

    // The gate must not break the thing it guards. After a real MD5 login SYN still has to answer with the
    // four contact lists, or every client sits forever waiting for contact sync to finish.
    [TestMethod]
    [Timeout(20000)]
    public async Task AnAuthenticatedSession_StillGetsItsContactLists()
    {
        MsnTestEnv.Ensure();

        using var conn = new MsnConn(NewServer());

        await conn.LoginAsync("alice");

        await conn.SendAsync("SYN 5");

        StringAssert.StartsWith(await conn.ReadLineAsync(), "SYN 5", "SYN stopped working for a properly signed-on client.");

        // GTC and BLP precede the lists; then FL must arrive.
        StringAssert.StartsWith(await conn.ReadLineAsync(), "GTC 5");
        StringAssert.StartsWith(await conn.ReadLineAsync(), "BLP 5");
        StringAssert.StartsWith(await conn.ReadLineAsync(), "LST 5 FL");
    }
}
