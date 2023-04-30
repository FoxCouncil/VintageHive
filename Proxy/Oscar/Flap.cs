// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar;

public class Flap
{
    public FlapFrameType Type { get; } = FlapFrameType.Data;

    public ushort Sequence { get; internal set; }

    public byte[] Data { get; internal set; } = Array.Empty<byte>();

    public Flap() { }

    public Flap(FlapFrameType type)
    {
        Type = type;
    }

    public Snac GetSnac()
    {
        if (Data.Length < 10)
        {
            throw new ApplicationException("FLAP data size too small for SNAC parsing!");
        }

        var decodedSnac = new Snac(OscarUtils.ToUInt16(Data[0..2]), OscarUtils.ToUInt16(Data[2..4]), OscarUtils.ToUInt16(Data[4..6]), OscarUtils.ToUInt32(Data[6..10]));

        decodedSnac.Data.Write(Data.AsSpan()[10..]);

        return decodedSnac;
    }

    public byte[] Encode()
    {
        var bytes = new MemoryStream();

        bytes.Append((byte)'*');

        bytes.Append((byte)Type);

        bytes.Append(OscarUtils.GetBytes(Sequence));

        bytes.Append(OscarUtils.GetBytes((ushort)Data.Length));

        bytes.Append(Data);

        return bytes.ToArray();
    }
}
