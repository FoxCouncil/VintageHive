// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;
using VintageHive.Utilities;

namespace VintageHive.Proxy.Irc;

public class IrcUser
{
    // Serializes writes to this user's socket so a broadcast from another connection can't interleave with
    // this connection's own reply (which corrupts IRC line framing, especially on partial sends).
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string Nick { get; set; }

    public string Username { get; set; }

    public string Hostname { get; set; }

    public string Realname { get; set; }

    public ConcurrentHashSet<string> Channels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentHashSet<char> Modes { get; set; } = new();

    public ListenerSocket ListenerSocket { get; set; }

    public string Fullname => $"{Nick}!{Username}@{Hostname}";

    public bool IsAuthenticated { get; set; }

    public bool IsAway { get; set; }

    public string AwayMessage { get; set; } = string.Empty;

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public int MessageCount { get; set; }

    public bool IsOperator { get; set; }

    public IrcUser(ListenerSocket listenerSocket)
    {
        ListenerSocket = listenerSocket;
    }

    public async Task SendData(byte[] data)
    {
        await _writeLock.WaitAsync();

        try
        {
            await ListenerSocket.Stream.WriteAsync(data, 0, data.Length);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
