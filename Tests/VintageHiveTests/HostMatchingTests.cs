// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Domain routing used a bare Host.EndsWith(domain), which has no notion of DNS label boundaries. An
// attacker-registered look-alike matched a curated entry - "evilblooberry.com" ends with "blooberry.com" -
// and was proxied through the passthrough path instead of being rejected. Same shape in the ProtoWeb site
// lookup. The recurring unanchored-match bug class, in string form rather than regex form.

using VintageHive.Proxy.Http;

namespace Adversarial5.HostMatching;

[TestClass]
public class HostMatchingTests
{
    [TestMethod]
    public void ALookAlikeDomain_DoesNotMatch()
    {
        Assert.IsFalse(HttpUtilities.HostMatchesDomain("evilblooberry.com", "blooberry.com"), "A look-alike host matched a curated domain, which is the whole bug.");
        Assert.IsFalse(HttpUtilities.HostMatchesDomain("notexample.com", "example.com"));
        Assert.IsFalse(HttpUtilities.HostMatchesDomain("myyahoo.com", "yahoo.com"));
    }

    [TestMethod]
    public void TheDomainItselfAndItsSubdomains_Match()
    {
        Assert.IsTrue(HttpUtilities.HostMatchesDomain("blooberry.com", "blooberry.com"));
        Assert.IsTrue(HttpUtilities.HostMatchesDomain("www.blooberry.com", "blooberry.com"));
        Assert.IsTrue(HttpUtilities.HostMatchesDomain("deep.sub.blooberry.com", "blooberry.com"));
    }

    [TestMethod]
    public void MatchingIsCaseInsensitive()
    {
        Assert.IsTrue(HttpUtilities.HostMatchesDomain("WWW.Blooberry.COM", "blooberry.com"));
        Assert.IsTrue(HttpUtilities.HostMatchesDomain("BLOOBERRY.COM", "blooberry.com"));
    }

    [TestMethod]
    public void EmptyInputsNeverMatch()
    {
        Assert.IsFalse(HttpUtilities.HostMatchesDomain(null, "example.com"));
        Assert.IsFalse(HttpUtilities.HostMatchesDomain("example.com", null));
        Assert.IsFalse(HttpUtilities.HostMatchesDomain(string.Empty, "example.com"));
        Assert.IsFalse(HttpUtilities.HostMatchesDomain("example.com", string.Empty));
    }

    // A host shorter than the domain cannot be a subdomain of it, and must not index out of range while
    // finding that out.
    [TestMethod]
    public void AShorterHostIsRejectedWithoutThrowing()
    {
        Assert.IsFalse(HttpUtilities.HostMatchesDomain("com", "example.com"));
        Assert.IsFalse(HttpUtilities.HostMatchesDomain("a", "example.com"));
    }
}
