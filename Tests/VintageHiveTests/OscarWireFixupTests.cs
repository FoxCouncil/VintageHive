// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Wire-format pins for the AIM 4.x fixes: the SSI max-item-counts array must carry one big-endian
// ushort per item type 0x00-0x14 (the old six-entry array fed AIM 4.7's by-type indexing garbage
// and page-faulted the client during "Starting services"), and the auth-reply email TLV follows
// the primary hosted mail domain with hive.com as the built-in fallback.

using System.Buffers.Binary;
using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Proxy.Oscar;
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

    private static void DeleteSsiItems(string screenName)
    {
        foreach (var item in Mind.Db.OscarGetSsiItems(screenName))
        {
            Mind.Db.OscarSsiDeleteItem(screenName, item.GroupId, item.ItemId, item.ItemType);
        }
    }

    [TestMethod]
    public void EmptyAccount_DefaultSsi_MasterGroupCarriesChildListTlv()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "sswp1";

        DeleteSsiItems(screenName);

        try
        {
            var items = new OscarSsiService(null).EnsureSsiItems(new OscarSession { ScreenName = screenName });

            Assert.AreEqual(2, items.Count, "fresh account should have exactly root + Buddies groups");

            // The master group (gid 0) MUST carry TLV 0x00C8 listing child group 1 even with zero
            // buddies - its absence sent AIM 4.7 into a stack-exhausting tree walk.
            var root = items.Single(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            Assert.AreNotEqual(0, root.TlvData.Length, "master group shipped with empty TlvData");

            var rootChildList = OscarUtils.DecodeTlvs(root.TlvData).GetTlv(0x00C8);

            Assert.IsNotNull(rootChildList, "master group lacks TLV 0x00C8");
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01 }, rootChildList.Value, "child list must name group id 1");

            // The Buddies group carries an EMPTY 0x00C8 - present but zero members.
            var buddies = items.Single(i => i.GroupId == 1 && i.ItemType == OscarSsiItem.TYPE_GROUP);
            var buddiesChildList = OscarUtils.DecodeTlvs(buddies.TlvData).GetTlv(0x00C8);

            Assert.IsNotNull(buddiesChildList, "Buddies group lacks TLV 0x00C8");
            Assert.AreEqual(0, buddiesChildList.Value.Length, "empty group's 0x00C8 must be empty");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }

    [TestMethod]
    public void PopulatedAccount_DefaultSsi_ChildAndMemberListsFilled()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "sswp2";

        DeleteSsiItems(screenName);

        try
        {
            var session = new OscarSession { ScreenName = screenName, Buddies = ["pal1", "pal2"] };

            var items = new OscarSsiService(null).EnsureSsiItems(session);

            Assert.AreEqual(4, items.Count, "root + Buddies + two migrated buddies");

            var root = items.Single(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01 }, OscarUtils.DecodeTlvs(root.TlvData).GetTlv(0x00C8)!.Value);

            var buddies = items.Single(i => i.GroupId == 1 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01, 0x00, 0x02 }, OscarUtils.DecodeTlvs(buddies.TlvData).GetTlv(0x00C8)!.Value, "member list must carry both migrated buddy item ids");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
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

    [TestMethod]
    public void StaleProfileEmail_ReDerivedFromCurrentMailDomainOnRead()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

        var screenName = "oswp2";

        Mind.Db.OscarEnsureProfileExists(screenName);

        Assert.AreEqual($"{screenName}@{HiveDomains.Base}", Mind.Db.OscarGetProfile(screenName).Email);

        // The hosted domain is configured AFTER the row was stamped, which is the real-world order:
        // the stored address now names a domain this host does not serve.
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        Assert.AreEqual($"{screenName}@example.com", Mind.Db.OscarGetProfile(screenName).Email, "a profile created before the domain was configured must still follow it");
    }

    [TestMethod]
    public void UserSetProfileEmail_SurvivesMailDomainChange()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);

        var screenName = "oswp3";

        Mind.Db.OscarEnsureProfileExists(screenName);

        var profile = Mind.Db.OscarGetProfile(screenName);

        profile.Email = "someone.else@aol.com";

        Mind.Db.OscarInsertOrUpdateProfile(profile);

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        Assert.AreEqual("someone.else@aol.com", Mind.Db.OscarGetProfile(screenName).Email, "an address the user set is not the auto-stamped default and must not be rewritten");
    }

    [TestMethod]
    public void LegacySsiTree_MissingChildLists_RepairedOnLoad()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "sswp3";

        DeleteSsiItems(screenName);

        try
        {
            // Exactly what the pre-fix code persisted: a complete tree with no child-list TLV on any
            // group. Creating the tree is the only path that ever wired those lists, so these rows
            // kept page-faulting AIM 4.7 on every sign-on.
            Mind.Db.OscarSsiAddItem(new OscarSsiItem { ScreenName = screenName, Name = string.Empty, GroupId = 0, ItemId = 0, ItemType = OscarSsiItem.TYPE_GROUP, TlvData = Array.Empty<byte>() });
            Mind.Db.OscarSsiAddItem(new OscarSsiItem { ScreenName = screenName, Name = "Buddies", GroupId = 1, ItemId = 0, ItemType = OscarSsiItem.TYPE_GROUP, TlvData = Array.Empty<byte>() });
            Mind.Db.OscarSsiAddItem(new OscarSsiItem { ScreenName = screenName, Name = "pal1", GroupId = 1, ItemId = 1, ItemType = OscarSsiItem.TYPE_BUDDY, TlvData = Array.Empty<byte>() });

            var items = new OscarSsiService(null).EnsureSsiItems(new OscarSession { ScreenName = screenName });

            Assert.AreEqual(3, items.Count, "repair must not add or drop rows");

            var root = items.Single(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01 }, OscarUtils.DecodeTlvs(root.TlvData).GetTlv(0x00C8)!.Value, "master group child list must be backfilled on load");

            var buddies = items.Single(i => i.GroupId == 1 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01 }, OscarUtils.DecodeTlvs(buddies.TlvData).GetTlv(0x00C8)!.Value, "group member list must be backfilled from the persisted buddy rows");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }

    [TestMethod]
    public void HealthySsiTree_ClientOrderedChildList_LeftAlone()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "sswp4";

        DeleteSsiItems(screenName);

        try
        {
            // A client-managed tree whose master group lists its groups in an order the server would
            // not have chosen. Repair must not touch a group that already carries a child list.
            var clientOrder = new Tlv(0x00C8, new byte[] { 0x00, 0x02, 0x00, 0x01 }).Encode();

            Mind.Db.OscarSsiAddItem(new OscarSsiItem { ScreenName = screenName, Name = string.Empty, GroupId = 0, ItemId = 0, ItemType = OscarSsiItem.TYPE_GROUP, TlvData = clientOrder });
            Mind.Db.OscarSsiAddItem(new OscarSsiItem { ScreenName = screenName, Name = "Buddies", GroupId = 1, ItemId = 0, ItemType = OscarSsiItem.TYPE_GROUP, TlvData = new Tlv(0x00C8, Array.Empty<byte>()).Encode() });
            Mind.Db.OscarSsiAddItem(new OscarSsiItem { ScreenName = screenName, Name = "Work", GroupId = 2, ItemId = 0, ItemType = OscarSsiItem.TYPE_GROUP, TlvData = new Tlv(0x00C8, Array.Empty<byte>()).Encode() });

            var items = new OscarSsiService(null).EnsureSsiItems(new OscarSession { ScreenName = screenName });

            var root = items.Single(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x02, 0x00, 0x01 }, OscarUtils.DecodeTlvs(root.TlvData).GetTlv(0x00C8)!.Value, "an existing child list must keep the client's ordering");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }
}
