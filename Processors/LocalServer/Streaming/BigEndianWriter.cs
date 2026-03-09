// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
// Big-endian binary writer for RealMedia container construction

using System.Buffers.Binary;

namespace VintageHive.Processors.LocalServer.Streaming;

internal class BigEndianWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public BigEndianWriter(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    public void WriteU8(byte value)
    {
        _stream.WriteByte(value);
    }

    public void WriteU16(ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, value);
        _stream.Write(buf);
    }

    public void WriteU32(uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        _stream.Write(buf);
    }

    public void WriteBytes(byte[] data)
    {
        _stream.Write(data, 0, data.Length);
    }

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
