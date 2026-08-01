// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// The service grid is kept in two places - AdminController.ToggleableServices on the server and the
// serviceLabels map in admin.hive.com/index.html - and they had already drifted: the backend registered
// "yahoopager" but the page had no label for it, and renderServices iterated the LABEL map, so the toggle
// the backend fully supported simply never appeared. Silent, and invisible precisely because the missing
// entry was what would have made it visible.
//
// The renderer now iterates what the API returned and falls back to the raw key, so an unlabelled service
// shows up ugly rather than not at all. This test is the other half: it reads the shipped HTML and fails if a
// registered service has no label, so the drift is caught here rather than by someone noticing an absence.

using System.Text.RegularExpressions;
using VintageHive.Processors.LocalServer.Controllers;

namespace Adversarial5.AdminServiceGrid;

[TestClass]
public class AdminServiceGridTests
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

    private static HashSet<string> LabelledKeys(string html)
    {
        var start = html.IndexOf("const serviceLabels", StringComparison.Ordinal);

        Assert.IsTrue(start >= 0, "serviceLabels is gone from the admin page; this test needs updating alongside it.");

        var open = html.IndexOf('{', start);
        var close = html.IndexOf("};", open, StringComparison.Ordinal);

        Assert.IsTrue(close > open, "Could not read the serviceLabels object literal.");

        var block = html[(open + 1)..close];

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(block, @"^\s*([A-Za-z0-9_]+)\s*:", RegexOptions.Multiline))
        {
            keys.Add(match.Groups[1].Value);
        }

        return keys;
    }

    [TestMethod]
    public void EveryToggleableService_HasALabelOnTheAdminPage()
    {
        var labelled = LabelledKeys(IndexHtml());

        var missing = AdminController.ToggleableServices.Keys.Where(x => !labelled.Contains(x)).ToList();

        Assert.AreEqual(0, missing.Count, $"These services are registered in ToggleableServices but have no label on the admin page, so they render under their raw key: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void EveryLabelOnTheAdminPage_MatchesARegisteredService()
    {
        var labelled = LabelledKeys(IndexHtml());

        var orphaned = labelled.Where(x => !AdminController.ToggleableServices.ContainsKey(x)).ToList();

        Assert.AreEqual(0, orphaned.Count, $"These labels have no matching entry in ToggleableServices, so they can never render: {string.Join(", ", orphaned)}");
    }

    // The panel is served over the HTTP proxy, so a toggle for it would be a one-way door: switch it off and
    // the only way back is a hand-edited database. Pinning the exclusion so nobody "completes" the grid later
    // without realising what that costs.
    [TestMethod]
    public void TheTransportsCarryingTheAdminPanel_AreNotToggleable()
    {
        Assert.IsFalse(AdminController.ToggleableServices.ContainsKey("http"), "Exposing the HTTP proxy as a toggle lets an admin lock themselves out of the panel that would turn it back on.");
        Assert.IsFalse(AdminController.ToggleableServices.ContainsKey("https"), "Exposing the HTTPS proxy as a toggle lets an admin lock themselves out of the panel that would turn it back on.");
    }

    // The ones this task added, so a later refactor cannot quietly drop them back out of reach.
    [TestMethod]
    public void ThePreviouslyUnreachableServices_AreNowToggleable()
    {
        foreach (var service in new[] { "ftp", "telnet", "socks", "oscar", "mms", "pna", "yahoopager" })
        {
            Assert.IsTrue(AdminController.ToggleableServices.ContainsKey(service), $"'{service}' is no longer toggleable, so it is back to being database-edit-only.");
        }
    }
}
