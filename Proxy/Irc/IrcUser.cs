// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Irc;

public class IrcUser
{
    public string Nick { get; set; }

    public string Username { get; set; }

    public string Hostname { get; set; }

    public string Realname { get; set; }

    public HashSet<string> Channels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<char> Modes { get; set; } = new();

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
        await ListenerSocket.Stream.WriteAsync(data, 0, data.Length);
    }
}
