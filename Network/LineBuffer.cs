// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Network;

/// <summary>
/// Carry-buffer mechanics for line-based protocols: joins a new read onto whatever was left over from the
/// previous one, hands back complete CRLF-terminated lines, and stashes the remainder for next time.
/// </summary>
/// <remarks>
/// Five protocols (POP3, SMTP, IMAP, NNTP, IRC) each had their own copy of this, differing only in the DataBag
/// key and the cap. That is where the bugs lived: a client that pipelines commands into one packet, or whose
/// command is split across TCP reads, was misparsed until the loop was fixed - and it had to be fixed five
/// separate times. FTP, the sixth line-based protocol, never got the fix at all and was still treating one TCP
/// read as exactly one command until recently.
///
/// Deliberately NOT a full read loop. The five protocols' CONTROL FLOW genuinely differs - IMAP has to drain a
/// byte-counted APPEND literal before line-splitting, SMTP switches into body mode mid-loop and hands the rest
/// of the buffer to a different consumer - and forcing those through one abstraction needs so many hooks that
/// it reads worse than the explicit loops. This owns only the part that was duplicated verbatim, so each
/// protocol keeps its own shape while the mechanics live in one place.
/// </remarks>
internal sealed class LineBuffer
{
    /// <summary>Default cap on a single unterminated line before the buffer is dropped.</summary>
    public const int DefaultMaxLineBytes = 16 * 1024;

    private readonly ListenerSocket _connection;

    private readonly string _key;

    private readonly int _maxLineBytes;

    private string _pending;

    private LineBuffer(ListenerSocket connection, string key, string pending, int maxLineBytes)
    {
        _connection = connection;
        _key = key;
        _pending = pending;
        _maxLineBytes = maxLineBytes;
    }

    /// <summary>Whatever has not yet been consumed as a complete line.</summary>
    public string Pending => _pending;

    /// <summary>
    /// Joins <paramref name="read"/> bytes of <paramref name="data"/> onto the stashed remainder.
    /// </summary>
    /// <remarks>
    /// The encoding is a parameter rather than a constant because it genuinely differs: the mail and news
    /// protocols are ASCII, IRC decodes UTF-8. Defaulting it would have silently changed IRC's behaviour.
    /// </remarks>
    public static LineBuffer Open(ListenerSocket connection, string key, byte[] data, int read, int maxLineBytes = DefaultMaxLineBytes, Encoding encoding = null)
    {
        var previous = connection.DataBag.TryGetValue(key, out var stashed) ? stashed as string ?? string.Empty : string.Empty;

        return new LineBuffer(connection, key, previous + (encoding ?? Encoding.ASCII).GetString(data, 0, read), maxLineBytes);
    }

    /// <summary>
    /// Takes the next complete line, trimming its CR. Returns false when only a partial line remains.
    /// </summary>
    public bool TryReadLine(out string line)
    {
        var newline = _pending.IndexOf('\n');

        if (newline < 0)
        {
            line = null;

            return false;
        }

        line = _pending[..newline].TrimEnd('\r');

        _pending = _pending[(newline + 1)..];

        return true;
    }

    /// <summary>Consumes up to <paramref name="count"/> characters of raw, non-line-oriented payload.</summary>
    public string TakeRaw(int count)
    {
        var take = Math.Min(count, _pending.Length);

        var taken = _pending[..take];

        _pending = _pending[take..];

        return taken;
    }

    /// <summary>Consumes everything left, leaving the buffer empty.</summary>
    public string TakeRest()
    {
        var rest = _pending;

        _pending = string.Empty;

        return rest;
    }

    /// <summary>
    /// Stashes the remainder for the next read, dropping it if it has grown past the cap.
    /// </summary>
    /// <remarks>
    /// The cap is what stops a peer that opens a connection and streams without ever sending a terminator from
    /// growing this without bound.
    /// </remarks>
    public void Save()
    {
        _connection.DataBag[_key] = _pending.Length > _maxLineBytes ? string.Empty : _pending;
    }

    /// <summary>Drops anything buffered. Used when the connection is about to close.</summary>
    public void Clear()
    {
        _pending = string.Empty;

        _connection.DataBag[_key] = string.Empty;
    }
}
