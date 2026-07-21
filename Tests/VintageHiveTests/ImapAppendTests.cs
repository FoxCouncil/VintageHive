// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// IMAP APPEND coverage: the Outlook Express save-to-Sent-Items sequence (TRYCREATE -> CREATE ->
// APPEND with a synchronizing literal), literal capture that must never leak into the command
// parser, arg/size adversarial forms, and envelope-column hygiene (the PostOffice readers rehydrate
// fromAddress/toAddress with the throwing EmailAddress ctor, so a dirty stored header breaks every
// later SELECT). Uses the shared file-backed MailTestEnv contexts; each test runs a unique user and
// deletes its users, emails, and created mailboxes in finally so reruns start clean.

using System.Net;
using System.Text;
using VintageHive;
using VintageHive.Network;
using VintageHive.Proxy.Imap;

namespace Adversarial5.ImapAppend;

[TestClass]
public class ImapAppendTests
{
    private const string TestPassword = "hunter2";

    [TestInitialize]
    public void Init()
    {
        Mail.MailTestEnv.Ensure();
    }

    private static void CreateUser(string username)
    {
        if (!Mind.Db.UserExistsByUsername(username))
        {
            Assert.IsTrue(Mind.Db.UserCreate(username, TestPassword), $"could not create test user {username}");
        }
    }

    private static void CleanupUser(string username, params string[] extraMailboxes)
    {
        foreach (var mailbox in Mind.PostOfficeDb.GetMailboxesForUser(username))
        {
            foreach (var email in Mind.PostOfficeDb.GetMessagesForMailbox(mailbox.Id))
            {
                Mind.PostOfficeDb.DeleteEmailById(email.Id);
            }
        }

        foreach (var name in extraMailboxes)
        {
            Mind.PostOfficeDb.DeleteMailbox(username, name);
        }

        Mind.Db.UserDelete(username);
    }

    private static async Task<(ImapProxy proxy, ListenerSocket conn)> AuthedSession(string user)
    {
        var proxy = new ImapProxy(IPAddress.Loopback, 0);
        var conn = new ListenerSocket();

        await proxy.ProcessConnection(conn);

        var login = await Mail.MailTestEnv.Cmd(proxy, conn, $"a1 LOGIN {user} {TestPassword}");

        StringAssert.Contains(login, "a1 OK", $"test setup: LOGIN failed: {login}");

        return (proxy, conn);
    }

    private static string Message(string user, string subject)
    {
        return $"From: \"Fox Council\" <{user}@hive.com>\r\nTo: {user}@hive.com\r\nSubject: {subject}\r\nDate: Thu, 01 Jan 1998 00:00:00 +0000\r\n\r\nBody line one.\r\nBody line two.\r\n";
    }

    [TestMethod]
    public async Task Append_OutlookExpressSentItemsSequence_Succeeds()
    {
        var user = "apnd1";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSession(user);

            // Defensive: a crashed earlier run may have left the mailbox behind.
            Mind.PostOfficeDb.DeleteMailbox(user, "Sent Items");

            // OE's exact dance: APPEND to a missing folder, get TRYCREATE, CREATE it, retry.
            var missing = await Mail.MailTestEnv.Cmd(proxy, conn, "a2 APPEND \"Sent Items\" (\\Seen) {10}");

            StringAssert.Contains(missing, "a2 NO [TRYCREATE]", missing);

            var create = await Mail.MailTestEnv.Cmd(proxy, conn, "a3 CREATE \"Sent Items\"");

            StringAssert.Contains(create, "a3 OK", create);

            var message = Message(user, "ARF");

            var challenge = await Mail.MailTestEnv.Cmd(proxy, conn, $"a4 APPEND \"Sent Items\" (\\Seen) {{{message.Length}}}");

            StringAssert.StartsWith(challenge, "+ ", challenge);

            var done = await Mail.MailTestEnv.Cmd(proxy, conn, message);

            StringAssert.Contains(done, "a4 OK APPEND completed", done);

            // SELECT proves both delivery and column hygiene (it rehydrates the stored addresses).
            var select = await Mail.MailTestEnv.Cmd(proxy, conn, "a5 SELECT \"Sent Items\"");

            StringAssert.Contains(select, "* 1 EXISTS", select);
            StringAssert.Contains(select, "a5 OK", select);

            var fetch = await Mail.MailTestEnv.Cmd(proxy, conn, "a6 FETCH 1 (FLAGS BODY[])");

            StringAssert.Contains(fetch, "Subject: ARF", fetch);
            StringAssert.Contains(fetch, "a6 OK", fetch);
        }
        finally
        {
            CleanupUser(user, "Sent Items");
        }
    }

    [TestMethod]
    public async Task Append_LiteralSplitAcrossPackets_Reassembles()
    {
        var user = "apnd2";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSession(user);

            var message = Message(user, "split");

            await Mail.MailTestEnv.Cmd(proxy, conn, $"a2 APPEND INBOX {{{message.Length}}}");

            // Feed the literal in three ragged chunks; nothing may complete until the count drains.
            var first = Encoding.ASCII.GetBytes(message[..10]);
            var second = Encoding.ASCII.GetBytes(message[10..25]);
            var third = Encoding.ASCII.GetBytes(message[25..] + "\r\n");

            Assert.IsNull(await proxy.ProcessRequest(conn, first, first.Length));
            Assert.IsNull(await proxy.ProcessRequest(conn, second, second.Length));

            var done = await proxy.ProcessRequest(conn, third, third.Length);

            Assert.IsNotNull(done);
            StringAssert.Contains(Encoding.ASCII.GetString(done), "a2 OK APPEND completed", "");
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Append_CommandLookingLinesInsideLiteral_AreDataNotCommands()
    {
        var user = "apnd3";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSession(user);

            var message = $"From: {user}@hive.com\r\nTo: {user}@hive.com\r\nSubject: bait\r\n\r\na9 LOGOUT\r\na10 DELETE INBOX\r\n";

            await Mail.MailTestEnv.Cmd(proxy, conn, $"a2 APPEND INBOX {{{message.Length}}}");

            var done = await Mail.MailTestEnv.Cmd(proxy, conn, message);

            StringAssert.Contains(done, "a2 OK APPEND completed", done);
            Assert.IsFalse(done.Contains("BYE"), $"literal content was executed as commands: {done}");

            // Session must still be alive and INBOX intact.
            var noop = await Mail.MailTestEnv.Cmd(proxy, conn, "a3 NOOP");

            StringAssert.Contains(noop, "a3 OK", noop);
            Assert.IsNotNull(Mind.PostOfficeDb.GetMailboxByName(user, "INBOX"), "INBOX was deleted by literal content");
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Append_WithFlagsAndInternalDate_ParsesAndStores()
    {
        var user = "apnd4";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSession(user);

            var message = Message(user, "dated");

            var challenge = await Mail.MailTestEnv.Cmd(proxy, conn, $"a2 APPEND INBOX (\\Seen \\Flagged) \"01-Jan-1998 12:34:56 +0000\" {{{message.Length}}}");

            StringAssert.StartsWith(challenge, "+ ", challenge);

            var done = await Mail.MailTestEnv.Cmd(proxy, conn, message);

            StringAssert.Contains(done, "a2 OK APPEND completed", done);

            var inbox = Mind.PostOfficeDb.GetMailboxByName(user, "INBOX");
            var stored = Mind.PostOfficeDb.GetMessagesForMailbox(inbox!.Value.Id);

            Assert.AreEqual(1, stored.Count);
            StringAssert.Contains(stored[0].Flags, "\\Seen", stored[0].Flags);
            Assert.AreEqual(1998, stored[0].Date.Year);
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Append_NonSyncLiteral_NoContinuationButAccepted()
    {
        var user = "apnd5";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSession(user);

            var message = Message(user, "litplus");

            // LITERAL+ clients send the payload immediately after the command line, one packet.
            var raw = Encoding.ASCII.GetBytes($"a2 APPEND INBOX {{{message.Length}+}}\r\n{message}\r\n");

            var done = await proxy.ProcessRequest(conn, raw, raw.Length);

            Assert.IsNotNull(done);

            var text = Encoding.ASCII.GetString(done);

            Assert.IsFalse(text.Contains("+ Ready"), $"non-sync literal must not get a continuation: {text}");
            StringAssert.Contains(text, "a2 OK APPEND completed", text);
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Append_IntoSelectedMailbox_AnnouncesExistsAndRefreshesCache()
    {
        var user = "apnd6";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSession(user);

            await Mail.MailTestEnv.Cmd(proxy, conn, "a2 SELECT INBOX");

            var message = Message(user, "selfappend");

            await Mail.MailTestEnv.Cmd(proxy, conn, $"a3 APPEND INBOX {{{message.Length}}}");

            var done = await Mail.MailTestEnv.Cmd(proxy, conn, message);

            var inbox = Mind.PostOfficeDb.GetMailboxByName(user, "INBOX");
            var count = Mind.PostOfficeDb.GetMailboxStatus(inbox!.Value.Id).MessageCount;

            StringAssert.Contains(done, $"* {count} EXISTS", done);
            StringAssert.Contains(done, "a3 OK APPEND completed", done);

            // The refreshed cache must serve the new message by sequence number right away.
            var fetch = await Mail.MailTestEnv.Cmd(proxy, conn, $"a4 FETCH {count} (BODY[])");

            StringAssert.Contains(fetch, "Subject: selfappend", fetch);
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Append_AdversarialArgsAndSizes_RejectedInProtocol()
    {
        var user = "apnd7";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSession(user);

            // No literal, bare command, garbage size, overflow-bait digits, oversize.
            StringAssert.Contains(await Mail.MailTestEnv.Cmd(proxy, conn, "a2 APPEND INBOX"), "a2 BAD", "missing literal");
            StringAssert.Contains(await Mail.MailTestEnv.Cmd(proxy, conn, "a3 APPEND"), "a3 BAD", "no args");
            StringAssert.Contains(await Mail.MailTestEnv.Cmd(proxy, conn, "a4 APPEND INBOX {abc}"), "a4 BAD", "non-numeric size");
            StringAssert.Contains(await Mail.MailTestEnv.Cmd(proxy, conn, "a5 APPEND INBOX {123456789012345678901234}"), "a5 BAD", "size digits overflow the cap");
            StringAssert.Contains(await Mail.MailTestEnv.Cmd(proxy, conn, "a6 APPEND INBOX {99999999}"), "a6 NO", "oversize literal");
            StringAssert.Contains(await Mail.MailTestEnv.Cmd(proxy, conn, "a7 APPEND nosuchbox {10}"), "a7 NO [TRYCREATE]", "missing mailbox");

            // None of those may have armed literal capture: the next command must parse normally.
            var noop = await Mail.MailTestEnv.Cmd(proxy, conn, "a8 NOOP");

            StringAssert.Contains(noop, "a8 OK", noop);
        }
        finally
        {
            CleanupUser(user);
        }
    }

    [TestMethod]
    public async Task Append_BeforeAuthentication_RejectedBeforeLiteralArms()
    {
        Mail.MailTestEnv.Ensure();

        var proxy = new ImapProxy(IPAddress.Loopback, 0);
        var conn = new ListenerSocket();

        await proxy.ProcessConnection(conn);

        var resp = await Mail.MailTestEnv.Cmd(proxy, conn, "a1 APPEND INBOX {10}");

        StringAssert.Contains(resp, "a1 NO", resp);

        // The would-be literal must be treated as (bad) commands, not swallowed silently.
        var after = await Mail.MailTestEnv.Cmd(proxy, conn, "0123456789");

        Assert.IsNotNull(after, "unauthenticated APPEND armed literal capture");
    }

    [TestMethod]
    public async Task Append_ZeroLengthLiteral_CompletesOnTrailingCrlf()
    {
        var user = "apnd8";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSession(user);

            var challenge = await Mail.MailTestEnv.Cmd(proxy, conn, "a2 APPEND INBOX {0}");

            StringAssert.StartsWith(challenge, "+ ", challenge);

            var done = await Mail.MailTestEnv.Cmd(proxy, conn, "");

            Assert.IsNotNull(done);
            StringAssert.Contains(done, "a2 OK APPEND completed", done);
        }
        finally
        {
            CleanupUser(user);
        }
    }
}
