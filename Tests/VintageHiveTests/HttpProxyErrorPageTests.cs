// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;
using HttpStatusCode = VintageHive.Proxy.Http.HttpStatusCode;

namespace Http;

// HttpProxy.ProcessRequest special-cases a handled 404 and used to replace it unconditionally with the embedded
// errors/404.html template - so a processor that answered 404 WITH its own page had that page thrown away and
// every hosted site's missing-page response came back branded VintageHive. These drive the real proxy over a
// loopback socket to pin which 404s get the built-in page and which keep the handler's.
internal static class HttpErrorPageEnv
{
    private static readonly object Gate = new();
    private static bool _ready;

    // Distinctive strings from Statics/errors/404.html - their presence means the built-in template won.
    public const string BuiltInMarker = "The following request was not found/handled";

    public static void EnsureContexts()
    {
        lock (Gate)
        {
            if (_ready)
            {
                return;
            }

            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "vfs", "data"));

            if (Mind.Db == null)
            {
                var setter = typeof(Mind).GetProperty(nameof(Mind.Db))!.GetSetMethod(nonPublic: true)!;
                setter.Invoke(null, new object[] { new HiveDbContext() });
            }

            // ProcessRequest consults the HTTP proxy cache before any handler runs; a null Cache would NRE.
            if (Mind.Cache == null)
            {
                var setter = typeof(Mind).GetProperty(nameof(Mind.Cache))!.GetSetMethod(nonPublic: true)!;
                setter.Invoke(null, new object[] { new CacheDbContext() });
            }

            _ready = true;
        }
    }

    // Every URL these tests use must be unique per run. The HTTP proxy cache is real on-disk SQLite that outlives
    // the process, and ProcessRequest consults it before any handler executes - so a single run that cached a
    // response under a fixed URL (say, while someone was verifying a guard by temporarily disabling it) would
    // silently serve that stale entry to every later run, making the test pass without exercising the proxy at
    // all. Do NOT replace these with literal URLs.
    public static string Unique(string host) => $"http://{host}/{Guid.NewGuid()}";

    // Runs one GET through the real HttpProxy pipeline and returns the wire response it produced.
    public static async Task<string> Get(string url, Func<HttpRequest, HttpResponse, Task<bool>> handler)
    {
        EnsureContexts();

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

            proxy.Use(handler);

            var uri = new Uri(url);

            var raw = Encoding.UTF8.GetBytes($"GET {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Host}\r\n\r\n");

            var result = await proxy.ProcessRequest(connection, raw, raw.Length);

            if (result != null)
            {
                return Encoding.UTF8.GetString(result);
            }

            // A streamed response is written straight to the socket and ProcessRequest returns null, so read the
            // wire from the client end instead. The write already completed, so it is sitting in the receive buffer.
            var clientStream = client.GetStream();

            clientStream.ReadTimeout = 2000;

            var received = new MemoryStream();
            var buffer = new byte[4096];

            while (client.Available > 0 || received.Length == 0)
            {
                int read;

                try
                {
                    read = clientStream.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                received.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(received.ToArray());
        }
        finally
        {
            listener.Stop();
        }
    }
}

[TestClass]
public class HttpProxyErrorPageTests
{
    [TestMethod]
    public async Task Handled404_WithOwnBody_KeepsHandlerPage()
    {
        const string page = "<html><body><h1>No such page on this plane</h1></body></html>";

        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("own-404.example.com"), (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.NotFound).SetBodyString(page);

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "404 NotFound");
        StringAssert.Contains(response, page);
        Assert.IsFalse(response.Contains(HttpErrorPageEnv.BuiltInMarker), "Handler's own 404 page was replaced by the built-in template.");
    }

    [TestMethod]
    public async Task Handled404_WithOwnStream_KeepsHandlerStream()
    {
        var payload = "streamed 404 body"u8.ToArray();

        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("own-404-stream.example.com"), (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.NotFound).SetStream(new MemoryStream(payload), HttpContentTypeMimeType.Text.Html);

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "404 NotFound");
        StringAssert.Contains(response, "streamed 404 body");
        Assert.IsFalse(response.Contains(HttpErrorPageEnv.BuiltInMarker), "Handler's streamed 404 was replaced by the built-in template.");
    }

    [TestMethod]
    public async Task Handled404_WithNoBody_ServesBuiltInPage()
    {
        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("bare-404.example.com"), (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.NotFound);

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "404 NotFound");
        StringAssert.Contains(response, HttpErrorPageEnv.BuiltInMarker);
    }

    [TestMethod]
    public async Task UnhandledRequest_ServesBuiltInPage()
    {
        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("unhandled.example.com"), (req, res) => Task.FromResult(false));

        StringAssert.Contains(response, "404 NotFound");
        StringAssert.Contains(response, HttpErrorPageEnv.BuiltInMarker);
    }

    // The guard above lets a handler-authored 404 skip ProcessErrorResponse, which is what used to clear Cache.
    // The proxy has to restore that invariant itself, or a cached miss keeps a published page missing for the TTL.
    [TestMethod]
    public async Task Handled404_WithOwnBody_IsNotCached()
    {
        var url = HttpErrorPageEnv.Unique("uncached-404.example.com");

        var response = await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            // Deliberately leaves Cache at its default true - the proxy must override it.
            res.SetStatusCode(HttpStatusCode.NotFound).SetBodyString("<html><body>gone</body></html>");

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "404 NotFound");
        Assert.IsNull(Mind.Cache.GetHttpProxy($"HPC-GET-{url}"), "A handler-authored 404 was written to the HTTP proxy cache.");
    }

    // Control for the test above: proves the cache assertion has teeth, ie. a 200 on the same path IS stored.
    [TestMethod]
    public async Task Handled200_WithOwnBody_IsCached()
    {
        var url = HttpErrorPageEnv.Unique("cached-200.example.com");

        await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            res.SetBodyString("<html><body>ok</body></html>");

            return Task.FromResult(true);
        });

        Assert.IsNotNull(Mind.Cache.GetHttpProxy($"HPC-GET-{url}"), "A 200 response was not cached, so the 404 cache assertion proves nothing.");
    }

    [TestMethod]
    public async Task Handled200_WithOwnBody_IsUntouched()
    {
        const string page = "<html><body>ok</body></html>";

        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("ok.example.com"), (req, res) =>
        {
            res.Cache = false;

            res.SetBodyString(page);

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "200 OK");
        StringAssert.Contains(response, page);
    }

    // Body is non-null but zero-length, which the guard counts as "the handler wrote something" - it reuses the
    // Body == null && Stream == null idiom from the direct-write check further down ProcessRequest. A handler
    // that deliberately answers 404 with nothing gets a bare 404, not the built-in page.
    [TestMethod]
    public async Task Handled404_WithEmptyBody_IsNotReplacedByTemplate()
    {
        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("empty-404.example.com"), (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.NotFound).SetBodyString(string.Empty);

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "404 NotFound");
        Assert.IsFalse(response.Contains(HttpErrorPageEnv.BuiltInMarker), "An empty handler-authored 404 was replaced by the built-in template.");
    }

    // Only 404 was ever special-cased. These pin that the other statuses stay pass-through, which is why a
    // whitelabelled "no such host" 503 always worked while a whitelabelled 404 did not.
    [TestMethod]
    public async Task Handled503_WithOwnBody_PassesThroughUntouched()
    {
        const string page = "<html><body>No such host on this plane</body></html>";

        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("own-503.example.com"), (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.ServiceUnavailable).SetBodyString(page);

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "503 ServiceUnavailable");
        StringAssert.Contains(response, page);
        Assert.IsFalse(response.Contains(HttpErrorPageEnv.BuiltInMarker));
    }

    [TestMethod]
    public async Task Handled500_WithOwnBody_PassesThroughUntouched()
    {
        const string page = "<html><body>our own server error page</body></html>";

        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("own-500.example.com"), (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.InternalServerError).SetBodyString(page);

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "500 InternalServerError");
        StringAssert.Contains(response, page);
        Assert.IsFalse(response.Contains("this is typically a"), "A handler-authored 500 was replaced by the built-in template.");
    }

    // The cache invariant has to hold on every route to a 404, not just the handler-authored one that motivated it.
    [TestMethod]
    public async Task BuiltIn404_IsNotCached()
    {
        var url = HttpErrorPageEnv.Unique("uncached-builtin.example.com");

        await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.NotFound);

            return Task.FromResult(true);
        });

        Assert.IsNull(Mind.Cache.GetHttpProxy($"HPC-GET-{url}"));
    }

    [TestMethod]
    public async Task Unhandled404_IsNotCached()
    {
        var url = HttpErrorPageEnv.Unique("uncached-unhandled.example.com");

        await HttpErrorPageEnv.Get(url, (req, res) => Task.FromResult(false));

        Assert.IsNull(Mind.Cache.GetHttpProxy($"HPC-GET-{url}"));
    }

    // Strongest form: the handler does not merely leave Cache at its default, it explicitly opts in. The proxy
    // owns this invariant, so the opt-in loses.
    [TestMethod]
    public async Task Handled404_ExplicitlyRequestingCache_IsStillNotCached()
    {
        var url = HttpErrorPageEnv.Unique("opt-in-404.example.com");

        await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.NotFound).SetBodyString("<html><body>gone</body></html>");

            res.Cache = true;

            return Task.FromResult(true);
        });

        Assert.IsNull(Mind.Cache.GetHttpProxy($"HPC-GET-{url}"), "A handler that opted a 404 into the cache was allowed to.");
    }

    // The whole reason the invariant matters, end to end: a member hits a page before it exists, then it is
    // published. Without the guard the second request is served the cached miss for the full 60 minute TTL.
    [TestMethod]
    public async Task Published404_IsNotServedFromCacheOnTheNextRequest()
    {
        var url = HttpErrorPageEnv.Unique("republished.example.com");

        const string page = "<html><body>the page, now published</body></html>";

        var first = await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            res.SetStatusCode(HttpStatusCode.NotFound).SetBodyString("<html><body>not yet</body></html>");

            return Task.FromResult(true);
        });

        StringAssert.Contains(first, "404 NotFound");

        var second = await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            res.SetBodyString(page);

            return Task.FromResult(true);
        });

        StringAssert.Contains(second, "200 OK");
        StringAssert.Contains(second, page);
        Assert.IsFalse(second.Contains("not yet"), "The second request was served the previous 404 from cache.");
    }
}

// The embedded error pages hardcoded "VintageHive" in their title and footer, so a whitelabelled plane still
// served another product's name on every unhandled 404 and every unhandled exception. Both now carry a
// ||PRODUCT|| token substituted per request.
[TestClass]
public class HttpProxyWhitelabelTests
{
    private const string Product = "RetroPlane";

    private const string Version = "9.9.9-plane";

    [TestInitialize]
    public void SetProductIdentity()
    {
        HttpErrorPageEnv.EnsureContexts();

        Mind.Db.ConfigSet(ConfigNames.ProductName, Product);
        Mind.Db.ConfigSet(ConfigNames.ProductVersion, Version);
    }

    [TestCleanup]
    public void ClearProductIdentity()
    {
        Mind.Db.ConfigSet(ConfigNames.ProductName, string.Empty);
        Mind.Db.ConfigSet(ConfigNames.ProductVersion, string.Empty);
    }

    [TestMethod]
    public async Task BuiltIn404_UsesConfiguredProductName()
    {
        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("whitelabel-404.example.com"), (req, res) => Task.FromResult(false));

        StringAssert.Contains(response, $"<title>{Product} - 404 NotFound</title>");
        // Anchored to the footer markup on purpose: a bare "Product/Version" would also match the Server header,
        // which HttpResponse builds from the same pair, so it would pass even if the page itself were unbranded.
        StringAssert.Contains(response, $">{Product}/{Version} - <b>");
        Assert.IsFalse(response.Contains("VintageHive"), "The built-in 404 page still leaks VintageHive branding.");
        Assert.IsFalse(response.Contains(Mind.ApplicationVersion), "The built-in 404 footer still asserts VintageHive's own version under the embedder's name.");
    }

    [TestMethod]
    public async Task BuiltIn500_UsesConfiguredProductName()
    {
        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("whitelabel-500.example.com"), (req, res) => throw new InvalidOperationException("kaboom"));

        StringAssert.Contains(response, "500 InternalServerError");
        StringAssert.Contains(response, $"<title>{Product} - 500 Server Error</title>");
        StringAssert.Contains(response, $"a {Product} internal issue");
        // Anchored to the footer markup on purpose: a bare "Product/Version" would also match the Server header,
        // which HttpResponse builds from the same pair, so it would pass even if the page itself were unbranded.
        StringAssert.Contains(response, $">{Product}/{Version} - <b>");

        // The ||ERROR|| substitution embeds a real stack trace, which legitimately names VintageHive types -
        // so only the chrome is asserted branding-clean here, not the whole page.
        Assert.IsFalse(response.Contains("VintageHive - 500"), "The built-in 500 title still leaks VintageHive branding.");
        Assert.IsFalse(response.Contains("VintageHive/"), "The built-in 500 footer still leaks VintageHive branding.");
        Assert.IsFalse(response.Contains("a VintageHive internal issue"), "The built-in 500 body text still leaks VintageHive branding.");
    }

    [TestMethod]
    public async Task ProductName_IsReadPerRequest_NotFrozenAtLoad()
    {
        // ErrorPages is static readonly and loaded once, so substituting at load time would bake in whatever
        // ProductName was at startup and silently diverge from any later ConfigSet - the PostOfficeDbContext trap.
        Mind.Db.ConfigSet(ConfigNames.ProductName, "SecondPlane");

        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("whitelabel-runtime.example.com"), (req, res) => Task.FromResult(false));

        StringAssert.Contains(response, "SecondPlane - 404 NotFound");
    }

    // ||VERSION|| reads Mind.ProductVersion so a whitelabelled footer does not assert VintageHive's version under
    // someone else's name. That must stay a no-op for anyone who never opted in: unset falls back to
    // ApplicationVersion, so default output is byte-identical to before.
    [TestMethod]
    public async Task ProductVersion_Unset_FallsBackToApplicationVersion()
    {
        Mind.Db.ConfigSet(ConfigNames.ProductName, string.Empty);
        Mind.Db.ConfigSet(ConfigNames.ProductVersion, string.Empty);

        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("default-branding.example.com"), (req, res) => Task.FromResult(false));

        StringAssert.Contains(response, "<title>VintageHive - 404 NotFound</title>");
        StringAssert.Contains(response, $">VintageHive/{Mind.ApplicationVersion} - <b>");
    }

    // Catches the half-wired case: a token added to the HTML with no matching Replace in ProcessErrorResponse
    // ships a page with literal ||PRODUCT|| in it. Asserting on the whole rendered page keeps that honest for
    // every token, not just the ones a test happens to name.
    [TestMethod]
    public async Task BuiltIn404_LeavesNoUnsubstitutedTokens()
    {
        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("tokens-404.example.com"), (req, res) => Task.FromResult(false));

        Assert.IsFalse(response.Contains("||"), $"The rendered 404 still contains an unsubstituted token: {response}");
    }

    [TestMethod]
    public async Task BuiltIn500_LeavesNoUnsubstitutedTokens()
    {
        var response = await HttpErrorPageEnv.Get(HttpErrorPageEnv.Unique("tokens-500.example.com"), (req, res) => throw new InvalidOperationException("kaboom"));

        Assert.IsFalse(response.Contains("||"), $"The rendered 500 still contains an unsubstituted token: {response}");
    }

    // The remaining substitutions have to keep working alongside the new one - ||ERROR_MESSAGE|| in particular,
    // since DialNineProcessor sets ErrorMessage and relies on the built-in page to surface it.
    [TestMethod]
    public async Task BuiltIn404_StillSubstitutesRequestTraceIdAndErrorMessage()
    {
        var url = HttpErrorPageEnv.Unique("substitution-404.example.com");

        var response = await HttpErrorPageEnv.Get(url, (req, res) =>
        {
            res.ErrorMessage = "upstream refused the connection";

            res.SetStatusCode(HttpStatusCode.NotFound);

            return Task.FromResult(true);
        });

        StringAssert.Contains(response, "substitution-404.example.com");
        StringAssert.Contains(response, "upstream refused the connection");
        StringAssert.Contains(response, "TraceID: ");
    }
}
