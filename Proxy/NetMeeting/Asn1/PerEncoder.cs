// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.Asn1;

/// <summary>
/// ASN.1 PER (Packed Encoding Rules) ALIGNED variant encoder.
/// Bit-level writer producing an octet stream with APER alignment rules.
/// Used by H.323 (H.225.0 RAS, Call Signaling, H.245) and T.120.
/// Reference: ITU-T X.691 (2021-02).
/// </summary>
internal class PerEncoder
{
    private readonly MemoryStream _stream;
    private byte _currentByte;
    private int _bitsInCurrentByte; // 0-7, number of bits written into _currentByte (MSB first)

    public PerEncoder()
    {
        _stream = new MemoryStream();
    }

    /// <summary>Number of complete bytes flushed so far.</summary>
    public int ByteCount => (int)_stream.Length;

    /// <summary>Number of bits written into the current partial byte (0-7).</summary>
    public int BitOffset => _bitsInCurrentByte;

    /// <summary>Total bits written.</summary>
    public int TotalBits => ByteCount * 8 + _bitsInCurrentByte;

    // ──────────────────────────────────────────────────────────
    //  Bit-level operations
    // ──────────────────────────────────────────────────────────

    /// <summary>Write a single bit (MSB-first within each byte).</summary>
    public void WriteBit(bool value)
    {
        if (value)
        {
            _currentByte |= (byte)(0x80 >> _bitsInCurrentByte);
        }

        _bitsInCurrentByte++;

        if (_bitsInCurrentByte == 8)
        {
            FlushByte();
        }
    }

    /// <summary>Write N bits from the least-significant end of value (MSB-first).</summary>
    public void WriteBits(uint value, int count)
    {
        if (count < 0 || count > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        for (var i = count - 1; i >= 0; i--)
        {
            WriteBit(((value >> i) & 1) != 0);
        }
    }

    /// <summary>Write N bits from a 64-bit value (MSB-first).</summary>
    public void WriteBitsLong(ulong value, int count)
    {
        if (count < 0 || count > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        for (var i = count - 1; i >= 0; i--)
        {
            WriteBit(((value >> i) & 1) != 0);
        }
    }

    /// <summary>
    /// Advance to the next octet boundary. Padding bits are zero.
    /// If already aligned, does nothing.
    /// </summary>
    public void AlignToOctet()
    {
        if (_bitsInCurrentByte > 0)
        {
            FlushByte();
        }
    }

    /// <summary>Write raw bytes. Must be octet-aligned.</summary>
    public void WriteOctets(byte[] data)
    {
        WriteOctets(data, 0, data.Length);
    }

    /// <summary>Write raw bytes from a slice. Must be octet-aligned.</summary>
    public void WriteOctets(byte[] data, int offset, int count)
    {
        if (_bitsInCurrentByte != 0)
        {
            throw new InvalidOperationException("WriteOctets requires octet alignment");
        }

        _stream.Write(data, offset, count);
    }

    // ──────────────────────────────────────────────────────────
    //  PER encoding primitives (ITU-T X.691)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Constrained whole number (X.691 Section 12.2).
    /// Encodes value in [lb..ub] using the minimum representation for the range.
    /// </summary>
    public void WriteConstrainedWholeNumber(long value, long lb, long ub)
    {
        var range = ub - lb + 1;
        var offset = value - lb;

        if (range <= 0)
        {
            throw new ArgumentException("Upper bound must be >= lower bound");
        }

        if (offset < 0 || offset >= range)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Value {value} outside constraint [{lb}..{ub}]");
        }

        if (range == 1)
        {
            // Nothing to encode — value is predetermined
            return;
        }

        if (range <= 255)
        {
            // Bit-field, NOT octet-aligned (X.691 12.2.1)
            var bits = BitsNeeded(range);
            WriteBits((uint)offset, bits);
        }
        else if (range == 256)
        {
            // 1 octet, octet-aligned (X.691 12.2.2)
            AlignToOctet();
            WriteBits((uint)offset, 8);
        }
        else if (range <= 65536)
        {
            // 2 octets, octet-aligned (X.691 12.2.2)
            AlignToOctet();
            WriteBits((uint)offset, 16);
        }
        else
        {
            // Length-determinant + minimal octets (X.691 12.2.6)
            AlignToOctet();
            var numBytes = MinimalUnsignedByteCount(offset);
            WriteConstrainedWholeNumber(numBytes, 1, 4);
            AlignToOctet();
            WriteMinimalUnsignedBytes(offset, numBytes);
        }
    }

    /// <summary>
    /// Semi-constrained whole number (X.691 Section 12.2.3).
    /// Has lower bound but no upper bound.
    /// </summary>
    public void WriteSemiConstrainedWholeNumber(long value, long lb)
    {
        var offset = value - lb;

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Value {value} below lower bound {lb}");
        }

        AlignToOctet();
        var numBytes = offset == 0 ? 1 : MinimalUnsignedByteCount(offset);
        WriteLengthDeterminant(numBytes);
        AlignToOctet();
        WriteMinimalUnsignedBytes(offset, numBytes);
    }

    /// <summary>
    /// Unconstrained whole number (X.691 Section 12.2.4).
    /// Two's complement, length-prefixed.
    /// </summary>
    public void WriteUnconstrainedWholeNumber(long value)
    {
        AlignToOctet();
        var bytes = MinimalSignedBytes(value);
        WriteLengthDeterminant(bytes.Length);
        AlignToOctet();
        WriteOctets(bytes);
    }

    /// <summary>
    /// Unconstrained length determinant (X.691 Section 11.9).
    /// </summary>
    public void WriteLengthDeterminant(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        AlignToOctet();

        if (length < 128)
        {
            WriteBits((uint)length, 8); // 0xxxxxxx
        }
        else if (length < 16384)
        {
            WriteBits((uint)(0x8000 | length), 16); // 10xxxxxx xxxxxxxx
        }
        else
        {
            throw new NotSupportedException(
                $"PER fragmentation (length {length} >= 16384) is not supported");
        }
    }

    /// <summary>
    /// Constrained length determinant.
    /// If ub &lt;= 65535, encode as constrained whole number; otherwise unconstrained.
    /// </summary>
    public void WriteConstrainedLengthDeterminant(int length, int lb, int ub)
    {
        if (ub <= 65535)
        {
            WriteConstrainedWholeNumber(length, lb, ub);
        }
        else
        {
            WriteLengthDeterminant(length);
        }
    }

    /// <summary>
    /// Normally-small non-negative whole number (X.691 Section 11.6).
    /// Used for CHOICE extension indices and extension bitmap counts.
    /// </summary>
    public void WriteNormallySmallNumber(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value <= 63)
        {
            WriteBit(false); // small
            WriteBits((uint)value, 6);
        }
        else
        {
            WriteBit(true); // large
            WriteSemiConstrainedWholeNumber(value, 0);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Common ASN.1 type encodings
    // ──────────────────────────────────────────────────────────

    /// <summary>PER BOOLEAN: single bit (X.691 Section 12.1).</summary>
    public void WriteBoolean(bool value)
    {
        WriteBit(value);
    }

    /// <summary>
    /// PER ENUMERATED (X.691 Section 13).
    /// Encodes the zero-based index into the enumeration root.
    /// </summary>
    public void WriteEnumerated(int index, int rootCount, bool extensible = false, bool isExtension = false)
    {
        if (extensible)
        {
            WriteBit(isExtension);
        }

        if (isExtension)
        {
            WriteNormallySmallNumber(index);
        }
        else
        {
            WriteConstrainedWholeNumber(index, 0, rootCount - 1);
        }
    }

    /// <summary>
    /// PER OCTET STRING (X.691 Section 17).
    /// Pass lb/ub for size-constrained strings.
    /// </summary>
    public void WriteOctetString(byte[] value, int? lb = null, int? ub = null)
    {
        var fixedSize = lb.HasValue && ub.HasValue && lb.Value == ub.Value;

        if (fixedSize)
        {
            var size = lb.Value;

            if (size == 0)
            {
                return;
            }

            if (size <= 2)
            {
                // Short fixed: NOT octet-aligned
                for (var i = 0; i < size; i++)
                {
                    WriteBits(value[i], 8);
                }
            }
            else
            {
                // Fixed >= 3: octet-aligned, no length
                AlignToOctet();
                WriteOctets(value, 0, size);
            }
        }
        else if (lb.HasValue && ub.HasValue && ub.Value <= 65535)
        {
            // Constrained variable
            WriteConstrainedWholeNumber(value.Length, lb.Value, ub.Value);
            AlignToOctet();

            if (value.Length > 0)
            {
                WriteOctets(value);
            }
        }
        else
        {
            // Unconstrained or semi-constrained
            WriteLengthDeterminant(value.Length);
            AlignToOctet();

            if (value.Length > 0)
            {
                WriteOctets(value);
            }
        }
    }

    /// <summary>
    /// PER BIT STRING (X.691 Section 16).
    /// bitCount = total number of significant bits in value.
    /// </summary>
    public void WriteBitString(byte[] value, int bitCount, int? lb = null, int? ub = null)
    {
        var fixedSize = lb.HasValue && ub.HasValue && lb.Value == ub.Value;

        if (fixedSize)
        {
            var size = lb.Value;

            if (size == 0)
            {
                return;
            }

            if (size <= 16)
            {
                // Short fixed: NOT octet-aligned, write individual bits
                for (var i = 0; i < size; i++)
                {
                    var byteIdx = i / 8;
                    var bitIdx = 7 - (i % 8);
                    WriteBit(((value[byteIdx] >> bitIdx) & 1) != 0);
                }
            }
            else
            {
                // Fixed > 16: octet-aligned
                AlignToOctet();
                var byteCount = (size + 7) / 8;
                WriteOctets(value, 0, byteCount);
            }
        }
        else if (lb.HasValue && ub.HasValue && ub.Value <= 65535)
        {
            // Constrained variable
            WriteConstrainedWholeNumber(bitCount, lb.Value, ub.Value);
            AlignToOctet();
            var byteCount = (bitCount + 7) / 8;

            if (byteCount > 0)
            {
                WriteOctets(value, 0, byteCount);
            }
        }
        else
        {
            // Unconstrained
            WriteLengthDeterminant(bitCount);
            AlignToOctet();
            var byteCount = (bitCount + 7) / 8;

            if (byteCount > 0)
            {
                WriteOctets(value, 0, byteCount);
            }
        }
    }

    /// <summary>
    /// PER OBJECT IDENTIFIER (X.691 Section 24).
    /// Encodes using BER content octets + unconstrained length.
    /// </summary>
    public void WriteObjectIdentifier(int[] components)
    {
        if (components.Length < 2)
        {
            throw new ArgumentException("OID must have at least 2 components");
        }

        var contents = new MemoryStream();

        // First two components: c1 * 40 + c2 (X.690 Section 8.19.4)
        contents.WriteByte((byte)(components[0] * 40 + components[1]));

        for (var i = 2; i < components.Length; i++)
        {
            EncodeOidComponent(contents, components[i]);
        }

        var bytes = contents.ToArray();
        WriteLengthDeterminant(bytes.Length);
        AlignToOctet();
        WriteOctets(bytes);
    }

    /// <summary>
    /// PER IA5String (X.691 Section 30).
    /// In APER: each character = 8 bits (7-bit char range > 4 bits, padded to octet).
    /// </summary>
    public void WriteIA5String(string value, int? lb = null, int? ub = null)
    {
        var fixedSize = lb.HasValue && ub.HasValue && lb.Value == ub.Value;

        if (fixedSize)
        {
            AlignToOctet();

            for (var i = 0; i < lb.Value; i++)
            {
                WriteBits((uint)value[i], 8);
            }
        }
        else if (lb.HasValue && ub.HasValue && ub.Value <= 65535)
        {
            WriteConstrainedWholeNumber(value.Length, lb.Value, ub.Value);
            AlignToOctet();

            for (var i = 0; i < value.Length; i++)
            {
                WriteBits((uint)value[i], 8);
            }
        }
        else
        {
            WriteLengthDeterminant(value.Length);
            AlignToOctet();

            for (var i = 0; i < value.Length; i++)
            {
                WriteBits((uint)value[i], 8);
            }
        }
    }

    /// <summary>
    /// PER BMPString (X.691 Section 30).
    /// Each character = 16 bits (Basic Multilingual Plane, UCS-2).
    /// </summary>
    public void WriteBMPString(string value, int? lb = null, int? ub = null)
    {
        var fixedSize = lb.HasValue && ub.HasValue && lb.Value == ub.Value;

        if (fixedSize)
        {
            AlignToOctet();

            for (var i = 0; i < lb.Value; i++)
            {
                WriteBits((uint)value[i], 16);
            }
        }
        else if (lb.HasValue && ub.HasValue && ub.Value <= 65535)
        {
            WriteConstrainedWholeNumber(value.Length, lb.Value, ub.Value);
            AlignToOctet();

            for (var i = 0; i < value.Length; i++)
            {
                WriteBits((uint)value[i], 16);
            }
        }
        else
        {
            WriteLengthDeterminant(value.Length);
            AlignToOctet();

            for (var i = 0; i < value.Length; i++)
            {
                WriteBits((uint)value[i], 16);
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  SEQUENCE / CHOICE helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>Write extension marker bit (first bit of an extensible type).</summary>
    public void WriteExtensionBit(bool hasExtensions)
    {
        WriteBit(hasExtensions);
    }

    /// <summary>
    /// Write presence bitmap for OPTIONAL/DEFAULT fields in a SEQUENCE root.
    /// Each true = field is present.
    /// </summary>
    public void WriteOptionalBitmap(params bool[] present)
    {
        foreach (var p in present)
        {
            WriteBit(p);
        }
    }

    /// <summary>Encode a CHOICE index for root alternatives.</summary>
    public void WriteChoiceIndex(int index, int numAlternatives, bool extensible = false, bool isExtension = false)
    {
        if (extensible)
        {
            WriteBit(isExtension);
        }

        if (isExtension)
        {
            WriteNormallySmallNumber(index);
        }
        else
        {
            WriteConstrainedWholeNumber(index, 0, numAlternatives - 1);
        }
    }

    /// <summary>
    /// Encode an open type value (X.691 Section 11.2).
    /// The inner encoding is length-prefixed for extension additions.
    /// </summary>
    public void WriteOpenType(Action<PerEncoder> writeContents)
    {
        var inner = new PerEncoder();
        writeContents(inner);
        var bytes = inner.ToArray();
        WriteLengthDeterminant(bytes.Length);
        AlignToOctet();
        WriteOctets(bytes);
    }

    /// <summary>
    /// Write extension additions for a SEQUENCE.
    /// Each non-null action encodes one present extension addition.
    /// Null entries indicate absent optional extensions.
    /// </summary>
    public void WriteExtensionAdditions(params Action<PerEncoder>[] additions)
    {
        // Build presence bitmap
        var present = new bool[additions.Length];

        for (var i = 0; i < additions.Length; i++)
        {
            present[i] = additions[i] != null;
        }

        // Count (normally-small number, minus 1 per X.691 19.6)
        WriteNormallySmallNumber(additions.Length - 1);

        // Presence bitmap
        WriteOptionalBitmap(present);

        // Encode each present addition as open type
        for (var i = 0; i < additions.Length; i++)
        {
            if (additions[i] != null)
            {
                WriteOpenType(additions[i]);
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Output
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Return the encoded byte array. Partial trailing byte (if any) is included
    /// with zero-padding in the unused low bits.
    /// </summary>
    public byte[] ToArray()
    {
        if (_bitsInCurrentByte == 0)
        {
            return _stream.ToArray();
        }

        var full = _stream.ToArray();
        var result = new byte[full.Length + 1];
        Array.Copy(full, result, full.Length);
        result[full.Length] = _currentByte;
        return result;
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private void FlushByte()
    {
        _stream.WriteByte(_currentByte);
        _currentByte = 0;
        _bitsInCurrentByte = 0;
    }

    /// <summary>
    /// Minimum number of bits needed to represent (range - 1).
    /// Used for constrained whole number bit-field width.
    /// </summary>
    internal static int BitsNeeded(long range)
    {
        if (range <= 1)
        {
            return 0;
        }

        var bits = 0;
        var v = range - 1;

        while (v > 0)
        {
            bits++;
            v >>= 1;
        }

        return bits;
    }

    private static int MinimalUnsignedByteCount(long value)
    {
        if (value < 0)
        {
            throw new ArgumentException("Value must be non-negative");
        }

        if (value <= 0xFF) return 1;
        if (value <= 0xFFFF) return 2;
        if (value <= 0xFFFFFF) return 3;
        if (value <= 0xFFFFFFFFL) return 4;
        if (value <= 0xFFFFFFFFFFL) return 5;
        if (value <= 0xFFFFFFFFFFFFL) return 6;
        if (value <= 0xFFFFFFFFFFFFFFL) return 7;
        return 8;
    }

    private void WriteMinimalUnsignedBytes(long value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            WriteBits((uint)((value >> (i * 8)) & 0xFF), 8);
        }
    }

    internal static byte[] MinimalSignedBytes(long value)
    {
        var bytes = new byte[8];

        for (var i = 0; i < 8; i++)
        {
            bytes[7 - i] = (byte)(value >> (i * 8));
        }

        // Find first significant byte (minimal two's complement)
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
        var result = new byte[length];
        Array.Copy(bytes, start, result, 0, length);
        return result;
    }

    private static void EncodeOidComponent(MemoryStream stream, int value)
    {
        if (value < 0)
        {
            throw new ArgumentException("OID component must be non-negative");
        }

        if (value < 128)
        {
            stream.WriteByte((byte)value);
            return;
        }

        // Base-128 encoding, big-endian, continuation bit in MSB
        var temp = new List<byte>();
        temp.Add((byte)(value & 0x7F)); // Last byte: no continuation bit

        value >>= 7;

        while (value > 0)
        {
            temp.Add((byte)(0x80 | (value & 0x7F))); // Continuation bit set
            value >>= 7;
        }

        temp.Reverse();

        foreach (var b in temp)
        {
            stream.WriteByte(b);
        }
    }
}
