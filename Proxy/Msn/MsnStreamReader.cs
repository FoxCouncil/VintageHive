// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Msn;

// MSNP is CRLF-delimited command lines, except payload commands (MSG) end with a byte count and are
// followed by exactly that many raw bytes. A shared buffered reader is required so bytes over-read while
// scanning for a line-ending are handed to the subsequent fixed-length payload read.
public sealed class MsnStreamReader
{
    const int MaxLineBytes = 16 * 1024;

    readonly Stream _stream;

    byte[] _buffer = new byte[8192];

    int _start;

    int _end;

    public MsnStreamReader(Stream stream)
    {
        _stream = stream;
    }

    // Reads one CRLF-terminated command line (without the CRLF), decoded as ASCII. Returns null on EOF.
    public async Task<string> ReadLineAsync()
    {
        while (true)
        {
            for (var i = _start; i + 1 < _end; i++)
            {
                if (_buffer[i] == '\r' && _buffer[i + 1] == '\n')
                {
                    var line = Encoding.ASCII.GetString(_buffer, _start, i - _start);

                    _start = i + 2;

                    return line;
                }
            }

            if (_end - _start > MaxLineBytes)
            {
                // Runaway line with no terminator; treat as a protocol error / disconnect.
                return null;
            }

            if (!await FillAsync())
            {
                return null;
            }
        }
    }

    // Reads exactly count raw bytes (an MSG payload body), drawing from the buffer first.
    public async Task<byte[]> ReadBytesAsync(int count)
    {
        var result = new byte[count];
        var written = 0;

        while (written < count)
        {
            if (_start == _end && !await FillAsync())
            {
                return null;
            }

            var available = Math.Min(_end - _start, count - written);

            Array.Copy(_buffer, _start, result, written, available);

            _start += available;
            written += available;
        }

        return result;
    }

    async Task<bool> FillAsync()
    {
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
        }
        else if (_end == _buffer.Length)
        {
            if (_start > 0)
            {
                // Slide the live window to the front to make room.
                Array.Copy(_buffer, _start, _buffer, 0, _end - _start);

                _end -= _start;
                _start = 0;
            }
            else
            {
                // Buffer full and the window starts at 0; grow it.
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }
        }

        var read = await _stream.ReadAsync(_buffer.AsMemory(_end, _buffer.Length - _end));

        if (read <= 0)
        {
            return false;
        }

        _end += read;

        return true;
    }
}
