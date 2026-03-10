// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar;

public class OscarChatRoom
{
    public string Name { get; set; }

    public ushort Exchange { get; set; } = 4;

    public ushort Instance { get; set; }

    public string Cookie { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<OscarSession> Members { get; } = new();

    public string FullyQualifiedName => $"!aol://2719:10-4-{Name}";

    public byte[] EncodeChatRoomInfo()
    {
        var mem = new MemoryStream();

        // Exchange
        mem.Write(OscarUtils.GetBytes(Exchange));

        // Cookie (room name as cookie)
        var cookieBytes = Encoding.ASCII.GetBytes(Name);
        mem.WriteByte((byte)cookieBytes.Length);
        mem.Write(cookieBytes);

        // Instance
        mem.Write(OscarUtils.GetBytes(Instance));

        return mem.ToArray();
    }

    public byte[] EncodeRoomInfoTlvs()
    {
        var tlvs = new List<Tlv>
        {
            new Tlv(0x00D3, Name),                                                    // Room name
            new Tlv(0x00D5, new byte[] { 0x02 }),                                     // Content type (ASCII)
            new Tlv(0x00D2, OscarUtils.GetBytes((ushort)0x0000)),                     // Max message length
            new Tlv(0x00D6, Encoding.ASCII.GetBytes("us-ascii")),                     // Language
            new Tlv(0x00DA, OscarUtils.GetBytes((ushort)Members.Count)),              // Occupants
            new Tlv(0x00D7, OscarUtils.GetBytes((uint)CreatedAt.ToUnixTimeSeconds())) // Creation time
        };

        var mem = new MemoryStream();

        foreach (var tlv in tlvs)
        {
            mem.Write(tlv.Encode());
        }

        return mem.ToArray();
    }
}
