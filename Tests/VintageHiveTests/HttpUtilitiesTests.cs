// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace Http;

[TestClass]
public class HttpUtilitiesTests
{
    #region AddOrUpdate Extension

    [TestMethod]
    public void AddOrUpdate_NewKey_Adds()
    {
        var dict = new Dictionary<string, string>();

        dict.AddOrUpdate("key", "value");

        Assert.AreEqual("value", dict["key"]);
    }

    [TestMethod]
    public void AddOrUpdate_ExistingKey_Updates()
    {
        var dict = new Dictionary<string, string> { { "key", "old" } };

        dict.AddOrUpdate("key", "new");

        Assert.AreEqual("new", dict["key"]);
    }

    [TestMethod]
    public void AddOrUpdate_NullDict_Throws()
    {
        Dictionary<string, string> dict = null;

        Assert.ThrowsExactly<ArgumentNullException>(() => dict.AddOrUpdate("key", "value"));
    }

    [TestMethod]
    public void AddOrUpdate_NullValue_Throws()
    {
        var dict = new Dictionary<string, string>();

        Assert.ThrowsExactly<ArgumentNullException>(() => dict.AddOrUpdate("key", null));
    }

    #endregion

    #region HttpVerbs

    [TestMethod]
    public void HttpVerbs_ContainsAllStandardMethods()
    {
        Assert.IsTrue(HttpVerbs.Contains("HEAD"));
        Assert.IsTrue(HttpVerbs.Contains("GET"));
        Assert.IsTrue(HttpVerbs.Contains("POST"));
        Assert.IsTrue(HttpVerbs.Contains("CONNECT"));
        Assert.IsTrue(HttpVerbs.Contains("PUT"));
        Assert.IsTrue(HttpVerbs.Contains("DELETE"));
        Assert.IsTrue(HttpVerbs.Contains("PATCH"));
        Assert.IsTrue(HttpVerbs.Contains("OPTIONS"));
        Assert.IsTrue(HttpVerbs.Contains("TRACE"));
    }

    [TestMethod]
    public void HttpVerbs_Count()
    {
        Assert.AreEqual(9, HttpVerbs.Count);
    }

    #endregion

    #region HttpVersions

    [TestMethod]
    public void HttpVersions_ContainsHttp10()
    {
        Assert.IsTrue(HttpVersions.Contains("HTTP/1.0"));
    }

    [TestMethod]
    public void HttpVersions_ContainsHttp11()
    {
        Assert.IsTrue(HttpVersions.Contains("HTTP/1.1"));
    }

    [TestMethod]
    public void HttpVersions_Count()
    {
        Assert.AreEqual(2, HttpVersions.Count);
    }

    #endregion

    #region Separators

    [TestMethod]
    public void HttpSeperator_IsCRLF()
    {
        Assert.AreEqual("\r\n", HttpSeperator);
    }

    [TestMethod]
    public void HttpBodySeperator_IsDoubleCRLF()
    {
        Assert.AreEqual("\r\n\r\n", HttpBodySeperator);
    }

    #endregion

    #region HttpMethodName Constants

    [TestMethod]
    public void HttpMethodName_AllValuesMatchVerbs()
    {
        Assert.AreEqual("GET", HttpMethodName.Get);
        Assert.AreEqual("POST", HttpMethodName.Post);
        Assert.AreEqual("PUT", HttpMethodName.Put);
        Assert.AreEqual("DELETE", HttpMethodName.Delete);
        Assert.AreEqual("HEAD", HttpMethodName.Head);
        Assert.AreEqual("OPTIONS", HttpMethodName.Options);
        Assert.AreEqual("TRACE", HttpMethodName.Trace);
        Assert.AreEqual("CONNECT", HttpMethodName.Connect);
        Assert.AreEqual("PATCH", HttpMethodName.Patch);
    }

    #endregion

    #region HttpHeaderName Constants

    [TestMethod]
    public void HttpHeaderName_StandardHeaders()
    {
        Assert.AreEqual("Content-Type", HttpHeaderName.ContentType);
        Assert.AreEqual("Content-Disposition", HttpHeaderName.ContentDisposition);
        Assert.AreEqual("Content-Length", HttpHeaderName.ContentLength);
        Assert.AreEqual("Date", HttpHeaderName.Date);
        Assert.AreEqual("Server", HttpHeaderName.Server);
        Assert.AreEqual("Location", HttpHeaderName.Location);
        Assert.AreEqual("User-Agent", HttpHeaderName.UserAgent);
        Assert.AreEqual("Host", HttpHeaderName.Host);
        Assert.AreEqual("Cookie", HttpHeaderName.Cookie);
    }

    [TestMethod]
    public void HttpHeaderName_CustomHeaders()
    {
        Assert.AreEqual("Icy-MetaData", HttpHeaderName.IcyMetadata);
        Assert.AreEqual("X-TraceId", HttpHeaderName.XTraceId);
    }

    #endregion

    #region HttpContentTypeMimeType Constants

    [TestMethod]
    public void MimeType_Audio()
    {
        Assert.AreEqual("audio/mpeg", HttpContentTypeMimeType.Audio.Mpeg);
        Assert.AreEqual("audio/aac", HttpContentTypeMimeType.Audio.Aac);
        Assert.AreEqual("audio/aacp", HttpContentTypeMimeType.Audio.Aacp);
        Assert.AreEqual("audio/mp4", HttpContentTypeMimeType.Audio.Mp4);
    }

    [TestMethod]
    public void MimeType_Application()
    {
        Assert.AreEqual("application/json", HttpContentTypeMimeType.Application.Json);
        Assert.AreEqual("application/octet-stream", HttpContentTypeMimeType.Application.OctetStream);
        Assert.AreEqual("application/pdf", HttpContentTypeMimeType.Application.Pdf);
        Assert.AreEqual("application/postscript", HttpContentTypeMimeType.Application.PostScript);
        Assert.AreEqual("application/x-www-form-urlencoded", HttpContentTypeMimeType.Application.XWwwFormUrlEncoded);
    }

    [TestMethod]
    public void MimeType_Text()
    {
        Assert.AreEqual("text/html", HttpContentTypeMimeType.Text.Html);
        Assert.AreEqual("text/plain", HttpContentTypeMimeType.Text.Plain);
    }

    [TestMethod]
    public void MimeType_Multipart()
    {
        Assert.AreEqual("multipart/form-data", HttpContentTypeMimeType.Multipart.FormData);
    }

    #endregion
}

[TestClass]
public class HttpStatusCodeTests
{
    [TestMethod]
    public void StatusCode_InformationalRange()
    {
        Assert.AreEqual(100, (int)HttpStatusCode.Continue);
        Assert.AreEqual(101, (int)HttpStatusCode.SwitchingProtocols);
        Assert.AreEqual(102, (int)HttpStatusCode.Processing);
        Assert.AreEqual(103, (int)HttpStatusCode.EarlyHints);
    }

    [TestMethod]
    public void StatusCode_SuccessRange()
    {
        Assert.AreEqual(200, (int)HttpStatusCode.OK);
        Assert.AreEqual(201, (int)HttpStatusCode.Created);
        Assert.AreEqual(204, (int)HttpStatusCode.NoContent);
        Assert.AreEqual(206, (int)HttpStatusCode.PartialContent);
    }

    [TestMethod]
    public void StatusCode_RedirectionRange()
    {
        Assert.AreEqual(301, (int)HttpStatusCode.MovedPermanently);
        Assert.AreEqual(302, (int)HttpStatusCode.Found);
        Assert.AreEqual(304, (int)HttpStatusCode.NotModified);
        Assert.AreEqual(307, (int)HttpStatusCode.TemporaryRedirect);
        Assert.AreEqual(308, (int)HttpStatusCode.PermanentRedirect);
    }

    [TestMethod]
    public void StatusCode_ClientErrorRange()
    {
        Assert.AreEqual(400, (int)HttpStatusCode.BadRequest);
        Assert.AreEqual(401, (int)HttpStatusCode.Unauthorized);
        Assert.AreEqual(403, (int)HttpStatusCode.Forbidden);
        Assert.AreEqual(404, (int)HttpStatusCode.NotFound);
        Assert.AreEqual(405, (int)HttpStatusCode.MethodNotAllowed);
        Assert.AreEqual(418, (int)HttpStatusCode.ImATeaPot);
        Assert.AreEqual(429, (int)HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public void StatusCode_ServerErrorRange()
    {
        Assert.AreEqual(500, (int)HttpStatusCode.InternalServerError);
        Assert.AreEqual(502, (int)HttpStatusCode.BadGateway);
        Assert.AreEqual(503, (int)HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(504, (int)HttpStatusCode.GatewayTimeout);
    }
}
