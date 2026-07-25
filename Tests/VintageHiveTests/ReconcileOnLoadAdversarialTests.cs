// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// Adversarial coverage for the two reconcile-on-load seams: the OSCAR profile mail address (stamped
// into the row at creation, so it has to be re-derived on read or it outlives the config that made
// it) and the SSI group child lists (wired only when the tree is CREATED, so a persisted tree needs
// repairing on load). Both take arbitrary persisted bytes as input - rows written by an older build,
// by a client, or by a half-finished write - so the edges here are what a fresh install never hits.

using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Oscar.Services;

namespace Adversarial6.ReconcileOnLoad;

[TestClass]
public class ProfileEmailReconcileAdversarialTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, HiveDomains.Base);
    }

    // Puts an arbitrary address into the stored row, bypassing whatever the creation path stamps.
    private static void StoreRawEmail(string screenName, string email)
    {
        Mind.Db.OscarEnsureProfileExists(screenName);

        var profile = Mind.Db.OscarGetProfile(screenName);

        profile.Email = email;

        Mind.Db.OscarInsertOrUpdateProfile(profile);
    }

    [TestMethod]
    public void EmptyStoredEmail_DerivedFromPrimaryOnRead()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        var screenName = "rlp01";

        StoreRawEmail(screenName, string.Empty);

        Assert.AreEqual($"{screenName}@example.com", Mind.Db.OscarGetProfile(screenName).Email, "a blank column must not surface as a blank address");
    }

    [TestMethod]
    public void StoredOnSecondaryHostedDomain_LeftAlone()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com,second.com");

        var screenName = "rlp02";

        StoreRawEmail(screenName, $"{screenName}@second.com");

        // second.com is hosted, just not primary. Reconciling toward Primary here would silently
        // move accounts off a domain this host genuinely serves.
        Assert.AreEqual($"{screenName}@second.com", Mind.Db.OscarGetProfile(screenName).Email, "any hosted domain is valid, not just the primary");
    }

    [TestMethod]
    public void StoredDomainDiffersOnlyByCase_LeftAlone()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        var screenName = "rlp03";

        StoreRawEmail(screenName, $"{screenName}@EXAMPLE.COM");

        Assert.AreEqual($"{screenName}@EXAMPLE.COM", Mind.Db.OscarGetProfile(screenName).Email, "domain matching is case-insensitive - this address is already hosted");
    }

    [TestMethod]
    public void StoredLocalPartDiffersOnlyByCase_StaleDomain_Reconciled()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        var screenName = "rlp04";

        StoreRawEmail(screenName, "RLP04@old.com");

        // AIM screen names are case-insensitive, so RLP04@ is the auto-stamped default for rlp04.
        Assert.AreEqual($"{screenName}@example.com", Mind.Db.OscarGetProfile(screenName).Email, "case-different local part is still the auto-stamped default");
    }

    [TestMethod]
    public void ScreenNameWithSpace_StaleDomain_Reconciled()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        // A screen name with a space produces an address no email parser accepts. Reconciliation
        // splits on the last '@' rather than parsing, precisely so these rows are still repairable.
        var screenName = "Rlp Zero Five";

        StoreRawEmail(screenName, $"{screenName}@old.com");

        Assert.AreEqual($"{screenName}@example.com", Mind.Db.OscarGetProfile(screenName).Email, "an unparseable-but-stamped address must still follow config");
    }

    [TestMethod]
    public void StoredWithoutAtSign_LeftAlone()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        var screenName = "rlp06";

        StoreRawEmail(screenName, "not-an-address");

        // No domain to judge, so there is nothing to reconcile against. Junk stays junk rather than
        // being silently replaced with an address the user never had.
        Assert.AreEqual("not-an-address", Mind.Db.OscarGetProfile(screenName).Email);
    }

    [TestMethod]
    public void StoredWithLeadingAt_LeftAlone()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        var screenName = "rlp07";

        StoreRawEmail(screenName, "@old.com");

        // An empty local part can never equal the screen name, so this is not the auto default.
        Assert.AreEqual("@old.com", Mind.Db.OscarGetProfile(screenName).Email);
    }

    [TestMethod]
    public void StoredWithMultipleAt_ForeignDomain_LeftAlone()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        var screenName = "rlp08";

        StoreRawEmail(screenName, $"{screenName}@sub@old.com");

        // Splitting on the LAST '@' makes the local part "rlp08@sub", which is not the screen name,
        // so this reads as a user-set address and is left intact.
        Assert.AreEqual($"{screenName}@sub@old.com", Mind.Db.OscarGetProfile(screenName).Email);
    }

    [TestMethod]
    public void NoConfiguredDomains_ReconcilesToBuiltInFallback()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, string.Empty);

        var screenName = "rlp09";

        StoreRawEmail(screenName, $"{screenName}@old.com");

        Assert.AreEqual($"{screenName}@{HiveDomains.Base}", Mind.Db.OscarGetProfile(screenName).Email, "an unset domain list falls back to hive.com, not to the dead stored domain");
    }

    [TestMethod]
    public void ReconcileFollowsConfigBothWays_NotAOneTimeMigration()
    {
        Mail.MailTestEnv.Ensure();

        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "example.com");

        var screenName = "rlp10";

        StoreRawEmail(screenName, $"{screenName}@old.com");

        Assert.AreEqual($"{screenName}@example.com", Mind.Db.OscarGetProfile(screenName).Email);

        // Derived on read, so moving the host to a new domain moves every account with it - no
        // backfill pass to run and nothing to miss.
        Mind.Db.ConfigSet(ConfigNames.ValidMailDomains, "third.com");

        Assert.AreEqual($"{screenName}@third.com", Mind.Db.OscarGetProfile(screenName).Email);
    }
}

[TestClass]
public class SsiChildListRepairAdversarialTests
{
    private static void DeleteSsiItems(string screenName)
    {
        foreach (var item in Mind.Db.OscarGetSsiItems(screenName))
        {
            Mind.Db.OscarSsiDeleteItem(screenName, item.GroupId, item.ItemId, item.ItemType);
        }
    }

    private static void AddGroup(string screenName, ushort groupId, string name, byte[] tlvData)
    {
        Mind.Db.OscarSsiAddItem(new OscarSsiItem { ScreenName = screenName, Name = name, GroupId = groupId, ItemId = 0, ItemType = OscarSsiItem.TYPE_GROUP, TlvData = tlvData });
    }

    private static void AddBuddy(string screenName, ushort groupId, ushort itemId, string name)
    {
        Mind.Db.OscarSsiAddItem(new OscarSsiItem { ScreenName = screenName, Name = name, GroupId = groupId, ItemId = itemId, ItemType = OscarSsiItem.TYPE_BUDDY, TlvData = Array.Empty<byte>() });
    }

    private static List<OscarSsiItem> Load(string screenName)
    {
        return new OscarSsiService(null).EnsureSsiItems(new OscarSession { ScreenName = screenName });
    }

    [TestMethod]
    public void MalformedGroupTlv_RepairedInsteadOfThrowing()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "rls01";

        DeleteSsiItems(screenName);

        try
        {
            // A TLV header claiming 255 bytes of value with none present - a truncated write, or a
            // client sending garbage. Decoding this throws, and this runs on the sign-on path, so
            // undecodable has to mean "repair it", not "take the whole SSI list down".
            AddGroup(screenName, 0, string.Empty, new byte[] { 0x00, 0xC8, 0x00, 0xFF });
            AddGroup(screenName, 1, "Buddies", Array.Empty<byte>());

            var items = Load(screenName);

            var root = items.Single(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01 }, OscarUtils.DecodeTlvs(root.TlvData).GetTlv(0x00C8)!.Value, "a malformed blob must be rebuilt, not propagated");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }

    [TestMethod]
    public void TruncatedTlvBlob_TooShortToDecode_Repaired()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "rls02";

        DeleteSsiItems(screenName);

        try
        {
            // Two bytes: shorter than a TLV header, so it cannot even be inspected.
            AddGroup(screenName, 0, string.Empty, new byte[] { 0x00, 0xC8 });
            AddGroup(screenName, 1, "Buddies", Array.Empty<byte>());

            var items = Load(screenName);

            var root = items.Single(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            Assert.IsNotNull(OscarUtils.DecodeTlvs(root.TlvData).GetTlv(0x00C8), "a sub-header-length blob counts as missing");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }

    [TestMethod]
    public void Repair_IsIdempotent_SecondLoadIsStable()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "rls03";

        DeleteSsiItems(screenName);

        try
        {
            AddGroup(screenName, 0, string.Empty, Array.Empty<byte>());
            AddGroup(screenName, 1, "Buddies", Array.Empty<byte>());
            AddBuddy(screenName, 1, 1, "pal1");

            var first = Load(screenName);
            var second = Load(screenName);

            Assert.AreEqual(first.Count, second.Count, "repair must not accumulate rows across sign-ons");

            foreach (var group in first.Where(i => i.ItemType == OscarSsiItem.TYPE_GROUP))
            {
                var after = second.Single(i => i.GroupId == group.GroupId && i.ItemType == OscarSsiItem.TYPE_GROUP);

                CollectionAssert.AreEqual(group.TlvData, after.TlvData, $"group {group.GroupId} changed on a second load");
            }
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }

    [TestMethod]
    public void Repair_MultipleGroups_EachGetsItsOwnMemberIds()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "rls04";

        DeleteSsiItems(screenName);

        try
        {
            AddGroup(screenName, 0, string.Empty, Array.Empty<byte>());
            AddGroup(screenName, 1, "Buddies", Array.Empty<byte>());
            AddGroup(screenName, 2, "Work", Array.Empty<byte>());
            AddBuddy(screenName, 1, 1, "pal1");
            AddBuddy(screenName, 2, 2, "coworker");
            AddBuddy(screenName, 2, 3, "boss");

            var items = Load(screenName);

            var root = items.Single(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);
            var buddies = items.Single(i => i.GroupId == 1 && i.ItemType == OscarSsiItem.TYPE_GROUP);
            var work = items.Single(i => i.GroupId == 2 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01, 0x00, 0x02 }, OscarUtils.DecodeTlvs(root.TlvData).GetTlv(0x00C8)!.Value, "root must list both groups");
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01 }, OscarUtils.DecodeTlvs(buddies.TlvData).GetTlv(0x00C8)!.Value);
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x02, 0x00, 0x03 }, OscarUtils.DecodeTlvs(work.TlvData).GetTlv(0x00C8)!.Value, "a group's list must carry only its own members");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }

    [TestMethod]
    public void Repair_PartialTree_OnlyTheGroupMissingItsListIsRewritten()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "rls05";

        DeleteSsiItems(screenName);

        try
        {
            // Root is healthy and lists its groups in the client's own order; only "Work" is broken.
            AddGroup(screenName, 0, string.Empty, new Tlv(0x00C8, new byte[] { 0x00, 0x02, 0x00, 0x01 }).Encode());
            AddGroup(screenName, 1, "Buddies", new Tlv(0x00C8, Array.Empty<byte>()).Encode());
            AddGroup(screenName, 2, "Work", Array.Empty<byte>());
            AddBuddy(screenName, 2, 5, "coworker");

            var items = Load(screenName);

            var root = items.Single(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);
            var work = items.Single(i => i.GroupId == 2 && i.ItemType == OscarSsiItem.TYPE_GROUP);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x02, 0x00, 0x01 }, OscarUtils.DecodeTlvs(root.TlvData).GetTlv(0x00C8)!.Value, "a healthy root must keep the client's ordering");
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x05 }, OscarUtils.DecodeTlvs(work.TlvData).GetTlv(0x00C8)!.Value, "the broken group must be rebuilt from its buddy rows");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }

    [TestMethod]
    public void Repair_GroupCarryingOnlyAnUnrelatedTlv_LosesThatTlv()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "rls06";

        DeleteSsiItems(screenName);

        try
        {
            // KNOWN TRADE-OFF, pinned deliberately: repair rebuilds TlvData wholesale rather than
            // merging, so a group with some other TLV but no 0x00C8 loses it. The rebuild is what
            // stops AIM 4.x page-faulting, and group rows in practice carry only the child list.
            AddGroup(screenName, 0, string.Empty, Array.Empty<byte>());
            AddGroup(screenName, 1, "Buddies", new Tlv(0x00C9, new byte[] { 0xDE, 0xAD }).Encode());

            var items = Load(screenName);

            var buddies = items.Single(i => i.GroupId == 1 && i.ItemType == OscarSsiItem.TYPE_GROUP);
            var tlvs = OscarUtils.DecodeTlvs(buddies.TlvData);

            Assert.IsNotNull(tlvs.GetTlv(0x00C8), "the child list must be written");
            Assert.IsNull(tlvs.GetTlv(0x00C9), "unrelated TLVs are dropped by the rebuild - change this assertion if repair learns to merge");
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }

    [TestMethod]
    public void Repair_GroupWithNoBuddies_GetsEmptyChildList_NotAMissingOne()
    {
        Mail.MailTestEnv.Ensure();

        var screenName = "rls07";

        DeleteSsiItems(screenName);

        try
        {
            AddGroup(screenName, 0, string.Empty, Array.Empty<byte>());
            AddGroup(screenName, 1, "Buddies", Array.Empty<byte>());

            var items = Load(screenName);

            var buddies = items.Single(i => i.GroupId == 1 && i.ItemType == OscarSsiItem.TYPE_GROUP);
            var childList = OscarUtils.DecodeTlvs(buddies.TlvData).GetTlv(0x00C8);

            // Present-but-empty is the whole point: omitting the TLV is what breaks the client.
            Assert.IsNotNull(childList, "an empty group still needs the TLV");
            Assert.AreEqual(0, childList.Value.Length);
        }
        finally
        {
            DeleteSsiItems(screenName);
        }
    }
}
