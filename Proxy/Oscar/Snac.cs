// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar;

public class Snac
{
    public ushort Family { get; set; }

    public ushort SubType { get; set; }

    public ushort Flags { get; set; }

    public uint RequestID { get; set; }

    public MemoryStream Data { get; set; } = new MemoryStream();

    public byte[] RawData => Data.ToArray();

    public Snac(ushort family, ushort subType, ushort flags = 0, uint requestID = 0)
    {
        Family = family;
        SubType = subType;
        Flags = flags;
        RequestID = requestID;
    }

    internal void Write(byte[] data)
    {
        Data.Write(data);
    }

    public void WriteString(string data)
    {
        Data.Write(Encoding.ASCII.GetBytes(data));
    }

    internal void WriteTlvs(List<Tlv> tlvs)
    {
        foreach (Tlv tlv in tlvs)
        {
            Data.Write(tlv.Encode());
        }
    }

    internal void WriteTlv(Tlv tlv)
    {
        Data.Write(tlv.Encode());
    }

    public void WriteUInt8(byte data)
    {
        Data.WriteByte(data);
    }

    public void WriteUInt16(ushort data)
    {
        Data.Write(OscarUtils.GetBytes(data));
    }

    public void WriteUInt32(uint data)
    {
        Data.Write(OscarUtils.GetBytes(data));
    }

    public void WriteUInt64(ulong data)
    {
        Data.Write(OscarUtils.GetBytes(data));
    }

    public Snac NewReply(ushort family = 0, ushort subType = 0, ushort flags = 0)
    {
        return new Snac(family != 0 ? family : Family, subType != 0 ? subType : SubType,  flags != 0 ? flags : Flags, RequestID);
    }

    public byte[] Encode()
    {
        var bytes = new MemoryStream();

        bytes.Write(OscarUtils.GetBytes(Family));
        bytes.Write(OscarUtils.GetBytes(SubType));
        bytes.Write(OscarUtils.GetBytes(Flags));
        bytes.Write(OscarUtils.GetBytes(RequestID));
        bytes.Write(Data.ToArray());

        return bytes.ToArray();
    }

    public override string ToString()
    {
        return $"SNAC({Family:X2}, {SubType:X2})";
    }
}
