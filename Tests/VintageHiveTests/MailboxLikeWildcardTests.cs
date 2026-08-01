// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// The mailbox queries matched "toAddress LIKE username + '@%'" with the username dropped in raw. Parameter
// binding stops injection but does nothing about LIKE's own wildcards, so a mailbox owner called "a_b" read
// everything addressed to "axb@...", and "jo%" read every address beginning "jo". UserCreate only ever
// checked LENGTH, and the admin panel's alphanumeric regex was used unanchored (IsMatch, no anchors) so it
// passed any string containing at least one alphanumeric character - "a_b" sailed through every registration
// path there is.
//
// Both layers are fixed, and both are tested here: the queries escape their pattern, and creation rejects the
// charset. The query test deliberately writes its rows through the store directly rather than via UserCreate,
// because the whole point is that rows predating the validation still exist on real boxes.

using Mail;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;

namespace Adversarial5.MailboxWildcard;

[TestClass]
public class MailboxLikeWildcardTests
{
    [ClassInitialize]
    public static void Init(TestContext _) => MailTestEnv.Ensure();

    private static void Deliver(string toLocalPart, string marker)
    {
        Mind.PostOfficeDb.ProcessAndInsertEmail(
            new EmailAddress($"sender@{MailDomains.Primary}"),
            new HashSet<EmailAddress> { new($"{toLocalPart}@{MailDomains.Primary}") },
            $"Subject: {marker}\r\n\r\n{marker} body");

        foreach (var undelivered in Mind.PostOfficeDb.GetUndeliveredEmails())
        {
            Mind.PostOfficeDb.MarkEmailAsDelivered(undelivered.Id);
        }
    }

    // '_' is LIKE's single-character wildcard, so "wcaUb" used to match the victim's "wcaXb@..." address.
    [TestMethod]
    [Timeout(20000)]
    public void AnUnderscoreInAUsername_DoesNotMatchAnotherMailbox()
    {
        var marker = $"victim-{Guid.NewGuid():N}";

        Deliver("wcaXb", marker);

        var stolen = Mind.PostOfficeDb.GetDeliveredEmailsForUser("wca_b");

        Assert.IsFalse(stolen.Any(x => x.Data.Contains(marker)), "An underscore in the username matched another user's mailbox: LIKE treated it as a single-character wildcard.");
    }

    // '%' is LIKE's any-length wildcard, so a bare "wc%" used to read every mailbox starting "wc".
    [TestMethod]
    [Timeout(20000)]
    public void APercentInAUsername_DoesNotMatchEveryMailbox()
    {
        var marker = $"victim-{Guid.NewGuid():N}";

        Deliver("wcpercent", marker);

        var stolen = Mind.PostOfficeDb.GetDeliveredEmailsForUser("wc%");

        Assert.IsFalse(stolen.Any(x => x.Data.Contains(marker)), "A percent sign in the username matched every mailbox sharing its prefix.");
    }

    // The quota accounting reads through the same pattern, so a wildcard name would bill someone else's mail
    // against this user (or hide their own usage).
    [TestMethod]
    [Timeout(20000)]
    public void MailboxUsage_IsNotInflatedByAWildcardUsername()
    {
        var marker = $"victim-{Guid.NewGuid():N}";

        Deliver("wcqXta", marker);

        Assert.AreEqual(0, Mind.PostOfficeDb.GetMailboxUsage("wcqZta"), "A non-existent mailbox reported usage, so the pattern is still matching other addresses.");
        Assert.AreEqual(0, Mind.PostOfficeDb.GetMailboxUsage("wcq_ta"), "An underscore in the username billed another user's mail against this one.");
        Assert.IsTrue(Mind.PostOfficeDb.GetMailboxUsage("wcqXta") > 0, "The real mailbox reported no usage, so escaping has broken ordinary lookups.");
    }

    // The delivered-mail lookup must still work normally - escaping is not allowed to break the common case.
    [TestMethod]
    [Timeout(20000)]
    public void AnOrdinaryUsername_StillReadsItsOwnMail()
    {
        var marker = $"mine-{Guid.NewGuid():N}";

        Deliver("wcplain", marker);

        Assert.IsTrue(Mind.PostOfficeDb.GetDeliveredEmailsForUser("wcplain").Any(x => x.Data.Contains(marker)), "Escaping the LIKE pattern broke ordinary mailbox reads.");
    }

    [TestMethod]
    public void EscapingALikePattern_NeutralisesWildcardsAndItself()
    {
        Assert.AreEqual("a\\_b", PostOfficeDbContext.EscapeLikePattern("a_b"));
        Assert.AreEqual("a\\%b", PostOfficeDbContext.EscapeLikePattern("a%b"));
        Assert.AreEqual("plain", PostOfficeDbContext.EscapeLikePattern("plain"));

        // The escape character itself has to be escaped FIRST, or the backslashes added for % and _ would be
        // doubled in turn and stop escaping anything.
        Assert.AreEqual("a\\\\b", PostOfficeDbContext.EscapeLikePattern("a\\b"));
        Assert.AreEqual("a\\\\\\_b", PostOfficeDbContext.EscapeLikePattern("a\\_b"));
    }

    // The other half of the fix: names like these cannot be minted any more, through any path.
    [TestMethod]
    public void UsernameValidation_RejectsEverythingButAlphanumerics()
    {
        foreach (var bad in new[] { "a_b", "jo%", "a b", "a'b", "a;b", "a<b", "a.b", "a\\b", "a-b" })
        {
            Assert.IsFalse(HiveDbContext.IsValidUsername(bad), $"'{bad}' was accepted as a username.");
        }

        foreach (var good in new[] { "alice", "fox", "user123", "ABC", "a1" })
        {
            Assert.IsTrue(HiveDbContext.IsValidUsername(good), $"'{good}' was rejected as a username.");
        }

        Assert.IsFalse(HiveDbContext.IsValidUsername(null));
        Assert.IsFalse(HiveDbContext.IsValidUsername(string.Empty));
    }

    [TestMethod]
    public void UserCreate_RefusesAWildcardName()
    {
        MailTestEnv.Ensure();

        Assert.IsFalse(Mind.Db.UserCreate("wc_x", "secret"), "A username containing a LIKE wildcard was created.");
        Assert.IsFalse(Mind.Db.UserCreate("wc%x", "secret"), "A username containing a LIKE wildcard was created.");
    }
}
