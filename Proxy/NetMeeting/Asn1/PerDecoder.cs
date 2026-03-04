// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.Asn1;

/// <summary>
/// ASN.1 PER (Packed Encoding Rules) ALIGNED variant decoder.
/// Bit-level reader over a byte buffer with APER alignment rules.
/// Used by H.323 (H.225.0 RAS, Call Signaling, H.245) and T.120.
/// Reference: ITU-T X.691 (2021-02).
/// </summary>
internal class PerDecoder
{
    private readonly byte[] _data;
    private readonly int _startBit;
    private readonly int _totalBits;
    private int _bitPosition;

    public PerDecoder(byte[] data) : this(data, 0, data.Length * 8) { }

    public PerDecoder(byte[] data, int startBit, int totalBits)
    {
        _data = data;
        _startBit = startBit;
        _totalBits = totalBits;
        _bitPosition = 0;
    }

    /// <summary>Current bit position within this decoder's range.</summary>
    public int BitPosition => _bitPosition;

    /// <summary>Number of bits remaining.</summary>
    public int BitsRemaining => _totalBits - _bitPosition;

    /// <summary>Whether more data is available.</summary>
    public bool HasData => _bitPosition < _totalBits;

    // ──────────────────────────────────────────────────────────
    //  Bit-level operations
    // ──────────────────────────────────────────────────────────

    /// <summary>Read a single bit (MSB-first within each byte).</summary>
    public bool ReadBit()
    {
        if (_bitPosition >= _totalBits)
        {
            throw new InvalidOperationException("Unexpected end of PER data");
        }

        var absoluteBit = _startBit + _bitPosition;
        var byteIndex = absoluteBit / 8;
        var bitIndex = 7 - (absoluteBit % 8); // MSB first
        _bitPosition++;
        return ((_data[byteIndex] >> bitIndex) & 1) != 0;
    }

    /// <summary>Read N bits as a uint (MSB-first), 0 &lt;= count &lt;= 32.</summary>
    public uint ReadBits(int count)
    {
        if (count < 0 || count > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        uint value = 0;

        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | (ReadBit() ? 1u : 0u);
        }

        return value;
    }

    /// <summary>Read N bits as a ulong (MSB-first), 0 &lt;= count &lt;= 64.</summary>
    public ulong ReadBitsLong(int count)
    {
        if (count < 0 || count > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        ulong value = 0;

        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | (ReadBit() ? 1ul : 0ul);
        }

        return value;
    }

    /// <summary>
    /// Advance to the next octet boundary. Skips padding bits.
    /// If already aligned, does nothing.
    /// </summary>
    public void AlignToOctet()
    {
        var remainder = _bitPosition % 8;

        if (remainder != 0)
        {
            _bitPosition += 8 - remainder;
        }
    }

    /// <summary>
    /// Read N octets from the bit stream.
    /// Optimized: if already aligned, copies directly from the backing array.
    /// </summary>
    public byte[] ReadOctets(int count)
    {
        if (count == 0)
        {
            return Array.Empty<byte>();
        }

        if (_bitPosition + (count * 8) > _totalBits)
        {
            throw new InvalidOperationException(
                $"Cannot read {count} octets, only {BitsRemaining} bits remaining");
        }

        // Fast path: already aligned → direct copy
        if (_bitPosition % 8 == 0)
        {
            var byteOffset = (_startBit + _bitPosition) / 8;
            var result = new byte[count];
            Array.Copy(_data, byteOffset, result, 0, count);
            _bitPosition += count * 8;
            return result;
        }

        // Slow path: read bit-by-bit
        var data = new byte[count];

        for (var i = 0; i < count; i++)
        {
            data[i] = (byte)ReadBits(8);
        }

        return data;
    }

    // ──────────────────────────────────────────────────────────
    //  PER decoding primitives (ITU-T X.691)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Constrained whole number (X.691 Section 12.2).
    /// Returns the decoded value in [lb..ub].
    /// </summary>
    public long ReadConstrainedWholeNumber(long lb, long ub)
    {
        var range = ub - lb + 1;

        if (range <= 0)
        {
            throw new ArgumentException("Upper bound must be >= lower bound");
        }

        if (range == 1)
        {
            return lb;
        }

        long offset;

        if (range <= 255)
        {
            // Bit-field, NOT octet-aligned
            var bits = PerEncoder.BitsNeeded(range);
            offset = ReadBits(bits);
        }
        else if (range == 256)
        {
            // 1 octet, octet-aligned
            AlignToOctet();
            offset = ReadBits(8);
        }
        else if (range <= 65536)
        {
            // 2 octets, octet-aligned
            AlignToOctet();
            offset = ReadBits(16);
        }
        else
        {
            // Length + minimal octets
            AlignToOctet();
            var numBytes = (int)ReadConstrainedWholeNumber(1, 4);
            AlignToOctet();
            offset = 0;

            for (var i = 0; i < numBytes; i++)
            {
                offset = (offset << 8) | ReadBits(8);
            }
        }

        return lb + offset;
    }

    /// <summary>
    /// Semi-constrained whole number (X.691 Section 12.2.3).
    /// </summary>
    public long ReadSemiConstrainedWholeNumber(long lb)
    {
        AlignToOctet();
        var numBytes = ReadLengthDeterminant();
        AlignToOctet();
        long offset = 0;

        for (var i = 0; i < numBytes; i++)
        {
            offset = (offset << 8) | ReadBits(8);
        }

        return lb + offset;
    }

    /// <summary>
    /// Unconstrained whole number (X.691 Section 12.2.4).
    /// Two's complement, length-prefixed.
    /// </summary>
    public long ReadUnconstrainedWholeNumber()
    {
        AlignToOctet();
        var numBytes = ReadLengthDeterminant();
        AlignToOctet();

        if (numBytes == 0)
        {
            return 0;
        }

        // Two's complement: sign-extend from first byte
        long value = (sbyte)(byte)ReadBits(8);

        for (var i = 1; i < numBytes; i++)
        {
            value = (value << 8) | ReadBits(8);
        }

        return value;
    }

    /// <summary>
    /// Unconstrained length determinant (X.691 Section 11.9).
    /// </summary>
    public int ReadLengthDeterminant()
    {
        AlignToOctet();
        var first = (int)ReadBits(8);

        if ((first & 0x80) == 0)
        {
            return first; // 0-127
        }

        if ((first & 0xC0) == 0x80)
        {
            var second = (int)ReadBits(8);
            return ((first & 0x3F) << 8) | second; // 128-16383
        }

        throw new NotSupportedException("PER fragmentation is not supported");
    }

    /// <summary>
    /// Constrained length determinant.
    /// </summary>
    public int ReadConstrainedLengthDeterminant(int lb, int ub)
    {
        if (ub <= 65535)
        {
            return (int)ReadConstrainedWholeNumber(lb, ub);
        }

        return ReadLengthDeterminant();
    }

    /// <summary>
    /// Normally-small non-negative whole number (X.691 Section 11.6).
    /// </summary>
    public int ReadNormallySmallNumber()
    {
        var large = ReadBit();

        if (!large)
        {
            return (int)ReadBits(6); // 0-63
        }

        return (int)ReadSemiConstrainedWholeNumber(0);
    }

    // ──────────────────────────────────────────────────────────
    //  Common ASN.1 type decodings
    // ──────────────────────────────────────────────────────────

    /// <summary>PER BOOLEAN: single bit.</summary>
    public bool ReadBoolean()
    {
        return ReadBit();
    }

    /// <summary>
    /// PER ENUMERATED (X.691 Section 13).
    /// Returns the zero-based index.
    /// </summary>
    public int ReadEnumerated(int rootCount, bool extensible = false)
    {
        if (extensible)
        {
            var isExtension = ReadBit();

            if (isExtension)
            {
                return ReadNormallySmallNumber();
            }
        }

        return (int)ReadConstrainedWholeNumber(0, rootCount - 1);
    }

    /// <summary>
    /// PER OCTET STRING (X.691 Section 17).
    /// </summary>
    public byte[] ReadOctetString(int? lb = null, int? ub = null)
    {
        var fixedSize = lb.HasValue && ub.HasValue && lb.Value == ub.Value;

        if (fixedSize)
        {
            var size = lb.Value;

            if (size == 0)
            {
                return Array.Empty<byte>();
            }

            if (size <= 2)
            {
                // Short fixed: NOT octet-aligned
                var result = new byte[size];

                for (var i = 0; i < size; i++)
                {
                    result[i] = (byte)ReadBits(8);
                }

                return result;
            }

            // Fixed >= 3: octet-aligned
            AlignToOctet();
            return ReadOctets(size);
        }

        int length;

        if (lb.HasValue && ub.HasValue && ub.Value <= 65535)
        {
            length = (int)ReadConstrainedWholeNumber(lb.Value, ub.Value);
        }
        else
        {
            length = ReadLengthDeterminant();
        }

        AlignToOctet();
        return ReadOctets(length);
    }

    /// <summary>
    /// PER BIT STRING (X.691 Section 16).
    /// Returns the raw bytes and the actual bit count.
    /// </summary>
    public (byte[] Data, int BitCount) ReadBitString(int? lb = null, int? ub = null)
    {
        var fixedSize = lb.HasValue && ub.HasValue && lb.Value == ub.Value;

        if (fixedSize)
        {
            var size = lb.Value;

            if (size == 0)
            {
                return (Array.Empty<byte>(), 0);
            }

            if (size <= 16)
            {
                // Short fixed: NOT octet-aligned, read individual bits
                var byteCount = (size + 7) / 8;
                var data = new byte[byteCount];

                for (var i = 0; i < size; i++)
                {
                    if (ReadBit())
                    {
                        data[i / 8] |= (byte)(0x80 >> (i % 8));
                    }
                }

                return (data, size);
            }

            // Fixed > 16: octet-aligned
            AlignToOctet();
            var bytes = ReadOctets((size + 7) / 8);
            return (bytes, size);
        }

        int bitCount;

        if (lb.HasValue && ub.HasValue && ub.Value <= 65535)
        {
            bitCount = (int)ReadConstrainedWholeNumber(lb.Value, ub.Value);
        }
        else
        {
            bitCount = ReadLengthDeterminant();
        }

        AlignToOctet();

        if (bitCount == 0)
        {
            return (Array.Empty<byte>(), 0);
        }

        var resultBytes = ReadOctets((bitCount + 7) / 8);
        return (resultBytes, bitCount);
    }

    /// <summary>
    /// PER OBJECT IDENTIFIER (X.691 Section 24).
    /// </summary>
    public int[] ReadObjectIdentifier()
    {
        var numBytes = ReadLengthDeterminant();
        AlignToOctet();
        var oidBytes = ReadOctets(numBytes);
        return DecodeOidBytes(oidBytes);
    }

    /// <summary>
    /// PER IA5String (X.691 Section 30).
    /// In APER: 8 bits per character.
    /// </summary>
    public string ReadIA5String(int? lb = null, int? ub = null)
    {
        var fixedSize = lb.HasValue && ub.HasValue && lb.Value == ub.Value;
        int length;

        if (fixedSize)
        {
            length = lb.Value;
        }
        else if (lb.HasValue && ub.HasValue && ub.Value <= 65535)
        {
            length = (int)ReadConstrainedWholeNumber(lb.Value, ub.Value);
        }
        else
        {
            length = ReadLengthDeterminant();
        }

        AlignToOctet();
        var chars = new char[length];

        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)ReadBits(8);
        }

        return new string(chars);
    }

    /// <summary>
    /// PER BMPString (X.691 Section 30).
    /// Each character = 16 bits (UCS-2).
    /// </summary>
    public string ReadBMPString(int? lb = null, int? ub = null)
    {
        var fixedSize = lb.HasValue && ub.HasValue && lb.Value == ub.Value;
        int length;

        if (fixedSize)
        {
            length = lb.Value;
        }
        else if (lb.HasValue && ub.HasValue && ub.Value <= 65535)
        {
            length = (int)ReadConstrainedWholeNumber(lb.Value, ub.Value);
        }
        else
        {
            length = ReadLengthDeterminant();
        }

        AlignToOctet();
        var chars = new char[length];

        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)ReadBits(16);
        }

        return new string(chars);
    }

    // ──────────────────────────────────────────────────────────
    //  SEQUENCE / CHOICE helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>Read extension marker bit (first bit of extensible type).</summary>
    public bool ReadExtensionBit()
    {
        return ReadBit();
    }

    /// <summary>
    /// Read presence bitmap for OPTIONAL/DEFAULT fields.
    /// Returns array of booleans (true = field present).
    /// </summary>
    public bool[] ReadOptionalBitmap(int count)
    {
        var bitmap = new bool[count];

        for (var i = 0; i < count; i++)
        {
            bitmap[i] = ReadBit();
        }

        return bitmap;
    }

    /// <summary>Read a CHOICE index for root alternatives.</summary>
    public int ReadChoiceIndex(int numAlternatives, bool extensible = false)
    {
        if (extensible)
        {
            var isExtension = ReadBit();

            if (isExtension)
            {
                return ReadNormallySmallNumber();
            }
        }

        return (int)ReadConstrainedWholeNumber(0, numAlternatives - 1);
    }

    /// <summary>Read an open type value (length-prefixed encoded data).</summary>
    public byte[] ReadOpenType()
    {
        var length = ReadLengthDeterminant();
        AlignToOctet();
        return ReadOctets(length);
    }

    /// <summary>
    /// Read extension additions for a SEQUENCE.
    /// Returns array of byte[] (one per extension), null entries for absent.
    /// </summary>
    public byte[][] ReadExtensionAdditions()
    {
        var count = ReadNormallySmallNumber() + 1;
        var present = ReadOptionalBitmap(count);
        var additions = new byte[count][];

        for (var i = 0; i < count; i++)
        {
            if (present[i])
            {
                additions[i] = ReadOpenType();
            }
        }

        return additions;
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static int[] DecodeOidBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return Array.Empty<int>();
        }

        var components = new List<int>();

        // First byte: c1 * 40 + c2 (X.690 Section 8.19.4)
        components.Add(bytes[0] / 40);
        components.Add(bytes[0] % 40);

        var i = 1;

        while (i < bytes.Length)
        {
            var value = 0;
            byte b;

            do
            {
                b = bytes[i++];
                value = (value << 7) | (b & 0x7F);
            } while ((b & 0x80) != 0 && i < bytes.Length);

            components.Add(value);
        }

        return components.ToArray();
    }
}
