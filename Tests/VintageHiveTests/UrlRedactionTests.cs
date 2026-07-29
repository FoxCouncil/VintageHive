// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive;
using VintageHive.Utilities;

namespace Http;

// Period clients sign in over plain HTTP GETs with the password in the query string. The pre-YMSG Yahoo Pager
// is the live example:
//
//   GET http://msg.edit.yahoo.com/config/ncclogin?.src=bl&login=<user>&passwd=<plaintext>&n=1&t=1
//
// Every URL VintageHive tracks lands in durable places - the requests table, the logs table, vfs/data/vintagehive.log,
// the rendered error page - so without scrubbing, one sign-on writes a member's password to disk in four of them.
[TestClass]
public class UrlRedactorTests
{
    const string NccLogin = "http://msg.edit.yahoo.com/config/ncclogin?.src=bl&login=alice&passwd=hunter2&n=1&t=1";

    [TestMethod]
    public void Redact_NccLoginUrl_RemovesPasswordAndKeepsEverythingElse()
    {
        var redacted = UrlRedactor.Redact(NccLogin);

        Assert.IsFalse(redacted.Contains("hunter2"), $"The password survived redaction: {redacted}");
        StringAssert.Contains(redacted, $"passwd={UrlRedactor.Placeholder}");

        // The rest of the request is the diagnostic value of tracking it at all, so it must come through intact.
        StringAssert.Contains(redacted, "msg.edit.yahoo.com/config/ncclogin");
        StringAssert.Contains(redacted, "login=alice");
        StringAssert.Contains(redacted, ".src=bl");
        StringAssert.Contains(redacted, "n=1");
        StringAssert.Contains(redacted, "t=1");
    }

    [TestMethod]
    public void Redact_CoversTheOtherPasswordParameterSpellings()
    {
        StringAssert.Contains(UrlRedactor.Redact("http://h/x?password=s3cret"), $"password={UrlRedactor.Placeholder}");
        StringAssert.Contains(UrlRedactor.Redact("http://h/x?pwd=s3cret"), $"pwd={UrlRedactor.Placeholder}");

        Assert.IsFalse(UrlRedactor.Redact("http://h/x?password=s3cret").Contains("s3cret"));
        Assert.IsFalse(UrlRedactor.Redact("http://h/x?pwd=s3cret").Contains("s3cret"));
    }

    [TestMethod]
    public void Redact_IsCaseInsensitiveOnTheParameterName()
    {
        Assert.IsFalse(UrlRedactor.Redact("http://h/x?PASSWD=s3cret").Contains("s3cret"));
        Assert.IsFalse(UrlRedactor.Redact("http://h/x?PassWd=s3cret").Contains("s3cret"));
    }

    // The password can be the first parameter, the last one, or the only one. A rule anchored on '&' alone
    // would miss the '?' case entirely, which is the single most likely shape.
    [TestMethod]
    public void Redact_HandlesEveryPositionInTheQuery()
    {
        Assert.IsFalse(UrlRedactor.Redact("http://h/x?passwd=s3cret&login=alice").Contains("s3cret"), "first parameter");
        Assert.IsFalse(UrlRedactor.Redact("http://h/x?login=alice&passwd=s3cret").Contains("s3cret"), "last parameter");
        Assert.IsFalse(UrlRedactor.Redact("http://h/x?passwd=s3cret").Contains("s3cret"), "only parameter");
        Assert.IsFalse(UrlRedactor.Redact("http://h/x?a=1&passwd=s3cret&b=2").Contains("s3cret"), "middle parameter");
    }

    // HttpProxy logs a raw request line when it cannot parse the request at all, so the value class has to
    // stop at whitespace or the HTTP version token gets eaten along with the password.
    [TestMethod]
    public void Redact_WorksOnAWholeRequestLine_AndStopsAtWhitespace()
    {
        var redacted = UrlRedactor.Redact("GET http://msg.edit.yahoo.com/config/ncclogin?login=alice&passwd=s3cret HTTP/1.0");

        Assert.IsFalse(redacted.Contains("s3cret"));
        StringAssert.Contains(redacted, "HTTP/1.0", "The redaction ate past the end of the URL.");
        StringAssert.StartsWith(redacted, "GET http://");
    }

    [TestMethod]
    public void Redact_StopsAtAFragment()
    {
        var redacted = UrlRedactor.Redact("http://h/x?passwd=s3cret#anchor");

        Assert.IsFalse(redacted.Contains("s3cret"));
        StringAssert.Contains(redacted, "#anchor");
    }

    [TestMethod]
    public void Redact_EmptyValue_IsStillRewrittenAndDoesNotThrow()
    {
        StringAssert.Contains(UrlRedactor.Redact("http://h/x?passwd=&login=alice"), $"passwd={UrlRedactor.Placeholder}");
    }

    // The list is deliberately narrow, and it rewrites text a human later reads to debug a request. A parameter
    // that merely contains one of the names is left alone.
    [TestMethod]
    public void Redact_DoesNotFireOnNamesThatMerelyContainAPasswordWord()
    {
        Assert.AreEqual("http://h/x?oldpwd=keepme", UrlRedactor.Redact("http://h/x?oldpwd=keepme"));
        Assert.AreEqual("http://h/x?pwd_hint=keepme", UrlRedactor.Redact("http://h/x?pwd_hint=keepme"));
        Assert.AreEqual("http://h/passwd/index.html", UrlRedactor.Redact("http://h/passwd/index.html"));
        Assert.AreEqual("http://h/x?q=passwd", UrlRedactor.Redact("http://h/x?q=passwd"));
    }

    [TestMethod]
    public void Redact_LeavesOrdinaryUrlsByteIdentical()
    {
        const string url = "http://www.geocities.com/SiliconValley/1234/index.html?page=2&sort=date";

        Assert.AreEqual(url, UrlRedactor.Redact(url));
        Assert.AreEqual("http://h/no-query-at-all", UrlRedactor.Redact("http://h/no-query-at-all"));
        Assert.AreEqual(string.Empty, UrlRedactor.Redact(string.Empty));
        Assert.IsNull(UrlRedactor.Redact(null));
    }

    [TestMethod]
    public void Redact_IsIdempotent()
    {
        var once = UrlRedactor.Redact(NccLogin);

        Assert.AreEqual(once, UrlRedactor.Redact(once));
    }

    [TestMethod]
    public void ContainsCredentials_MatchesWhatRedactWouldRewrite()
    {
        Assert.IsTrue(UrlRedactor.ContainsCredentials(NccLogin));
        Assert.IsTrue(UrlRedactor.ContainsCredentials("http://h/x?PWD=s3cret"));

        Assert.IsFalse(UrlRedactor.ContainsCredentials("http://h/x?page=2"));
        Assert.IsFalse(UrlRedactor.ContainsCredentials("http://h/x?oldpwd=keepme"));
        Assert.IsFalse(UrlRedactor.ContainsCredentials("http://h/plain"));
        Assert.IsFalse(UrlRedactor.ContainsCredentials(null));
    }

    // This is the reason HttpProxy skips the cache for credential-bearing URLs instead of keying it on the
    // redacted form. Two members signing in to the same endpoint produce the SAME redacted string, so a cache
    // keyed on it would hand one of them the other's login response. Pinned so nobody "simplifies" it later.
    [TestMethod]
    public void Redact_CollapsesDistinctPasswordsToTheSameString()
    {
        var alice = UrlRedactor.Redact("http://msg.edit.yahoo.com/config/ncclogin?login=alice&passwd=aaa");
        var bob = UrlRedactor.Redact("http://msg.edit.yahoo.com/config/ncclogin?login=alice&passwd=bbb");

        Assert.AreEqual(alice, bob, "If this ever differs, the redacted form became safe to use as a cache key - but nothing depends on that.");
    }
}

// End to end over the real HttpProxy pipeline: the transform above is only worth anything if it sits in front
// of every sink the proxy writes to.
[TestClass]
public class HttpProxyCredentialHandlingTests
{
    static string LoginUrl(string password) => $"http://msg.edit.yahoo.com/config/ncclogin/{Guid.NewGuid()}?.src=bl&login=alice&passwd={password}&n=1";

    [TestMethod]
    public async Task LoginRequest_DoesNotWriteThePasswordToTheLog()
    {
        // Unique per run so a hit can only have come from this request, and distinctive enough that a substring
        // search over the log table cannot collide with anything else.
        var password = "pw-" + Guid.NewGuid().ToString("N");

        await HttpErrorPageEnv.Get(LoginUrl(password), (req, res) =>
        {
            res.SetBodyString("OK");

            return Task.FromResult(true);
        });

        var logged = Mind.Db.GetLogItems(1, 200);

        Assert.IsFalse(logged.Any(x => x.Message != null && x.Message.Contains(password)), "The plaintext password reached the log table.");
        Assert.IsTrue(logged.Any(x => x.Message != null && x.Message.Contains("ncclogin") && x.Message.Contains(UrlRedactor.Placeholder)), "The sign-on request was not tracked at all, so this test proves nothing.");
    }

    [TestMethod]
    public async Task LoginRequest_IsNotWrittenToTheResponseCache()
    {
        var url = LoginUrl("hunter2");

        await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            // Leaves Cache at its default true on purpose - the proxy must override it.
            res.SetBodyString("Set-Cookie payload");

            return Task.FromResult(true);
        });

        Assert.IsNull(Mind.Cache.GetHttpProxy($"HPC-GET-{url}"), "A credential-bearing URL was persisted into the cache database as a key.");
    }

    // Control for the test above, so the null assertion is not just measuring a cache that never stores anything.
    [TestMethod]
    public async Task SameUrlWithoutCredentials_IsCachedNormally()
    {
        var url = $"http://msg.edit.yahoo.com/config/ncclogin/{Guid.NewGuid()}?.src=bl&login=alice&n=1";

        await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            res.SetBodyString("ordinary response");

            return Task.FromResult(true);
        });

        Assert.IsNotNull(Mind.Cache.GetHttpProxy($"HPC-GET-{url}"), "Nothing is being cached here, so the credential cache assertion proves nothing.");
    }

    // The failure mode that made "just redact the cache key" wrong: two members hitting the same sign-on
    // endpoint would key to one entry, and the second would be served the first one's session cookie.
    [TestMethod]
    public async Task TwoMembersSigningIn_DoNotShareACachedResponse()
    {
        var path = $"http://msg.edit.yahoo.com/config/ncclogin/{Guid.NewGuid()}";

        var first = await HttpErrorPageEnv.Get($"{path}?login=alice&passwd=alice-secret", (req, res) =>
        {
            res.SetBodyString("SESSION-FOR-ALICE");

            return Task.FromResult(true);
        });

        StringAssert.Contains(first, "SESSION-FOR-ALICE");

        var second = await HttpErrorPageEnv.Get($"{path}?login=bob&passwd=bob-secret", (req, res) =>
        {
            res.SetBodyString("SESSION-FOR-BOB");

            return Task.FromResult(true);
        });

        StringAssert.Contains(second, "SESSION-FOR-BOB");
        Assert.IsFalse(second.Contains("SESSION-FOR-ALICE"), "The second member was served the first member's login response from cache.");
    }

    // The built-in error page echoes the request back, and it is the thing a member screenshots into a bug report.
    [TestMethod]
    public async Task ErrorPage_DoesNotEchoThePasswordBack()
    {
        var password = "pw-" + Guid.NewGuid().ToString("N");

        var response = await HttpErrorPageEnv.Get(LoginUrl(password), (req, res) => Task.FromResult(false));

        StringAssert.Contains(response, "404 NotFound");
        Assert.IsFalse(response.Contains(password), "The 404 page echoed the plaintext password back.");
        StringAssert.Contains(response, UrlRedactor.Placeholder);
    }
}
