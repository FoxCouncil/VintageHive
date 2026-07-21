// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// FETCH response data-item naming (RFC 3501 7.4.2): the response item is named after the REQUEST
// item with .PEEK stripped; BODY.PEEK is request-only syntax and must never appear in server
// output. Violations here rendered populated folders as empty in Outlook Express (it matches
// responses to requests by item name). Every assertion drives the real SELECT/FETCH surface with a
// message inserted through the PostOffice context; literal byte counts are re-verified after the
// rename because the count-then-payload framing is what clients actually parse by.

using System.Net;
using VintageHive;
using VintageHive.Network;
using VintageHive.Proxy.Imap;

namespace Adversarial5.ImapFetchNaming;

[TestClass]
public class ImapFetchNamingTests
{
    private const string TestPassword = "hunter2";

    private const string MessageBody = "Hello body line one.\r\nLine two.\r\n";

    [TestInitialize]
    public void Init()
    {
        Mail.MailTestEnv.Ensure();
    }

    private static string MessageData(string user)
    {
        return $"From: \"Fox\" <{user}@hive.com>\r\nTo: {user}@hive.com\r\nSubject: naming\r\nReferences: <ref-1@hive.com>\r\nDate: Thu, 01 Jan 1998 00:00:00 +0000\r\n\r\n{MessageBody}";
    }

    private static void CreateUser(string username)
    {
        if (!Mind.Db.UserExistsByUsername(username))
        {
            Assert.IsTrue(Mind.Db.UserCreate(username, TestPassword), $"could not create test user {username}");
        }
    }

    private static void CleanupUser(string username)
    {
        foreach (var mailbox in Mind.PostOfficeDb.GetMailboxesForUser(username))
        {
            foreach (var email in Mind.PostOfficeDb.GetMessagesForMailbox(mailbox.Id))
            {
                Mind.PostOfficeDb.DeleteEmailById(email.Id);
            }
        }

        Mind.Db.UserDelete(username);
    }

    // Authenticated session with one known message in INBOX, INBOX selected.
    private static async Task<(ImapProxy proxy, ListenerSocket conn, int inboxId)> SelectedSession(string user)
    {
        var proxy = new ImapProxy(IPAddress.Loopback, 0);
        var conn = new ListenerSocket();

        await proxy.ProcessConnection(conn);

        var login = await Mail.MailTestEnv.Cmd(proxy, conn, $"a1 LOGIN {user} {TestPassword}");

        StringAssert.Contains(login, "a1 OK", $"test setup: LOGIN failed: {login}");

        var inbox = Mind.PostOfficeDb.GetMailboxByName(user, "INBOX");

        Assert.IsNotNull(inbox, "test setup: INBOX missing");

        Mind.PostOfficeDb.AppendMessage(inbox.Value.Id, "", new DateTime(1998, 1, 1), MessageData(user), $"{user}@hive.com", $"{user}@hive.com", "naming");

        var select = await Mail.MailTestEnv.Cmd(proxy, conn, "a2 SELECT INBOX");

        StringAssert.Contains(select, "a2 OK", $"test setup: SELECT failed: {select}");

        return (proxy, conn, inbox.Value.Id);
    }

    // Verifies "<name> {n}\r\n" followed by EXACTLY n payload chars and a structural delimiter,
    // returning the payload. This is the framing clients count bytes by.
    private static string AssertLiteral(string response, string itemName)
    {
        var marker = itemName + " {";
        var idx = response.IndexOf(marker, StringComparison.Ordinal);

        Assert.IsTrue(idx >= 0, $"response lacks item '{itemName}': {response}");

        var numStart = idx + marker.Length;
        var numEnd = response.IndexOf('}', numStart);

        Assert.IsTrue(numEnd > numStart, $"unterminated literal count: {response}");

        var count = int.Parse(response[numStart..numEnd]);

        Assert.AreEqual("\r\n", response.Substring(numEnd + 1, 2), "literal count must be followed by CRLF");

        var dataStart = numEnd + 3;

        Assert.IsTrue(response.Length > dataStart + count, $"literal shorter than declared count {count}: {response}");

        var following = response[dataStart + count];

        Assert.IsTrue(following == ')' || following == ' ', $"declared count {count} does not land on a structural delimiter (got '{following}'): {response}");

        return response.Substring(dataStart, count);
    }

    [TestMethod]
    public async Task Fetch_EveryRequestForm_AnswersWithMatchingItemName()
    {
        var user = "fnam1";

        CreateUser(user);

        try
        {
            var (proxy, conn, _) = await SelectedSession(user);

            var matrix = new (string Request, string ExpectedName)[]
            {
                ("BODY[]", "BODY[]"),
                ("BODY.PEEK[]", "BODY[]"),
                ("BODY[HEADER]", "BODY[HEADER]"),
                ("BODY.PEEK[HEADER]", "BODY[HEADER]"),
                ("BODY[TEXT]", "BODY[TEXT]"),
                ("BODY.PEEK[TEXT]", "BODY[TEXT]"),
                ("BODY[HEADER.FIELDS (Subject From)]", "BODY[HEADER.FIELDS (Subject From)]"),
                ("BODY.PEEK[HEADER.FIELDS (Subject From)]", "BODY[HEADER.FIELDS (Subject From)]"),
                ("RFC822", "RFC822"),
                ("RFC822.HEADER", "RFC822.HEADER"),
                ("RFC822.TEXT", "RFC822.TEXT"),
            };

            var tagNum = 10;

            foreach (var (request, expectedName) in matrix)
            {
                var tag = $"t{tagNum++}";

                var response = await Mail.MailTestEnv.Cmd(proxy, conn, $"{tag} FETCH 1 ({request})");

                StringAssert.Contains(response, $"{tag} OK", $"{request}: {response}");
                Assert.IsFalse(response.Contains("BODY.PEEK", StringComparison.OrdinalIgnoreCase), $"{request}: BODY.PEEK leaked into a response: {response}");

                AssertLiteral(response, expectedName);
            }
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Fetch_LiteralCounts_ExactAcrossContentShapes()
    {
        var user = "fnam2";

        CreateUser(user);

        try
        {
            var (proxy, conn, _) = await SelectedSession(user);

            // Full body: the literal is the entire stored message, byte for byte.
            var full = AssertLiteral(await Mail.MailTestEnv.Cmd(proxy, conn, "t1 FETCH 1 (BODY.PEEK[])"), "BODY[]");

            Assert.AreEqual(MessageData(user), full);

            // CRLF-heavy header subset in the client's requested casing.
            var fields = AssertLiteral(await Mail.MailTestEnv.Cmd(proxy, conn, "t2 FETCH 1 (BODY.PEEK[HEADER.FIELDS (Subject References)])"), "BODY[HEADER.FIELDS (Subject References)]");

            StringAssert.Contains(fields, "Subject: naming", fields);
            StringAssert.Contains(fields, "References: <ref-1@hive.com>", fields);

            // Fields that do not exist in the message: the count must still frame the (empty-ish)
            // payload exactly - a drifted count desynchronizes the client's stream parser.
            AssertLiteral(await Mail.MailTestEnv.Cmd(proxy, conn, "t3 FETCH 1 (BODY.PEEK[HEADER.FIELDS (X-Nonexistent-Header)])"), "BODY[HEADER.FIELDS (X-Nonexistent-Header)]");

            // Body text only.
            var text = AssertLiteral(await Mail.MailTestEnv.Cmd(proxy, conn, "t4 FETCH 1 (BODY.PEEK[TEXT])"), "BODY[TEXT]");

            StringAssert.Contains(text, "Hello body line one.", text);
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Fetch_PeekVersusNonPeek_SeenSemanticsUnchangedByRename()
    {
        var user = "fnam3";

        CreateUser(user);

        try
        {
            var (proxy, conn, inboxId) = await SelectedSession(user);

            await Mail.MailTestEnv.Cmd(proxy, conn, "t1 FETCH 1 (BODY.PEEK[])");

            var afterPeek = Mind.PostOfficeDb.GetMessagesForMailbox(inboxId)[0].Flags;

            Assert.IsFalse(afterPeek.Contains(@"\Seen"), $"PEEK must not set \\Seen: {afterPeek}");

            await Mail.MailTestEnv.Cmd(proxy, conn, "t2 FETCH 1 (BODY[])");

            var afterFetch = Mind.PostOfficeDb.GetMessagesForMailbox(inboxId)[0].Flags;

            StringAssert.Contains(afterFetch, @"\Seen", $"non-PEEK BODY[] must set \\Seen: {afterFetch}");
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Fetch_OutlookExpressHeaderSyncCommand_ProducesMatchableResponse()
    {
        var user = "fnam4";

        CreateUser(user);

        try
        {
            var (proxy, conn, _) = await SelectedSession(user);

            // The client's real header-sync command, verbatim from the live capture.
            var response = await Mail.MailTestEnv.Cmd(proxy, conn, "t1 FETCH 1:* (BODY.PEEK[HEADER.FIELDS (References X-Ref X-Priority X-MSMail-Priority X-MSOESRec Newsgroups)] ENVELOPE RFC822.SIZE UID FLAGS INTERNALDATE)");

            StringAssert.Contains(response, "t1 OK", response);
            Assert.IsFalse(response.Contains("BODY.PEEK", StringComparison.OrdinalIgnoreCase), $"BODY.PEEK leaked: {response}");

            // The item name comes back with the original field-list casing so OE can match it.
            AssertLiteral(response, "BODY[HEADER.FIELDS (References X-Ref X-Priority X-MSMail-Priority X-MSOESRec Newsgroups)]");

            StringAssert.Contains(response, "ENVELOPE (", response);
            StringAssert.Contains(response, "RFC822.SIZE ", response);
            StringAssert.Contains(response, "UID ", response);
            StringAssert.Contains(response, "FLAGS (", response);
            StringAssert.Contains(response, "INTERNALDATE \"", response);
        }
        finally
        {
            CleanupUser(user);
        }
    }
}
