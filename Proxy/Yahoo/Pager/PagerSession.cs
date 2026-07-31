// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;

namespace VintageHive.Proxy.Yahoo.Pager;

/// <summary>
/// The HTTP Pager transport: no socket, a queue. Where <see cref="YmsgSession"/> writes a packet down a held
/// connection, this enqueues one and the client's next POST to /notify/ drains it.
/// <para>
/// It is a <see cref="YahooSession"/> and lives in <see cref="YahooSessionRegistry"/> alongside YMSG sessions,
/// which is the whole point: a Pager member and a YMSG member on the same small network see each other's
/// presence and exchange messages, and a login on either wire supersedes the other.
/// </para>
/// </summary>
public sealed class PagerSession : YahooSession
{
    /// <summary>
    /// How long a session survives without being polled. The POST loop IS the connection here, so a client
    /// that stops polling is a client that has gone away - and until it is reaped it still owns the username
    /// and swallows every message relayed to it.
    /// </summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Outbound queue depth. Bounded because nothing forces a client to come back for these: a session that
    /// stops polling before it is reaped would otherwise accumulate every broadcast on the network. Hitting
    /// the cap fails the delivery rather than dropping it silently, which sends an IM to offline storage
    /// instead of into a bin.
    /// </summary>
    public const int MaxQueuedPackets = 256;

    // The content grammar is positional and delimited by these, so text carrying one re-partitions the packet
    // on the client's parser. Which characters are structural depends on WHICH field the text lands in, so
    // there is a sanitiser per position rather than one blunt filter - stripping commas out of message bodies
    // would mangle ordinary typing, and the parser does not need it.
    const char StatusMessageTerminator = (char)0x01;

    const char ImvironmentSeparator = (char)0x06;

    static readonly char[] IdentifierStructuralCharacters = { ',', '(', ')', StatusMessageTerminator, ImvironmentSeparator };

    readonly ConcurrentQueue<YpnsPacket> _outbound = new();

    public PagerSession(string token)
    {
        Token = token;

        // The client caches this from the first packet the server sends and echoes it on everything after.
        // Random rather than sequential so it is not a guessable handle to somebody else's session.
        MagicId = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

        LastSeen = DateTimeOffset.UtcNow;
    }

    public override string Transport => "Pager";

    /// <summary>The sign-on token from the Y cookie. Identifies this session on every subsequent POST.</summary>
    public string Token { get; }

    public uint MagicId { get; }

    public DateTimeOffset LastSeen { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow - LastSeen > IdleTimeout;

    public int QueueDepth => _outbound.Count;

    // Live IM: "<from>,<flag>,<text>". The reference parser reads the sender up to the first comma, skips the
    // flag entirely, and takes everything after the second comma as the message - so the flag is left empty,
    // which is exactly the "userid,,msg" shape its own comments document. The message runs to the end of the
    // content, so commas inside it are fine and are deliberately NOT stripped.
    //
    // Offline IM: "6,6,<to>,<from>,<timestamp>,<text>". The first two numbers are ignored by the parser, the
    // timestamp is carried as free text, and the message is again everything after the last delimiter.
    //
    // The IMVironment pair is accepted and dropped: the reference parser ends the message field at \x06 and
    // nothing verifiable reads what follows, so no imvironment suffix is invented for this wire.
    public override Task<bool> DeliverMessageAsync(string from, string text, long timestamp, bool offline, string imvironment = null, string imvironmentFlag = null)
    {
        var body = SanitiseMessageText(text);

        var content = offline
            ? $"6,6,{SanitiseIdentifier(Username)},{SanitiseIdentifier(from)},{FormatTimestamp(timestamp)},{body}"
            : $"{SanitiseIdentifier(from)},,{body}";

        return Enqueue(new YpnsPacket(YpnsService.Message, Username, Username, content, offline ? YpnsMsgType.Offline : YpnsMsgType.None));
    }

    public override Task<bool> DeliverPresenceAsync(YahooSession subject)
    {
        return Enqueue(new YpnsPacket(YpnsService.Logon, Username, Username, StatusRecord(subject)));
    }

    public override Task<bool> DeliverLogoffAsync(YahooSession subject)
    {
        return Enqueue(new YpnsPacket(YpnsService.Logoff, Username, Username, StatusRecord(subject)));
    }

    // The reference client has no typing notification on this protocol generation - there is no service code
    // for it in libyahoo.h. Accepted and dropped rather than invented, so a YMSG member typing at a Pager
    // member is a no-op instead of an unparseable packet.
    public override Task<bool> DeliverTypingAsync(YahooSession from, string kind, string flag, uint status)
    {
        return Task.FromResult(true);
    }

    public override Task SupersedeAsync()
    {
        // Revoking the token is what actually ends this session: the old client keeps polling, and its next
        // POST no longer resolves to anything. There is no socket to close and no verified way to tell it why,
        // so nothing is invented here - it simply stops being signed in.
        PagerLoginTokens.Revoke(Token);

        _outbound.Clear();

        return Task.CompletedTask;
    }

    public override void Drop()
    {
        PagerLoginTokens.Revoke(Token);

        _outbound.Clear();
    }

    /// <summary>
    /// Takes everything queued for this client and encodes it as the response body for its POST. The client
    /// resynchronises on the "YHOO" magic and walks the buffer packet by packet, so a plain concatenation is
    /// the whole framing.
    /// </summary>
    public byte[] DrainOutbound()
    {
        if (_outbound.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        using var body = new MemoryStream();

        while (_outbound.TryDequeue(out var packet))
        {
            packet.ConnectionId = SessionId;
            packet.MagicId = MagicId;

            var bytes = packet.Encode();

            body.Write(bytes, 0, bytes.Length);
        }

        return body.ToArray();
    }

    /// <summary>Queues a packet the transport built directly (the sign-on roster, and friends).</summary>
    public Task<bool> Enqueue(YpnsPacket packet)
    {
        if (_outbound.Count >= MaxQueuedPackets)
        {
            return Task.FromResult(false);
        }

        _outbound.Enqueue(packet);

        return Task.FromResult(true);
    }

    /// <summary>
    /// One buddy's presence record: <c>nick(status,connection_id,unk,in_pager,in_chat,in_game)</c>, with a
    /// message field inserted after the status when - and only when - the status is Custom (99):
    /// <c>nick(99,msg\x01,connection_id,unk,in_pager,in_chat,in_game)</c>.
    ///
    /// <para>
    /// The conditional shape is not a guess and not a protocol-version switch. The reference parser keys the
    /// field count off the status value it just read: at Custom it takes a branch that reads a message
    /// delimited by \x01, and otherwise it skips straight to the connection id. So there is ONE format with
    /// six fields normally and seven for a custom status, rather than a six-field YPNS1.x and a seven-field
    /// YPNS2.0 to pick between. If a capture ever shows a client wanting the field unconditionally, this
    /// method is the single place to change.
    /// </para>
    ///
    /// <para>
    /// KNOWN REFERENCE-CLIENT QUIRK, left uncompensated on purpose. The custom form here reproduces the worked
    /// example in libyahoo's own source comment, ie. what the real Yahoo! server evidently sent. libyahoo then
    /// mis-parses its own example: it consumes the \x01 as the message terminator and reads the comma
    /// immediately after it as an empty connection id, shifting in_pager / in_chat / in_game one field along.
    /// Compensating would mean emitting something the real server did not, so the real server's shape wins and
    /// the client wears its own bug. The skewed fields are cosmetic.
    /// </para>
    /// </summary>
    internal static string StatusRecord(YahooSession subject)
    {
        var status = subject.YahooStatus;

        var connectionId = subject.SessionId.ToString("X8", CultureInfo.InvariantCulture);

        // in_pager 1, in_chat 0, in_game 0 - the hive has no chat rooms or games for anyone to be in.
        const string Presence = "0,1,0,0";

        var nick = SanitiseIdentifier(subject.Username);

        if (status == YmsgStatus.Custom)
        {
            // Commas inside the message are fine and are kept: \x01 is what terminates this field, which is
            // precisely why the format has a terminator here and nowhere else.
            var message = SanitiseStatusMessage(subject.CustomStatusMessage);

            return $"{nick}({status},{message}{StatusMessageTerminator},{connectionId},{Presence})";
        }

        return $"{nick}({status},{connectionId},{Presence})";
    }

    /// <summary>
    /// The roster snapshot sent at sign-on: <c>&lt;count&gt;,rec,rec,...</c>, where count is how many buddies
    /// are online. The count has to be accurate: the reference client sizes its status array from it and walks
    /// exactly that many records.
    /// </summary>
    internal static string RosterSnapshot(IEnumerable<YahooSession> online)
    {
        var records = online.Select(StatusRecord).ToList();

        if (records.Count == 0)
        {
            return "0";
        }

        return $"{records.Count},{string.Join(",", records)}";
    }

    // asctime shape, "Tue Mar  7 12:14:50 2000", matching the reference's own worked example. The client
    // carries it as free text, so this is for display rather than parsing.
    internal static string FormatTimestamp(long unixSeconds)
    {
        var moment = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        return $"{moment.ToString("ddd MMM", CultureInfo.InvariantCulture)} {moment.Day,2} {moment.ToString("HH:mm:ss yyyy", CultureInfo.InvariantCulture)}";
    }

    // Usernames land in comma and paren delimited positions. Hive usernames are alphanumeric so this should
    // never fire; it is here so that loosening the username rules later cannot silently become a way to forge
    // extra fields in somebody else's roster.
    internal static string SanitiseIdentifier(string value)
    {
        return Replace(value, IdentifierStructuralCharacters);
    }

    // Only the terminator. A custom status message is allowed to contain commas.
    internal static string SanitiseStatusMessage(string value)
    {
        return Replace(value, new[] { StatusMessageTerminator });
    }

    // Only the imvironment separator, which is what ends the message field. Commas, quotes and everything else
    // a member might actually type are left exactly as they typed them.
    internal static string SanitiseMessageText(string value)
    {
        return Replace(value, new[] { ImvironmentSeparator });
    }

    static string Replace(string value, char[] structural)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOfAny(structural) < 0)
        {
            return value ?? string.Empty;
        }

        return new string(value.Select(c => Array.IndexOf(structural, c) >= 0 ? ' ' : c).ToArray());
    }
}
