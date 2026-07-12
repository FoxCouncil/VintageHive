// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System;
using System.Text;
using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace Adversarial.HttpParse;

// Adversarial coverage for HttpRequest.Parse. These feed the codec the kind of malformed request a hostile or
// broken client sends: junk request lines, colon-less / whitespace-keyed headers, degenerate cookies, absurd
// Content-Length values, integer overflow, control characters, non-ASCII, and empty buffers. The parser contract
// is that Parse must NEVER throw out of the handler - it returns HttpRequest.Invalid or degrades gracefully.
// Every assertion below records the parser's ACTUAL observed behavior, not an idealized spec. Cases that currently
// throw (multipart body parsing) are deliberately NOT asserted here; they are reported separately as bugs.
[TestClass]
public class HttpParseAdversarialTests
{
    // ASCII decode path - the normal transport for these vintage clients.
    static HttpRequest ParseAscii(string raw)
    {
        return HttpRequest.Parse(Encoding.ASCII.GetBytes(raw), raw, Encoding.ASCII);
    }

    static HttpRequest ParseUtf8(string raw)
    {
        return HttpRequest.Parse(Encoding.UTF8.GetBytes(raw), raw, Encoding.UTF8);
    }

    // ----- Request line -----------------------------------------------------------------------------------------

    [TestMethod]
    public void RequestLine_FourTokens_IsInvalid()
    {
        // Split(" ") yields 4 fields; the length != 3 guard rejects it rather than mis-indexing.
        var request = ParseAscii("GET / HTTP/1.0 extra\r\nHost: example.org\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void RequestLine_SingleToken_IsInvalid()
    {
        var request = ParseAscii("GET\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void RequestLine_DoubleSpace_IsInvalid()
    {
        // A stray double space splits into an empty middle token, tripping the 3-field guard.
        var request = ParseAscii("GET  http://example.org/ HTTP/1.0\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void RequestLine_TabDelimited_IsInvalid()
    {
        // Splitting is space-only, so a tab-separated line is one token and fails the guard.
        var request = ParseAscii("GET\thttp://example.org/\tHTTP/1.0\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void UnknownVerb_IsInvalid()
    {
        var request = ParseAscii("FOOBAR http://example.org/ HTTP/1.0\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void LowercaseVerb_IsInvalid()
    {
        // The verb allow-list is case-sensitive; "get" is not "GET".
        var request = ParseAscii("get http://example.org/ HTTP/1.0\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void UnknownVersion_IsInvalid()
    {
        // Only HTTP/1.0 and HTTP/1.1 are accepted; HTTP/2.0 is rejected.
        var request = ParseAscii("GET http://example.org/ HTTP/2.0\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void UriWithSchemeOnly_IsInvalid()
    {
        // "http" starts with "http" so no Host is prepended, and Uri.TryCreate(Absolute) rejects the bare scheme.
        var request = ParseAscii("GET http HTTP/1.0\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void BareRequestLine_NoTrailingCrlf_IsValid()
    {
        // A single absolute-form request line with no header terminator still parses: the whole buffer is the
        // header block, the one line is the request line, and the absolute URI needs no Host.
        var request = ParseAscii("GET http://example.org/ HTTP/1.0");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual("example.org", request.Host);
    }

    // ----- Headers ----------------------------------------------------------------------------------------------

    [TestMethod]
    public void ColonOnlyHeaderLine_IsSkipped_RequestStillValid()
    {
        // A line that is just ":" splits to an empty key; the empty-key guard drops it without corrupting state.
        var request = ParseAscii("GET http://example.org/ HTTP/1.0\r\n: value\r\nHost: example.org\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.IsFalse(request.Headers!.ContainsKey(""));
    }

    [TestMethod]
    public void EmptyHeaderValue_IsStoredAsEmptyString()
    {
        // "X-Foo:" with nothing after the colon stores an empty value rather than throwing.
        var request = ParseAscii("GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\nX-Foo:\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.IsTrue(request.Headers!.ContainsKey("X-Foo"));
        Assert.AreEqual("", request.Headers["X-Foo"]);
    }

    [TestMethod]
    public void DuplicateHeaderValues_AreCombinedWithComma()
    {
        // Repeated header names are folded into one comma-joined value per the HTTP list-header rule.
        var request = ParseAscii("GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\nX-Foo: a\r\nX-Foo: b\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual("a, b", request.Headers!["X-Foo"]);
    }

    [TestMethod]
    public void HostHeaderKeyWithTrailingSpace_BreaksHostLookup_IsInvalid()
    {
        // The header KEY is not trimmed (only the value is), so "Host " never matches the Host lookup that would
        // absolutize an origin-form URI. The request therefore stays relative and is rejected. Documents the
        // untrimmed-key behavior as a hostile-input hazard.
        var request = ParseAscii("GET / HTTP/1.0\r\nHost : example.org\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void HostHeaderKeyWithLeadingSpace_IsInvalid()
    {
        // Same untrimmed-key hazard from the other side: " Host" != "Host".
        var request = ParseAscii("GET / HTTP/1.0\r\n Host: example.org\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void HeaderValueWithEmbeddedNul_DoesNotThrow_IsValid()
    {
        // A NUL inside a header value is carried through the string decode without incident.
        var request = ParseAscii("GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\nX-Foo: a\0b\r\n\r\n");

        Assert.IsTrue(request.IsValid);
    }

    [TestMethod]
    public void HugeHeaderCount_DoesNotThrow_AndParses()
    {
        // 10,000 header lines must not blow the parser up or hang it (absolute URI so no Host needed).
        var sb = new StringBuilder();
        sb.Append("GET http://example.org/ HTTP/1.0\r\n");

        for (var i = 0; i < 10000; i++)
        {
            sb.Append("X-N").Append(i).Append(": v\r\n");
        }

        sb.Append("\r\n");

        var request = ParseAscii(sb.ToString());

        Assert.IsTrue(request.IsValid);
        Assert.IsTrue(request.Headers!.Count >= 10000);
    }

    // ----- Cookies ----------------------------------------------------------------------------------------------

    [TestMethod]
    public void Cookie_LeadingEqualsEmptyName_IsSkipped()
    {
        // "=value" has an empty name; the zero-length-name guard drops it, leaving no cookies.
        var request = ParseAscii("GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\nCookie: =value\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual(0, request.Cookies.Count);
    }

    [TestMethod]
    public void Cookie_MultipleEquals_ValueKeepsRemainder()
    {
        // Split("=", 2) means only the first '=' delimits; "a=b=c" yields value "b=c".
        var request = ParseAscii("GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\nCookie: a=b=c\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual(1, request.Cookies.Count);
        Assert.AreEqual("b=c", request.Cookies["a"]);
    }

    [TestMethod]
    public void Cookie_EmptyFragmentBetweenValidPairs_IsSkipped()
    {
        // "a=1; ; b=2" has an empty middle fragment; it is dropped and the two real pairs survive.
        var request = ParseAscii("GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\nCookie: a=1; ; b=2\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual(2, request.Cookies.Count);
        Assert.AreEqual("1", request.Cookies["a"]);
        Assert.AreEqual("2", request.Cookies["b"]);
    }

    // ----- URI / target -----------------------------------------------------------------------------------------

    [TestMethod]
    public void NulInUri_DoesNotThrow_IsValid()
    {
        // A NUL byte embedded in the request target does not crash Uri.TryCreate; the request parses.
        var request = ParseAscii("GET http://example.org/\0x HTTP/1.0\r\nHost: example.org\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual("example.org", request.Host);
    }

    [TestMethod]
    public void UnicodeHostHeader_Utf8_DoesNotThrow_IsValid()
    {
        // Non-ASCII in the Host header (decoded as UTF-8) is accepted without throwing.
        var request = ParseUtf8("GET / HTTP/1.0\r\nHost: exämple.org\r\n\r\n");

        Assert.IsTrue(request.IsValid);
    }

    [TestMethod]
    public void Connect_NonHttpsPort_UsesHttpScheme()
    {
        // CONNECT to a non-:443 target is schemed http:// (only :443 becomes https), and the port is preserved.
        var request = ParseAscii("CONNECT example.org:80 HTTP/1.0\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual("http", request.Uri.Scheme);
        Assert.AreEqual("example.org", request.Host);
        Assert.AreEqual(80, request.Uri.Port);
    }

    [TestMethod]
    public void Connect_EmptyTarget_IsInvalid()
    {
        // "CONNECT  HTTP/1.0" (double space, no target) fails the 3-token request-line guard.
        var request = ParseAscii("CONNECT  HTTP/1.0\r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    // ----- Line endings -----------------------------------------------------------------------------------------

    [TestMethod]
    public void LfOnlyLineEndings_NetscapeStyle_IsValid()
    {
        // LF-only requests (old Netscape) fall through to the "\n" re-split branch and parse.
        var request = ParseAscii("GET http://example.org/ HTTP/1.0\nHost: example.org\n\n");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual("example.org", request.Host);
    }

    // ----- Content-Length (Parse ignores it; only Build acts on it) ---------------------------------------------

    [TestMethod]
    public void NegativeContentLength_IsIgnoredByParse_IsValid()
    {
        // Parse never interprets Content-Length (that is Build's job), so a negative value is inert, not a crash.
        var request = ParseAscii("POST http://example.org/ HTTP/1.0\r\nHost: example.org\r\nContent-Length: -5\r\n\r\nbody");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual("body", request.Body);
    }

    [TestMethod]
    public void OverflowContentLength_DoesNotThrow_IsValid()
    {
        // A Content-Length far beyond Int64 range must not overflow-throw inside Parse.
        var request = ParseAscii("POST http://example.org/ HTTP/1.0\r\nHost: example.org\r\nContent-Length: 999999999999999999999\r\n\r\nbody");

        Assert.IsTrue(request.IsValid);
    }

    // ----- Query string -----------------------------------------------------------------------------------------

    [TestMethod]
    public void QueryString_BareKeyNoValue_DoesNotThrow()
    {
        // "?justkey" with no '=' is handled by ParseQueryString/ToDictionary without a null-key crash.
        var request = ParseAscii("GET http://example.org/?justkey HTTP/1.0\r\nHost: example.org\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.IsNotNull(request.QueryParams);
        Assert.AreEqual(1, request.QueryParams!.Count);
    }

    [TestMethod]
    public void QueryString_AmpersandsOnly_DoesNotThrow()
    {
        var request = ParseAscii("GET http://example.org/?&&& HTTP/1.0\r\nHost: example.org\r\n\r\n");

        Assert.IsTrue(request.IsValid);
        Assert.IsNotNull(request.QueryParams);
    }

    // ----- Body / content type ----------------------------------------------------------------------------------

    [TestMethod]
    public void FormUrlEncoded_MalformedBody_DoesNotThrow()
    {
        // Degenerate url-encoded body ("a&b&&=c&d==e") is parsed by ParseQueryString without throwing.
        var request = ParseAscii("POST http://example.org/ HTTP/1.0\r\nHost: example.org\r\nContent-Type: application/x-www-form-urlencoded\r\n\r\na&b&&=c&d==e");

        Assert.IsTrue(request.IsValid);
        Assert.IsNotNull(request.FormData);
    }

    [TestMethod]
    public void ContentType_SemicolonOnly_NoFormParsing_IsValid()
    {
        // "Content-Type: ;" gives an empty base type that matches no form branch; body is left untouched.
        var request = ParseAscii("POST http://example.org/ HTTP/1.0\r\nHost: example.org\r\nContent-Type: ;\r\n\r\nx");

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual(0, request.FormData!.Count);
    }

    // ----- Empty / degenerate buffers ---------------------------------------------------------------------------

    [TestMethod]
    public void WhitespaceOnlyRequest_IsInvalid()
    {
        var request = ParseAscii("   \r\n\r\n");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void BodySeparatorOnly_NoRequestLine_IsInvalid()
    {
        // Leading CRLFCRLF makes the header block empty; there is no request line to validate.
        var request = ParseAscii("\r\n\r\nbody");

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void NullRawBytes_WithGarbageString_DoesNotThrow()
    {
        // rawData == null must be tolerated (BodyData falls back to empty) rather than NRE on the byte-slice path.
        var request = HttpRequest.Parse(null, "not a request", Encoding.ASCII);

        Assert.IsFalse(request.IsValid);
    }

    // ----- Contract sweep: none of these hostile inputs may throw out of Parse -----------------------------------

    [TestMethod]
    public void HostileInputs_NeverThrowOutOfParse()
    {
        var inputs = new[]
        {
            "",
            "\r\n\r\n",
            "   ",
            "GET",
            "GET / HTTP/1.0 x\r\n\r\n",
            "PATCH\thttp://x/\tHTTP/1.1\r\n\r\n",
            "get http://x/ HTTP/1.0\r\n\r\n",
            "GET http://x/ HTTP/9.9\r\n\r\n",
            "GET / HTTP/1.0\r\nHost : x\r\n\r\n",
            "GET http://x/ HTTP/1.0\r\n:\r\n:::\r\nHost: x\r\n\r\n",
            "GET http://x/ HTTP/1.0\r\nHost: x\r\nCookie: =;=;a;=b;c=\r\n\r\n",
            "GET http://x/?a=1&a=2&&=&x HTTP/1.0\r\nHost: x\r\n\r\n",
            "POST http://x/ HTTP/1.0\r\nHost: x\r\nContent-Length: -99999999999\r\n\r\n\0\0\0",
            "POST http://x/ HTTP/1.0\r\nHost: x\r\nContent-Type: application/x-www-form-urlencoded\r\n\r\n%%%&=&==",
            "CONNECT x:443 HTTP/1.0\r\n\r\n",
            "CONNECT :::: HTTP/1.0\r\n\r\n",
            "GET http://x/\0\0 HTTP/1.0\r\nHost: x\0\r\n\r\n",
            "\0\0\0\0",
        };

        foreach (var input in inputs)
        {
            try
            {
                var request = HttpRequest.Parse(Encoding.ASCII.GetBytes(input), input, Encoding.ASCII);

                Assert.IsNotNull(request, $"Parse returned null for input <{Escape(input)}>");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Parse threw {ex.GetType().Name} for input <{Escape(input)}>: {ex.Message}");
            }
        }
    }

    static string Escape(string s)
    {
        return s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0");
    }
}