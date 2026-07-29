// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Processors;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Yahoo.Pager;
using VintageHive.Utilities;

namespace Yahoo;

// Drives YahooPagerProcessor through the real HttpProxy pipeline, so the assertions cover the wire response a
// period client would actually read - headers, status line and all - rather than the processor's return value.
internal static class PagerEnv
{
    public const string BuiltIn404Marker = "The following request was not found/handled";

    public static async Task<string> Send(string method, string url, string body = null)
    {
        Http.HttpErrorPageEnv.EnsureContexts();
        YmsgTestEnv.Ensure();

        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            using var client = new TcpClient();

            await client.ConnectAsync(IPAddress.Loopback, port);

            using var serverSocket = await listener.AcceptSocketAsync();

            var connection = new ListenerSocket
            {
                RawSocket = serverSocket,
                Stream = new NetworkStream(serverSocket),
            };

            var proxy = new HttpProxy(IPAddress.Loopback, 0, false);

            proxy.Use(YahooPagerProcessor.ProcessHttpRequest);

            var uri = new Uri(url);

            var request = new StringBuilder();

            request.Append($"{method} {uri.PathAndQuery} HTTP/1.1\r\n");
            request.Append($"Host: {uri.Host}\r\n");
            request.Append("User-Agent: Mozilla/4.01 [en] (Win95; I)\r\n");

            if (Cookie != null)
            {
                request.Append($"Cookie: {Cookie}\r\n");
            }

            if (body != null)
            {
                request.Append($"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n");
            }

            request.Append("\r\n");

            if (body != null)
            {
                request.Append(body);
            }

            var raw = Encoding.UTF8.GetBytes(request.ToString());

            var result = await proxy.ProcessRequest(connection, raw, raw.Length);

            return result == null ? string.Empty : Encoding.UTF8.GetString(result);
        }
        finally
        {
            listener.Stop();
        }
    }

    // Cookie to send on the next request, mirroring what a client would echo back after sign-on.
    public static string Cookie { get; set; }

    public static Task<string> Get(string url) => Send("GET", url);

    // Every URL is unique per run: the HTTP proxy cache is real on-disk SQLite that outlives the process, and
    // ProcessRequest consults it before any handler runs.
    public static string LoginUrl(string user, string password)
    {
        return $"http://{PagerHosts.Auth}/config/ncclogin?.src=bl&login={Uri.EscapeDataString(user)}&passwd={Uri.EscapeDataString(password)}&n=1&t=1&nonce={Guid.NewGuid():N}";
    }

    public static string SetCookieValue(string response)
    {
        var match = Regex.Match(response, @"^Set-Cookie:\s*(.+?)\r?$", RegexOptions.Multiline);

        return match.Success ? match.Groups[1].Value : null;
    }

    public static string HeaderValue(string response, string name)
    {
        var match = Regex.Match(response, $@"^{Regex.Escape(name)}:\s*(.+?)\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    public static string BodyOf(string response)
    {
        var split = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        return split < 0 ? string.Empty : response[(split + 4)..];
    }
}

// Sign-on: GET /config/ncclogin?.src=bl&login=<user>&passwd=<plaintext>&n=1&t=1
//
// Response shape is taken from libyahoo 0.18.4 (protocol.txt plus yahoo_fetchcookies / yahoo_get_config), the
// GPL client library descended from the yppro2.c prototype pager code, which targets this exact protocol
// generation. Nothing here is inferred from the request-side capture.
[TestClass]
public class YahooPagerLoginTests
{
    [TestInitialize]
    public void Setup()
    {
        Http.HttpErrorPageEnv.EnsureContexts();
        YmsgTestEnv.Ensure();

        PagerEnv.Cookie = null;

        PagerLoginTokens.Clear();

        Mind.Db!.ConfigSet(ConfigNames.ServiceYahooPager, true);
    }

    [TestMethod]
    public async Task CorrectPassword_MintsASessionCookieAndReturnsTheAccountBlock()
    {
        var response = await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password));

        StringAssert.Contains(response, "200 OK");

        var cookie = PagerEnv.SetCookieValue(response);

        Assert.IsNotNull(cookie, "Sign-on did not set the Y cookie the client uses as its session handle.");
        StringAssert.StartsWith(cookie, "Y=v=1&n=", "The cookie is not in the shape the reference client parses.");
        StringAssert.Contains(cookie, "domain=.yahoo.com");

        var body = PagerEnv.BodyOf(response);

        StringAssert.StartsWith(body, "OK", "The reference client's first expectation is an OK line.");
        StringAssert.Contains(body, "BEGIN BUDDYLIST");
        StringAssert.Contains(body, "END BUDDYLIST");
        StringAssert.Contains(body, "BEGIN IGNORELIST");
        StringAssert.Contains(body, "END IGNORELIST");
        StringAssert.Contains(body, "BEGIN IDENTITIES");
        StringAssert.Contains(body, "END IDENTITIES");
        StringAssert.Contains(body, "Mail=0");
        StringAssert.Contains(body, "Login=alice");
    }

    // The n= sub-value is the only load-bearing part of the cookie: the client extracts it and sends it as its
    // session handle on everything afterwards, so it has to resolve back to the account that signed on.
    [TestMethod]
    public async Task TheMintedTokenResolvesToTheAccountThatSignedOn()
    {
        var response = await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password));

        var token = Regex.Match(PagerEnv.SetCookieValue(response), @"[?&]?n=([a-z0-9]+)").Groups[1].Value;

        Assert.AreEqual(13, token.Length, "The token is not the length period cookies use.");
        Assert.IsTrue(Regex.IsMatch(token, "^[a-z0-9]+$"), "The token left the lowercase alphanumeric character class period clients expect.");
        Assert.AreEqual("alice", PagerLoginTokens.Resolve(token));
    }

    [TestMethod]
    public async Task TheRosterIsServerBuiltAndExcludesTheCallersOwnAccount()
    {
        var body = PagerEnv.BodyOf(await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password)));

        StringAssert.Contains(body, "Hive:", "The buddy list is not filed under the hive roster group.");
        StringAssert.Contains(body, "bob");
        Assert.IsFalse(Regex.IsMatch(body, @"^Hive:.*\balice\b", RegexOptions.Multiline), "The signing-in account listed itself as its own buddy.");
    }

    // The load-bearing refusal. Everything below guards a way this could regress into letting someone in.
    [TestMethod]
    public async Task WrongPassword_IsRefused()
    {
        var response = await PagerEnv.Get(PagerEnv.LoginUrl("alice", "TOTALLY-WRONG-PASSWORD"));

        StringAssert.Contains(PagerEnv.BodyOf(response), "ERROR: Invalid NCC Login");
        Assert.IsNull(PagerEnv.SetCookieValue(response), "A refused sign-on still handed out a session cookie.");
    }

    [TestMethod]
    public async Task UnknownAccount_IsRefused()
    {
        var response = await PagerEnv.Get(PagerEnv.LoginUrl("nobody-at-all", "anything"));

        StringAssert.Contains(PagerEnv.BodyOf(response), "ERROR: Invalid NCC Login");
        Assert.IsNull(PagerEnv.SetCookieValue(response));
    }

    // HiveDbContext.UserFetch drops its password clause entirely when handed an empty string, so a verifier
    // written as UserFetch(login, passwd) would match on username alone and let anyone in with no password at
    // all. The compare is done in code for exactly this reason; this pins it.
    [TestMethod]
    public async Task EmptyOrMissingPassword_IsRefused()
    {
        var empty = await PagerEnv.Get(PagerEnv.LoginUrl("alice", string.Empty));

        StringAssert.Contains(PagerEnv.BodyOf(empty), "ERROR: Invalid NCC Login", "An empty password signed in.");
        Assert.IsNull(PagerEnv.SetCookieValue(empty));

        var missing = await PagerEnv.Get($"http://{PagerHosts.Auth}/config/ncclogin?.src=bl&login=alice&n=1&nonce={Guid.NewGuid():N}");

        StringAssert.Contains(PagerEnv.BodyOf(missing), "ERROR: Invalid NCC Login", "A sign-on with no passwd parameter at all signed in.");
        Assert.IsNull(PagerEnv.SetCookieValue(missing));
    }

    // Catches a comparison that only checks length, or one that got case-folded on its way through SQL.
    [TestMethod]
    public async Task NearMissPasswords_AreRefused()
    {
        foreach (var attempt in new[] { YmsgTestEnv.Password + "x", YmsgTestEnv.Password.ToUpperInvariant(), YmsgTestEnv.Password[..^1] })
        {
            var response = await PagerEnv.Get(PagerEnv.LoginUrl("alice", attempt));

            StringAssert.Contains(PagerEnv.BodyOf(response), "ERROR: Invalid NCC Login", $"'{attempt}' was accepted as alice's password.");
        }
    }

    // A refused attempt must not disturb a session that is already signed on: a wrong password typed by
    // someone else must not be able to sign the real member out.
    [TestMethod]
    public async Task ARefusedSignOn_DoesNotRevokeALiveSession()
    {
        var good = await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password));

        var token = Regex.Match(PagerEnv.SetCookieValue(good), @"[?&]?n=([a-z0-9]+)").Groups[1].Value;

        await PagerEnv.Get(PagerEnv.LoginUrl("alice", "TOTALLY-WRONG-PASSWORD"));

        Assert.AreEqual("alice", PagerLoginTokens.Resolve(token), "A failed sign-on attempt signed the real member out.");
    }

    // One identity, one live session - the same rule the session registry enforces for YMSG.
    [TestMethod]
    public async Task AFreshSignOn_InvalidatesTheAccountsPreviousToken()
    {
        var first = await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password));

        var firstToken = Regex.Match(PagerEnv.SetCookieValue(first), @"[?&]?n=([a-z0-9]+)").Groups[1].Value;

        var second = await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password));

        var secondToken = Regex.Match(PagerEnv.SetCookieValue(second), @"[?&]?n=([a-z0-9]+)").Groups[1].Value;

        Assert.AreNotEqual(firstToken, secondToken, "Two sign-ons produced the same session handle.");
        Assert.IsNull(PagerLoginTokens.Resolve(firstToken), "The superseded sign-on's token is still usable.");
        Assert.AreEqual("alice", PagerLoginTokens.Resolve(secondToken));
    }

    // The whole reason commit 1 exists. This request carries a plaintext password in its query string.
    [TestMethod]
    public async Task TheSignOnUrl_NeverReachesTheLogOrTheCache()
    {
        var password = "pw-" + Guid.NewGuid().ToString("N");

        var url = PagerEnv.LoginUrl("alice", password);

        await PagerEnv.Get(url);

        Assert.IsFalse(Mind.Db!.GetLogItems(1, 200).Any(x => x.Message != null && x.Message.Contains(password)), "The Pager sign-on wrote a plaintext password to the log.");
        Assert.IsNull(Mind.Cache.GetHttpProxy($"HPC-GET-{url}"), "The Pager sign-on response was cached under a key containing the password.");
    }

    [TestMethod]
    public async Task GetBuddyList_RequiresTheSessionCookie()
    {
        var url = $"http://{PagerHosts.Auth}/config/get_buddylist?.src=bl&nonce={Guid.NewGuid():N}";

        PagerEnv.Cookie = null;

        StringAssert.Contains(PagerEnv.BodyOf(await PagerEnv.Get(url)), "ERROR: Invalid NCC Login", "An unauthenticated buddy list fetch was served.");

        PagerEnv.Cookie = "Y=v=1&n=totallybogus&l=x&p=y";

        StringAssert.Contains(PagerEnv.BodyOf(await PagerEnv.Get(url)), "ERROR: Invalid NCC Login", "A bogus session token was accepted.");
    }

    [TestMethod]
    public async Task GetBuddyList_WithTheSessionCookie_ReturnsTheSameAccountBlock()
    {
        var login = await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password));

        // Echo the cookie back the way the client would, minus the attributes.
        PagerEnv.Cookie = PagerEnv.SetCookieValue(login).Split(';')[0];

        var body = PagerEnv.BodyOf(await PagerEnv.Get($"http://{PagerHosts.Auth}/config/get_buddylist?.src=bl&nonce={Guid.NewGuid():N}"));

        StringAssert.StartsWith(body, "OK");
        StringAssert.Contains(body, "Login=alice");
    }

    [TestMethod]
    public async Task AnUnknownPathOnTheAuthHost_IsAnsweredHereNotPassedDownstream()
    {
        var response = await PagerEnv.Get($"http://{PagerHosts.Auth}/config/something-else?nonce={Guid.NewGuid():N}");

        StringAssert.Contains(response, "404 NotFound");
        Assert.IsFalse(response.Contains("ERROR: Invalid NCC Login"));
    }

    // Turning the service off has to hand the host back to the rest of the chain, not answer it half way.
    [TestMethod]
    public async Task WithTheServiceDisabled_TheHostFallsThroughToTheRestOfTheChain()
    {
        Mind.Db!.ConfigSet(ConfigNames.ServiceYahooPager, false);

        try
        {
            var response = await PagerEnv.Get(PagerEnv.LoginUrl("alice", YmsgTestEnv.Password));

            StringAssert.Contains(response, PagerEnv.BuiltIn404Marker, "A disabled service still answered the sign-on.");
            Assert.IsNull(PagerEnv.SetCookieValue(response));
        }
        finally
        {
            Mind.Db!.ConfigSet(ConfigNames.ServiceYahooPager, true);
        }
    }

    [TestMethod]
    public async Task AHostThatIsNotThePagers_IsLeftAlone()
    {
        var response = await PagerEnv.Get($"http://www.geocities.com/config/ncclogin?login=alice&passwd={YmsgTestEnv.Password}&nonce={Guid.NewGuid():N}");

        StringAssert.Contains(response, PagerEnv.BuiltIn404Marker, "The processor claimed a host that is not the Pager's.");
    }
}

// The version check. Deliberately inert: no reference for this format exists (libyahoo never implemented the
// update check, and every Wayback capture of that host returned its robots.txt to the crawler), so these pin
// the properties that make an unknown format safe rather than pretending to know the format.
[TestClass]
public class YahooPagerUpdateCheckTests
{
    // Unique per run. The HTTP proxy cache is real on-disk SQLite that outlives the process and is consulted
    // before any handler executes, so a fixed URL would let every later run assert against a cached response
    // and pass even if the processor stopped answering. A query string does not change AbsolutePath, which is
    // what the handler routes on.
    static string ClientsUrl() => $"http://{PagerHosts.Update}/clients.html?nonce={Guid.NewGuid():N}";

    [TestInitialize]
    public void Setup()
    {
        Http.HttpErrorPageEnv.EnsureContexts();
        YmsgTestEnv.Ensure();

        PagerEnv.Cookie = null;

        Mind.Db!.ConfigSet(ConfigNames.ServiceYahooPager, true);
    }

    [TestMethod]
    public async Task Get_IsAnsweredSuccessfully()
    {
        var response = await PagerEnv.Send("GET", ClientsUrl());

        StringAssert.Contains(response, "200 OK");
        StringAssert.Contains(response, "text/html");
        Assert.IsFalse(response.Contains(PagerEnv.BuiltIn404Marker));
    }

    // The client HEADs before it GETs. A HEAD that reports a different size than the GET would deliver is the
    // classic way to make an update checker think the resource changed.
    [TestMethod]
    public async Task Head_MatchesTheGetsMetadataAndCarriesNoBody()
    {
        var head = await PagerEnv.Send("HEAD", ClientsUrl());
        var get = await PagerEnv.Send("GET", ClientsUrl());

        StringAssert.Contains(head, "200 OK");

        Assert.AreEqual(PagerEnv.HeaderValue(get, "Content-Length"), PagerEnv.HeaderValue(head, "Content-Length"), "HEAD advertised a different length than GET delivers.");
        Assert.AreEqual(PagerEnv.HeaderValue(get, "ETag"), PagerEnv.HeaderValue(head, "ETag"));
        Assert.AreEqual(PagerEnv.HeaderValue(get, "Last-Modified"), PagerEnv.HeaderValue(head, "Last-Modified"));

        Assert.AreEqual(string.Empty, PagerEnv.BodyOf(head), "A HEAD response carried a body, which desynchronises a keep-alive connection.");
        Assert.AreNotEqual(string.Empty, PagerEnv.BodyOf(get), "The GET returned nothing, so the comparison above proves nothing.");
    }

    // The resource must look unchanged forever, or a checker that diffs on Last-Modified sees churn.
    [TestMethod]
    public async Task TheResourceNeverAppearsToChange()
    {
        var first = await PagerEnv.Send("GET", ClientsUrl());

        var second = await PagerEnv.Send("GET", ClientsUrl());

        Assert.AreEqual(PagerEnv.HeaderValue(first, "Last-Modified"), PagerEnv.HeaderValue(second, "Last-Modified"));
        Assert.AreEqual(PagerEnv.BodyOf(first), PagerEnv.BodyOf(second));

        Assert.IsFalse(PagerEnv.HeaderValue(first, "Last-Modified").Contains(DateTime.UtcNow.Year.ToString()), "Last-Modified is being stamped from the clock, so the resource looks freshly changed on every check.");
    }

    [TestMethod]
    public async Task AnUnknownPathOnTheUpdateHost_IsAnsweredHereNotPassedDownstream()
    {
        var response = await PagerEnv.Send("GET", $"http://{PagerHosts.Update}/pgdownload/ymsgr.exe?nonce={Guid.NewGuid():N}");

        StringAssert.Contains(response, "404 NotFound");
    }
}

// The transport is not implemented yet: it waits on a capture with request bodies, since nothing in the
// reference pins which wire dialect this client build speaks. What IS pinned is that the request stops here
// rather than being sent outward to the archive.
[TestClass]
public class YahooPagerTransportTests
{
    [TestInitialize]
    public void Setup()
    {
        Http.HttpErrorPageEnv.EnsureContexts();
        YmsgTestEnv.Ensure();

        PagerEnv.Cookie = null;

        Mind.Db!.ConfigSet(ConfigNames.ServiceYahooPager, true);
    }

    [TestMethod]
    public async Task NotifyPost_IsAnsweredLocallyAndNotForwarded()
    {
        var response = await PagerEnv.Send("POST", $"http://{PagerHosts.Notify}/notify/", "YPNS2.0");

        StringAssert.Contains(response, "503 ServiceUnavailable");
        Assert.IsFalse(response.Contains(PagerEnv.BuiltIn404Marker), "The transport request fell through to the rest of the chain.");
    }

    // libyahoo targets http.pager.yahoo.com for the same service; the captured client uses
    // http.messenger.yahoo.com. Both are claimed so neither leaks outward.
    [TestMethod]
    public async Task TheAlternateTransportHostname_IsClaimedToo()
    {
        var response = await PagerEnv.Send("POST", $"http://{PagerHosts.NotifyAlternate}/notify", "YPNS2.0");

        StringAssert.Contains(response, "503 ServiceUnavailable");
    }
}

[TestClass]
public class PagerLoginTokenTests
{
    [TestInitialize]
    public void Setup()
    {
        PagerLoginTokens.Clear();
    }

    [TestMethod]
    public void MintedTokensAreUniqueAndInThePeriodCharacterClass()
    {
        var seen = new HashSet<string>();

        for (var i = 0; i < 200; i++)
        {
            var token = PagerLoginTokens.Mint("alice");

            Assert.IsTrue(seen.Add(token), "A minted token repeated, so one member could be handed another's session.");
            Assert.IsTrue(Regex.IsMatch(token, "^[a-z0-9]{13}$"), $"Token '{token}' is outside the character class period clients expect.");
        }
    }

    [TestMethod]
    public void ResolveRejectsUnknownAndRevokedTokens()
    {
        var token = PagerLoginTokens.Mint("alice");

        Assert.AreEqual("alice", PagerLoginTokens.Resolve(token));

        Assert.IsNull(PagerLoginTokens.Resolve("nosuchtokenx"));
        Assert.IsNull(PagerLoginTokens.Resolve(null));
        Assert.IsNull(PagerLoginTokens.Resolve(string.Empty));

        PagerLoginTokens.Revoke(token);

        Assert.IsNull(PagerLoginTokens.Resolve(token), "A revoked token still resolved to its account.");
    }

    [TestMethod]
    public void RevokeAllForDropsEveryTokenForOneAccountAndLeavesOthersAlone()
    {
        var aliceOne = PagerLoginTokens.Mint("alice");
        var aliceTwo = PagerLoginTokens.Mint("ALICE");
        var bob = PagerLoginTokens.Mint("bob");

        PagerLoginTokens.RevokeAllFor("alice");

        Assert.IsNull(PagerLoginTokens.Resolve(aliceOne));
        Assert.IsNull(PagerLoginTokens.Resolve(aliceTwo), "Token matching must be case insensitive on the account, like every other username check.");
        Assert.AreEqual("bob", PagerLoginTokens.Resolve(bob), "Revoking one account's sessions signed another member out.");
    }
}
