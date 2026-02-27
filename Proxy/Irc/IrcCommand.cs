// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Irc;

public class IrcCommand
{
    public const string USER = "USER";

    public const string NICK = "NICK";

    public const string QUIT = "QUIT";

    public const string PRIVMSG = "PRIVMSG";

    public const string MOTD = "MOTD";

    public const string JOIN = "JOIN";

    public const string PART = "PART";

    public const string TOPIC = "TOPIC";

    public const string LIST = "LIST";

    public const string NAMES = "NAMES";

    public const string WHO = "WHO";

    public const string WHOIS = "WHOIS";

    public const string KICK = "KICK";

    public const string MODE = "MODE";

    public const string NOTICE = "NOTICE";

    public const string AWAY = "AWAY";

    public const string PING = "PING";

    public const string PONG = "PONG";

    public const string PASS = "PASS";

    public const string ISON = "ISON";

    public const string USERHOST = "USERHOST";

    public const string INVITE = "INVITE";

    public string Command { get; set; }

    public List<string> Params { get; set; } = new List<string>();

    public string Trailing { get; set; }
}
