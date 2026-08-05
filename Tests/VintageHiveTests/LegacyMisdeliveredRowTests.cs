// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Before domain enforcement, RCPT accepted any domain and the delivery pass matched on local part
// alone, so a real database can hold delivered rows addressed to "user@gmail.com" that a prefix
// query ("user@%") hands straight to the local "user". New mail can't create such rows any more;
// these tests pin that the READ side no longer surfaces the old ones either: the POP3 mailbox query
// and the quota SUM both constrain to the hosted-domain list. Rows are seeded through the store
// while the foreign domain is temporarily hosted - exactly how a legacy build minted them - and the
// config is narrowed back before asserting.

using Mail;
using VintageHive;
using VintageHive.Data.Types;

namespace Adversarial7.LegacyMisdelivered;

[TestClass]
public class LegacyMisdeliveredRowTests
{
    [ClassInitialize]
    public static void Init(TestContext _) => MailTestEnv.Ensure();

    [TestCleanup]
    public void Cleanup()
    {
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);
    }

    // Seeds a delivered row exactly the way a pre-enforcement build did: straight through the store,
    // no RCPT gate involved. The caller controls the hosted-domain config around it.
    private static void SeedDeliveredRow(string localPart, string domain, string marker)
    {
        Mind.PostOfficeDb.ProcessAndInsertEmail(
            new EmailAddress($"sender@{HiveDomains.Base}"),
            new HashSet<EmailAddress> { new($"{localPart}@{domain}") },
            $"Subject: {marker}\r\n\r\n{marker} body");

        foreach (var undelivered in Mind.PostOfficeDb.GetUndeliveredEmails())
        {
            Mind.PostOfficeDb.MarkEmailAsDelivered(undelivered.Id);
        }
    }

    // Deleting has to see the rows first, so the cleanup widens the domain list before enumerating.
    private static void DeleteAllMailFor(string localPart, params string[] domains)
    {
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base + "," + string.Join(",", domains));

        foreach (var email in Mind.PostOfficeDb.GetDeliveredEmailsForUser(localPart))
        {
            Mind.PostOfficeDb.DeleteEmailById(email.Id);
        }

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);
    }

    [TestMethod]
    [Timeout(20000)]
    public void LegacyForeignDomainRow_IsInvisibleToTheMailboxRead()
    {
        var user = "lmr1";
        var hostedMarker = $"hosted-{Guid.NewGuid():N}";
        var foreignMarker = $"foreign-{Guid.NewGuid():N}";

        try
        {
            SeedDeliveredRow(user, HiveDomains.Base, hostedMarker);

            Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base + ",gmail.com");

            SeedDeliveredRow(user, "gmail.com", foreignMarker);

            // Sanity: while gmail.com is hosted, the row is a legitimate mailbox entry.
            Assert.IsTrue(Mind.PostOfficeDb.GetDeliveredEmailsForUser(user).Any(x => x.Data.Contains(foreignMarker)), "The seed row never became visible, so this test never exercised the fix.");

            Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

            var mailbox = Mind.PostOfficeDb.GetDeliveredEmailsForUser(user);

            Assert.IsTrue(mailbox.Any(x => x.Data.Contains(hostedMarker)), "The hosted-domain row disappeared - the domain filter is over-matching.");
            Assert.IsFalse(mailbox.Any(x => x.Data.Contains(foreignMarker)), "A legacy row addressed to a foreign domain is still served to the local user.");
        }
        finally
        {
            DeleteAllMailFor(user, "gmail.com");
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public void LegacyForeignDomainRow_DoesNotBillTheQuota()
    {
        var user = "lmr2";

        try
        {
            Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base + ",gmail.com");

            SeedDeliveredRow(user, "gmail.com", $"quota-{Guid.NewGuid():N}");

            Assert.IsTrue(Mind.PostOfficeDb.GetMailboxUsage(user) > 0, "The seed row never counted, so this test never exercised the fix.");

            Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

            Assert.AreEqual(0, Mind.PostOfficeDb.GetMailboxUsage(user), "A legacy foreign-domain row still bills the local user's quota.");
        }
        finally
        {
            DeleteAllMailFor(user, "gmail.com");
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public void EveryHostedDomain_IsReadable()
    {
        var user = "lmr3";
        var primaryMarker = $"primary-{Guid.NewGuid():N}";
        var secondaryMarker = $"secondary-{Guid.NewGuid():N}";

        try
        {
            Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base + ",example.com");

            SeedDeliveredRow(user, HiveDomains.Base, primaryMarker);
            SeedDeliveredRow(user, "example.com", secondaryMarker);

            var mailbox = Mind.PostOfficeDb.GetDeliveredEmailsForUser(user);

            Assert.IsTrue(mailbox.Any(x => x.Data.Contains(primaryMarker)), "Mail on the primary hosted domain went missing.");
            Assert.IsTrue(mailbox.Any(x => x.Data.Contains(secondaryMarker)), "Mail on a secondary hosted domain went missing - the OR over the domain list is broken.");
        }
        finally
        {
            DeleteAllMailFor(user, "example.com");
        }
    }

    // The prefix match was ASCII case-insensitive (SQLite LIKE); the domain-constrained match must
    // stay that way, which is why the predicate uses wildcard-free LIKE rather than '='.
    [TestMethod]
    [Timeout(20000)]
    public void MailboxRead_StaysCaseInsensitive()
    {
        var user = "lmr4";
        var marker = $"case-{Guid.NewGuid():N}";

        try
        {
            SeedDeliveredRow(user, HiveDomains.Base, marker);

            Assert.IsTrue(Mind.PostOfficeDb.GetDeliveredEmailsForUser(user.ToUpperInvariant()).Any(x => x.Data.Contains(marker)), "An upper-cased login no longer reads its own mailbox - the domain filter dropped LIKE's case-insensitivity.");
            Assert.IsTrue(Mind.PostOfficeDb.GetMailboxUsage(user.ToUpperInvariant()) > 0, "An upper-cased login no longer sees its own quota usage.");
        }
        finally
        {
            DeleteAllMailFor(user);
        }
    }
}
