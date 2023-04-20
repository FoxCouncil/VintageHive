namespace VintageHive.Proxy.Oscar;

public static class OscarUtils
{
    private static readonly byte[] ROAST_KEY = new byte[] { 0xF3, 0x26, 0x81, 0xC4, 0x39, 0x86, 0xDB, 0x92, 0x71, 0xA3, 0xB9, 0xE6, 0x53, 0x7A, 0x95, 0x7C };

    public static byte[] RoastPassword(string password)
    {
        var roastedPassword = new MemoryStream();

        for (int idx = 0; idx < password.Length; idx++)
        {
            roastedPassword.WriteByte((byte)(password[idx] ^ ROAST_KEY[idx % 16]));
        }

        return roastedPassword.ToArray();
    }

    public static byte[] GetBytes(ushort number)
    {
        var bytes = BitConverter.GetBytes(number);

        MakeNetworkByteOrder(bytes);

        return bytes;
    }

    public static byte[] GetBytes(uint number)
    {
        var bytes = BitConverter.GetBytes(number);

        MakeNetworkByteOrder(bytes);

        return bytes;
    }

    public static byte[] GetBytes(ulong number)
    {
        var bytes = BitConverter.GetBytes(number);

        MakeNetworkByteOrder(bytes);

        return bytes;
    }

    public static ushort ToUInt16(byte[] data)
    {
        MakeNetworkByteOrder(data);

        return BitConverter.ToUInt16(data);
    }

    public static uint ToUInt32(byte[] data)
    {
        MakeNetworkByteOrder(data);

        return BitConverter.ToUInt32(data);
    }

    public static ulong ToUint64(byte[] data)
    {
        MakeNetworkByteOrder(data);

        return BitConverter.ToUInt64(data);
    }

    public static OscarSession GetSessionByCookie(this List<OscarSession> sessions, string cookie)
    {
        return sessions.FirstOrDefault(x => x.Cookie == cookie);
    }

    public static OscarSession GetSessionByScreenName(this List<OscarSession> sessions, string screenname)
    {
        return sessions.FirstOrDefault(x => x.ScreenName == screenname);
    }

    public static Flap[] DecodeFlaps(byte[] data)
    {
        if (data.Length < 6)
        {
            throw new ArgumentException("Incorrect FLAP data size");
        }

        var flaps = new List<Flap>();

        var readIdx = 0;

        while (readIdx < data.Length)
        {
            if (data[readIdx] != (byte)'*')
            {
                throw new ArgumentException("Invalid FLAP header");
            }

            var flap = new Flap((FlapFrameType)data[readIdx + 1])
            {
                Sequence = ToUInt16(data[(readIdx + 2)..(readIdx + 4)]),
                Data = new byte[ToUInt16(data[(readIdx + 4)..(readIdx + 6)])]
            };

            Array.Copy(data[(readIdx + 6)..(readIdx + 6 + flap.Data.Length)], flap.Data, flap.Data.Length);

            flaps.Add(flap);

            readIdx += 6 + flap.Data.Length;
        }

        return flaps.ToArray();
    }

    public static Tlv GetTlv(this List<Tlv> tlvs, ushort type)
    {
        return tlvs.Find(t => t.Type == type);
    }

    public static Tlv GetTlv(this Tlv[] tlvs, ushort type)
    {
        return Array.Find(tlvs, t => t.Type == type);
    }

    public static Tlv[] DecodeTlvs(byte[] data)
    {
        if (data.Length < 4)
        {
            throw new ApplicationException("Data is too short for a valid TLV");
        }

        var tlvList = new List<Tlv>();

        var readIdx = 0;

        while (readIdx < data.Length)
        {
            var type = ToUInt16(data[readIdx..(readIdx + 2)]);

            var length = ToUInt16(data[(readIdx + 2)..(readIdx + 4)]);

            byte[] value = Array.Empty<byte>();

            if (length > 0)
            {
                var valOffset = readIdx + 4;

                value = data[valOffset..(valOffset + length)];
            }

            if (length != value.Length)
            {
                throw new ApplicationException("TLV data lenght missmatch!");
            }

            var tlv = new Tlv(type, value);

            tlvList.Add(tlv);

            readIdx += 4 + length;
        }

        return tlvList.ToArray();
    }

    public static byte[] EncodeTlvs(this List<Tlv> tlvs)
    {
        var bytes = new MemoryStream();

        foreach (var tlv in tlvs)
        {
            bytes.Append(tlv.Encode());
        }

        return bytes.ToArray();
    }

    public static OscarSession GetByScreenName(this List<OscarSession> sessions, string screenName)
    {
        return sessions.FirstOrDefault(x => x.ScreenName.ToLower() == screenName.ToLower());
    }

    public static string ToASCII(this byte[] data)
    {
        return Encoding.ASCII.GetString(data);
    }

    public static string ToCLSID(byte[] data)
    {
        if (data.Length != 16)
        {
            throw new ApplicationException("Not a valid CLSID!");
        }

        var partOne = BitConverter.ToString(data[0..4]).Replace("-", string.Empty);
        var partTwo = BitConverter.ToString(data[4..6]).Replace("-", string.Empty);
        var partThree = BitConverter.ToString(data[6..8]).Replace("-", string.Empty);
        var partFour = BitConverter.ToString(data[8..10]).Replace("-", string.Empty);
        var partFive = BitConverter.ToString(data[10..16]).Replace("-", string.Empty);

        return $"{partOne}-{partTwo}-{partThree}-{partFour}-{partFive}";
    }

    private static void MakeNetworkByteOrder(byte[] bytes)
    {
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
    }
}
