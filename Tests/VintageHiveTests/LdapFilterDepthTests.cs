// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// LdapFilter.Parse recursed once per nested and/or/not with no depth limit. Each level costs about two bytes
// on the wire against a 1 MB message cap, so a single SearchRequest could drive it tens of thousands of levels
// deep. StackOverflowException cannot be caught in .NET, so this did not merely reset the connection - it took
// the whole VintageHive process down, from an unauthenticated TCP connection to the ILS port.
//
// Building the payload here mirrors the attack exactly: N nested FILTER_NOT wrappers around one trivial
// FILTER_PRESENT, which is the cheapest nesting per byte the encoding allows.

using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.ILS;

namespace Adversarial5.LdapDepth;

[TestClass]
public class LdapFilterDepthTests
{
    // A FILTER_PRESENT for "cn": tag, length, then the attribute name.
    private static byte[] PresentFilter()
    {
        var name = "cn"u8.ToArray();

        var bytes = new List<byte> { LdapConstants.FILTER_PRESENT, (byte)name.Length };

        bytes.AddRange(name);

        return bytes.ToArray();
    }

    // Wraps the payload in `levels` nested NOT filters. Short-form BER lengths only, which keeps this honest:
    // every level really is a couple of bytes, exactly as it would be from a hostile client.
    private static byte[] NestNot(byte[] payload, int levels)
    {
        var current = payload;

        for (var i = 0; i < levels; i++)
        {
            if (current.Length > 127)
            {
                // Long-form length: one byte count, then the length itself. Two bytes covers 64 KB.
                var lengthBytes = current.Length > 255
                    ? new byte[] { 0x82, (byte)(current.Length >> 8), (byte)(current.Length & 0xFF) }
                    : new byte[] { 0x81, (byte)current.Length };

                var wrapped = new List<byte> { LdapConstants.FILTER_NOT };

                wrapped.AddRange(lengthBytes);
                wrapped.AddRange(current);

                current = wrapped.ToArray();
            }
            else
            {
                var wrapped = new List<byte> { LdapConstants.FILTER_NOT, (byte)current.Length };

                wrapped.AddRange(current);

                current = wrapped.ToArray();
            }
        }

        return current;
    }

    // The one that matters. If the cap is ever removed this does not fail - it kills the test host outright,
    // which is precisely the point being defended against.
    [TestMethod]
    [Timeout(30000)]
    public void APathologicallyNestedFilter_IsRejectedRatherThanOverflowingTheStack()
    {
        var hostile = NestNot(PresentFilter(), 5000);

        Assert.ThrowsExactly<ApplicationException>(() => LdapFilter.Parse(new BerDecoder(hostile)), "A 5000-deep filter was parsed instead of refused.");
    }

    [TestMethod]
    public void AFilterExactlyAtTheLimit_IsStillAccepted()
    {
        // The root sits at depth 0, so MaxNestingDepth wrappers is the deepest legal tree.
        var atLimit = NestNot(PresentFilter(), LdapFilter.MaxNestingDepth);

        var filter = LdapFilter.Parse(new BerDecoder(atLimit));

        Assert.IsNotNull(filter, "A filter exactly at the documented limit was refused, so the cap is off by one.");
    }

    [TestMethod]
    public void AFilterOneLevelPastTheLimit_IsRefused()
    {
        var pastLimit = NestNot(PresentFilter(), LdapFilter.MaxNestingDepth + 1);

        Assert.ThrowsExactly<ApplicationException>(() => LdapFilter.Parse(new BerDecoder(pastLimit)));
    }

    // Ordinary filters are a level or two deep; the cap must be nowhere near them.
    [TestMethod]
    public void AnOrdinaryShallowFilter_IsUnaffected()
    {
        var ordinary = NestNot(PresentFilter(), 2);

        Assert.IsNotNull(LdapFilter.Parse(new BerDecoder(ordinary)));

        Assert.IsNotNull(LdapFilter.Parse(new BerDecoder(PresentFilter())));
    }
}
