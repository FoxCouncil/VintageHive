// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
// Big-endian bitstream writer for cook codec frames

namespace VintageHive.Processors.LocalServer.Streaming;

internal class CookBitWriter
{
    private readonly byte[] _buffer;
    private int _bitPos;
    private readonly int _totalBits;

    public int BitsWritten => _bitPos;

    public CookBitWriter(int byteSize)
    {
        _buffer = new byte[byteSize];
        _bitPos = 0;
        _totalBits = byteSize * 8;
    }

    public void Write(uint value, int numBits)
    {
        if (numBits <= 0 || _bitPos + numBits > _totalBits)
        {
            return;
        }

        for (int i = numBits - 1; i >= 0; i--)
        {
            int byteIdx = _bitPos / 8;
            int bitIdx = 7 - (_bitPos % 8);
            if (((value >> i) & 1) != 0)
            {
                _buffer[byteIdx] |= (byte)(1 << bitIdx);
            }
            _bitPos++;
        }
    }

    public void PadToEnd()
    {
        // Remaining bits are already zero
        _bitPos = _totalBits;
    }

    public byte[] ToArray()
    {
        return (byte[])_buffer.Clone();
    }
}
