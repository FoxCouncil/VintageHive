// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Msn;

// One connection to the MSN server. The same port carries both Notification-Server (NS) and
// Switchboard (SB) connections; Role records which this became after its opening command.
public sealed class MsnSession
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

    // The MSNP dialect number negotiated at VER (7 for "MSNP7"). Zero until VER answers, which keeps
    // the pre-7 SYN shape: only MSNP7 adds LSG group lines and the FL group-id field.
    public int Dialect { get; set; }

    public string Account { get; set; }

    // The wire identity and the account it authenticates as split when a Passport-style name@domain
    // signs on: Account stays the presented address - contacts, presence and switchboard addressing
    // all carry it - while the credential lookup and the NsSessions key follow the owning username.
    // Defaults to Account, so a bare-name session behaves exactly as before.
    string accountUsername;

    public string AccountUsername
    {
        get => accountUsername ?? Account;

        set => accountUsername = value;
    }

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

    // List serial echoed in ADD/REM acks. SYN always answers version 1, so the first edit acks 2. Only the
    // session's own sequential NS loop touches it, so a bare increment is enough.
    int listSerial = 1;

    public int NextListSerial()
    {
        return ++listSerial;
    }

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

public enum MsnRole
{
    Unknown,
    Notification,
    Switchboard
}
