// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Yahoo;

/// <summary>
/// One signed-on Yahoo! identity, independent of how it is connected.
/// <para>
/// YMSG holds a socket for the life of the session and writes packets down it. The pre-YMSG HTTP Pager has no
/// socket at all: it POSTs and drains a queue. Everything above the wire - who is online, who supersedes whom,
/// where a message goes - is identical for both, so it lives here and in <see cref="YahooSessionRegistry"/>,
/// and the transports only implement rendering and delivery.
/// </para>
/// <para>
/// Session ids are minted here rather than per transport. The registry is one flat table keyed by id, so two
/// transports handing out their own id spaces would collide and silently evict each other's sessions.
/// </para>
/// </summary>
public abstract class YahooSession
{
    static uint NextSessionId = 0x00010000;

    // A dead or stalled peer must not be able to hang a broadcast that another session is driving.
    protected const int DeliverTimeoutMs = 5000;

    protected YahooSession()
    {
        SessionId = Interlocked.Increment(ref NextSessionId);
    }

    public uint SessionId { get; }

    public string Username { get; set; }

    public bool IsAuthenticated { get; set; }

    public uint YahooStatus { get; set; } = YmsgStatus.Available;

    public string CustomStatusMessage { get; set; } = string.Empty;

    public DateTimeOffset SignOnTime { get; } = DateTimeOffset.UtcNow;

    public DateTimeOffset IdleSince { get; set; } = DateTimeOffset.MinValue;

    /// <summary>Which wire this session is on. Diagnostic only - nothing routes on it, by design.</summary>
    public abstract string Transport { get; }

    public uint GetCurrentIdleSeconds()
    {
        if (IdleSince == DateTimeOffset.MinValue)
        {
            return 0;
        }

        return (uint)Math.Max(0, (DateTimeOffset.UtcNow - IdleSince).TotalSeconds);
    }

    /// <summary>
    /// Delivers an instant message to this session. Returns false when the peer is gone or stalled, so the
    /// caller can fall back to offline storage instead of letting the failure take down the sender.
    /// <para>
    /// <paramref name="imvironment"/> and <paramref name="imvironmentFlag"/> are the originating message's
    /// IMVironment pair (YMSG fields 63/64), carried through the relay the way the real servers did. Null
    /// means the originator supplied none; a transport whose wire wants the pair substitutes the plain-IM
    /// values, and a transport whose wire has no such fields ignores them.
    /// </para>
    /// </summary>
    public abstract Task<bool> DeliverMessageAsync(string from, string text, long timestamp, bool offline, string imvironment = null, string imvironmentFlag = null);

    /// <summary>Tells this session that <paramref name="subject"/> is online, or changed status.</summary>
    public abstract Task<bool> DeliverPresenceAsync(YahooSession subject);

    /// <summary>Tells this session that <paramref name="subject"/> went offline (or invisible).</summary>
    public abstract Task<bool> DeliverLogoffAsync(YahooSession subject);

    /// <summary>
    /// Relays an ephemeral notification (typing, and friends). Best effort: failures are dropped.
    /// <paramref name="status"/> is the originating packet's header status, echoed through so YMSG's
    /// notification variants survive the relay; a transport without that concept ignores it.
    /// </summary>
    public abstract Task<bool> DeliverTypingAsync(YahooSession from, string kind, string flag, uint status);

    /// <summary>
    /// Tells this session it lost the account to a newer login, then tears it down. Called only after the
    /// registry has already removed it, so it can take as long as the transport needs without holding the gate.
    /// </summary>
    public abstract Task SupersedeAsync();

    /// <summary>
    /// Drops this session's transport after a failed delivery. A partial write leaves a length-prefixed stream
    /// misframed, so the peer is better off reconnecting than parsing everything after it as garbage.
    /// </summary>
    public abstract void Drop();

    /// <summary>
    /// Runs one delivery under the shared timeout, reporting failure instead of throwing. Every Deliver*
    /// implementation goes through this so the bounded-write policy is stated once rather than per transport.
    /// </summary>
    protected static async Task<bool> GuardedAsync(Func<CancellationToken, Task> send)
    {
        try
        {
            using var cts = new CancellationTokenSource(DeliverTimeoutMs);

            await send(cts.Token);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
