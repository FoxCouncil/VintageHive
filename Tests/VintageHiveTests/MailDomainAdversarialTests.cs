// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Adversarial coverage of the hosted mail-domain list (ConfigNames.ValidMailDomains / MailDomains):
// login resolution across POP3 USER, IMAP LOGIN, and SMTP AUTH LOGIN; MAIL FROM sender-domain and
// RCPT TO relay enforcement; runtime list mutation without restart; and proof that the historical
// misdelivery (mail for fred@gmail.com landing in local fred's inbox) is dead at both the RCPT door
// and the postmaster queue. Uses the shared file-backed MailTestEnv contexts; every test that
// mutates config or creates users/emails restores state in finally/cleanup so the persistent test
// DB cannot poison other suites (which pin banners against the default hive.com domain).

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Adversarial5.Smtp;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Imap;
using VintageHive.Proxy.Pop3;
using VintageHive.Proxy.Smtp;

namespace Adversarial5.MailDomain;

[TestClass]
public class MailDomainAdversarialTests
{
    private const string HostedDomain = "example.com";
    private const string TestPassword = "hunter2";

    [TestInitialize]
    public void Init()
    {
        Mail.MailTestEnv.Ensure();

        SetDomains(HostedDomain);
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Restore the shipped default so suites that pin hive.com banners see a clean config.
        SetDomains(HiveDomains.Base);
    }

    private static void SetDomains(string csv)
    {
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, csv);
    }

    private static string B64(string value)
    {
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(value));
    }

    // Unique-named user per test so tests stay order-independent against the persistent DB.
    private static void CreateUser(string username)
    {
        if (!Mind.Db.UserExistsByUsername(username))
        {
            Assert.IsTrue(Mind.Db.UserCreate(username, TestPassword), $"could not create test user {username}");
        }
    }

    private static void DeleteUserAndMail(string username)
    {
        foreach (var email in Mind.PostOfficeDb.GetDeliveredEmailsForUser(username))
        {
            Mind.PostOfficeDb.DeleteEmailById(email.Id);
        }

        foreach (var email in Mind.PostOfficeDb.GetUndeliveredEmails().Where(e => e.ToAddress.User == username || e.FromAddress.User == username))
        {
            Mind.PostOfficeDb.DeleteEmailById(email.Id);
        }

        Mind.Db.UserDelete(username);
    }

    #region POP3 USER resolution

    private static async Task<(Pop3Proxy proxy, ListenerSocket conn)> Pop3Session()
    {
        var proxy = new Pop3Proxy(IPAddress.Loopback, 0);
        var conn = new ListenerSocket();

        await proxy.ProcessConnection(conn);

        return (proxy, conn);
    }

    [TestMethod]
    public async Task Pop3_User_BareLocalPart_StaysValid()
    {
        var (proxy, conn) = await Pop3Session();

        var resp = await Mail.MailTestEnv.Cmd(proxy, conn, "USER fred");

        StringAssert.StartsWith(resp, "+OK", resp);
        Assert.AreEqual("fred", conn.DataBag["username"]);
    }

    [TestMethod]
    public async Task Pop3_User_HostedDomain_ResolvesToLocalPart()
    {
        var (proxy, conn) = await Pop3Session();

        var resp = await Mail.MailTestEnv.Cmd(proxy, conn, $"USER fred@{HostedDomain}");

        StringAssert.StartsWith(resp, "+OK", resp);
        Assert.AreEqual("fred", conn.DataBag["username"]);
    }

    [TestMethod]
    public async Task Pop3_User_HostedDomainMixedCase_ResolvesToLocalPart()
    {
        var (proxy, conn) = await Pop3Session();

        var resp = await Mail.MailTestEnv.Cmd(proxy, conn, "USER fred@EXAMPLE.COM");

        StringAssert.StartsWith(resp, "+OK", resp);
        Assert.AreEqual("fred", conn.DataBag["username"]);
    }

    [TestMethod]
    public async Task Pop3_User_ForeignDomain_RejectedNotStripped()
    {
        var (proxy, conn) = await Pop3Session();

        var resp = await Mail.MailTestEnv.Cmd(proxy, conn, "USER fred@evil.com");

        StringAssert.StartsWith(resp, "-ERR", resp);

        // The rejection must not leave 'fred' staged for PASS - that would be silent domain stripping.
        Assert.AreEqual(string.Empty, conn.DataBag["username"]);
    }

    [TestMethod]
    public async Task Pop3_User_LookalikeDomain_Rejected()
    {
        var (proxy, conn) = await Pop3Session();

        var resp = await Mail.MailTestEnv.Cmd(proxy, conn, "USER fred@examp1e.com");

        StringAssert.StartsWith(resp, "-ERR", resp);
    }

    [TestMethod]
    public async Task Pop3_User_MalformedQualifiedForms_Rejected()
    {
        foreach (var form in new[] { "fred@", "@example.com", "fred@@example.com", "fred@exa mple.com", "fred@exa\tmple.com", "fred@example.com>" })
        {
            var (proxy, conn) = await Pop3Session();

            var resp = await Mail.MailTestEnv.Cmd(proxy, conn, $"USER {form}");

            StringAssert.StartsWith(resp, "-ERR", $"USER {form} should be rejected, got: {resp}");
            Assert.AreEqual(string.Empty, conn.DataBag["username"], $"USER {form} leaked a username");
        }
    }

    [TestMethod]
    public async Task Pop3_FullLogin_WithHostedMixedCaseDomain_AuthenticatesLocalUser()
    {
        var user = "mdp3fred";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await Pop3Session();

            await Mail.MailTestEnv.Cmd(proxy, conn, $"USER {user}@EXAMPLE.COM");

            var resp = await Mail.MailTestEnv.Cmd(proxy, conn, $"PASS {TestPassword}");

            StringAssert.StartsWith(resp, "+OK", resp);
            Assert.AreEqual(true, conn.DataBag["auth"]);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    #endregion

    #region IMAP LOGIN resolution

    private static async Task<(ImapProxy proxy, ListenerSocket conn)> ImapSession()
    {
        var proxy = new ImapProxy(IPAddress.Loopback, 0);
        var conn = new ListenerSocket();

        await proxy.ProcessConnection(conn);

        return (proxy, conn);
    }

    [TestMethod]
    public async Task Imap_Login_HostedDomain_AuthenticatesLocalUser()
    {
        var user = "mdimfred";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await ImapSession();

            var resp = await Mail.MailTestEnv.Cmd(proxy, conn, $"a1 LOGIN {user}@{HostedDomain} {TestPassword}");

            StringAssert.Contains(resp, "a1 OK", resp);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Imap_Login_ForeignDomain_RejectedAsDomainNotPasswordFailure()
    {
        // Usernames are capped at 8 chars (UserCreate's retro 3-8 rule) - keep fixture names short.
        var user = "mdimfr2";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await ImapSession();

            var resp = await Mail.MailTestEnv.Cmd(proxy, conn, $"a1 LOGIN {user}@evil.com {TestPassword}");

            StringAssert.Contains(resp, "a1 NO", resp);
            StringAssert.Contains(resp, "not hosted", resp);

            // Correct credentials + foreign domain must NOT authenticate (that would be stripping).
            Assert.IsFalse(resp.Contains("OK LOGIN completed"), resp);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Imap_Authenticate_HostedMixedCaseDomain_AuthenticatesLocalUser()
    {
        var user = "mdia1";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await ImapSession();

            var challenge = await Mail.MailTestEnv.Cmd(proxy, conn, "A1 AUTHENTICATE LOGIN");

            StringAssert.StartsWith(challenge, "+ ", challenge);

            await Mail.MailTestEnv.Cmd(proxy, conn, B64($"{user}@EXAMPLE.COM"));

            var resp = await Mail.MailTestEnv.Cmd(proxy, conn, B64(TestPassword));

            StringAssert.Contains(resp, "A1 OK AUTHENTICATE completed", resp);

            // Re-running AUTHENTICATE on the authenticated session must be refused.
            var again = await Mail.MailTestEnv.Cmd(proxy, conn, "A2 AUTHENTICATE LOGIN");

            StringAssert.StartsWith(again, "A2 BAD", again);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Imap_Authenticate_ForeignDomain_CorrectPasswordNeverHelps()
    {
        var user = "mdia2";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await ImapSession();

            await Mail.MailTestEnv.Cmd(proxy, conn, "A1 AUTHENTICATE LOGIN");

            var resp = await Mail.MailTestEnv.Cmd(proxy, conn, B64($"{user}@evil.com"));

            StringAssert.StartsWith(resp, "A1 NO", resp);
            StringAssert.Contains(resp, "not hosted", resp);

            // The exchange is dead; the would-be password line is a command-parse failure, not a login.
            var after = await Mail.MailTestEnv.Cmd(proxy, conn, B64(TestPassword));

            Assert.IsFalse(after.Contains("OK AUTHENTICATE completed"), after);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Imap_Authenticate_CurlCommandSequence_ReachesInbox()
    {
        var user = "mdia3";

        CreateUser(user);

        try
        {
            // The exact conversation curl runs for "imap://host/INBOX --user fred:pass": capability
            // probe, SASL AUTHENTICATE against the advertised LOGIN mechanism, then SELECT.
            var (proxy, conn) = await ImapSession();

            var capa = await Mail.MailTestEnv.Cmd(proxy, conn, "A001 CAPABILITY");

            StringAssert.Contains(capa, "AUTH=LOGIN", capa);

            var challenge = await Mail.MailTestEnv.Cmd(proxy, conn, "A002 AUTHENTICATE LOGIN");

            StringAssert.StartsWith(challenge, "+ ", challenge);

            await Mail.MailTestEnv.Cmd(proxy, conn, B64(user));

            var auth = await Mail.MailTestEnv.Cmd(proxy, conn, B64(TestPassword));

            StringAssert.Contains(auth, "A002 OK AUTHENTICATE completed", auth);

            var select = await Mail.MailTestEnv.Cmd(proxy, conn, "A003 SELECT INBOX");

            StringAssert.Contains(select, "A003 OK", select);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    #endregion

    #region SMTP AUTH LOGIN resolution

    [TestMethod]
    public async Task Smtp_Auth_HostedDomain_AuthenticatesLocalUser()
    {
        var user = "mdsmfred";

        CreateUser(user);

        try
        {
            var proxy = SmtpAdv.NewProxy();
            var conn = await SmtpAdv.Connect(proxy);

            await SmtpAdv.Send(proxy, conn, "AUTH LOGIN\r\n");
            await SmtpAdv.Send(proxy, conn, B64($"{user}@EXAMPLE.COM") + "\r\n");

            var resp = await SmtpAdv.Send(proxy, conn, B64(TestPassword) + "\r\n");

            Assert.AreEqual("235", SmtpAdv.Code(resp), resp);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Smtp_Auth_ForeignOrMalformedUsername_FailsAtUsernameStep()
    {
        foreach (var form in new[] { "fred@evil.com", "fred@", "@example.com", "fred@@example.com" })
        {
            var proxy = SmtpAdv.NewProxy();
            var conn = await SmtpAdv.Connect(proxy);

            await SmtpAdv.Send(proxy, conn, "AUTH LOGIN\r\n");

            var resp = await SmtpAdv.Send(proxy, conn, B64(form) + "\r\n");

            Assert.AreEqual("535", SmtpAdv.Code(resp), $"AUTH username {form}: {resp}");

            // The handshake must be fully aborted - a follow-up line is an unknown command (500),
            // not a password challenge continuation.
            var after = await SmtpAdv.Send(proxy, conn, B64(TestPassword) + "\r\n");

            Assert.AreEqual("500", SmtpAdv.Code(after), $"AUTH state leaked after {form}: {after}");
        }
    }

    #endregion

    #region SMTP MAIL FROM / RCPT TO enforcement

    private static async Task<(SmtpProxy proxy, ListenerSocket conn)> AuthedSmtpSession(string user)
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        await SmtpAdv.Send(proxy, conn, "EHLO tester\r\n");
        await SmtpAdv.Send(proxy, conn, "AUTH LOGIN\r\n");
        await SmtpAdv.Send(proxy, conn, B64(user) + "\r\n");

        var resp = await SmtpAdv.Send(proxy, conn, B64(TestPassword) + "\r\n");

        Assert.AreEqual("235", SmtpAdv.Code(resp), $"test setup: AUTH failed: {resp}");

        return (proxy, conn);
    }

    [TestMethod]
    public async Task Smtp_MailFrom_HostedOwnAddress_Accepted()
    {
        var user = "mdmf1";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSmtpSession(user);

            var resp = await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{user}@{HostedDomain}>\r\n");

            Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Smtp_MailFrom_ForeignDomain_Rejected()
    {
        var user = "mdmf2";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSmtpSession(user);

            var resp = await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{user}@evil.com>\r\n");

            Assert.AreEqual("553", SmtpAdv.Code(resp), resp);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Smtp_MailFrom_OtherHostedUser_StillRejected()
    {
        var user = "mdmf3";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSmtpSession(user);

            var resp = await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<somebodyelse@{HostedDomain}>\r\n");

            Assert.AreEqual("550", SmtpAdv.Code(resp), resp);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Smtp_Rcpt_RelayAttempts_Get5xx()
    {
        var user = "mdrc1";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSmtpSession(user);

            await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{user}@{HostedDomain}>\r\n");

            foreach (var target in new[] { "bob@gmail.com", "bob@examp1e.com", "bob@example.com.evil.com" })
            {
                var resp = await SmtpAdv.Send(proxy, conn, $"RCPT TO:<{target}>\r\n");

                Assert.AreEqual("550", SmtpAdv.Code(resp), $"RCPT {target}: {resp}");
            }

            // Angle-bracket abuse: no parseable hosted address -> in-protocol 553, not an exception.
            var malformed = await SmtpAdv.Send(proxy, conn, $"RCPT TO:<bob@{HostedDomain}@evil.com>\r\n");

            Assert.AreEqual("553", SmtpAdv.Code(malformed), malformed);

            // First parseable address wins and it is foreign -> rejected despite the hosted decoy.
            var decoy = await SmtpAdv.Send(proxy, conn, $"RCPT TO:<bob@evil.com> <bob@{HostedDomain}>\r\n");

            Assert.AreEqual("550", SmtpAdv.Code(decoy), decoy);

            // No recipient was accepted, so DATA must still be refused as out of sequence.
            var data = await SmtpAdv.Send(proxy, conn, "DATA\r\n");

            Assert.AreEqual("503", SmtpAdv.Code(data), data);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Smtp_Rcpt_HostedDomain_Accepted()
    {
        var user = "mdrc2";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSmtpSession(user);

            await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{user}@{HostedDomain}>\r\n");

            var resp = await SmtpAdv.Send(proxy, conn, $"RCPT TO:<bob@{HostedDomain}>\r\n");

            Assert.AreEqual("250", SmtpAdv.Code(resp), resp);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    #endregion

    #region Runtime list mutation

    [TestMethod]
    public async Task RuntimeAdd_SecondDomain_WorksWithoutRestart()
    {
        var user = "mdrt1";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSmtpSession(user);

            // Not hosted yet.
            await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{user}@{HostedDomain}>\r\n");

            var before = await SmtpAdv.Send(proxy, conn, "RCPT TO:<bob@second.com>\r\n");

            Assert.AreEqual("550", SmtpAdv.Code(before), before);

            // The embedding host adds a domain at runtime; the SAME session honors it on the next read.
            SetDomains($"{HostedDomain},second.com");

            var after = await SmtpAdv.Send(proxy, conn, "RCPT TO:<bob@second.com>\r\n");

            Assert.AreEqual("250", SmtpAdv.Code(after), after);

            // And a brand-new POP3 login against the added domain resolves without restart.
            var (pop3, pop3Conn) = await Pop3Session();

            var userResp = await Mail.MailTestEnv.Cmd(pop3, pop3Conn, $"USER {user}@second.com");

            StringAssert.StartsWith(userResp, "+OK", userResp);
            Assert.AreEqual(user, pop3Conn.DataBag["username"]);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task RuntimeRemove_BetweenAuthAndMailFrom_RejectsSender()
    {
        var user = "mdrt2";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSmtpSession(user);

            SetDomains("other.com");

            var resp = await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{user}@{HostedDomain}>\r\n");

            Assert.AreEqual("553", SmtpAdv.Code(resp), resp);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task RuntimePrimaryChange_BannerFollowsWithoutRestart()
    {
        SetDomains("brandnew.test");

        var greeting = await SmtpAdv.Greet(SmtpAdv.NewProxy(), new ListenerSocket());

        StringAssert.Contains(greeting, "smtp.brandnew.test", greeting);
    }

    #endregion

    #region Misdelivery is dead

    [TestMethod]
    public async Task Rcpt_ForeignDomainSameLocalPart_NeverReachesLocalInbox()
    {
        var user = "mdmd1";

        CreateUser(user);

        try
        {
            var (proxy, conn) = await AuthedSmtpSession(user);

            await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{user}@{HostedDomain}>\r\n");

            var rcpt = await SmtpAdv.Send(proxy, conn, $"RCPT TO:<{user}@gmail.com>\r\n");

            Assert.AreEqual("550", SmtpAdv.Code(rcpt), rcpt);

            // Nothing queued, nothing delivered - the address never entered the system.
            Assert.IsFalse(Mind.PostOfficeDb.GetUndeliveredEmails().Any(e => e.ToAddress.User == user), "foreign-domain recipient was queued");
            Assert.AreEqual(0, Mind.PostOfficeDb.GetDeliveredEmailsForUser(user).Count, "foreign-domain mail landed in a local mailbox");
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public async Task Bounce_FullRoundTrip_VisibleOverPop3AndImap()
    {
        var user = "bnc1";

        CreateUser(user);

        try
        {
            // Real submission: authenticated sender mails a hosted-domain user that does not exist.
            var (proxy, conn) = await AuthedSmtpSession(user);

            await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{user}@{HostedDomain}>\r\n");
            await SmtpAdv.Send(proxy, conn, "RCPT TO:<bnghost@example.com>\r\n");
            await SmtpAdv.Send(proxy, conn, "DATA\r\n");

            var accepted = await SmtpAdv.Send(proxy, conn, "Subject: hi ghost\r\nDate: Thu, 01 Jan 1998 00:00:00 +0000\r\n\r\nanyone home?\r\n.\r\n");

            Assert.AreEqual("250", SmtpAdv.Code(accepted), accepted);

            // Pass 1 bounces the unknown recipient (queuing the bounce), pass 2 delivers the bounce.
            proxy.ProcessUndeliveredQueue(nameof(Bounce_FullRoundTrip_VisibleOverPop3AndImap));
            proxy.ProcessUndeliveredQueue(nameof(Bounce_FullRoundTrip_VisibleOverPop3AndImap));

            // POP3 view (flat toAddress query).
            var pop3View = Mind.PostOfficeDb.GetDeliveredEmailsForUser(user);

            Assert.AreEqual(1, pop3View.Count, "bounce not visible to POP3");
            Assert.AreEqual($"postmaster@{HostedDomain}", pop3View[0].FromAddress.Full);
            StringAssert.Contains(pop3View[0].Subject, "UNDELIVERABLE", pop3View[0].Subject);

            // IMAP view (message_mailbox join): the row must exist with a UID and \Recent.
            var inbox = Mind.PostOfficeDb.GetMailboxByName(user, "INBOX");

            Assert.IsNotNull(inbox, "postmaster did not create default mailboxes for the sender");

            var imapView = Mind.PostOfficeDb.GetMessagesForMailbox(inbox.Value.Id);

            Assert.AreEqual(1, imapView.Count, "bounce has no message_mailbox row - invisible to IMAP");
            Assert.IsTrue(imapView[0].Uid >= 1, $"bounce got no UID: {imapView[0].Uid}");
            StringAssert.Contains(imapView[0].Flags, @"\Recent", imapView[0].Flags);

            // And the full IMAP client path serves it.
            var (imap, imapConn) = await ImapSession();

            await Mail.MailTestEnv.Cmd(imap, imapConn, $"a1 LOGIN {user} {TestPassword}");

            var select = await Mail.MailTestEnv.Cmd(imap, imapConn, "a2 SELECT INBOX");

            StringAssert.Contains(select, "* 1 EXISTS", select);

            var fetch = await Mail.MailTestEnv.Cmd(imap, imapConn, "a3 FETCH 1 (BODY.PEEK[])");

            StringAssert.Contains(fetch, "UNDELIVERABLE", fetch);
            StringAssert.Contains(fetch, "a3 OK", fetch);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public void Bounce_UidSequence_CorrectWhenMixedWithNormalDeliveries()
    {
        var user = "bnc2";
        var peer = "bnc2pal";

        CreateUser(user);
        CreateUser(peer);

        try
        {
            var proxy = SmtpAdv.NewProxy();

            // Normal delivery first, then a bounce, then another normal delivery.
            Mind.PostOfficeDb.ProcessAndInsertEmail(new EmailAddress($"{peer}@{HostedDomain}"), [new EmailAddress($"{user}@{HostedDomain}")], "Subject: one\r\nDate: Thu, 01 Jan 1998 00:00:00 +0000\r\n\r\nfirst\r\n");
            proxy.ProcessUndeliveredQueue(nameof(Bounce_UidSequence_CorrectWhenMixedWithNormalDeliveries));

            Mind.PostOfficeDb.InsertUndeliverableEmail($"{user}@{HostedDomain}", "bnghost@example.com");
            proxy.ProcessUndeliveredQueue(nameof(Bounce_UidSequence_CorrectWhenMixedWithNormalDeliveries));

            Mind.PostOfficeDb.ProcessAndInsertEmail(new EmailAddress($"{peer}@{HostedDomain}"), [new EmailAddress($"{user}@{HostedDomain}")], "Subject: two\r\nDate: Thu, 01 Jan 1998 00:00:00 +0000\r\n\r\nsecond\r\n");
            proxy.ProcessUndeliveredQueue(nameof(Bounce_UidSequence_CorrectWhenMixedWithNormalDeliveries));

            var inbox = Mind.PostOfficeDb.GetMailboxByName(user, "INBOX");
            var messages = Mind.PostOfficeDb.GetMessagesForMailbox(inbox!.Value.Id);

            Assert.AreEqual(3, messages.Count);

            var uids = messages.Select(m => m.Uid).ToList();

            Assert.AreEqual(uids.Count, uids.Distinct().Count(), $"duplicate UIDs: {string.Join(",", uids)}");
            CollectionAssert.AreEqual(uids.OrderBy(u => u).ToList(), uids, $"UIDs not ascending: {string.Join(",", uids)}");
            Assert.IsTrue(messages.Any(m => m.Subject.Contains("UNDELIVERABLE")), "bounce missing from the sequence");
        }
        finally
        {
            DeleteUserAndMail(user);
            DeleteUserAndMail(peer);
        }
    }

    [TestMethod]
    public async Task Bounce_DeletedViaPop3_LeavesNoOrphanForImap()
    {
        var user = "bnc3";

        CreateUser(user);

        try
        {
            var smtp = SmtpAdv.NewProxy();

            Mind.PostOfficeDb.InsertUndeliverableEmail($"{user}@{HostedDomain}", "bnghost@example.com");

            smtp.ProcessUndeliveredQueue(nameof(Bounce_DeletedViaPop3_LeavesNoOrphanForImap));

            var inbox = Mind.PostOfficeDb.GetMailboxByName(user, "INBOX");

            Assert.AreEqual(1, Mind.PostOfficeDb.GetMessagesForMailbox(inbox!.Value.Id).Count, "test setup: bounce not delivered");

            // Delete it over POP3 (DELE marks, QUIT commits).
            var (pop3, conn) = await Pop3Session();

            await Mail.MailTestEnv.Cmd(pop3, conn, $"USER {user}");
            await Mail.MailTestEnv.Cmd(pop3, conn, $"PASS {TestPassword}");

            var dele = await Mail.MailTestEnv.Cmd(pop3, conn, "DELE 1");

            StringAssert.StartsWith(dele, "+OK", dele);

            await Mail.MailTestEnv.Cmd(pop3, conn, "QUIT");

            // IMAP must not see a dangling message_mailbox row afterwards.
            Assert.AreEqual(0, Mind.PostOfficeDb.GetMessagesForMailbox(inbox.Value.Id).Count, "POP3 delete left an orphaned message_mailbox row");
            Assert.AreEqual(0, Mind.PostOfficeDb.GetDeliveredEmailsForUser(user).Count);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    // Generates one bounce letter under the CURRENT config and returns its full raw message
    // (headers + body), cleaning the row up immediately. Unique recipients keep calls unambiguous.
    private static string GenerateBounceLetter(string recipient)
    {
        Mind.PostOfficeDb.InsertUndeliverableEmail(recipient, "ghost@example.com");

        var row = Mind.PostOfficeDb.GetUndeliveredEmails().Last(e => e.ToAddress.Full == recipient);

        Mind.PostOfficeDb.DeleteEmailById(row.Id);

        return row.Data;
    }

    // True when the string is strict CRLF: stripping every CRLF pair leaves no stray \n or \r.
    private static bool IsStrictCrlf(string text)
    {
        var stripped = text.Replace("\r\n", "");

        return !stripped.Contains('\n') && !stripped.Contains('\r');
    }

    [TestMethod]
    public void BounceLetter_PureBuilder_EmitsExplicitCrlfBytesOnAnyPlatform()
    {
        // The builder is a pure function, so this pins exact wire bytes regardless of what
        // Environment.NewLine is on the machine running the suite.
        var letter = PostOfficeDbContext.BuildUndeliverableLetter("a@example.com", "b@example.com", "SUBJ", "postmaster@example.com", new DateTimeOffset(1998, 1, 1, 0, 0, 0, TimeSpan.Zero), "Acme");

        Assert.IsTrue(IsStrictCrlf(letter), $"bare line ending in synthesized letter: {letter.Replace("\r", "<CR>").Replace("\n", "<LF>")}");

        StringAssert.Contains(letter, "\r\nFrom: postmaster@example.com\r\nTo: a@example.com\r\nSubject: SUBJ\r\n\r\nYour message reached the Acme Postmaster", letter);
        Assert.IsTrue(letter.EndsWith("Helping lost bytes find their way home.\r\n"), letter);
    }

    [TestMethod]
    public void Bounce_RawStoredData_IsStrictCrlfWithHeaderSeparator()
    {
        var letter = GenerateBounceLetter("crlf1@example.com");

        Assert.IsTrue(IsStrictCrlf(letter), "stored bounce contains bare line endings");

        // Headers must terminate with CRLFCRLF (the Subject line is the last header).
        StringAssert.Contains(letter, "Rejected!\r\n\r\n", letter);
    }

    [TestMethod]
    public void SynthesizedMail_SourceNeverUsesAppendLineOrEnvironmentNewLine()
    {
        // AppendLine is byte-identical to explicit CRLF on Windows, so no runtime assert can catch
        // a regression here when the suite runs on Windows - lint the source instead.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Data", "Contexts", "PostOfficeDbContext.cs"));

        Assert.IsFalse(source.Contains("AppendLine"), "synthesized mail must be built with explicit \\r\\n, not AppendLine (bare LF on Linux)");
        Assert.IsFalse(source.Contains("Environment.NewLine"), "synthesized mail must be built with explicit \\r\\n, not Environment.NewLine");
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
    }

    [TestMethod]
    public async Task Bounce_Pop3Retr_EmitsStrictCrlf()
    {
        var user = "bnc4";

        CreateUser(user);

        try
        {
            Mind.PostOfficeDb.InsertUndeliverableEmail($"{user}@{HostedDomain}", "ghost@example.com");

            SmtpAdv.NewProxy().ProcessUndeliveredQueue(nameof(Bounce_Pop3Retr_EmitsStrictCrlf));

            var (pop3, conn) = await Pop3Session();

            await Mail.MailTestEnv.Cmd(pop3, conn, $"USER {user}");
            await Mail.MailTestEnv.Cmd(pop3, conn, $"PASS {TestPassword}");

            var retr = await Mail.MailTestEnv.Cmd(pop3, conn, "RETR 1");

            StringAssert.StartsWith(retr, "+OK", retr);
            StringAssert.Contains(retr, "Subject: UNDELIVERABLE", retr);
            Assert.IsTrue(IsStrictCrlf(retr), "POP3 RETR emitted bare line endings");
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public void Bounce_WhitelabeledProductName_NoBrandStringsLeak()
    {
        try
        {
            Mind.Db.ConfigSet(ConfigNames.ProductName, "Acme");

            var letter = GenerateBounceLetter("wl1@example.com");

            StringAssert.Contains(letter, "Acme Postmaster", letter);

            // NOWHERE means headers included - the hosted list is example.com here, so any "hive"
            // hit is a leaked brand string ("hive" also covers "VintageHive").
            Assert.IsFalse(letter.Contains("hive", StringComparison.OrdinalIgnoreCase), $"brand string leaked into a whitelabeled bounce: {letter}");
        }
        finally
        {
            Mind.Db.ConfigSet<string>(ConfigNames.ProductName, null);
        }
    }

    [TestMethod]
    public void Bounce_DefaultProductName_ReadsCorrectly()
    {
        Mind.Db.ConfigSet<string>(ConfigNames.ProductName, null);

        var letter = GenerateBounceLetter("wl2@example.com");

        StringAssert.Contains(letter, "VintageHive Postmaster", letter);
        StringAssert.Contains(letter, "ERROR: 404", letter);
        StringAssert.Contains(letter, "postmaster@example.com", letter);
    }

    [TestMethod]
    public void Bounce_ProductNameChangedAtRuntime_BrandsTheNextLetter()
    {
        try
        {
            Mind.Db.ConfigSet(ConfigNames.ProductName, "Acme");

            StringAssert.Contains(GenerateBounceLetter("wl3@example.com"), "Acme Postmaster", "first letter");

            Mind.Db.ConfigSet(ConfigNames.ProductName, "Zorp");

            var second = GenerateBounceLetter("wl4@example.com");

            StringAssert.Contains(second, "Zorp Postmaster", second);
            Assert.IsFalse(second.Contains("Acme"), $"stale brand in a post-change letter: {second}");
        }
        finally
        {
            Mind.Db.ConfigSet<string>(ConfigNames.ProductName, null);
        }
    }

    [TestMethod]
    public void Bounce_ProductNameWithFormatAndRegexSpecials_LandsVerbatim()
    {
        try
        {
            Mind.Db.ConfigSet(ConfigNames.ProductName, "Acme {0} & Co.");

            var letter = GenerateBounceLetter("wl5@example.com");

            StringAssert.Contains(letter, "Acme {0} & Co. Postmaster", letter);
        }
        finally
        {
            Mind.Db.ConfigSet<string>(ConfigNames.ProductName, null);
        }
    }

    [TestMethod]
    public void Bounce_OfABounce_DroppedInsteadOfLoopingForever()
    {
        var proxy = SmtpAdv.NewProxy();

        // A bounce addressed to a user that does not exist: the postmaster must drop it, not
        // re-bounce it - a bounce of a bounce would ping-pong through the queue forever.
        Mind.PostOfficeDb.InsertUndeliverableEmail("noone@example.com", "bnghost@example.com");

        proxy.ProcessUndeliveredQueue(nameof(Bounce_OfABounce_DroppedInsteadOfLoopingForever));

        Assert.IsFalse(Mind.PostOfficeDb.GetUndeliveredEmails().Any(e => e.FromAddress.User == "postmaster"), "undeliverable bounce was re-queued");

        // A second pass must be a no-op, not another generation of bounces.
        proxy.ProcessUndeliveredQueue(nameof(Bounce_OfABounce_DroppedInsteadOfLoopingForever));

        Assert.IsFalse(Mind.PostOfficeDb.GetUndeliveredEmails().Any(e => e.FromAddress.User == "postmaster"));
        Assert.AreEqual(0, Mind.PostOfficeDb.GetDeliveredEmailsForUser("noone").Count, "bounce delivered to a nonexistent user");
    }

    [TestMethod]
    public void Postmaster_QueuedForeignDomain_BouncesInsteadOfDeliveringLocally()
    {
        var user = "mdmd2";

        CreateUser(user);

        try
        {
            // Simulate a foreign-domain recipient already in the queue (accepted before the RCPT
            // check existed, or its domain removed at runtime after acceptance).
            var from = new EmailAddress($"{user}@{HostedDomain}");
            var to = new HashSet<EmailAddress> { new EmailAddress($"{user}@gmail.com") };

            Mind.PostOfficeDb.ProcessAndInsertEmail(from, to, $"Subject: bait\r\nDate: Thu, 01 Jan 1998 00:00:00 +0000\r\n\r\nshould bounce\r\n");

            // Two passes: the first bounces the foreign recipient (queuing the bounce as normal
            // undelivered mail), the second delivers that bounce to the sender.
            var proxy = SmtpAdv.NewProxy();

            proxy.ProcessUndeliveredQueue(nameof(Postmaster_QueuedForeignDomain_BouncesInsteadOfDeliveringLocally));
            proxy.ProcessUndeliveredQueue(nameof(Postmaster_QueuedForeignDomain_BouncesInsteadOfDeliveringLocally));

            // The queue entry is gone but did NOT deliver into the same-named local mailbox...
            Assert.IsFalse(Mind.PostOfficeDb.GetUndeliveredEmails().Any(e => e.ToAddress.User == user), "queue entry survived the postmaster pass");

            var deliveredToUser = Mind.PostOfficeDb.GetDeliveredEmailsForUser(user);

            Assert.IsFalse(deliveredToUser.Any(e => e.ToAddress.Domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase)), "foreign-domain mail was delivered locally");

            // ...and the sender got a bounce from postmaster@<primary hosted domain>.
            var bounce = deliveredToUser.FirstOrDefault(e => e.Subject.Contains("UNDELIVERABLE"));

            Assert.IsNotNull(bounce, "no bounce was generated for the foreign-domain recipient");
            Assert.AreEqual($"postmaster@{HostedDomain}", bounce.FromAddress.Full);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    #endregion
}
