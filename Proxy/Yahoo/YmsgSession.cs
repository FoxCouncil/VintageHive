// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Yahoo;

internal sealed class YmsgSession
{
    static uint NextSessionId = 0x00010000;

    // Serializes socket writes so a broadcast from another session cannot interleave with this session's
    // own reply and corrupt YMSG framing (mirrors OscarSession's writeLock).
    readonly SemaphoreSlim _writeLock = new(1, 1);

    public YmsgSession(ListenerSocket client)
    {
        Client = client;
        SessionId = Interlocked.Increment(ref NextSessionId);
    }

    public ListenerSocket Client { get; }

    public uint SessionId { get; }

    public string Username { get; set; }

    public bool IsAuthenticated { get; set; }

    // Echo the client's protocol version back so it stays happy across YM 5.x builds.
    public ushort Version { get; set; } = 0x0009;

    public uint YahooStatus { get; set; } = YmsgStatus.Available;

    public string CustomStatusMessage { get; set; } = string.Empty;

    public DateTimeOffset SignOnTime { get; } = DateTimeOffset.UtcNow;

    public DateTimeOffset IdleSince { get; set; } = DateTimeOffset.MinValue;

    public uint GetCurrentIdleSeconds()
    {
        if (IdleSince == DateTimeOffset.MinValue)
        {
            return 0;
        }

        return (uint)Math.Max(0, (DateTimeOffset.UtcNow - IdleSince).TotalSeconds);
    }

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
}
