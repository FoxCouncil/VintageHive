// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Data;

namespace VintageHive.Proxy.Oscar;

public class OscarSsiItem
{
    public const ushort TYPE_BUDDY = 0x0000;
    public const ushort TYPE_GROUP = 0x0001;
    public const ushort TYPE_PERMIT = 0x0002;
    public const ushort TYPE_DENY = 0x0003;
    public const ushort TYPE_PERMIT_DENY_SETTINGS = 0x0004;
    public const ushort TYPE_PRESENCE = 0x0005;
    public const ushort TYPE_ICON = 0x0014;

    public string ScreenName { get; set; }

    public string Name { get; set; }

    public ushort GroupId { get; set; }

    public ushort ItemId { get; set; }

    public ushort ItemType { get; set; }

    public byte[] TlvData { get; set; } = Array.Empty<byte>();

    public OscarSsiItem() { }

    public OscarSsiItem(IDataReader reader)
    {
        ScreenName = reader.GetString(0);
        Name = reader.GetString(1);
        GroupId = (ushort)reader.GetInt32(2);
        ItemId = (ushort)reader.GetInt32(3);
        ItemType = (ushort)reader.GetInt32(4);
        TlvData = (byte[])reader.GetValue(5);
    }

    public byte[] Encode()
    {
        var mem = new MemoryStream();

        mem.Write(OscarUtils.GetBytes((ushort)Name.Length));
        mem.Write(Encoding.ASCII.GetBytes(Name));
        mem.Write(OscarUtils.GetBytes(GroupId));
        mem.Write(OscarUtils.GetBytes(ItemId));
        mem.Write(OscarUtils.GetBytes(ItemType));
        mem.Write(OscarUtils.GetBytes((ushort)TlvData.Length));
        mem.Write(TlvData);

        return mem.ToArray();
    }
}
