// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive.Network;
using VintageHive.Proxy.Gopher;

namespace Gopher;

[TestClass]
public class GopherMenuItemTests
{
    [TestMethod]
    public void Parse_FullMenuLine_ExtractsAllFields()
    {
        var item = GopherMenuItem.Parse("1Fun and Games\t/fun\tgopher.floodgap.com\t70");

        Assert.IsNotNull(item);
        Assert.AreEqual('1', item.Type);
        Assert.AreEqual("Fun and Games", item.Display);
        Assert.AreEqual("/fun", item.Selector);
        Assert.AreEqual("gopher.floodgap.com", item.Host);
        Assert.AreEqual(70, item.Port);
    }

    [TestMethod]
    public void Parse_InfoLine_KeepsFakeHostAndPortZero()
    {
        var item = GopherMenuItem.Parse("iWelcome to Gopherspace\tfake\t(NULL)\t0");

        Assert.IsNotNull(item);
        Assert.AreEqual('i', item.Type);
        Assert.AreEqual("Welcome to Gopherspace", item.Display);
        Assert.AreEqual("(NULL)", item.Host);
        Assert.AreEqual(0, item.Port);
    }

    [TestMethod]
    public void Parse_MissingPort_DefaultsTo70()
    {
        var item = GopherMenuItem.Parse("0About\t/about\texample.com");

        Assert.IsNotNull(item);
        Assert.AreEqual(70, item.Port);
    }

    [TestMethod]
    public void Parse_EmptyOrNullLine_ReturnsNull()
    {
        Assert.IsNull(GopherMenuItem.Parse(""));
        Assert.IsNull(GopherMenuItem.Parse(null!));
    }

    [TestMethod]
    public void Serialize_RoundTripsThroughParse()
    {
        var original = "9Download Me\t/files/game.zip\tftp.example.com\t7070";
        var item = GopherMenuItem.Parse(original);

        Assert.IsNotNull(item);
        Assert.AreEqual(original, item.Serialize());
    }
}

[TestClass]
public class GopherProxySelectorTests
{
    [TestMethod]
    public void BuildProxySelector_EmptySelector_EndsWithSlash()
    {
        Assert.AreEqual("/g/1/gopher.floodgap.com:70/", GopherServer.BuildProxySelector('1', "gopher.floodgap.com", 70, ""));
    }

    [TestMethod]
    public void BuildProxySelector_RoundTripsThroughTryParse()
    {
        foreach (var selector in new[] { "", "fun", "/fun", "//weird", "/v2/vs", "/files/game.zip" })
        {
            var built = GopherServer.BuildProxySelector('7', "sdf.org", 7070, selector);

            Assert.IsTrue(GopherServer.TryParseProxySelector(built, out var type, out var host, out var port, out var remoteSelector), $"Failed to parse '{built}'");
            Assert.AreEqual('7', type);
            Assert.AreEqual("sdf.org", host);
            Assert.AreEqual(7070, port);
            Assert.AreEqual(selector, remoteSelector, $"Selector mangled through '{built}'");
        }
    }

    [TestMethod]
    public void TryParseProxySelector_MissingPort_DefaultsTo70()
    {
        Assert.IsTrue(GopherServer.TryParseProxySelector("/g/1/example.com/fun", out _, out var host, out var port, out var remoteSelector));
        Assert.AreEqual("example.com", host);
        Assert.AreEqual(70, port);
        Assert.AreEqual("fun", remoteSelector);
    }

    [TestMethod]
    public void TryParseProxySelector_LocalSelectors_AreRejected()
    {
        Assert.IsFalse(GopherServer.TryParseProxySelector("/news", out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseProxySelector("", out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseProxySelector(null!, out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_BadPort_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/example.com:abc/fun", out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/example.com:0/fun", out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/example.com:99999/fun", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_MissingHost_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/:70/fun", out _, out _, out _, out _));
    }
}

[TestClass]
public class GopherMenuRewriteTests
{
    [TestMethod]
    public void RewriteMenu_PointsFetchableItemsAtTheProxy()
    {
        var upstream = "1Fun\t/fun\tgopher.example.com\t70\r\n.\r\n";

        var rewritten = GopherServer.RewriteMenu(upstream, "10.0.0.5", 70);

        Assert.AreEqual("1Fun\t/g/1/gopher.example.com:70//fun\t10.0.0.5\t70\r\n.\r\n", rewritten);
    }

    [TestMethod]
    public void RewriteMenu_LeavesInfoErrorAndTelnetLinesAlone()
    {
        var upstream = "iWelcome\tfake\t(NULL)\t0\r\n3Bad thing\tfake\t(NULL)\t0\r\n8Telnet BBS\t\tbbs.example.com\t23\r\n.\r\n";

        var rewritten = GopherServer.RewriteMenu(upstream, "10.0.0.5", 70);

        Assert.AreEqual("iWelcome\tfake\t(NULL)\t0\r\n3Bad thing\tfake\t(NULL)\t0\r\n8Telnet BBS\t\tbbs.example.com\t23\r\n.\r\n", rewritten);
    }

    [TestMethod]
    public void RewriteMenu_ToleratesBareLineFeedsAndMissingTerminator()
    {
        var upstream = "0About\t/about\texample.com\t70\n1Stuff\t/stuff\texample.com\t70";

        var rewritten = GopherServer.RewriteMenu(upstream, "10.0.0.5", 70);

        StringAssert.Contains(rewritten, "0About\t/g/0/example.com:70//about\t10.0.0.5\t70\r\n");
        StringAssert.Contains(rewritten, "1Stuff\t/g/1/example.com:70//stuff\t10.0.0.5\t70\r\n");
        Assert.IsTrue(rewritten.EndsWith(".\r\n"));
    }

    [TestMethod]
    public void RewriteMenu_DoesNotDuplicateUpstreamTerminator()
    {
        var rewritten = GopherServer.RewriteMenu("iHello\tfake\t(NULL)\t0\r\n.\r\n", "10.0.0.5", 70);

        Assert.AreEqual(1, rewritten.Split(".\r\n").Length - 1);
    }

    [TestMethod]
    public void RewriteMenu_RewrittenSelectorRoundTripsToOriginalTarget()
    {
        var rewritten = GopherServer.RewriteMenu("7Search Veronica\t/v2/vs\tgopher.floodgap.com\t70\r\n.\r\n", "10.0.0.5", 70);
        var item = GopherMenuItem.Parse(rewritten.Split("\r\n")[0]);

        Assert.IsNotNull(item);
        Assert.IsTrue(GopherServer.TryParseProxySelector(item.Selector, out var type, out var host, out var port, out var remoteSelector));
        Assert.AreEqual('7', type);
        Assert.AreEqual("gopher.floodgap.com", host);
        Assert.AreEqual(70, port);
        Assert.AreEqual("/v2/vs", remoteSelector);
    }

    [TestMethod]
    public void RewriteMenu_LeavesUrlWebLinksUntouched()
    {
        var upstream = "hVisit us\tURL:http://example.com/\texample.com\t70\r\n.\r\n";

        var rewritten = GopherServer.RewriteMenu(upstream, "10.0.0.5", 70);

        Assert.AreEqual("hVisit us\tURL:http://example.com/\texample.com\t70\r\n.\r\n", rewritten);
    }
}

[TestClass]
public class GopherTextDocumentTests
{
    [TestMethod]
    public void FormatTextDocument_DotStuffsLeadingDots()
    {
        var wire = GopherServer.FormatTextDocument("hello\n.hidden\nworld");

        Assert.AreEqual("hello\r\n..hidden\r\nworld\r\n.\r\n", wire);
    }

    [TestMethod]
    public void FormatTextDocument_NormalizesLineEndingsToCrLf()
    {
        var wire = GopherServer.FormatTextDocument("one\r\ntwo\nthree");

        Assert.AreEqual("one\r\ntwo\r\nthree\r\n.\r\n", wire);
    }

    [TestMethod]
    public void FormatTextDocument_NullText_StillTerminates()
    {
        Assert.AreEqual("\r\n.\r\n", GopherServer.FormatTextDocument(null!));
    }

    [TestMethod]
    public void FinalizeMenu_AppendsSingleTerminator()
    {
        var wire = GopherServer.FinalizeMenu("iHello\tfake\t(NULL)\t0\r\n");

        Assert.AreEqual("iHello\tfake\t(NULL)\t0\r\n.\r\n", wire);
    }

    [TestMethod]
    public void UnstuffGopherText_StripsTerminatorAndUnstuffsLeadingDots()
    {
        var wire = "hello\r\n..plan\r\nworld\r\n.\r\n";

        var doc = GopherServer.UnstuffGopherText(wire);

        Assert.AreEqual("hello\r\n.plan\r\nworld\r\n", doc);
    }

    [TestMethod]
    public void UnstuffGopherText_NoTerminator_KeepsAllContent()
    {
        var doc = GopherServer.UnstuffGopherText("just\ntext");

        Assert.AreEqual("just\r\ntext\r\n", doc);
    }
}

[TestClass]
public class GopherHttpModeTests
{
    [TestMethod]
    public void IsHttpProxyRequest_DetectsBrowserProxyRequests()
    {
        Assert.IsTrue(GopherServer.IsHttpProxyRequest("GET gopher://example.com/ HTTP/1.0\r\n\r\n"));
        Assert.IsFalse(GopherServer.IsHttpProxyRequest("/news\r\n"));
        Assert.IsFalse(GopherServer.IsHttpProxyRequest("\r\n"));
        Assert.IsFalse(GopherServer.IsHttpProxyRequest(null!));
    }

    [TestMethod]
    public void TryParseGopherUrl_ParsesTypeAndSelector()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://gopher.floodgap.com/1/fun", out var host, out var port, out var type, out var selector, out var search));
        Assert.AreEqual("gopher.floodgap.com", host);
        Assert.AreEqual(70, port);
        Assert.AreEqual('1', type);
        Assert.AreEqual("/fun", selector);
        Assert.IsNull(search);
    }

    [TestMethod]
    public void TryParseGopherUrl_BareHost_IsRootMenu()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://example.com", out _, out var port, out var type, out var selector, out _));
        Assert.AreEqual(70, port);
        Assert.AreEqual('1', type);
        Assert.AreEqual(string.Empty, selector);
    }

    [TestMethod]
    public void TryParseGopherUrl_ExplicitPort_IsKept()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://example.com:7070/0/about", out _, out var port, out var type, out _, out _));
        Assert.AreEqual(7070, port);
        Assert.AreEqual('0', type);
    }

    [TestMethod]
    public void TryParseGopherUrl_EscapedTabCarriesSearchTerms()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://gopher.floodgap.com/7/v2/vs%09retro%20computers", out _, out _, out var type, out var selector, out var search));
        Assert.AreEqual('7', type);
        Assert.AreEqual("/v2/vs", selector);
        Assert.AreEqual("retro computers", search);
    }

    [TestMethod]
    public void TryParseGopherUrl_QueryStringCarriesSearchTerms()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://gopher.floodgap.com/7/v2/vs?retro+computers", out _, out _, out _, out _, out var search));
        Assert.AreEqual("retro computers", search);
    }

    [TestMethod]
    public void TryParseGopherUrl_EncodedPlusInSearchSurvives()
    {
        // 'C++' submitted through the isindex form arrives as %2B%2B; the literal plus must not be turned into a space.
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host/7/v2/vs?C%2B%2B", out _, out _, out _, out _, out var search));
        Assert.AreEqual("C++", search);
    }

    [TestMethod]
    public void TryParseGopherUrl_RawOctetSelectorDecodesAsLatin1()
    {
        // RFC 4266 raw-octet escape: %E9 is Latin1 0xE9 (é), not a UTF-8 sequence.
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host/0/caf%E9", out _, out _, out _, out var selector, out _));
        Assert.AreEqual("/café", selector);
    }

    [TestMethod]
    public void PercentDecodeLatin1_LeavesInvalidEscapesLiteral()
    {
        Assert.AreEqual("100%off", GopherServer.PercentDecodeLatin1("100%off", false));
        Assert.AreEqual("a b", GopherServer.PercentDecodeLatin1("a+b", true));
        Assert.AreEqual("a+b", GopherServer.PercentDecodeLatin1("a+b", false));
    }

    [TestMethod]
    public void TryParseGopherUrl_NonGopherScheme_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseGopherUrl("http://example.com/", out _, out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseGopherUrl("not a url", out _, out _, out _, out _, out _));
    }

    [TestMethod]
    public void GetHttpContentType_MapsGopherTypes()
    {
        Assert.AreEqual("text/plain", GopherServer.GetHttpContentType('0', "/about"));
        Assert.AreEqual("text/html", GopherServer.GetHttpContentType('h', "/page"));
        Assert.AreEqual("image/gif", GopherServer.GetHttpContentType('g', "/pic"));
        Assert.AreEqual("image/gif", GopherServer.GetHttpContentType('I', "/pic.gif"));
        Assert.AreEqual("image/jpeg", GopherServer.GetHttpContentType('I', ""));
        Assert.AreEqual("application/octet-stream", GopherServer.GetHttpContentType('9', ""));
    }

    [TestMethod]
    public void MenuToHtml_RendersAnchorsAndEscapesDisplayText()
    {
        var html = GopherServer.MenuToHtml("1<Fun & Games>\t/fun stuff\tgopher.example.com\t70\r\niJust info\tfake\t(NULL)\t0\r\n.\r\n", "gopher://gopher.example.com/");

        StringAssert.Contains(html, "<a href=\"gopher://gopher.example.com:70/1/fun%20stuff\">&lt;Fun &amp; Games&gt;</a>");
        StringAssert.Contains(html, "Just info");
        Assert.IsFalse(html.Contains("\r\n.\r\n"), "Upstream terminator leaked into the HTML rendering");
    }

    [TestMethod]
    public void MenuToHtml_RendersTelnetItemsAsTelnetLinks()
    {
        var html = GopherServer.MenuToHtml("8Some BBS\t\tbbs.example.com\t23\r\n.\r\n", "gopher://gopher.example.com/");

        StringAssert.Contains(html, "<a href=\"telnet://bbs.example.com:23/\">Some BBS</a>");
    }

    [TestMethod]
    public void MenuToHtml_EncodesHostilehostInHrefSoScriptCannotBreakOut()
    {
        var hostile = "1Click\t/sel\t\"><script>alert(1)</script>\t70\r\n.\r\n";

        var html = GopherServer.MenuToHtml(hostile, "gopher://evil/");

        Assert.IsFalse(html.Contains("<script>alert(1)</script>"), "Unescaped script tag survived into the HTML output");
        StringAssert.Contains(html, "&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [TestMethod]
    public void MenuToHtml_EncodesHostileTypeCharInHref()
    {
        var hostile = "\"onmouseover=alert(1) x\t/sel\tgood.example.com\t70\r\n.\r\n";

        var html = GopherServer.MenuToHtml(hostile, "gopher://evil/");

        Assert.IsFalse(html.Contains("href=\"gopher://good.example.com:70/\"onmouseover"), "Type char broke out of the href attribute");
    }

    [TestMethod]
    public void MenuToHtml_RendersUrlSelectorsAsDirectWebLinks()
    {
        var html = GopherServer.MenuToHtml("hMy Homepage\tURL:http://example.com/\texample.com\t70\r\n.\r\n", "gopher://example.com/");

        StringAssert.Contains(html, "<a href=\"http://example.com/\">My Homepage</a>");
    }
}

[TestClass]
public class GopherEndToEndTests
{
    [TestMethod]
    [Timeout(10000)]
    public async Task NativeRequest_EmptySelector_ReturnsRootMenuAndServerCloses()
    {
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

            var server = new GopherServer(IPAddress.Loopback, 0);
            var serverTask = Task.Run(() => server.ProcessConnection(connection));

            var clientStream = client.GetStream();

            await clientStream.WriteAsync("\r\n"u8.ToArray());

            using var response = new MemoryStream();

            var buffer = new byte[4096];

            while (true)
            {
                int read;

                try
                {
                    read = await clientStream.ReadAsync(buffer);
                }
                catch (IOException)
                {
                    // Windows surfaces the server-side close as an IOException instead of a zero read.
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                response.Write(buffer, 0, read);
            }

            await serverTask;

            var text = Encoding.Latin1.GetString(response.ToArray());

            StringAssert.Contains(text, "VintageHive Gopher Server");
            StringAssert.Contains(text, "1News Headlines\t/news\t127.0.0.1\t0\r\n");
            Assert.IsTrue(text.EndsWith(".\r\n"), "Menu is not dot-terminated");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task NativeRequest_SelfProxyLoop_IsRefusedWithoutDialing()
    {
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

            // A GopherServer bound to the same port the /g/ selector targets on loopback would recurse into itself.
            var server = new GopherServer(IPAddress.Loopback, port);
            var serverTask = Task.Run(() => server.ProcessConnection(connection));

            var clientStream = client.GetStream();

            await clientStream.WriteAsync(Encoding.Latin1.GetBytes($"/g/1/127.0.0.1:{port}/\r\n"));

            using var response = new MemoryStream();

            var buffer = new byte[4096];

            while (true)
            {
                int read;

                try
                {
                    read = await clientStream.ReadAsync(buffer);
                }
                catch (IOException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                response.Write(buffer, 0, read);
            }

            await serverTask;

            var text = Encoding.Latin1.GetString(response.ToArray());

            Assert.IsTrue(text.StartsWith("3"), $"Expected a refusal error menu, got: {text}");
            StringAssert.Contains(text, "Refusing to proxy");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task NativeRequest_UnknownSelector_ReturnsErrorMenu()
    {
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

            var server = new GopherServer(IPAddress.Loopback, 0);
            var serverTask = Task.Run(() => server.ProcessConnection(connection));

            var clientStream = client.GetStream();

            await clientStream.WriteAsync("/no/such/thing\r\n"u8.ToArray());

            using var response = new MemoryStream();

            var buffer = new byte[4096];

            while (true)
            {
                int read;

                try
                {
                    read = await clientStream.ReadAsync(buffer);
                }
                catch (IOException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                response.Write(buffer, 0, read);
            }

            await serverTask;

            var text = Encoding.Latin1.GetString(response.ToArray());

            Assert.IsTrue(text.StartsWith("3"), $"Expected an error menu, got: {text}");
            Assert.IsTrue(text.EndsWith(".\r\n"));
        }
        finally
        {
            listener.Stop();
        }
    }
}
