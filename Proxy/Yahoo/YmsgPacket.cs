// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;

namespace VintageHive.Proxy.Yahoo;

// A single YMSG packet: a fixed 20-byte big-endian header followed by a flat body of
// key\xC0\x80 value\xC0\x80 pairs, where each key is an ASCII decimal field number and each value is
// raw (usually UTF-8) bytes. Reference: libyahoo2 / Wireshark packet-ymsg.c.
public sealed class YmsgPacket
{
    public const int HeaderSize = 20;

    static readonly byte[] Magic = "YMSG"u8.ToArray();

    // 0xC0 0x80 is the UTF-8 overlong encoding of NUL; it never appears inside valid UTF-8 text, so it is a
    // safe field/value delimiter that survives a UTF-8 payload.
    static readonly byte[] Separator = { 0xC0, 0x80 };

    public ushort Version { get; set; }

    public ushort Vendor { get; set; }

    public YmsgService Service { get; set; }

    public uint Status { get; set; }

    public uint SessionId { get; set; }

    public List<KeyValuePair<int, string>> Fields { get; } = new();

    public YmsgPacket() { }

    public YmsgPacket(YmsgService service, uint status, uint sessionId, ushort version = 0x0009)
    {
        Service = service;
        Status = status;
        SessionId = sessionId;
        Version = version;
    }

    public YmsgPacket Add(int key, string value)
    {
        Fields.Add(new KeyValuePair<int, string>(key, value ?? string.Empty));

        return this;
    }

    public string Get(int key)
    {
        foreach (var field in Fields)
        {
            if (field.Key == key)
            {
                return field.Value;
            }
        }

        return null;
    }

    public byte[] Encode()
    {
        using var body = new MemoryStream();

        foreach (var field in Fields)
        {
            var keyBytes = Encoding.ASCII.GetBytes(field.Key.ToString());

            body.Write(keyBytes);
            body.Write(Separator);

            var valueBytes = Encoding.UTF8.GetBytes(field.Value ?? string.Empty);

            body.Write(valueBytes);
            body.Write(Separator);
        }

        var bodyBytes = body.ToArray();

        // The YMSG body length is a 16-bit field; surface an oversized body instead of silently truncating it.
        if (bodyBytes.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException($"YMSG body of {bodyBytes.Length} bytes exceeds the 16-bit length field");
        }

        var buffer = new byte[HeaderSize + bodyBytes.Length];

        Magic.CopyTo(buffer, 0);

        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6), Vendor);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(8), (ushort)bodyBytes.Length);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(10), (ushort)Service);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12), Status);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(16), SessionId);

        bodyBytes.CopyTo(buffer, HeaderSize);

        return buffer;
    }

    // header must be exactly the 20-byte header; body is the packet body of the length the header declared.
    public static YmsgPacket Decode(byte[] header, byte[] body)
    {
        var packet = new YmsgPacket
        {
            Version = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)),
            Vendor = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6)),
            Service = (YmsgService)BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(10)),
            Status = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12)),
            SessionId = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16))
        };

        ParseBody(body, packet);

        return packet;
    }

    static void ParseBody(byte[] body, YmsgPacket packet)
    {
        var tokens = SplitOnSeparator(body);

        // Tokens alternate key, value. A trailing lone token (malformed) is ignored.
        for (var i = 0; i + 1 < tokens.Count; i += 2)
        {
            if (int.TryParse(Encoding.ASCII.GetString(tokens[i]), out var key))
            {
                packet.Add(key, Encoding.UTF8.GetString(tokens[i + 1]));
            }
        }
    }

    static List<byte[]> SplitOnSeparator(byte[] body)
    {
        var tokens = new List<byte[]>();
        var start = 0;

        for (var i = 0; i + 1 < body.Length; i++)
        {
            if (body[i] == Separator[0] && body[i + 1] == Separator[1])
            {
                tokens.Add(body[start..i]);

                i++;
                start = i + 1;
            }
        }

        return tokens;
    }

    // Validates the 4-byte magic of a header buffer.
    public static bool HasMagic(byte[] header)
    {
        return header.Length >= 4 && header[0] == Magic[0] && header[1] == Magic[1] && header[2] == Magic[2] && header[3] == Magic[3];
    }

    // The declared body length from a 20-byte header.
    public static int BodyLength(byte[] header)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(8));
    }
}
