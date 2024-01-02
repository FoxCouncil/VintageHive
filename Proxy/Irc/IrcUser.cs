using VintageHive.Network;

namespace VintageHive.Proxy.Irc;

public class IrcUser
{
    // Nickname of the user
    public string Nick { get; set; }

    // Username (ident)
    public string Username { get; set; }

    // Hostname
    public string Hostname { get; set; }

    // Real name (gecos)
    public string Realname { get; set; }

    // Channels the user is currently in
    public HashSet<string> Channels { get; set; } = new HashSet<string>();

    // User modes
    public HashSet<char> Modes { get; set; } = new HashSet<char>();

    // Reference to the ListenerSocket to manage communication
    public ListenerSocket ListenerSocket { get; set; }

    public string Fullname => $"{Nick}!{Username}@{Hostname}";

    public IrcUser(ListenerSocket listenerSocket)
    {
        ListenerSocket = listenerSocket;
    }

    public async Task SendData(byte[] data)
    {
        await ListenerSocket.Stream.WriteAsync(data, 0, data.Length);
    }
}