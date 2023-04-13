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
        var request = HttpRequest.Parse(rawRequest, Encoding.ASCII);

        // Assert
        Assert.IsNotNull(request);
        Assert.IsFalse(request.IsValid);
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
        var request = HttpRequest.Parse(rawRequest, Encoding.ASCII);

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

        var rawRequest = BuildHttpString(type, version, path, "",
            new Tuple<string, string>(HttpHeaderName.Host, domain),
            new Tuple<string, string>(HttpHeaderName.ContentType, contentType)
        );

        // Act
        var request = HttpRequest.Parse(rawRequest, Encoding.ASCII);

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

        var rawRequest = BuildHttpString(type, version, path, "",
            new Tuple<string, string>(HttpHeaderName.Host, domain),
            new Tuple<string, string>(HttpHeaderName.UserAgent, userAgent)
        );

        // Act
        var request = HttpRequest.Parse(rawRequest, Encoding.ASCII);

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

        var rawRequest = BuildHttpString(type, version, path, "",
            new Tuple<string, string>(HttpHeaderName.UserAgent, userAgent)
        );

        // Act
        var request = HttpRequest.Parse(rawRequest, Encoding.ASCII);

        // Assert
        AssertBasicRequest(request, type, version, "/", domain);

        AssertHeader(request, HttpHeaderName.UserAgent, userAgent);
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
