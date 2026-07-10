// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities;

internal class HttpFixerDelegatingStream : DelegatingStream
{
    private static readonly byte[] IcyPrefix = Encoding.ASCII.GetBytes("ICY");
    private static readonly byte[] IcyStatus = Encoding.ASCII.GetBytes("ICY 200 OK");
    private static readonly byte[] HttpStatus = Encoding.ASCII.GetBytes("HTTP/1.0 200 OK");

    private bool headerChecked;

    internal HttpFixerDelegatingStream(Stream innerStream) : base(innerStream) { }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await base.ReadAsync(buffer, cancellationToken);

        // Only the very first read can carry the ICY status line. Every subsequent read is opaque audio and must
        // pass through byte-for-byte - sniffing it for "ICY" would mangle binary frames that happen to start with
        // those bytes and could inspect past what was actually read.
        if (headerChecked)
        {
            return read;
        }

        headerChecked = true;

        var span = buffer.Span;

        // Need at least the 3-byte "ICY" prefix present in this read before touching anything.
        if (read < 3 || span[0] != IcyPrefix[0] || span[1] != IcyPrefix[1] || span[2] != IcyPrefix[2])
        {
            return read;
        }

        if (!StartsWith(span, read, IcyStatus))
        {
            return read;
        }

        // "ICY 200 OK" (10 bytes) -> "HTTP/1.0 200 OK" (15 bytes): +5. Only rewrite if the buffer can hold the
        // expanded status plus the untouched remainder of this read; otherwise pass the bytes through as-is rather
        // than overrun the caller's buffer or report a length larger than it.
        var delta = HttpStatus.Length - IcyStatus.Length;

        if (read + delta > buffer.Length)
        {
            return read;
        }

        // Shift the bytes after the status token right by `delta` (iterate downward so we don't clobber), then
        // splice the HTTP status line in at the front. All non-status bytes are preserved exactly.
        for (var i = read - 1; i >= IcyStatus.Length; i--)
        {
            span[i + delta] = span[i];
        }

        for (var i = 0; i < HttpStatus.Length; i++)
        {
            span[i] = HttpStatus[i];
        }

        return read + delta;
    }

    private static bool StartsWith(Span<byte> span, int read, byte[] token)
    {
        if (read < token.Length)
        {
            return false;
        }

        for (var i = 0; i < token.Length; i++)
        {
            if (span[i] != token[i])
            {
                return false;
            }
        }

        return true;
    }
}
