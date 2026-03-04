// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;

namespace VintageHive.Proxy.NetMeeting.Asn1;

/// <summary>
/// ASN.1 BER (Basic Encoding Rules) decoder. Cursor-based reader over a byte buffer.
/// Supports definite-length encoding only (as required by LDAP / RFC 4511 Section 5.1).
/// </summary>
internal class BerDecoder
{
    private readonly byte[] _data;
    private readonly int _offset;
    private readonly int _length;
    private int _position;

    public BerDecoder(byte[] data) : this(data, 0, data.Length) { }

    public BerDecoder(byte[] data, int offset, int length)
    {
        _data = data;
        _offset = offset;
        _length = length;
        _position = 0;
    }

    public int Position => _position;
    public int Remaining => _length - _position;
    public bool HasData => _position < _length;

    // --- Raw byte access ---

    private byte ReadByte()
    {
        if (_position >= _length)
        {
            throw new InvalidOperationException("Unexpected end of BER data");
        }
        return _data[_offset + _position++];
    }

    private byte PeekByte()
    {
        if (_position >= _length)
        {
            throw new InvalidOperationException("Unexpected end of BER data");
        }
        return _data[_offset + _position];
    }

    // --- Tag reading ---

    public BerTag ReadTag()
    {
        var b = ReadByte();
        if ((b & 0x1F) != 0x1F)
        {
            return new BerTag(b);
        }

        // Multi-byte tag (X.690 Section 8.1.2.4): base-128 with continuation bit
        var tagNumber = 0;
        byte cont;
        do
        {
            cont = ReadByte();
            tagNumber = (tagNumber << 7) | (cont & 0x7F);
        }
        while ((cont & 0x80) != 0);

        return new BerTag(b, tagNumber);
    }

    public BerTag PeekTag()
    {
        var saved = _position;
        var tag = ReadTag();
        _position = saved;
        return tag;
    }

    public bool IsNextTag(byte expected)
    {
        if (!HasData)
        {
            return false;
        }
        return PeekByte() == expected;
    }

    // --- Length reading ---

    public int ReadLength()
    {
        var b = ReadByte();
        if ((b & 0x80) == 0)
        {
            return b;
        }

        var numBytes = b & 0x7F;
        if (numBytes == 0)
        {
            throw new InvalidOperationException("Indefinite length form is not supported (RFC 4511 Section 5.1)");
        }
        if (numBytes > 4)
        {
            throw new InvalidOperationException($"Length encoding uses {numBytes} bytes, maximum supported is 4");
        }

        var length = 0;
        for (var i = 0; i < numBytes; i++)
        {
            length = (length << 8) | ReadByte();
        }
        return length;
    }

    // --- Raw value reading ---

    public byte[] ReadBytes(int count)
    {
        if (count < 0 || _position + count > _length)
        {
            throw new InvalidOperationException($"Cannot read {count} bytes, only {Remaining} remaining");
        }
        var result = new byte[count];
        Array.Copy(_data, _offset + _position, result, 0, count);
        _position += count;
        return result;
    }

    public BerDecoder Slice(int length)
    {
        if (length < 0 || _position + length > _length)
        {
            throw new InvalidOperationException($"Cannot slice {length} bytes, only {Remaining} remaining");
        }
        var slice = new BerDecoder(_data, _offset + _position, length);
        _position += length;
        return slice;
    }

    public byte[] ReadRemainingBytes()
    {
        return ReadBytes(Remaining);
    }

    // --- Full TLV reading ---

    public byte[] ReadRawTlv()
    {
        var start = _position;
        ReadTag();
        var length = ReadLength();
        if (_position + length > _length)
        {
            throw new InvalidOperationException($"TLV value extends past end of data");
        }
        _position += length;
        var total = _position - start;
        var result = new byte[total];
        Array.Copy(_data, _offset + start, result, 0, total);
        return result;
    }

    // --- Skipping ---

    public void Skip()
    {
        ReadTag();
        var length = ReadLength();
        if (_position + length > _length)
        {
            throw new InvalidOperationException($"Cannot skip {length} bytes, only {Remaining} remaining");
        }
        _position += length;
    }

    // --- Primitive types (read tag + length + value) ---

    public int ReadInteger()
    {
        var tag = ReadTag();
        if (!tag.Is(BerTag.INTEGER))
        {
            throw new InvalidOperationException($"Expected INTEGER (0x02), got {tag}");
        }
        var length = ReadLength();
        return ReadIntegerValue(length);
    }

    public long ReadLong()
    {
        var tag = ReadTag();
        if (!tag.Is(BerTag.INTEGER))
        {
            throw new InvalidOperationException($"Expected INTEGER (0x02), got {tag}");
        }
        var length = ReadLength();
        return ReadLongValue(length);
    }

    public byte[] ReadOctetString()
    {
        var tag = ReadTag();
        if (!tag.Is(BerTag.OCTET_STRING))
        {
            throw new InvalidOperationException($"Expected OCTET STRING (0x04), got {tag}");
        }
        var length = ReadLength();
        return ReadBytes(length);
    }

    public string ReadString()
    {
        var bytes = ReadOctetString();
        return Encoding.UTF8.GetString(bytes);
    }

    public bool ReadBoolean()
    {
        var tag = ReadTag();
        if (!tag.Is(BerTag.BOOLEAN))
        {
            throw new InvalidOperationException($"Expected BOOLEAN (0x01), got {tag}");
        }
        var length = ReadLength();
        if (length != 1)
        {
            throw new InvalidOperationException($"BOOLEAN must be exactly 1 byte, got {length}");
        }
        return ReadByte() != 0;
    }

    public int ReadEnumerated()
    {
        var tag = ReadTag();
        if (!tag.Is(BerTag.ENUMERATED))
        {
            throw new InvalidOperationException($"Expected ENUMERATED (0x0A), got {tag}");
        }
        var length = ReadLength();
        return ReadIntegerValue(length);
    }

    public void ReadNull()
    {
        var tag = ReadTag();
        if (!tag.Is(BerTag.NULL))
        {
            throw new InvalidOperationException($"Expected NULL (0x05), got {tag}");
        }
        var length = ReadLength();
        if (length != 0)
        {
            throw new InvalidOperationException($"NULL must have zero length, got {length}");
        }
    }

    // --- Constructed types (read tag + length, return sub-decoder) ---

    public BerDecoder ReadSequence()
    {
        var tag = ReadTag();
        if (!tag.Is(BerTag.SEQUENCE))
        {
            throw new InvalidOperationException($"Expected SEQUENCE (0x30), got {tag}");
        }
        var length = ReadLength();
        return Slice(length);
    }

    public BerDecoder ReadSet()
    {
        var tag = ReadTag();
        if (!tag.Is(BerTag.SET))
        {
            throw new InvalidOperationException($"Expected SET (0x31), got {tag}");
        }
        var length = ReadLength();
        return Slice(length);
    }

    // --- Application tag reading ---

    public BerDecoder ReadApplicationConstructed(int tagNumber)
    {
        var expected = BerTag.Application(tagNumber, constructed: true);
        var tag = ReadTag();
        if (!tag.Is(expected))
        {
            throw new InvalidOperationException($"Expected APPLICATION CONSTRUCTED {tagNumber} (0x{expected:X2}), got {tag}");
        }
        var length = ReadLength();
        return Slice(length);
    }

    public byte[] ReadApplicationPrimitive(int tagNumber)
    {
        var expected = BerTag.Application(tagNumber, constructed: false);
        var tag = ReadTag();
        if (!tag.Is(expected))
        {
            throw new InvalidOperationException($"Expected APPLICATION PRIMITIVE {tagNumber} (0x{expected:X2}), got {tag}");
        }
        var length = ReadLength();
        return ReadBytes(length);
    }

    // --- Context-specific tag reading ---

    public BerDecoder ReadContextConstructed(int tagNumber)
    {
        var expected = BerTag.Context(tagNumber, constructed: true);
        var tag = ReadTag();
        if (!tag.Is(expected))
        {
            throw new InvalidOperationException($"Expected CONTEXT CONSTRUCTED {tagNumber} (0x{expected:X2}), got {tag}");
        }
        var length = ReadLength();
        return Slice(length);
    }

    public byte[] ReadContextPrimitive(int tagNumber)
    {
        var expected = BerTag.Context(tagNumber, constructed: false);
        var tag = ReadTag();
        if (!tag.Is(expected))
        {
            throw new InvalidOperationException($"Expected CONTEXT PRIMITIVE {tagNumber} (0x{expected:X2}), got {tag}");
        }
        var length = ReadLength();
        return ReadBytes(length);
    }

    // --- Value-only readers (for use after reading tag + length manually) ---

    public int ReadIntegerValue(int length)
    {
        if (length == 0 || length > 4)
        {
            throw new InvalidOperationException($"Invalid INTEGER length: {length} (expected 1-4)");
        }
        var value = (int)(sbyte)ReadByte();
        for (var i = 1; i < length; i++)
        {
            value = (value << 8) | ReadByte();
        }
        return value;
    }

    public long ReadLongValue(int length)
    {
        if (length == 0 || length > 8)
        {
            throw new InvalidOperationException($"Invalid INTEGER length for long: {length} (expected 1-8)");
        }
        var value = (long)(sbyte)ReadByte();
        for (var i = 1; i < length; i++)
        {
            value = (value << 8) | ReadByte();
        }
        return value;
    }
}
