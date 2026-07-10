// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Msn;

// One connection to the MSN server. The same port carries both Notification-Server (NS) and
// Switchboard (SB) connections; Role records which this became after its opening command.
internal sealed class MsnSession
{
    // Serializes writes so a pushed presence/IM notification cannot interleave with a command reply.
    readonly SemaphoreSlim _writeLock = new(1, 1);

    public MsnSession(ListenerSocket client)
    {
        Client = client;
        Reader = new MsnStreamReader(client.Stream);
    }

    public ListenerSocket Client { get; }

    public MsnStreamReader Reader { get; }

    public MsnRole Role { get; set; } = MsnRole.Unknown;

    public string Account { get; set; }

    public string DisplayName { get; set; }

    public string Status { get; set; } = MsnStatus.Offline;

    public bool IsAuthenticated { get; set; }

    // The MD5 challenge issued during USR, retained until the client's hashed response arrives.
    public string AuthChallenge { get; set; }

    public DateTimeOffset SignOnTime { get; } = DateTimeOffset.UtcNow;

    // When the client went idle (CHG IDL); MinValue while not idle. Mirrors YmsgSession so the presence
    // registry can report a real idle duration.
    public DateTimeOffset IdleSince { get; set; } = DateTimeOffset.MinValue;

    // Switchboard-only: the session id this SB connection belongs to.
    public string SwitchboardId { get; set; }

    public uint GetCurrentIdleSeconds()
    {
        if (IdleSince == DateTimeOffset.MinValue)
        {
            return 0;
        }

        return (uint)Math.Max(0, (DateTimeOffset.UtcNow - IdleSince).TotalSeconds);
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");

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

    // Sends a command line followed by a raw payload body (for switchboard MSG relay), atomically.
    public async Task SendPayloadAsync(string line, byte[] payload, CancellationToken cancellationToken = default)
    {
        var head = Encoding.ASCII.GetBytes(line + "\r\n");

        await _writeLock.WaitAsync(cancellationToken);

        try
        {
            await Client.Stream.WriteAsync(head, cancellationToken);
            await Client.Stream.WriteAsync(payload, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

internal enum MsnRole
{
    Unknown,
    Notification,
    Switchboard
}
