// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// GetResponseEncodedData built every header as "{key}: {value}\r\n" with no sanitisation at all, and the
// api.hive.com/image/fetch route feeds it a client-supplied value: the fburl query parameter goes to
// SetFound -> SetLocation -> the Location header, and HttpUtility.ParseQueryString has already URL-decoded
// it, so %0d%0a arrives as a real CR LF. That let any LAN client end the Location header early and write
// whatever it wanted after it - extra headers, or a whole body - which is response splitting.
//
// The fix sanitises at serialisation rather than at the one known call site, so these drive the real proxy
// and assert on the actual bytes that reach the wire.
//
// Note these assert STRUCTURALLY - that the payload never becomes its own header line or its own body - not
// that its text is absent. Flattening is the intended outcome: the value survives with the framing
// characters removed, so "Set-Cookie: pwned=1" quite correctly still appears as part of the Location value.
// A substring assertion here would fail against a perfectly good fix.

using Http;
using VintageHive.Proxy.Http;

namespace Adversarial5.HttpHeaderInjection;

[TestClass]
public class HttpHeaderInjectionTests
{
    private static (string[] HeaderLines, string Body) Split(string wire)
    {
        var separator = wire.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        Assert.IsTrue(separator >= 0, $"The response had no header/body separator at all:\n{wire}");

        var headerLines = wire[..separator].Split("\r\n");
        var body = wire[(separator + 4)..];

        return (headerLines, body);
    }

    private static void AssertNoHeaderNamed(string[] headerLines, string name, string wire)
    {
        foreach (var line in headerLines)
        {
            var colon = line.IndexOf(':');

            if (colon <= 0)
            {
                continue;
            }

            Assert.IsFalse(string.Equals(line[..colon].Trim(), name, StringComparison.OrdinalIgnoreCase), $"'{name}' arrived as its own header line, so the response was split:\n{wire}");
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task ACrlfInARedirectTarget_CannotInjectAnotherHeader()
    {
        var wire = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("injection.test"), (request, response) =>
        {
            response.SetFound("http://example.com/\r\nSet-Cookie: pwned=1");

            return Task.FromResult(true);
        });

        var (headerLines, _) = Split(wire);

        AssertNoHeaderNamed(headerLines, "Set-Cookie", wire);

        // The redirect must still happen - sanitising is not the same as dropping the header.
        Assert.IsTrue(headerLines.Any(x => x.StartsWith("Location:", StringComparison.OrdinalIgnoreCase) && x.Contains("example.com")), $"The Location header lost its value entirely rather than being flattened:\n{wire}");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task ACrlfCrlfInARedirectTarget_CannotInjectABody()
    {
        var wire = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("injection.test"), (request, response) =>
        {
            response.SetFound("http://example.com/\r\n\r\n<html>attacker page</html>");

            return Task.FromResult(true);
        });

        var (_, body) = Split(wire);

        Assert.IsFalse(body.Contains("<html>attacker page</html>"), $"A double CR LF in the redirect target split the response and became the body:\n{wire}");
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task ACrlfInAnArbitraryHeaderValue_IsFlattened()
    {
        var wire = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("injection.test"), (request, response) =>
        {
            response.Headers.AddOrUpdate("X-Test", "safe\r\nX-Injected: yes");

            response.SetBodyString("ok");

            return Task.FromResult(true);
        });

        var (headerLines, body) = Split(wire);

        AssertNoHeaderNamed(headerLines, "X-Injected", wire);

        Assert.AreEqual("ok", body, $"The injected value disturbed the body:\n{wire}");
    }

    // The sanitiser must not damage what is legitimately allowed in a field value, or it would mangle ordinary
    // headers on every response. SP and HTAB are both legal inside field content.
    [TestMethod]
    public void SanitisingAHeaderField_KeepsEverythingLegal()
    {
        Assert.AreEqual("text/html; charset=utf-8", HttpResponse.SanitiseHeaderField("text/html; charset=utf-8"));
        Assert.AreEqual("a\tb", HttpResponse.SanitiseHeaderField("a\tb"), "HTAB is legal inside a field value and was stripped.");
        Assert.AreEqual("plain value", HttpResponse.SanitiseHeaderField("plain value"));
        Assert.IsNull(HttpResponse.SanitiseHeaderField(null));
        Assert.AreEqual(string.Empty, HttpResponse.SanitiseHeaderField(string.Empty));
    }

    [TestMethod]
    public void SanitisingAHeaderField_RemovesEveryFramingCharacter()
    {
        Assert.AreEqual("abcd", HttpResponse.SanitiseHeaderField("ab\r\ncd"));
        Assert.AreEqual("abcd", HttpResponse.SanitiseHeaderField("ab\rcd"));
        Assert.AreEqual("abcd", HttpResponse.SanitiseHeaderField("ab\ncd"));
        Assert.AreEqual("abcd", HttpResponse.SanitiseHeaderField("ab\0cd"));
        Assert.AreEqual("abcd", HttpResponse.SanitiseHeaderField("abcd"));
        Assert.AreEqual("abcd", HttpResponse.SanitiseHeaderField("abcd"));
    }
}
