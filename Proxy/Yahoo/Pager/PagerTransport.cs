// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Yahoo.Pager;

/// <summary>
/// The polled message transport: the client POSTs one YPNS packet to /notify/ and reads back whatever the
/// server has queued for it. There is no persistent connection - the POST loop IS the connection, which is
/// why <see cref="PagerSession"/> has a queue where <see cref="YmsgSession"/> has a socket.
///
/// <para>
/// Written against libyahoo 0.18.4: <c>yahoo_sendcmd_http</c> for the request framing, <c>yahoo_getpacket</c>
/// for the response framing, and the <c>yahoo_parsepacket_*</c> family for every content grammar below. No
/// capture of a real client's request BODIES exists, so where the reference is silent this says so rather
/// than inventing something.
/// </para>
/// </summary>
public static class PagerTransport
{
    // The client's own send buffer is 4*256 + 104 bytes, so its packets never exceed that. The cap is well
    // above it and exists to bound what a malformed or hostile POST can make the server allocate, not to
    // constrain a real client.
    public const int MaxNotifyBodyBytes = 64 * 1024;

    // Outbound message text cap. The reference client builds a 1128-byte packet for everything it SENDS,
    // which is the only evidence anywhere of the size it was designed around; its receive path allocates from
    // the length field so it could take more, but nothing pins how much. Truncation is logged rather than
    // silent.
    public const int MaxMessageChars = 1024;

    const char StatusMessageTerminator = (char)0x01;

    public enum Outcome
    {
        Ok,
        BadRequest,
        Unauthenticated,
    }

    public sealed class Result
    {
        public Outcome Outcome { get; init; }

        public byte[] Body { get; init; } = Array.Empty<byte>();
    }

    static readonly Result Unauthenticated = new() { Outcome = Outcome.Unauthenticated };

    static readonly Result BadRequest = new() { Outcome = Outcome.BadRequest };

    /// <summary>
    /// Handles one POST. <paramref name="cookieToken"/> is the n= sub-value the client echoes from its Y
    /// cookie; it identifies the session on every packet except LOGON, which carries its own token inline.
    /// </summary>
    public static async Task<Result> HandleAsync(byte[] body, string cookieToken, string traceId, Action<string> track = null)
    {
        // Reap first, so a member whose client walked away stops owning the username before anything below
        // resolves a recipient or builds a roster.
        await ReapExpiredAsync();

        if (body == null || body.Length < YpnsPacket.HeaderSize || body.Length > MaxNotifyBodyBytes)
        {
            return BadRequest;
        }

        var packet = YpnsPacket.Decode(body);

        if (packet == null)
        {
            return BadRequest;
        }

        if (packet.Service == YpnsService.Logon)
        {
            return await HandleLogonAsync(packet, traceId, track);
        }

        var session = FindByToken(cookieToken);

        if (session == null)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(PagerTransport), $"Unauthenticated packet, service {packet.Service}", traceId);

            return Unauthenticated;
        }

        session.LastSeen = DateTimeOffset.UtcNow;

        await DispatchAsync(session, packet, traceId, track);

        return new Result { Outcome = Outcome.Ok, Body = session.DrainOutbound() };
    }

    // LOGON content is "<token>\x01<user>[,<identity>...]", with the initial status in the header's msgtype.
    static async Task<Result> HandleLogonAsync(YpnsPacket packet, string traceId, Action<string> track)
    {
        var separator = packet.Content.IndexOf(StatusMessageTerminator);

        if (separator <= 0)
        {
            return BadRequest;
        }

        var token = packet.Content[..separator];

        var identities = packet.Content[(separator + 1)..];

        var claimed = identities.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        var username = PagerLoginTokens.Resolve(token);

        if (username == null)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(PagerTransport), "Sign-on with an unknown or expired token", traceId);

            return Unauthenticated;
        }

        // The token says who this is; the packet also names an account. They have to agree, or a member's own
        // valid token would be a way to sign on as somebody else.
        if (!string.IsNullOrEmpty(claimed) && !string.Equals(claimed, username, StringComparison.OrdinalIgnoreCase))
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(PagerTransport), $"Sign-on token for '{username}' presented for '{claimed}'", traceId);

            return Unauthenticated;
        }

        var existing = FindByToken(token);

        if (existing != null)
        {
            // A repeat LOGON on a live token is the same session, not a new one. Creating a second would make
            // it supersede itself - and PagerSession.SupersedeAsync revokes the token, which is the very token
            // this request authenticated with.
            existing.LastSeen = DateTimeOffset.UtcNow;

            await SendRosterAsync(existing);

            return new Result { Outcome = Outcome.Ok, Body = existing.DrainOutbound() };
        }

        var session = new PagerSession(token)
        {
            YahooStatus = packet.MsgType,
        };

        YahooSessionRegistry.Add(session);

        // Cross-transport supersede: a YMSG socket signed in as this account is evicted here exactly like
        // another Pager session would be.
        var superseded = await YahooSessionRegistry.ClaimIdentityAsync(session, username);

        await YahooSessionRegistry.SupersedeAllAsync(superseded);

        track?.Invoke($"logon {username}");

        Log.WriteLine(Log.LEVEL_INFO, nameof(PagerTransport), $"Pager sign-on for '{username}'", traceId);

        await SendRosterAsync(session);

        await YahooSessionRegistry.BroadcastPresenceAsync(session);

        await DeliverOfflineMessagesAsync(session, traceId);

        return new Result { Outcome = Outcome.Ok, Body = session.DrainOutbound() };
    }

    static async Task DispatchAsync(PagerSession session, YpnsPacket packet, string traceId, Action<string> track)
    {
        switch (packet.Service)
        {
            case YpnsService.Ping:
            {
                // The poll itself. Nothing to do but let the caller drain the queue.
            }
            break;

            case YpnsService.Message:
            {
                await HandleMessageAsync(session, packet, traceId, track);
            }
            break;

            case YpnsService.IsAway:
            case YpnsService.IsBack:
            {
                await HandleStatusChangeAsync(session, packet);
            }
            break;

            case YpnsService.Idle:
            {
                session.YahooStatus = YmsgStatus.Idle;
                session.IdleSince = DateTimeOffset.UtcNow;

                await YahooSessionRegistry.BroadcastPresenceAsync(session);
            }
            break;

            case YpnsService.UserStat:
            {
                await SendRosterAsync(session);
            }
            break;

            case YpnsService.Logoff:
            {
                await SignOffAsync(session, track);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(PagerTransport), $"Unhandled Pager service {packet.Service}", traceId);
            }
            break;
        }
    }

    // Client-to-server message content is "<touser>,<msg>".
    static async Task HandleMessageAsync(PagerSession session, YpnsPacket packet, string traceId, Action<string> track)
    {
        var split = packet.Content.IndexOf(',');

        if (split <= 0)
        {
            return;
        }

        var to = packet.Content[..split];

        var text = packet.Content[(split + 1)..];

        if (text.Length > MaxMessageChars)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(PagerTransport), $"Truncating a {text.Length} character message from '{session.Username}'", traceId);

            text = text[..MaxMessageChars];
        }

        track?.Invoke($"msg {session.Username} -> {to}");

        // Same relay every other transport uses, so a Pager member messaging a YMSG member just works.
        if (await YahooSessionRegistry.RelayMessageAsync(session.Username, to, text))
        {
            return;
        }

        if (Mind.Db?.UserExistsByUsername(to) != true)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(PagerTransport), $"Message to unknown user {to} dropped", traceId);

            return;
        }

        Mind.Db.YahooStoreOfflineMessage(session.Username, to, text);
    }

    // ISAWAY / ISBACK content is "<status>" or "<status>\x01<message>".
    static async Task HandleStatusChangeAsync(PagerSession session, YpnsPacket packet)
    {
        var content = packet.Content ?? string.Empty;

        var separator = content.IndexOf(StatusMessageTerminator);

        var statusText = separator >= 0 ? content[..separator] : content;

        // set_back_mode appends a trailing space to work around a Yahoo! bug of its own, so the message needs
        // trimming rather than being taken literally.
        var message = separator >= 0 ? content[(separator + 1)..].Trim() : string.Empty;

        session.YahooStatus = uint.TryParse(statusText.Trim(), out var status)
            ? status
            : packet.Service == YpnsService.IsBack ? YmsgStatus.Available : YmsgStatus.BeRightBack;

        session.CustomStatusMessage = message;

        if (session.YahooStatus == YmsgStatus.Available)
        {
            session.IdleSince = DateTimeOffset.MinValue;
        }
        else if (session.YahooStatus == YmsgStatus.Idle)
        {
            session.IdleSince = DateTimeOffset.UtcNow;
        }

        // Going invisible has to look like a departure to peers, matching YMSG: a presence broadcast is
        // suppressed for invisible subjects, so peers would otherwise be left showing the member as online.
        if (session.YahooStatus == YmsgStatus.Invisible)
        {
            await YahooSessionRegistry.BroadcastLogoffAsync(session);
        }
        else
        {
            await YahooSessionRegistry.BroadcastPresenceAsync(session);
        }
    }

    static async Task SendRosterAsync(PagerSession session)
    {
        var online = YahooSessionRegistry.Peers(session).Where(s => !YahooSessionRegistry.IsInvisibleTo(s, session)).ToList();

        await session.Enqueue(new YpnsPacket(YpnsService.Logon, session.Username, session.Username, PagerSession.RosterSnapshot(online), session.YahooStatus));
    }

    // Mirrors YmsgServer's login flush, including the rule that matters: the queue is only deleted when every
    // message was actually handed over. A full outbound queue fails the enqueue, and those messages stay
    // stored for the next sign-on rather than being deleted as if they had been delivered.
    static async Task DeliverOfflineMessagesAsync(PagerSession session, string traceId)
    {
        var messages = Mind.Db?.YahooGetOfflineMessages(session.Username);

        if (messages == null || messages.Count == 0)
        {
            return;
        }

        var delivered = new List<long>();

        foreach (var message in messages)
        {
            if (!await session.DeliverMessageAsync(message.FromUsername, message.Message, message.Timestamp, offline: true))
            {
                Log.WriteLine(Log.LEVEL_WARN, nameof(PagerTransport), $"Outbound queue full flushing offline messages for '{session.Username}'; {messages.Count - delivered.Count} left queued", traceId);

                break;
            }

            delivered.Add(message.Id);
        }

        if (delivered.Count > 0)
        {
            Mind.Db.YahooDeleteOfflineMessages(delivered);
        }
    }

    static async Task SignOffAsync(PagerSession session, Action<string> track)
    {
        YahooSessionRegistry.Remove(session);

        PagerLoginTokens.Revoke(session.Token);

        // A superseding login on another transport may already own this username; announcing a logoff then
        // would tell peers the still-online member left.
        if (YahooSessionRegistry.GetByUsername(session.Username) == null)
        {
            await YahooSessionRegistry.BroadcastLogoffAsync(session);
        }

        track?.Invoke($"logoff {session.Username}");
    }

    /// <summary>
    /// Drops sessions that have stopped polling. Without this a client that was closed, crashed or lost its
    /// network keeps owning the username: peers see it online forever and every IM relayed to it is enqueued
    /// for a client that will never come back for it.
    /// </summary>
    public static async Task ReapExpiredAsync()
    {
        foreach (var session in YahooSessionRegistry.Sessions.Values.OfType<PagerSession>().ToArray())
        {
            if (!session.IsExpired)
            {
                continue;
            }

            Log.WriteLine(Log.LEVEL_INFO, nameof(PagerTransport), $"Reaping idle Pager session for '{session.Username}'", string.Empty);

            await SignOffAsync(session, null);
        }
    }

    /// <summary>
    /// Finds a live Pager session by its sign-on token.
    /// <para>
    /// A scan of the shared registry rather than a second token-keyed table, deliberately. The population is a
    /// handful of members, and a second index of live sessions is somewhere for the two to disagree about who
    /// is signed on - which is the exact failure this whole design exists to avoid.
    /// </para>
    /// </summary>
    public static PagerSession FindByToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        foreach (var session in YahooSessionRegistry.Sessions.Values.ToArray())
        {
            if (session is PagerSession pager && pager.IsAuthenticated && string.Equals(pager.Token, token, StringComparison.Ordinal))
            {
                return pager;
            }
        }

        return null;
    }
}
