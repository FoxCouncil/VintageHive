// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Security.Cryptography;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Yahoo;
using VintageHive.Proxy.Yahoo.Pager;
using static VintageHive.Proxy.Http.HttpUtilities;
using HttpStatusCode = VintageHive.Proxy.Http.HttpStatusCode;

namespace VintageHive.Processors;

/// <summary>
/// Serves the HTTP side of the pre-YMSG Yahoo! Pager, for the client generation that never opens port 5050
/// and so cannot reach <see cref="Proxy.Yahoo.YmsgServer"/> at all.
///
/// <para>
/// SOURCES. The request side is a capture from a real client (Yahoo! Pager on Win98, intercept-DNS). The
/// response side is NOT guesswork and is not derived from that capture: it comes from libyahoo 0.18.4, the
/// GPL client library behind GTKYahoo, which is descended from Doug Winslow's yppro2.c prototype pager code
/// and targets exactly this protocol generation. Its <c>protocol.txt</c> documents the sign-on exchange with
/// a verbatim capture of the response body, and <c>yahoo_fetchcookies</c> / <c>yahoo_get_config</c> are the
/// parsers this code is written against. Where something is not recoverable from that reference it is called
/// out inline rather than quietly invented.
/// </para>
///
/// <para>
/// SCOPE. Sign-on and the version check only. The polled message transport (/notify/) is deliberately not
/// implemented here: nothing in the reference pins the wire dialect THIS client build speaks, so it answers
/// 503 until a capture with request bodies settles it. The transport, when it lands, shares
/// <see cref="YahooSessionRegistry"/> with YMSG - one identity, one presence, one delivery path.
/// </para>
/// </summary>
public static class YahooPagerProcessor
{
    // The reference client reads the body a line at a time and deletes non-printables, so CRLF and bare LF are
    // both safe for it. CRLF is what a period HTTP server would have sent, so that is the default - but it is
    // one constant precisely because no capture of the real response exists to settle it.
    const string LineEnding = "\r\n";

    const string InvalidLogin = "ERROR: Invalid NCC Login";

    // The group every account is filed under, matching the server-built roster YMSG hands out.
    const string RosterGroup = "Hive";

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        await Task.Delay(0);

        var host = req.Host;

        if (!PagerHosts.IsPagerHost(host))
        {
            return false;
        }

        if (!Mind.Db.ConfigGet<bool>(ConfigNames.ServiceYahooPager))
        {
            return false;
        }

        var path = req.Uri.AbsolutePath;

        if (string.Equals(host, PagerHosts.Auth, StringComparison.OrdinalIgnoreCase))
        {
            // Both endpoints answer with the same account block; only ncclogin takes credentials.
            if (path.Equals("/config/ncclogin", StringComparison.OrdinalIgnoreCase))
            {
                return HandleNccLogin(req, res);
            }

            // Not in the captured sequence, but libyahoo issues it straight after sign-on with the cookie from
            // ncclogin, and it returns the same body. Cheap to answer, and a client that does issue it would
            // otherwise get a 404 in the middle of signing on.
            if (path.Equals("/config/get_buddylist", StringComparison.OrdinalIgnoreCase))
            {
                return HandleGetBuddyList(req, res);
            }

            return NotHandledHere(req, res, "unknown auth endpoint");
        }

        if (string.Equals(host, PagerHosts.Update, StringComparison.OrdinalIgnoreCase))
        {
            return HandleClientsList(req, res, path);
        }

        // Everything left is the transport host.
        return HandleNotify(req, res);
    }

    // GET /config/ncclogin?.src=bl&login=<user>&passwd=<plaintext>&n=1&t=1
    //
    // Verification is a straight password compare - there is no crypt to reproduce, unlike YMSG v9, because
    // this protocol generation puts the password in the query string in the clear. That is also why the whole
    // request must stay out of the cache and out of every log: see UrlRedactor, which HttpProxy applies to
    // this URL on the way to the requests table, the log tables and the error page.
    static bool HandleNccLogin(HttpRequest req, HttpResponse res)
    {
        // Belt and braces. HttpProxy already refuses to cache a URL carrying a password, so this cannot be the
        // only thing standing between a login response and the cache database - but a response that mints a
        // session token has no business being cacheable regardless of how it was addressed.
        res.Cache = false;

        var login = Query(req, "login");
        var password = Query(req, "passwd");

        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password) || !VerifyPassword(login, password))
        {
            // The reference client matches this line exactly (case-insensitively) and reports a bad password.
            // Status stays 200: it reads the response line by line and never looks at the status line, so an
            // error status would only risk an intermediary rewriting the body out from under it.
            Log.WriteLine(Log.LEVEL_WARN, nameof(YahooPagerProcessor), $"Pager sign-on refused for '{login}'", req.ListenerSocket.TraceId.ToString());

            res.SetBodyString(InvalidLogin + LineEnding, HttpContentTypeMimeType.Text.Plain);

            return true;
        }

        // A fresh sign-on invalidates the account's older tokens: one identity, one live session, which is the
        // same rule the session registry enforces for YMSG.
        PagerLoginTokens.RevokeAllFor(login);

        var token = PagerLoginTokens.Mint(login);

        res.SetCookie("Y", BuildCookie(token));

        res.SetBodyString(BuildAccountBlock(login), HttpContentTypeMimeType.Text.Plain);

        Log.WriteLine(Log.LEVEL_INFO, nameof(YahooPagerProcessor), $"Pager sign-on for '{login}'", req.ListenerSocket.TraceId.ToString());

        Mind.Db?.RequestsTrack(req.ListenerSocket, req.UserAgent, "PAGER", $"logon {login}", nameof(YahooPagerProcessor));

        return true;
    }

    // GET /config/get_buddylist?.src=bl, authenticated by the Y cookie rather than by credentials.
    static bool HandleGetBuddyList(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        var username = PagerLoginTokens.Resolve(TokenFromCookie(req));

        if (username == null)
        {
            res.SetBodyString(InvalidLogin + LineEnding, HttpContentTypeMimeType.Text.Plain);

            return true;
        }

        // libyahoo expects a second cookie here (B=<token>&b=3) and stores it, but never reads it back. It is
        // emitted for shape only; nothing on this side consults it.
        res.SetCookie("B", $"{PagerLoginTokens.RandomToken(13)}&b=3; path=/; domain=.yahoo.com");

        res.SetBodyString(BuildAccountBlock(username), HttpContentTypeMimeType.Text.Plain);

        return true;
    }

    // HEAD then GET of /clients.html on update.pager.yahoo.com, the client's self-update check.
    //
    // UNVERIFIED, AND DELIBERATELY INERT. No reference for this format exists: libyahoo never implemented the
    // update check, and although the Wayback Machine has the URL captured (2001-10-07) every capture on that
    // host returned the site's robots.txt to the crawler rather than the real document. So rather than invent
    // a format, this answers the request successfully with a document that advertises nothing. A client
    // parsing it finds no newer build and has no reason to nag or self-update; a client that only wants the
    // request to succeed gets that. The HEAD and the GET agree on Content-Length, Last-Modified and ETag, so a
    // conditional or diffing update check sees a stable resource that never changes.
    static bool HandleClientsList(HttpRequest req, HttpResponse res, string path)
    {
        if (!path.Equals("/clients.html", StringComparison.OrdinalIgnoreCase))
        {
            return NotHandledHere(req, res, "unknown update endpoint");
        }

        var body = $"<html><head><title>Clients</title></head><body></body></html>{LineEnding}";

        var bytes = Encoding.UTF8.GetBytes(body);

        // Fixed, not DateTime.UtcNow: the whole point is that this resource never appears to change.
        res.Headers.AddOrUpdate("Last-Modified", new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("R"));
        res.Headers.AddOrUpdate("ETag", "\"pager-clients-1\"");

        if (req.Method == HttpMethodName.Head)
        {
            // A HEAD must carry the headers the GET would, with no body. An empty (not null) body is what tells
            // HttpProxy this response was authored rather than written straight to the socket, so it still
            // emits the header block; the real length is then restored over the zero SetBodyData computed.
            res.SetBodyData(Array.Empty<byte>(), HttpContentTypeMimeType.Text.Html);

            res.Headers.AddOrUpdate(HttpHeaderName.ContentLength, bytes.Length.ToString());

            return true;
        }

        res.SetBodyData(bytes, HttpContentTypeMimeType.Text.Html);

        return true;
    }

    // POST /notify/ - the polled message transport, not implemented yet.
    //
    // Answered here rather than left to fall through on purpose. These hostnames are claimed by VintageHive, so
    // letting the request reach the archive processor would send a lookup outward for a host the archive cannot
    // possibly have a useful capture of - which is both pointless and the wrong posture for a walled plane.
    static bool HandleNotify(HttpRequest req, HttpResponse res)
    {
        res.Cache = false;

        res.ErrorMessage = "The Yahoo! Pager message transport is not implemented yet; sign-on and the version check are.";

        res.SetStatusCode(HttpStatusCode.ServiceUnavailable);

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(YahooPagerProcessor), $"Pager transport not implemented: {req.Method} {req.Uri.AbsolutePath}", req.ListenerSocket.TraceId.ToString());

        return true;
    }

    // A claimed host with an unclaimed path. Answered as a 404 here rather than returned false, so the request
    // does not continue down the chain to the outward-reaching processors.
    static bool NotHandledHere(HttpRequest req, HttpResponse res, string why)
    {
        res.Cache = false;

        res.SetStatusCode(HttpStatusCode.NotFound);

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(YahooPagerProcessor), $"{why}: {req.Uri.AbsolutePath}", req.ListenerSocket.TraceId.ToString());

        return true;
    }

    /// <summary>
    /// The account block both endpoints return, verbatim in shape from libyahoo's protocol.txt:
    /// <code>
    /// OK
    /// BEGIN BUDDYLIST
    /// Friends:tranec
    /// END BUDDYLIST
    /// BEGIN IGNORELIST
    ///
    /// END IGNORELIST
    /// BEGIN IDENTITIES
    /// tranec2
    /// END IDENTITIES
    /// Mail=0
    /// Login=tranec2
    /// </code>
    /// The roster is server-built - every other account is everyone's buddy - which is the same rule
    /// <see cref="Proxy.Yahoo.YmsgServer"/> applies when it builds the YMSG roster field, so the two protocols
    /// show the same buddy list.
    /// </summary>
    internal static string BuildAccountBlock(string username)
    {
        var buddies = YahooSessionRegistry.OtherUsernames(username);

        var block = new StringBuilder();

        block.Append("OK").Append(LineEnding);

        block.Append("BEGIN BUDDYLIST").Append(LineEnding);

        if (buddies.Count > 0)
        {
            block.Append($"{RosterGroup}:{string.Join(",", buddies)}").Append(LineEnding);
        }

        block.Append("END BUDDYLIST").Append(LineEnding);

        // The reference has no parser for this section at all, so it is emitted empty purely to match the
        // captured shape - a client that scans for the markers finds them where it expects.
        block.Append("BEGIN IGNORELIST").Append(LineEnding);
        block.Append(LineEnding);
        block.Append("END IGNORELIST").Append(LineEnding);

        // Secondary identities are not a thing on a hive account, so the list is just the login itself.
        block.Append("BEGIN IDENTITIES").Append(LineEnding);
        block.Append(username).Append(LineEnding);
        block.Append("END IDENTITIES").Append(LineEnding);

        // Mail=0 on purpose. VintageHive does host mailboxes, but nothing is known about what this client does
        // when told it has Yahoo! mail, and the likely answer is that it starts polling an endpoint that does
        // not exist here. Flip it when there is something to point it at.
        block.Append("Mail=0").Append(LineEnding);

        block.Append($"Login={username}").Append(LineEnding);

        return block.ToString();
    }

    /// <summary>
    /// The sign-on cookie: <c>Y=v=1&amp;n=&lt;token&gt;&amp;l=..&amp;p=..&amp;r=..&amp;lg=us&amp;intl=us</c>.
    /// <para>
    /// Only <c>n</c> is load-bearing. The reference client keeps the whole value verbatim to echo back as a
    /// Cookie header, and separately extracts <c>n</c> as its session handle; it never reads the rest. The
    /// other sub-values are real Yahoo! account fields nobody has a description of, so they are emitted in the
    /// right character class and the right order and nothing here depends on their content.
    /// </para>
    /// </summary>
    internal static string BuildCookie(string token)
    {
        // Random per sign-on rather than a fixed string, so nothing downstream can start treating one of these
        // as a stable identifier for the account.
        var l = PagerLoginTokens.RandomToken(9);
        var p = PagerLoginTokens.RandomToken(14);

        return $"v=1&n={token}&l={l}&p={p}&r=83&lg=us&intl=us; path=/; domain=.yahoo.com";
    }

    // Pulls the n= sub-value back out of the Y cookie the client echoes, the same way the client extracted it.
    internal static string TokenFromCookie(HttpRequest req)
    {
        if (!req.Cookies.TryGetValue("Y", out var cookie) || string.IsNullOrEmpty(cookie))
        {
            return null;
        }

        foreach (var part in cookie.Split('&'))
        {
            if (part.StartsWith("n=", StringComparison.Ordinal))
            {
                return part[2..];
            }
        }

        return null;
    }

    static string Query(HttpRequest req, string name)
    {
        if (req.QueryParams == null)
        {
            return null;
        }

        return req.QueryParams.TryGetValue(name, out var value) ? value : null;
    }

    // Fixed-time compare against the stored password, mirroring how YmsgServer verifies its crypt response.
    // Done here rather than by handing the password to UserFetch's SQL clause: that comparison runs under
    // SQLite's collation rather than an explicit one, and an empty password would drop the clause entirely
    // and match on username alone.
    static bool VerifyPassword(string username, string password)
    {
        var user = Mind.Db?.UserFetch(username);

        if (user == null || string.IsNullOrEmpty(user.Password))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user.Password), Encoding.UTF8.GetBytes(password));
    }
}
