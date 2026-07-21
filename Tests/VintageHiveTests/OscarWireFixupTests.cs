// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Wire-format pins for the AIM 4.x fixes: the SSI max-item-counts array must carry one big-endian
// ushort per item type 0x00-0x14 (the old six-entry array fed AIM 4.7's by-type indexing garbage
// and page-faulted the client during "Starting services"), and the auth-reply email TLV follows
// the primary hosted mail domain with hive.com as the built-in fallback.

using System.Buffers.Binary;
using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Proxy.Oscar.Services;

namespace Adversarial5.OscarWire;

[TestClass]
public class OscarWireFixupTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);
    }

    [TestMethod]
    public void SsiMaxItemCounts_21BigEndianUshorts_FirstSixPreserved()
    {
        var bytes = OscarSsiService.BuildMaxItemCounts();

        Assert.AreEqual(42, bytes.Length, "TLV 0x04 payload must cover item types 0x00-0x14");

        var counts = new ushort[21];

        for (var i = 0; i < counts.Length; i++)
        {
            counts[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i * 2, 2));
        }

        CollectionAssert.AreEqual(new ushort[] { 200, 30, 100, 100, 1, 1 }, counts[..6], "historical first six counts changed");

        Assert.AreEqual((ushort)1, counts[0x14], "buddy-icon slot (0x14) should be 1");

        for (var type = 0x06; type < 0x14; type++)
        {
            Assert.IsTrue(counts[type] > 0, $"type 0x{type:X2} has a zero cap - AIM treats that as unusable");
        }
    }

    [TestMethod]
    public void AuthReplyEmail_FollowsConfiguredMailDomain_FallsBackToHiveCom()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

        Assert.AreEqual("fox@hive.com", OscarAuthorizationService.BuildAccountEmail("fox"));

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com,second.com");

        Assert.AreEqual("fox@example.com", OscarAuthorizationService.BuildAccountEmail("fox"), "auth email must follow the PRIMARY hosted domain");
    }

    [TestMethod]
    public void ProfileCreation_EmailFollowsConfiguredMailDomain()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        var screenName = "oswp1";

        Mind.Db.OscarEnsureProfileExists(screenName);

        var profile = Mind.Db.OscarGetProfile(screenName);

        Assert.IsNotNull(profile);
        Assert.AreEqual($"{screenName}@example.com", profile.Email, "profile email must follow the primary hosted domain");
    }
}
