// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace Http;

[TestClass]
public class HttpRequestTests
{
    [TestMethod]
    public void InvalidRequest()
    {
        // Arrange
        var rawRequest = string.Empty;

        // Act
        var request = HttpRequest.Parse([], rawRequest, Encoding.ASCII);

        // Assert
        Assert.IsNotNull(request);
        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void Parse_BinaryBody_PreservedByteForByte()
    {
        // A binary POST body (e.g. an IPP/PDF print job) must survive Parse even though the request is decoded as
        // ASCII - BodyData is sliced from the raw bytes, not round-tripped through the string (which turned every
        // byte above 0x7F into '?').
        var headerText = "POST /printer HTTP/1.1\r\nHost: printer.local\r\nContent-Type: application/ipp\r\nContent-Length: 6\r\n\r\n";
        var body = new byte[] { 0x25, 0x50, 0x44, 0x80, 0xFF, 0x00 }; // "%PD" + high bytes + NUL

        var headerBytes = Encoding.ASCII.GetBytes(headerText);
        var rawData = new byte[headerBytes.Length + body.Length];
        Buffer.BlockCopy(headerBytes, 0, rawData, 0, headerBytes.Length);
        Buffer.BlockCopy(body, 0, rawData, headerBytes.Length, body.Length);

        var request = HttpRequest.Parse(rawData, Encoding.ASCII.GetString(rawData), Encoding.ASCII);

        Assert.IsTrue(request.IsValid);
        CollectionAssert.AreEqual(body, request.BodyData);
    }

    [TestMethod]
    public void GET_Request()
    {
        // Arrange
        var type = HttpMethodName.Get;
        var version = "HTTP/1.0";
        var path = "/index.html";
        var domain = "example.org";

        var rawRequest = BuildHttpString(type, version, path, "", new Tuple<string, string>(HttpHeaderName.Host, domain));

        // Act
        var request = HttpRequest.Parse(Encoding.UTF8.GetBytes(rawRequest), rawRequest, Encoding.ASCII);

        // Assert
        AssertBasicRequest(request, type, version, path, domain);
    }

    [TestMethod]
    public void GET_RequestWithHeaders()
    {
        // Arrange
        var type = HttpMethodName.Get;
        var version = "HTTP/1.0";
        var path = "/index.html";
        var domain = "example.org";
        var contentType = "application/json";

        var rawRequest = BuildHttpString(type, version, path, "", new Tuple<string, string>(HttpHeaderName.Host, domain), new Tuple<string, string>(HttpHeaderName.ContentType, contentType));

        // Act
        var request = HttpRequest.Parse(Encoding.UTF8.GetBytes(rawRequest), rawRequest, Encoding.ASCII);

        // Assert
        AssertBasicRequest(request, type, version, path, domain);

        AssertHeader(request, HttpHeaderName.Host, domain);
        AssertHeader(request, HttpHeaderName.ContentType, contentType);
    }

    [TestMethod]
    public void CONNECT_RequestWithHost()
    {
        // Arrange
        var type = HttpMethodName.Connect;
        var version = "HTTP/1.0";
        var domain = "example.org";
        var path = $"{domain}:443";

        var userAgent = "Mozilla/3.04 (WinNT; U)";

        var rawRequest = BuildHttpString(type, version, path, "", new Tuple<string, string>(HttpHeaderName.Host, domain), new Tuple<string, string>(HttpHeaderName.UserAgent, userAgent));

        // Act
        var request = HttpRequest.Parse(Encoding.UTF8.GetBytes(rawRequest), rawRequest, Encoding.ASCII);

        // Assert
        AssertBasicRequest(request, type, version, "/", domain);

        AssertHeader(request, HttpHeaderName.Host, domain);
        AssertHeader(request, HttpHeaderName.UserAgent, userAgent);
    }

    [TestMethod]
    public void CONNECT_RequestWithoutHost()
    {
        // Arrange
        var type = HttpMethodName.Connect;
        var version = "HTTP/1.0";
        var domain = "example.org";
        var path = $"{domain}:443";

        var userAgent = "Mozilla/3.04 (WinNT; U)";

        var rawRequest = BuildHttpString(type, version, path, "", new Tuple<string, string>(HttpHeaderName.UserAgent, userAgent));

        // Act
        var request = HttpRequest.Parse(Encoding.UTF8.GetBytes(rawRequest), rawRequest, Encoding.ASCII);

        // Assert
        AssertBasicRequest(request, type, version, "/", domain);

        AssertHeader(request, HttpHeaderName.UserAgent, userAgent);
    }

    [TestMethod]
    public void MalformedHeaderLine_IsSkipped()
    {
        // A colon-less header line used to throw IndexOutOfRange; it must be skipped, request still valid
        var raw = "GET http://example.org/ HTTP/1.0\r\nJunkNoColon\r\nHost: example.org\r\nUser-Agent: Moo\r\n\r\n";

        var request = HttpRequest.Parse(Encoding.ASCII.GetBytes(raw), raw, Encoding.ASCII);

        Assert.IsTrue(request.IsValid);
        Assert.IsFalse(request.Headers!.ContainsKey("JunkNoColon"));
        Assert.AreEqual("Moo", request.Headers[HttpHeaderName.UserAgent]);
    }

    [TestMethod]
    public void ValuelessCookie_IsSkipped()
    {
        // A cookie fragment with no '=' used to throw; it must be skipped
        var raw = "GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\nCookie: bareflag\r\n\r\n";

        var request = HttpRequest.Parse(Encoding.ASCII.GetBytes(raw), raw, Encoding.ASCII);

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual(0, request.Cookies.Count);
    }

    [TestMethod]
    public void DuplicateCookieName_LastWins()
    {
        // Duplicate cookie names (browsers send path-scoped dupes) used to throw from Dictionary.Add
        var raw = "GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\nCookie: a=1; a=2\r\n\r\n";

        var request = HttpRequest.Parse(Encoding.ASCII.GetBytes(raw), raw, Encoding.ASCII);

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual("2", request.Cookies["a"]);
    }

    [TestMethod]
    public void OriginFormWithoutHost_IsInvalid()
    {
        // An origin-form request with no Host header has no absolute URI; must be Invalid, not a throw
        // (and not a bogus file:/// URI, which Uri.TryCreate accepts on Linux)
        var raw = "GET /index.html HTTP/1.0\r\n\r\n";

        var request = HttpRequest.Parse(Encoding.ASCII.GetBytes(raw), raw, Encoding.ASCII);

        Assert.IsFalse(request.IsValid);
    }

    [TestMethod]
    public void MissingUserAgent_ReturnsNa()
    {
        // The Headers dictionary indexer throws on a missing key, so UserAgent must use TryGetValue
        var raw = "GET http://example.org/ HTTP/1.0\r\nHost: example.org\r\n\r\n";

        var request = HttpRequest.Parse(Encoding.ASCII.GetBytes(raw), raw, Encoding.ASCII);

        Assert.IsTrue(request.IsValid);
        Assert.AreEqual("NA", request.UserAgent);
    }

    [TestMethod]
    public void InvalidRequest_UserAgentDoesNotThrow()
    {
        Assert.AreEqual("NA", HttpRequest.Invalid.UserAgent);
    }

    static void AssertBasicRequest(HttpRequest request, string type, string version, string path, string domain)
    {
        Assert.IsNotNull(request, "Request was null");
        Assert.IsTrue(request.IsValid);

        Assert.AreEqual(request.Type, type);
        Assert.AreEqual(request.Version, version);

        Assert.IsNotNull(request.Uri, "Uri wasn't parsed properly!");
        Assert.AreEqual(request.Uri.Host, domain, "The domains in the header do not match!");
        Assert.AreEqual(request.Uri.AbsolutePath, path);
    }

    static void AssertHeader(HttpRequest request, string key, string value)
    {
        Assert.IsNotNull(request.Headers);
        Assert.IsTrue(request.Headers.ContainsKey(key));
        Assert.AreEqual(request.Headers[key], value);
    }

    static string BuildHttpString(string type, string version, string path, string body = "", params Tuple<string, string>[] headers)
    {
        var httpRequest = new StringBuilder();

        // Add request type and version
        httpRequest.AppendFormat("{0} {1} {2}\r\n", type.ToUpper(), path, version);

        // Add headers
        foreach (var header in headers)
        {
            httpRequest.AppendFormat("{0}: {1}\r\n", header.Item1, header.Item2);
        }

        // Add content length header if there's a body
        if (!string.IsNullOrEmpty(body))
        {
            httpRequest.AppendFormat("Content-Length: {0}\r\n", body.Length);
        }

        // End headers section
        httpRequest.Append("\r\n");

        // Add body if there's one
        if (!string.IsNullOrEmpty(body))
        {
            httpRequest.Append(body);
        }

        return httpRequest.ToString();
    }
}
