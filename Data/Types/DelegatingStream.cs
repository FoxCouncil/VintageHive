// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;

namespace VintageHive.Data.Types;

internal class DelegatingStream : Stream
{
    private readonly Stream innerStream;

    #region Properties

    public override bool CanRead
    {
        get { return innerStream.CanRead; }
    }

    public override bool CanSeek
    {
        get { return innerStream.CanSeek; }
    }

    public override bool CanWrite
    {
        get { return innerStream.CanWrite; }
    }

    public override long Length
    {
        get { return innerStream.Length; }
    }

    public override long Position
    {
        get { return innerStream.Position; }
        set { innerStream.Position = value; }
    }

    public override int ReadTimeout
    {
        get { return innerStream.ReadTimeout; }
        set { innerStream.ReadTimeout = value; }
    }

    public override bool CanTimeout
    {
        get { return innerStream.CanTimeout; }
    }

    public override int WriteTimeout
    {
        get { return innerStream.WriteTimeout; }
        set { innerStream.WriteTimeout = value; }
    }

    #endregion Properties

    protected DelegatingStream(Stream innerStream)
    {
        Debug.Assert(innerStream != null);

        this.innerStream = innerStream;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            innerStream.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        return innerStream.DisposeAsync();
    }

    #region Read

    public override long Seek(long offset, SeekOrigin origin)
    {
        return innerStream.Seek(offset, origin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return innerStream.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        return innerStream.Read(buffer);
    }

    public override int ReadByte()
    {
        return innerStream.ReadByte();
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return innerStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return innerStream.ReadAsync(buffer, cancellationToken);
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        return innerStream.BeginRead(buffer, offset, count, callback, state);
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        return innerStream.EndRead(asyncResult);
    }

    public override void CopyTo(Stream destination, int bufferSize)
    {
        innerStream.CopyTo(destination, bufferSize);
    }

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        return innerStream.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    #endregion Read

    #region Write

    public override void Flush()
    {
        innerStream.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return innerStream.FlushAsync(cancellationToken);
    }

    public override void SetLength(long value)
    {
        innerStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        innerStream.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        innerStream.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        innerStream.WriteByte(value);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return innerStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return innerStream.WriteAsync(buffer, cancellationToken);
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        return innerStream.BeginWrite(buffer, offset, count, callback, state);
    }

    public override void EndWrite(IAsyncResult asyncResult)
    {
        innerStream.EndWrite(asyncResult);
    }
    #endregion Write
}
