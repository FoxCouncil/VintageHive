// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;

namespace VintageHive.Utilities;

/// <summary>
/// Stream wrapper that strips ICY metadata from an upstream radio stream.
/// Reads audio data normally, but at every metaInterval bytes, reads the
/// ICY metadata block (1-byte length indicator + length*16 bytes of text)
/// and parses StreamTitle from it.
/// </summary>
internal class IcyMetadataStrippingStream : Stream
{
    private readonly Stream _inner;
    private readonly int _metaInterval;
    private int _bytesUntilMeta;
    private volatile string _currentTrack;

    private static readonly Regex StreamTitleRegex = new(
        @"StreamTitle='([^']*)'", RegexOptions.Compiled);

    public string CurrentTrack => _currentTrack;

    public event Action<string> TrackChanged;

    public IcyMetadataStrippingStream(Stream inner, int metaInterval)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _metaInterval = metaInterval;
        _bytesUntilMeta = metaInterval;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_bytesUntilMeta <= 0)
        {
            await ReadAndParseMetadataAsync(cancellationToken);
            _bytesUntilMeta = _metaInterval;
        }

        // Only read up to the next metadata boundary
        int toRead = Math.Min(count, _bytesUntilMeta);
        int read = await _inner.ReadAsync(buffer.AsMemory(offset, toRead), cancellationToken);
        _bytesUntilMeta -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_bytesUntilMeta <= 0)
        {
            await ReadAndParseMetadataAsync(cancellationToken);
            _bytesUntilMeta = _metaInterval;
        }

        int toRead = Math.Min(buffer.Length, _bytesUntilMeta);
        int read = await _inner.ReadAsync(buffer[..toRead], cancellationToken);
        _bytesUntilMeta -= read;
        return read;
    }

    private async Task ReadAndParseMetadataAsync(CancellationToken ct)
    {
        // Read 1-byte length indicator
        var lengthBuf = new byte[1];
        int read = await ReadExactFromInnerAsync(lengthBuf, 0, 1, ct);
        if (read == 0) return;

        int metaLength = lengthBuf[0] * 16;
        if (metaLength == 0) return;

        // Read metadata block
        var metaBuf = new byte[metaLength];
        read = await ReadExactFromInnerAsync(metaBuf, 0, metaLength, ct);
        if (read == 0) return;

        // Parse StreamTitle — metadata is ASCII/UTF-8 null-padded
        var metaText = Encoding.UTF8.GetString(metaBuf).TrimEnd('\0');
        var match = StreamTitleRegex.Match(metaText);
        if (match.Success)
        {
            var title = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(title))
            {
                var oldTrack = _currentTrack;
                _currentTrack = title;
                if (title != oldTrack)
                    TrackChanged?.Invoke(title);
            }
        }
    }

    private async Task<int> ReadExactFromInnerAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await _inner.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    // Required Stream overrides — delegate to inner
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
