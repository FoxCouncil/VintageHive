namespace VintageHive.Proxy.Irc;

public class IrcCommand
{
    public const string USER = "USER";

    public const string NICK = "NICK";

    public const string QUIT = "QUIT";

    public const string PRIVMSG = "PRIVMSG";

    public const string MOTD = "MOTD";

    public string Command { get; set; }

    public List<string> Params { get; set; } = new List<string>();

    public string Trailing { get; set; }
}
