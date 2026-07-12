// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace Adversarial.Bugfixes;

// Regression tests for bugs surfaced by the adversarial parser sweep and then fixed: three multipart parsing
// crashes that broke HttpRequest.Parse's never-throw contract, and a BER decoder integer-overflow bounds
// bypass that allowed a ~2GB allocation from a tiny hostile TLV.
[TestClass]
public class HttpMultipartRegressionTests
{
    private static HttpRequest Parse(string raw)
    {
        return HttpRequest.Parse(Encoding.ASCII.GetBytes(raw), raw, Encoding.ASCII);
    }

    [TestMethod]
    public void Multipart_BodyIsOnlyClosingBoundary_DoesNotThrow()
    {
        var req = Parse("POST http://x/ HTTP/1.0\r\nHost: x\r\nContent-Type: multipart/form-data; boundary=XXXXXXXX\r\n\r\n--XXXXXXXX--");

        Assert.IsNotNull(req);
    }

    [TestMethod]
    public void Multipart_PartMissingBodySection_DoesNotThrow()
    {
        var req = Parse("POST http://x/ HTTP/1.0\r\nHost: x\r\nContent-Type: multipart/form-data; boundary=XXXXXXXX\r\n\r\n--XXXXXXXX\r\nContent-Disposition: form-data; name=\"f\"\r\n--XXXXXXXX--");

        Assert.IsNotNull(req);
    }

    [TestMethod]
    public void Multipart_NoBoundaryParameter_DoesNotThrow()
    {
        var req = Parse("POST http://x/ HTTP/1.0\r\nHost: x\r\nContent-Type: multipart/form-data\r\n\r\n----------12345");

        Assert.IsNotNull(req);
    }
}

[TestClass]
public class BerOverflowRegressionTests
{
    [TestMethod]
    public void ReadBytes_CountNearIntMax_ThrowsInsteadOfAllocating()
    {
        var dec = new BerDecoder(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 });

        Assert.ThrowsExactly<InvalidOperationException>(() => dec.ReadBytes(int.MaxValue));
    }

    [TestMethod]
    public void Skip_LengthNearIntMax_ThrowsInsteadOfBypassingGuard()
    {
        // 04 84 7F FF FF FF: OCTET STRING, 4-byte long-form length = int.MaxValue. The bounds check must
        // reject it rather than overflow (_position + length) past the guard.
        var dec = new BerDecoder(new byte[] { 0x04, 0x84, 0x7F, 0xFF, 0xFF, 0xFF });

        Assert.ThrowsExactly<InvalidOperationException>(() => dec.Skip());
    }
}
