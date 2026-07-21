// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarSsiService : IOscarService
{
    public const ushort FAMILY_ID = 0x13;

    public const ushort CLI_SSI_RIGHTS_REQ = 0x02;
    public const ushort SRV_SSI_RIGHTS_REPLY = 0x03;
    public const ushort CLI_SSI_REQ_IFMODIFIED = 0x04;
    public const ushort CLI_SSI_REQ = 0x05;
    public const ushort SRV_SSI_LIST = 0x06;
    public const ushort CLI_SSI_ACTIVATE = 0x07;
    public const ushort CLI_SSI_ADD = 0x08;
    public const ushort CLI_SSI_UPDATE = 0x09;
    public const ushort CLI_SSI_DELETE = 0x0A;
    public const ushort SRV_SSI_MOD_ACK = 0x0E;
    public const ushort SRV_SSI_LIST_UNAVAILABLE = 0x0F;
    public const ushort CLI_SSI_EDIT_BEGIN = 0x11;
    public const ushort CLI_SSI_EDIT_END = 0x12;
    public const ushort SRV_SSI_YOU_WERE_ADDED = 0x1C;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarSsiService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_SSI_RIGHTS_REQ:
            {
                var reply = snac.NewReply(FAMILY_ID, SRV_SSI_RIGHTS_REPLY);

                // Max items per type
                reply.WriteTlv(new Tlv(0x04, BuildMaxItemCounts()));
                // Max recent buddies
                reply.WriteTlv(new Tlv(0x05, OscarUtils.GetBytes((ushort)100)));
                // Max watcher buddies
                reply.WriteTlv(new Tlv(0x06, OscarUtils.GetBytes((ushort)100)));

                await session.SendSnac(reply);
            }
            break;

            case CLI_SSI_REQ:
            case CLI_SSI_REQ_IFMODIFIED:
            {
                await SendSsiList(session, snac);
            }
            break;

            case CLI_SSI_ACTIVATE:
            {
                // Client is telling us SSI is active - load permit/deny from SSI
                LoadPrivacyFromSsi(session);

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarSsiService), "SSI activated", traceId);
            }
            break;

            case CLI_SSI_ADD:
            {
                await ProcessSsiModification(session, snac, SsiOperation.Add);
            }
            break;

            case CLI_SSI_UPDATE:
            {
                await ProcessSsiModification(session, snac, SsiOperation.Update);
            }
            break;

            case CLI_SSI_DELETE:
            {
                await ProcessSsiModification(session, snac, SsiOperation.Delete);
            }
            break;

            case CLI_SSI_EDIT_BEGIN:
            {
                // Client is entering SSI edit mode
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarSsiService), "SSI edit begin", traceId);
            }
            break;

            case CLI_SSI_EDIT_END:
            {
                // Client finished editing SSI
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarSsiService), "SSI edit end", traceId);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarSsiService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }

    private async Task SendSsiList(OscarSession session, Snac snac)
    {
        var items = EnsureSsiItems(session);

        var reply = snac.NewReply(FAMILY_ID, SRV_SSI_LIST);

        // SSI protocol version
        reply.WriteUInt8(0x00);

        // Item count
        reply.WriteUInt16((ushort)items.Count);

        foreach (var item in items)
        {
            reply.Write(item.Encode());
        }

        // Timestamp (last modification)
        reply.WriteUInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await session.SendSnac(reply);
    }

    // Loads the account's SSI items, creating the default tree on first contact. Internal so tests
    // can drive the empty-account path without a socket.
    internal List<OscarSsiItem> EnsureSsiItems(OscarSession session)
    {
        var items = Mind.Db.OscarGetSsiItems(session.ScreenName);

        // If no SSI data exists, create a default root group
        if (items.Count == 0)
        {
            var rootGroup = new OscarSsiItem
            {
                ScreenName = session.ScreenName,
                Name = string.Empty,
                GroupId = 0,
                ItemId = 0,
                ItemType = OscarSsiItem.TYPE_GROUP,
                TlvData = Array.Empty<byte>()
            };

            Mind.Db.OscarSsiAddItem(rootGroup);

            var buddiesGroup = new OscarSsiItem
            {
                ScreenName = session.ScreenName,
                Name = "Buddies",
                GroupId = 1,
                ItemId = 0,
                ItemType = OscarSsiItem.TYPE_GROUP,
                TlvData = Array.Empty<byte>()
            };

            Mind.Db.OscarSsiAddItem(buddiesGroup);

            // Migrate any existing buddy list to SSI
            if (session.Buddies.Count > 0)
            {
                ushort nextItemId = 1;

                foreach (var buddy in session.Buddies)
                {
                    var buddyItem = new OscarSsiItem
                    {
                        ScreenName = session.ScreenName,
                        Name = buddy,
                        GroupId = 1,
                        ItemId = nextItemId++,
                        ItemType = OscarSsiItem.TYPE_BUDDY,
                        TlvData = Array.Empty<byte>()
                    };

                    Mind.Db.OscarSsiAddItem(buddyItem);
                }
            }

            // ALWAYS wire up the tree's child lists, buddies or not. AIM 4.x walks the tree from
            // the master group's TLV 0x00C8; a root group without one sent 4.7.2480 into an
            // unbounded walk that exhausted its stack (KERNEL32 page fault right after
            // CLI_SSI_ACTIVATE on a fresh, empty account).
            UpdateGroupMemberList(session.ScreenName, 1);
            UpdateRootGroupList(session.ScreenName);

            items = Mind.Db.OscarGetSsiItems(session.ScreenName);
        }

        return items;
    }

    private async Task ProcessSsiModification(OscarSession session, Snac snac, SsiOperation operation)
    {
        var traceId = session.Client.TraceId.ToString();
        var data = snac.RawData;
        var readIdx = 0;
        var results = new List<ushort>();

        while (readIdx < data.Length)
        {
            var nameLength = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
            readIdx += 2;

            var name = nameLength > 0 ? Encoding.ASCII.GetString(data[readIdx..(readIdx + nameLength)]) : string.Empty;
            readIdx += nameLength;

            var groupId = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
            readIdx += 2;

            var itemId = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
            readIdx += 2;

            var itemType = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
            readIdx += 2;

            var tlvLength = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
            readIdx += 2;

            var tlvData = tlvLength > 0 ? data[readIdx..(readIdx + tlvLength)] : Array.Empty<byte>();
            readIdx += tlvLength;

            var item = new OscarSsiItem
            {
                ScreenName = session.ScreenName,
                Name = name,
                GroupId = groupId,
                ItemId = itemId,
                ItemType = itemType,
                TlvData = tlvData
            };

            try
            {
                switch (operation)
                {
                    case SsiOperation.Add:
                    {
                        Mind.Db.OscarSsiAddItem(item);

                        Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarSsiService), $"SSI add: type=0x{itemType:X4} group={groupId} item={itemId} name=\"{name}\"", traceId);

                        // If a buddy was added, notify them (you-were-added)
                        if (itemType == OscarSsiItem.TYPE_BUDDY)
                        {
                            var targetSession = OscarServer.Sessions.GetByScreenName(name);

                            if (targetSession != null)
                            {
                                var addedSnac = new Snac(FAMILY_ID, SRV_SSI_YOU_WERE_ADDED);

                                addedSnac.WriteUInt8((byte)session.ScreenName.Length);
                                addedSnac.WriteString(session.ScreenName);

                                await targetSession.SendSnac(addedSnac);
                            }

                            // Also add to session buddies for legacy compatibility
                            if (!session.Buddies.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            {
                                session.Buddies.Add(name);
                                session.Save();
                            }
                        }

                        results.Add(0x0000); // Success
                    }
                    break;

                    case SsiOperation.Update:
                    {
                        Mind.Db.OscarSsiUpdateItem(item);

                        Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarSsiService), $"SSI update: type=0x{itemType:X4} group={groupId} item={itemId} name=\"{name}\"", traceId);

                        results.Add(0x0000); // Success
                    }
                    break;

                    case SsiOperation.Delete:
                    {
                        Mind.Db.OscarSsiDeleteItem(session.ScreenName, groupId, itemId, itemType);

                        Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarSsiService), $"SSI delete: type=0x{itemType:X4} group={groupId} item={itemId} name=\"{name}\"", traceId);

                        // Remove from session buddies for legacy compatibility
                        if (itemType == OscarSsiItem.TYPE_BUDDY)
                        {
                            session.Buddies.RemoveAll(b => b.Equals(name, StringComparison.OrdinalIgnoreCase));
                            session.Save();
                        }

                        results.Add(0x0000); // Success
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(OscarSsiService), ex, traceId);
                results.Add(0x000A); // Error
            }
        }

        // Send acknowledgment
        var ackSnac = snac.NewReply(FAMILY_ID, SRV_SSI_MOD_ACK);

        foreach (var result in results)
        {
            ackSnac.WriteUInt16(result);
        }

        await session.SendSnac(ackSnac);

        // Reload permit/deny after any SSI change
        LoadPrivacyFromSsi(session);
    }

    private void LoadPrivacyFromSsi(OscarSession session)
    {
        var items = Mind.Db.OscarGetSsiItems(session.ScreenName);

        session.PermitList = items.Where(i => i.ItemType == OscarSsiItem.TYPE_PERMIT).Select(i => i.Name).ToList();
        session.DenyList = items.Where(i => i.ItemType == OscarSsiItem.TYPE_DENY).Select(i => i.Name).ToList();

        var pdSettings = items.FirstOrDefault(i => i.ItemType == OscarSsiItem.TYPE_PERMIT_DENY_SETTINGS);

        if (pdSettings != null && pdSettings.TlvData.Length >= 5)
        {
            // TLV 0x00CA contains the privacy mode byte
            var tlvs = OscarUtils.DecodeTlvs(pdSettings.TlvData);
            var modeTlv = tlvs.GetTlv(0x00CA);

            if (modeTlv != null && modeTlv.Value.Length >= 1)
            {
                session.PrivacyMode = modeTlv.Value[0];
            }
        }
    }

    private void UpdateGroupMemberList(string screenName, ushort groupId)
    {
        var items = Mind.Db.OscarGetSsiItems(screenName);
        var members = items.Where(i => i.GroupId == groupId && i.ItemType == OscarSsiItem.TYPE_BUDDY).ToList();

        var tlvData = new MemoryStream();
        var memberIds = new MemoryStream();

        foreach (var member in members)
        {
            memberIds.Write(OscarUtils.GetBytes(member.ItemId));
        }

        // An EMPTY 0x00C8 is valid and required - omitting the TLV entirely is what breaks AIM 4.x.
        tlvData.Write(new Tlv(0x00C8, memberIds.ToArray()).Encode());

        var groupItem = items.FirstOrDefault(i => i.GroupId == groupId && i.ItemType == OscarSsiItem.TYPE_GROUP && i.ItemId == 0);

        if (groupItem != null)
        {
            groupItem.TlvData = tlvData.ToArray();
            Mind.Db.OscarSsiUpdateItem(groupItem);
        }
    }

    private void UpdateRootGroupList(string screenName)
    {
        var items = Mind.Db.OscarGetSsiItems(screenName);
        var groups = items.Where(i => i.ItemType == OscarSsiItem.TYPE_GROUP && i.GroupId != 0).ToList();

        var memberIds = new MemoryStream();

        foreach (var group in groups)
        {
            memberIds.Write(OscarUtils.GetBytes(group.GroupId));
        }

        var tlvData = new MemoryStream();

        // An EMPTY 0x00C8 is valid and required - omitting the TLV entirely is what breaks AIM 4.x.
        tlvData.Write(new Tlv(0x00C8, memberIds.ToArray()).Encode());

        var rootItem = items.FirstOrDefault(i => i.GroupId == 0 && i.ItemType == OscarSsiItem.TYPE_GROUP);

        if (rootItem != null)
        {
            rootItem.TlvData = tlvData.ToArray();
            Mind.Db.OscarSsiUpdateItem(rootItem);
        }
    }

    // One max-count slot per SSI item type 0x00-0x14. AIM 4.x indexes this array BY ITEM TYPE while
    // building the buddy list, so a short array feeds it out-of-bounds garbage and the client
    // page-faults during "Starting services" (reproduced: AIM 4.7.2480 on Win98, KERNEL32 fault
    // ~3.5s after CLI_SSI_ACTIVATE). 21 big-endian ushorts = 42 bytes inside TLV 0x04.
    internal static byte[] BuildMaxItemCounts()
    {
        var counts = new ushort[]
        {
            200, // 0x00 buddies
            30,  // 0x01 groups
            100, // 0x02 permit
            100, // 0x03 deny
            1,   // 0x04 permit/deny settings
            1,   // 0x05 presence info
            100, // 0x06
            100, // 0x07
            100, // 0x08
            100, // 0x09
            100, // 0x0A
            100, // 0x0B
            100, // 0x0C
            100, // 0x0D
            100, // 0x0E
            100, // 0x0F
            100, // 0x10
            100, // 0x11
            100, // 0x12
            100, // 0x13
            1,   // 0x14 buddy icon
        };

        var mem = new MemoryStream();

        foreach (var count in counts)
        {
            mem.Write(OscarUtils.GetBytes(count));
        }

        return mem.ToArray();
    }

    private enum SsiOperation
    {
        Add,
        Update,
        Delete
    }
}
