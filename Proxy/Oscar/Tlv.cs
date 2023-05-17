// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar;

public class Tlv
{
    public const ushort Type_ScreenName = 0x0001;

    public const ushort Type_ErrorSubCode = 0x0008;

    public const ushort Error_NoMatch = 0x0014;

    public ushort Type { get; }

    public byte[] Value { get; internal set; }

    public Tlv(ushort type, string value) : this(type, Encoding.ASCII.GetBytes(value)) { }

    public Tlv(ushort type, ushort value) 
    {
        Type = type;

        Value = OscarUtils.GetBytes(value);
    }

    public Tlv(ushort type, byte[] value)
    {
        if (value.Length > ushort.MaxValue)
        {
            throw new ApplicationException("Cannot store value lengths larger than " + ushort.MaxValue);
        }

        Type = type;
        Value = value;
    }

    public byte[] Encode()
    {
        var bytes = new MemoryStream();

        bytes.Append(OscarUtils.GetBytes(Type));

        bytes.Append(OscarUtils.GetBytes((ushort)Value.Length));

        bytes.Append(Value);

        return bytes.ToArray();
    }
}
