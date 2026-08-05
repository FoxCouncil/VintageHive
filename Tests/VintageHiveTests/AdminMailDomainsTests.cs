// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// The admin field for the hosted mail-domain list. The validation/normalization core lives in
// MailDomains.TryNormalizeList (the seam every hosted-domain decision goes through), so it is tested
// directly; the route is a thin wrapper over it. The HTML checks are the same drift guard the service
// grid needed: the backend half of a dashboard feature compiles on its own, so the only thing that
// catches a missing front-end half is a test that reads the shipped page.

using Mail;
using VintageHive;
using VintageHive.Data.Types;

namespace Adversarial7.AdminMailDomains;

[TestClass]
public class MailDomainsNormalizeTests
{
    [TestMethod]
    public void ASingleDomain_PassesThrough()
    {
        Assert.IsTrue(MailDomains.TryNormalizeList("hive.com", out var normalized));
        Assert.AreEqual("hive.com", normalized);
    }

    [TestMethod]
    public void CaseAndWhitespace_AreNormalized()
    {
        Assert.IsTrue(MailDomains.TryNormalizeList("  Hive.COM ,  Example.Org  ", out var normalized));
        Assert.AreEqual("hive.com,example.org", normalized);
    }

    [TestMethod]
    public void Duplicates_CollapseKeepingFirstOccurrenceOrder()
    {
        // Order is semantic: the first entry is the primary domain the postmaster signs bounces with.
        Assert.IsTrue(MailDomains.TryNormalizeList("b.com,a.com,B.COM,a.com", out var normalized));
        Assert.AreEqual("b.com,a.com", normalized);
    }

    [TestMethod]
    public void EmptyInput_NormalizesToTheRestoreDefaultShape()
    {
        Assert.IsTrue(MailDomains.TryNormalizeList("", out var empty));
        Assert.AreEqual(string.Empty, empty);

        Assert.IsTrue(MailDomains.TryNormalizeList("   ", out var blank));
        Assert.AreEqual(string.Empty, blank);

        Assert.IsTrue(MailDomains.TryNormalizeList(null, out var nul));
        Assert.AreEqual(string.Empty, nul);

        // Bare separators carry no domains; same restore-default outcome rather than a reject.
        Assert.IsTrue(MailDomains.TryNormalizeList(" , ,", out var commas));
        Assert.AreEqual(string.Empty, commas);
    }

    [TestMethod]
    public void ASingleLabelLanDomain_IsAccepted()
    {
        Assert.IsTrue(MailDomains.TryNormalizeList("hive", out var normalized));
        Assert.AreEqual("hive", normalized);
    }

    [TestMethod]
    public void GarbageEntries_RejectTheWholeList()
    {
        // EmailAddress's domain match is loose by design, so this gate is what keeps these out of
        // config. One bad entry fails the whole submit - a silently-dropped entry would look saved.
        foreach (var bad in new[]
        {
            "not a domain",
            "a b.com",
            "hive.com,a b.com",
            "user@hive.com",
            "hive_com",
            "a%b.com",
            "-hive.com",
            "hive-.com",
            "hive..com",
            ".hive.com",
            "hive.com.",
            "hi;ve.com",
            new string('a', 254),
        })
        {
            Assert.IsFalse(MailDomains.TryNormalizeList(bad, out _), $"'{bad}' was accepted as a hosted-domain list.");
        }
    }

    [TestMethod]
    public void ANormalizedList_RoundTripsThroughConfigIntoAll()
    {
        MailTestEnv.Ensure();

        try
        {
            Assert.IsTrue(MailDomains.TryNormalizeList("Example.COM, other.org", out var normalized));

            Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, normalized);

            CollectionAssert.AreEqual(new[] { "example.com", "other.org" }, MailDomains.All.ToArray(), "The normalized shape did not survive the trip through config into the live list.");
            Assert.AreEqual("example.com", MailDomains.Primary);

            // The restore-default shape really restores the default.
            Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, string.Empty);

            CollectionAssert.AreEqual(new[] { HiveDomains.Base }, MailDomains.All.ToArray(), "An empty stored value must fall back to the built-in default.");
        }
        finally
        {
            Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);
        }
    }
}

[TestClass]
public class AdminMailDomainsDriftTests
{
    private static string IndexHtml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Statics", "controllers", "admin.hive.com", "index.html");

        if (!File.Exists(path))
        {
            // Statics are embedded resources; fall back to the repo copy when the test host has no extracted tree.
            var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Statics", "controllers", "admin.hive.com", "index.html"));

            Assert.IsTrue(File.Exists(repo), $"Could not locate the admin index.html at '{path}' or '{repo}'.");

            return File.ReadAllText(repo);
        }

        return File.ReadAllText(path);
    }

    [TestMethod]
    public void TheDashboard_CarriesTheMailDomainsField()
    {
        var html = IndexHtml();

        StringAssert.Contains(html, "id=\"mail_domains\"", "The hosted mail domains input is gone from the admin page.");
        StringAssert.Contains(html, "maildomainsset", "Nothing on the admin page submits to /api/maildomainsset.");
        StringAssert.Contains(html, "status.mailDomains", "The status poll no longer populates the mail domains field.");
    }
}
