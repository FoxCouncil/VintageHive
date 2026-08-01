// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Yahoo;

// The YMSG transport: one held socket per session, key/value packets written straight down it. Everything
// about identity, presence and routing lives in YahooSession / YahooSessionRegistry; this class only renders
// the shared events into YMSG packets and puts them on the wire.
public sealed class YmsgSession : YahooSession
{
    // Serializes socket writes so a broadcast from another session cannot interleave with this session's
    // own reply and corrupt YMSG framing (mirrors OscarSession's writeLock).
    readonly SemaphoreSlim _writeLock = new(1, 1);

    public YmsgSession(ListenerSocket client)
    {
        Client = client;
    }

    public override string Transport => "YMSG";

    public ListenerSocket Client { get; }

    // The challenge issued to this connection, and everything derivable from it. Held so AUTHRESP verifies
    // against the seed this session actually sent rather than recomputing one, and so the costly derivation
    // happens once per connection instead of once per login attempt.
    internal string ChallengeSeed { get; set; }

    internal YmsgCrypt.ChallengeState Challenge { get; set; }

    // Echo the client's protocol version back so it stays happy across YM 5.x builds.
    public ushort Version { get; set; } = 0x0009;

    public async Task SendAsync(YmsgPacket packet, CancellationToken cancellationToken = default)
    {
        packet.Version = Version;

        var bytes = packet.Encode();

        await _writeLock.WaitAsync(cancellationToken);

        try
        {
            await Client.Stream.WriteAsync(bytes, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // The HEADER STATUS is the load-bearing part: a live delivery must go out as YmsgStatus.LiveDelivery (1).
    // YM 5.5.0.1244 silently discards a server-to-client Message with any other status, which made every live
    // delivery - chat-service replies and member-to-member relays alike - invisible to the real client while
    // the loopback tests stayed green, because they assert the server's bytes rather than the client's parser.
    // Proved with an A/B probe against a signed-on 5.5 session; see YmsgStatus.LiveDelivery.
    //
    // Body fields: 4 (from), 5 (to), 14 (text), 15 (timestamp), 97 (utf8) and the IMVironment pair 63/64.
    // Clients stamp 63/64 on every IM they compose (";0"/"0" for a plain window; libyahoo2.c:4038 does the
    // same) and real servers relayed the pair through, so it is echoed when the originator supplied one and
    // defaulted otherwise. Note the same probe proved 63/64 is NOT what gates rendering - the pair was present
    // in the dropped variants too - so keep it for fidelity, not as the fix.
    // Fields 0/1 must NOT be present: libyahoo2-lineage clients treat the FIRST of key 1 or 4 as the
    // sender, so a leading field 1 would attribute the message to the recipient themselves.
    public override Task<bool> DeliverMessageAsync(string from, string text, long timestamp, bool offline, string imvironment = null, string imvironmentFlag = null)
    {
        var delivery = new YmsgPacket(YmsgService.Message, offline ? YmsgStatus.OfflineMessage : YmsgStatus.LiveDelivery, SessionId)
            .Add(4, from)
            .Add(5, Username)
            .Add(14, text)
            .Add(15, timestamp.ToString())
            .Add(97, "1")
            .Add(63, imvironment ?? ";0")
            .Add(64, imvironmentFlag ?? "0");

        return GuardedAsync(ct => SendAsync(delivery, ct));
    }

    public override Task<bool> DeliverPresenceAsync(YahooSession subject)
    {
        var presence = new YmsgPacket(YmsgService.Logon, 0, SessionId)
            .Add(0, Username)
            .Add(7, subject.Username)
            .Add(10, subject.YahooStatus.ToString())
            .Add(11, subject.SessionId.ToString("X"))
            .Add(13, "1");

        if (subject.YahooStatus == YmsgStatus.Custom && !string.IsNullOrEmpty(subject.CustomStatusMessage))
        {
            presence.Add(19, subject.CustomStatusMessage);
            presence.Add(47, "1");
        }

        return GuardedAsync(ct => SendAsync(presence, ct));
    }

    public override Task<bool> DeliverLogoffAsync(YahooSession subject)
    {
        var logoff = new YmsgPacket(YmsgService.Logoff, 0, SessionId)
            .Add(0, Username)
            .Add(7, subject.Username)
            .Add(10, YmsgStatus.Available.ToString())
            .Add(11, subject.SessionId.ToString("X"))
            .Add(13, "0");

        return GuardedAsync(ct => SendAsync(logoff, ct));
    }

    // The sender's status word is deliberately NOT echoed here. A 5.5 client stamps 22 (0x16) on its outgoing
    // TYPING, and relaying that verbatim produced exactly the shape the A/B probe proved the client drops.
    // Notify follows the same rule as Message: live deliveries go out as status 1. Typing on/off is carried in
    // field 13, which is where Escargot puts it too. The status parameter stays on the signature because
    // PagerSession's YPNS delivery has its own use for it.
    public override Task<bool> DeliverTypingAsync(YahooSession from, string kind, string flag, uint status)
    {
        var notify = new YmsgPacket(YmsgService.Notify, YmsgStatus.LiveDelivery, SessionId)
            .Add(4, from.Username)
            .Add(5, Username)
            .Add(49, kind)
            .Add(13, flag);

        return GuardedAsync(ct => SendAsync(notify, ct));
    }

    public override async Task SupersedeAsync()
    {
        await GuardedAsync(ct => SendAsync(new YmsgPacket(YmsgService.Logoff, YmsgStatus.Duplicate, SessionId).Add(0, Username), ct));

        // Shutdown(Send) flushes the notice and FINs first; a bare Close with the old handler's read
        // still pending is an abortive reset that discards the notice in flight.
        try { Client.RawSocket.Shutdown(SocketShutdown.Send); } catch { }
        try { Client.RawSocket.Close(); } catch { }
    }

    public override void Drop()
    {
        try { Client.RawSocket.Close(); } catch { }
    }
}
