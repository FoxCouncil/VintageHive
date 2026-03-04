// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;

namespace VintageHive.Proxy.NetMeeting.Asn1;

/// <summary>
/// ASN.1 BER (Basic Encoding Rules) encoder. Builds a byte buffer with proper TLV framing.
/// Uses definite-length encoding only (as required by LDAP / RFC 4511 Section 5.1).
/// </summary>
internal class BerEncoder
{
    private readonly MemoryStream _stream;

    public BerEncoder()
    {
        _stream = new MemoryStream();
    }

    public BerEncoder(int capacity)
    {
        _stream = new MemoryStream(capacity);
    }

    public byte[] ToArray() => _stream.ToArray();
    public int Length => (int)_stream.Length;

    // --- Raw writing ---

    public void WriteByte(byte value)
    {
        _stream.WriteByte(value);
    }

    public void WriteRawBytes(byte[] data)
    {
        _stream.Write(data, 0, data.Length);
    }

    public void WriteRawBytes(byte[] data, int offset, int count)
    {
        _stream.Write(data, offset, count);
    }

    // --- Tag and length ---

    public void WriteTag(byte tag)
    {
        _stream.WriteByte(tag);
    }

    /// <summary>
    /// Write a multi-byte tag (X.690 Section 8.1.2.4).
    /// The tagByte has the class/constructed bits set and low 5 bits = 0x1F.
    /// The tagNumber is encoded as base-128 continuation bytes.
    /// </summary>
    public void WriteTag(byte tagByte, int tagNumber)
    {
        if (tagNumber < 31)
        {
            _stream.WriteByte((byte)((tagByte & 0xE0) | tagNumber));
            return;
        }

        _stream.WriteByte(tagByte);

        // Encode tag number in base-128, most-significant group first
        // Find how many 7-bit groups we need
        var temp = tagNumber;
        var byteCount = 0;
        do
        {
            byteCount++;
            temp >>= 7;
        }
        while (temp > 0);

        for (var i = byteCount - 1; i >= 0; i--)
        {
            var group = (byte)((tagNumber >> (i * 7)) & 0x7F);
            if (i > 0)
            {
                group |= 0x80; // continuation bit
            }
            _stream.WriteByte(group);
        }
    }

    public void WriteLength(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative");
        }

        if (length <= 127)
        {
            _stream.WriteByte((byte)length);
        }
        else if (length <= 0xFF)
        {
            _stream.WriteByte(0x81);
            _stream.WriteByte((byte)length);
        }
        else if (length <= 0xFFFF)
        {
            _stream.WriteByte(0x82);
            _stream.WriteByte((byte)(length >> 8));
            _stream.WriteByte((byte)length);
        }
        else if (length <= 0xFFFFFF)
        {
            _stream.WriteByte(0x83);
            _stream.WriteByte((byte)(length >> 16));
            _stream.WriteByte((byte)(length >> 8));
            _stream.WriteByte((byte)length);
        }
        else
        {
            _stream.WriteByte(0x84);
            _stream.WriteByte((byte)(length >> 24));
            _stream.WriteByte((byte)(length >> 16));
            _stream.WriteByte((byte)(length >> 8));
            _stream.WriteByte((byte)length);
        }
    }

    // --- Primitive types (write tag + length + value) ---

    public void WriteBoolean(bool value)
    {
        WriteTag(BerTag.BOOLEAN);
        WriteLength(1);
        WriteByte(value ? (byte)0xFF : (byte)0x00);
    }

    public void WriteInteger(int value)
    {
        WriteTag(BerTag.INTEGER);
        WriteIntegerValue(value);
    }

    public void WriteLong(long value)
    {
        WriteTag(BerTag.INTEGER);
        WriteLongValue(value);
    }

    public void WriteOctetString(byte[] value)
    {
        WriteTag(BerTag.OCTET_STRING);
        WriteLength(value.Length);
        WriteRawBytes(value);
    }

    public void WriteString(string value)
    {
        WriteOctetString(Encoding.UTF8.GetBytes(value));
    }

    public void WriteEnumerated(int value)
    {
        WriteTag(BerTag.ENUMERATED);
        WriteIntegerValue(value);
    }

    public void WriteNull()
    {
        WriteTag(BerTag.NULL);
        WriteLength(0);
    }

    // --- Constructed types ---

    public void WriteSequence(Action<BerEncoder> writeContents)
    {
        WriteConstructed(BerTag.SEQUENCE, writeContents);
    }

    public void WriteSet(Action<BerEncoder> writeContents)
    {
        WriteConstructed(BerTag.SET, writeContents);
    }

    // --- Application tags ---

    public void WriteApplicationConstructed(int tagNumber, Action<BerEncoder> writeContents)
    {
        WriteConstructed(BerTag.Application(tagNumber, constructed: true), writeContents);
    }

    public void WriteApplicationPrimitive(int tagNumber, byte[] value)
    {
        WriteTag(BerTag.Application(tagNumber, constructed: false));
        WriteLength(value.Length);
        WriteRawBytes(value);
    }

    // --- Context-specific tags ---

    public void WriteContextConstructed(int tagNumber, Action<BerEncoder> writeContents)
    {
        WriteConstructed(BerTag.Context(tagNumber, constructed: true), writeContents);
    }

    public void WriteContextPrimitive(int tagNumber, byte[] value)
    {
        WriteTag(BerTag.Context(tagNumber, constructed: false));
        WriteLength(value.Length);
        WriteRawBytes(value);
    }

    // --- Value-only writers (for use after writing a custom tag) ---

    public void WriteIntegerValue(int value)
    {
        if (value >= -128 && value <= 127)
        {
            WriteLength(1);
            WriteByte((byte)value);
        }
        else if (value >= -32768 && value <= 32767)
        {
            WriteLength(2);
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }
        else if (value >= -8388608 && value <= 8388607)
        {
            WriteLength(3);
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }
        else
        {
            WriteLength(4);
            WriteByte((byte)(value >> 24));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }
    }

    public void WriteLongValue(long value)
    {
        var bytes = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            bytes[i] = (byte)value;
            value >>= 8;
        }

        // Find first significant byte (minimal two's complement encoding)
        var start = 0;
        while (start < 7)
        {
            if (bytes[start] == 0x00 && (bytes[start + 1] & 0x80) == 0)
            {
                start++;
            }
            else if (bytes[start] == 0xFF && (bytes[start + 1] & 0x80) != 0)
            {
                start++;
            }
            else
            {
                break;
            }
        }

        var length = 8 - start;
        WriteLength(length);
        _stream.Write(bytes, start, length);
    }

    // --- Internal helpers ---

    private void WriteConstructed(byte tag, Action<BerEncoder> writeContents)
    {
        var inner = new BerEncoder();
        writeContents(inner);
        var contents = inner.ToArray();
        WriteTag(tag);
        WriteLength(contents.Length);
        WriteRawBytes(contents);
    }
}
