// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Adversarial coverage of the hosted mail-domain list (ConfigNames.ValidMailDomains / MailDomains):
// login resolution across POP3 USER, IMAP LOGIN, and SMTP AUTH LOGIN; MAIL FROM sender-domain and
// RCPT TO relay enforcement; runtime list mutation without restart; and proof that the historical
// misdelivery (mail for fred@gmail.com landing in local fred's inbox) is dead at both the RCPT door
// and the postmaster queue. Uses the shared file-backed MailTestEnv contexts; every test that
// mutates config or creates users/emails restores state in finally/cleanup so the persistent test
// DB cannot poison other suites (which pin banners against the default hive.com domain).

using System.Net;
using System.Text;
using Adversarial5.Smtp;
using VintageHive;
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

            SmtpAdv.NewProxy().ProcessUndeliveredQueue(nameof(Postmaster_QueuedForeignDomain_BouncesInsteadOfDeliveringLocally));

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
