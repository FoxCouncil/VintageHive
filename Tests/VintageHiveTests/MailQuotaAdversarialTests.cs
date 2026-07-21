// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Mailbox quota enforcement at delivery. The gate is Mind.MailboxQuotaProvider (host-injected,
// username -> bytes, null = unlimited), read LIVE per decision, >= semantics, fail-open on every
// provider edge. Enforcement happens at store-insertion time only: SMTP answers 452 when ALL
// recipients were skipped, partial delivery succeeds, bounces to full mailboxes are dropped, and
// deletes are never gated. The size COLUMN is authoritative for the SUM - pinned here with a
// deliberately drifted row. Every test resets the provider and hosted-domain config in cleanup:
// both are process-global and would poison other suites.

using System.Net;
using System.Text;
using Adversarial5.Smtp;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Smtp;

namespace Adversarial5.MailQuota;

// Drift poke-hole: DbContextBase derives its filename from the TYPE name, so a subclass would
// silently target its own fresh .db - open the real postoffice.db directly instead.
internal static class QuotaDrift
{
    public static void SetSizeColumn(int emailId, long size)
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "vfs", "data", "postoffice.db");

        Assert.IsTrue(File.Exists(dbPath), $"postoffice.db not found at {dbPath}");

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Cache=Shared");

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = "UPDATE emails SET size = @size WHERE id = @id";

        command.Parameters.AddWithValue("@size", size);
        command.Parameters.AddWithValue("@id", emailId);

        Assert.AreEqual(1, command.ExecuteNonQuery(), $"drift update touched no row (id {emailId})");
    }
}

[TestClass]
public class MailQuotaAdversarialTests
{
    private const string TestPassword = "hunter2";
    private const string HostedDomain = "example.com";

    // One live message body used everywhere so sizes are predictable.
    private const string Body = "Subject: quota probe\r\nDate: Thu, 01 Jan 1998 00:00:00 +0000\r\n\r\nsome retro bytes\r\n";

    private static readonly Dictionary<string, long?> Quotas = new(StringComparer.OrdinalIgnoreCase);

    [TestInitialize]
    public void Init()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HostedDomain);

        Quotas.Clear();
        Mind.MailboxQuotaProvider = username => Quotas.TryGetValue(username, out var quota) ? quota : null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Mind.MailboxQuotaProvider = null;

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);
    }

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

    private static EmailAddress Addr(string user)
    {
        return new EmailAddress($"{user}@{HostedDomain}");
    }

    private static List<EmailAddress> Deliver(string fromUser, params string[] toUsers)
    {
        return Mind.PostOfficeDb.ProcessAndInsertEmail(Addr(fromUser), toUsers.Select(Addr).ToHashSet(), Body);
    }

    private static int QueuedCountFor(string user)
    {
        return Mind.PostOfficeDb.GetUndeliveredEmails().Count(e => e.ToAddress.User == user);
    }

    #region Core semantics

    [TestMethod]
    public void UnderQuota_Delivered_AtQuota_NextMessageSkipped()
    {
        var user = "qta1";

        try
        {
            // Under quota: delivered.
            Quotas[user] = Body.Length + 1;

            Assert.AreEqual(0, Deliver("qtapal", user).Count, "under-quota delivery was skipped");
            Assert.AreEqual(1, QueuedCountFor(user));
            Assert.AreEqual(Body.Length, Mind.PostOfficeDb.GetMailboxUsage(user));

            // Usage now EXACTLY == quota: >= semantics refuse the next message.
            Quotas[user] = Body.Length;

            var skipped = Deliver("qtapal", user);

            Assert.AreEqual(1, skipped.Count, "at-quota delivery was not skipped (>= semantics)");
            Assert.AreEqual(user, skipped[0].User);
            Assert.AreEqual(1, QueuedCountFor(user), "skipped message was inserted anyway");
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public void DeleteMessage_UsageDrops_NextSendAccepted_NoStaleCache()
    {
        var user = "qta2";

        try
        {
            Quotas[user] = Body.Length;

            Deliver("qtapal", user);

            Assert.AreEqual(1, Deliver("qtapal", user).Count, "test setup: second send should be at-quota");

            // Deleting mail is always the way out of over-quota - never gated, and the SUM is live.
            var stored = Mind.PostOfficeDb.GetUndeliveredEmails().First(e => e.ToAddress.User == user);

            Mind.PostOfficeDb.DeleteEmailById(stored.Id);

            Assert.AreEqual(0, Mind.PostOfficeDb.GetMailboxUsage(user));
            Assert.AreEqual(0, Deliver("qtapal", user).Count, "post-delete send still skipped: a SUM went stale");
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public void ProviderEdges_NullResultUnsetOrThrowing_AllMeanUnlimited()
    {
        var user = "qta3";

        try
        {
            // Provider returns null for this user (no entry in the dictionary).
            Assert.AreEqual(0, Deliver("qtapal", user).Count, "null quota result must mean unlimited");

            // No provider at all.
            Mind.MailboxQuotaProvider = null;

            Assert.AreEqual(0, Deliver("qtapal", user).Count, "unset provider must mean unlimited");

            // Provider throws: fail-open, delivery proceeds.
            Mind.MailboxQuotaProvider = _ => throw new InvalidOperationException("quota backend down");

            Assert.AreEqual(0, Deliver("qtapal", user).Count, "throwing provider must fail open");
            Assert.AreEqual(3, QueuedCountFor(user));
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public void ProviderResult_ChangedBetweenSends_SecondSendHonorsNewValue()
    {
        var user = "qta4";

        try
        {
            Quotas[user] = 0;

            Assert.AreEqual(1, Deliver("qtapal", user).Count, "zero quota must skip immediately");

            // The host flips the quota at runtime; the very next decision sees it (no caching).
            Quotas[user] = null;

            Assert.AreEqual(0, Deliver("qtapal", user).Count, "provider change was not honored - something cached");
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    #endregion

    #region SMTP layer

    private static async Task<(SmtpProxy proxy, ListenerSocket conn)> AuthedSmtpSession(string user)
    {
        var proxy = SmtpAdv.NewProxy();
        var conn = await SmtpAdv.Connect(proxy);

        await SmtpAdv.Send(proxy, conn, "EHLO tester\r\n");
        await SmtpAdv.Send(proxy, conn, "AUTH LOGIN\r\n");
        await SmtpAdv.Send(proxy, conn, Convert.ToBase64String(Encoding.ASCII.GetBytes(user)) + "\r\n");

        var resp = await SmtpAdv.Send(proxy, conn, Convert.ToBase64String(Encoding.ASCII.GetBytes(TestPassword)) + "\r\n");

        Assert.AreEqual("235", SmtpAdv.Code(resp), $"test setup: AUTH failed: {resp}");

        return (proxy, conn);
    }

    [TestMethod]
    public async Task Smtp_AllRecipientsFull_Exact452Reply_NothingInserted()
    {
        var sender = "qta5";
        var full = "qta5full";

        CreateUser(sender);
        CreateUser(full);

        try
        {
            Quotas[full] = 0;

            var (proxy, conn) = await AuthedSmtpSession(sender);

            await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{sender}@{HostedDomain}>\r\n");
            await SmtpAdv.Send(proxy, conn, $"RCPT TO:<{full}@{HostedDomain}>\r\n");
            await SmtpAdv.Send(proxy, conn, "DATA\r\n");

            var reply = await SmtpAdv.Send(proxy, conn, Body + ".\r\n");

            // RFC 5321 4.2.3, exact code and wording.
            Assert.AreEqual("452 Requested action not taken: insufficient system storage\r\n", reply);
            Assert.AreEqual(0, QueuedCountFor(full), "452'd message was inserted anyway");

            // The transaction is complete: a fresh MAIL FROM must be accepted afterwards.
            var next = await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{sender}@{HostedDomain}>\r\n");

            Assert.AreEqual("250", SmtpAdv.Code(next), next);
        }
        finally
        {
            DeleteUserAndMail(sender);
            DeleteUserAndMail(full);
        }
    }

    [TestMethod]
    public async Task Smtp_MixedFullAndEmptyRecipients_250_EmptyOneReceives()
    {
        var sender = "qta6";
        var full = "qta6full";
        var open = "qta6open";

        CreateUser(sender);
        CreateUser(full);
        CreateUser(open);

        try
        {
            Quotas[full] = 0;

            var (proxy, conn) = await AuthedSmtpSession(sender);

            await SmtpAdv.Send(proxy, conn, $"MAIL FROM:<{sender}@{HostedDomain}>\r\n");
            await SmtpAdv.Send(proxy, conn, $"RCPT TO:<{full}@{HostedDomain}>\r\n");
            await SmtpAdv.Send(proxy, conn, $"RCPT TO:<{open}@{HostedDomain}>\r\n");
            await SmtpAdv.Send(proxy, conn, "DATA\r\n");

            var reply = await SmtpAdv.Send(proxy, conn, Body + ".\r\n");

            Assert.AreEqual("250", SmtpAdv.Code(reply), $"partial skip must not 452: {reply}");
            Assert.AreEqual(0, QueuedCountFor(full), "full mailbox received mail");
            Assert.AreEqual(1, QueuedCountFor(open), "open mailbox did not receive mail");
        }
        finally
        {
            DeleteUserAndMail(sender);
            DeleteUserAndMail(full);
            DeleteUserAndMail(open);
        }
    }

    #endregion

    #region Bounces and adversarial edges

    [TestMethod]
    public void Bounce_ToFullSenderMailbox_DroppedNoLoopNoCrash()
    {
        var user = "qta7";

        CreateUser(user);

        try
        {
            Quotas[user] = 0;

            Mind.PostOfficeDb.InsertUndeliverableEmail($"{user}@{HostedDomain}", $"ghost@{HostedDomain}");

            Assert.AreEqual(0, QueuedCountFor(user), "bounce to a full mailbox was queued");

            // Queue passes stay quiet: nothing to ping-pong, nothing to crash on.
            var proxy = SmtpAdv.NewProxy();

            proxy.ProcessUndeliveredQueue(nameof(Bounce_ToFullSenderMailbox_DroppedNoLoopNoCrash));
            proxy.ProcessUndeliveredQueue(nameof(Bounce_ToFullSenderMailbox_DroppedNoLoopNoCrash));

            Assert.AreEqual(0, QueuedCountFor(user));
            Assert.AreEqual(0, Mind.PostOfficeDb.GetDeliveredEmailsForUser(user).Count);
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public void SizeColumnDrift_ColumnIsAuthoritative_DataLengthIgnored()
    {
        var user = "qta8";

        try
        {
            // A row whose size column says 3 while the data is far larger: the COLUMN is the
            // authority, so usage is 3 and the next delivery fits a quota the data length would bust.
            Deliver("qtapal", user);

            var drifted = Mind.PostOfficeDb.GetUndeliveredEmails().First(e => e.ToAddress.User == user);

            QuotaDrift.SetSizeColumn(drifted.Id, 3);

            Assert.AreEqual(3, Mind.PostOfficeDb.GetMailboxUsage(user), "size column is not driving the SUM");

            Quotas[user] = 3 + Body.Length;

            Assert.AreEqual(0, Deliver("qtapal", user).Count, "drifted data length corrupted the quota decision");
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    [TestMethod]
    public void UnknownRecipient_ProviderThrowsForThem_FailOpenThenNormalBounce()
    {
        var sender = "qta9";

        CreateUser(sender);

        try
        {
            // The user was deleted between RCPT and delivery; the provider knows nothing and throws.
            Mind.MailboxQuotaProvider = username => username == "qtagone" ? throw new KeyNotFoundException(username) : null;

            var skipped = Deliver(sender, "qtagone");

            Assert.AreEqual(0, skipped.Count, "throwing provider must fail open for unknown users");
            Assert.AreEqual(1, QueuedCountFor("qtagone"));

            // The normal postmaster path takes over from there (unknown user -> bounce to sender).
            var proxy = SmtpAdv.NewProxy();

            proxy.ProcessUndeliveredQueue(nameof(UnknownRecipient_ProviderThrowsForThem_FailOpenThenNormalBounce));
            proxy.ProcessUndeliveredQueue(nameof(UnknownRecipient_ProviderThrowsForThem_FailOpenThenNormalBounce));

            Assert.IsTrue(Mind.PostOfficeDb.GetDeliveredEmailsForUser(sender).Any(e => e.Subject.Contains("UNDELIVERABLE")), "unknown recipient did not bounce normally");
        }
        finally
        {
            DeleteUserAndMail(sender);
            DeleteUserAndMail("qtagone");
        }
    }

    [TestMethod]
    public void ConcurrentDeliveries_NearFullMailbox_OvershootTolerated_NoCorruption()
    {
        var user = "qta10";

        try
        {
            // Room for exactly one more message. The check-then-insert pair is documented as
            // non-transactional: two racing deliveries may both pass the check (small overshoot,
            // accepted); corruption or a crash is not.
            Quotas[user] = Body.Length;

            Exception first = null, second = null;

            Parallel.Invoke(
                () => { try { Deliver("qtapal", user); } catch (Exception ex) { first = ex; } },
                () => { try { Deliver("qtapal", user); } catch (Exception ex) { second = ex; } });

            Assert.IsNull(first, first?.ToString());
            Assert.IsNull(second, second?.ToString());

            var count = QueuedCountFor(user);

            Assert.IsTrue(count is 1 or 2, $"expected 1 (both raced the gate) or 2 (overshoot), got {count}");

            // The store stayed coherent: usage is exactly count * size and every row reads back.
            Assert.AreEqual(count * (long)Body.Length, Mind.PostOfficeDb.GetMailboxUsage(user));

            // And the gate is closed now either way.
            Assert.AreEqual(1, Deliver("qtapal", user).Count, "over-quota mailbox accepted another message");
        }
        finally
        {
            DeleteUserAndMail(user);
        }
    }

    #endregion
}
