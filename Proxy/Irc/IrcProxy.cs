using System.Text.RegularExpressions;
using VintageHive.Network;

using static VintageHive.Proxy.Irc.IrcServerReplyType;
using static VintageHive.Proxy.Irc.IrcCommand;

namespace VintageHive.Proxy.Irc;

internal class IrcProxy : Listener
{
    private const string IRCD_HOSTNAME = "irc.hive.com";

    private readonly Dictionary<Guid, IrcUser> Users = new();

    private DateTime StartTime;

    public IrcProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
        StartTime = DateTime.Now;
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        await Task.Delay(0);

        connection.IsKeepAlive = true;

        return SendIrcReply(new List<(string, IrcServerReplyType, string, string[], string)> { 
            (IRCD_HOSTNAME, STR_NOTICE, "AUTH", null, "*** Looking up your butt"),
            (IRCD_HOSTNAME, STR_NOTICE, "AUTH", null, "*** Butt found!"),
            (IRCD_HOSTNAME, STR_NOTICE, "AUTH", null, "*** Using Butt to find Sheath"),
            (IRCD_HOSTNAME, STR_NOTICE, "AUTH", null, "*** SHEATH found, inflating you big and round"),
        });
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var client = ParseIrcCommand(data[..read].ToUTF8());

        switch (client.Command)
        {
            case USER:
            {
                connection.DataBag.Add(USER, new IrcUser(connection) { Username = client.Params[0], Hostname = client.Params[1].Replace("\"", ""), Realname = client.Trailing });

                return CheckIfRegistrationComplete(connection);
            }

            case NICK:
            {
                var nick = client.Params.Count == 1 ? client.Params[0] : client.Trailing;

                if (Users.Values.Any(x => x.Nick.Equals(nick, StringComparison.InvariantCultureIgnoreCase)))
                {
                    return SendIrcReply(IRCD_HOSTNAME, ERR_NICKNAMEINUSE, nick, null, "Nickname is already in use");
                }

                connection.DataBag.Add(NICK, nick);

                return CheckIfRegistrationComplete(connection);
            }

            case PRIVMSG:
            {
                var nick = client.Params[0];

                var fromUser = Users[connection.TraceId];

                var toUser = Users.Values.FirstOrDefault(x => x.Nick.Equals(nick, StringComparison.InvariantCultureIgnoreCase));

                if (toUser == null)
                {
                    return SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHNICK, fromUser.Nick, new[] { nick } , "No such nick/channel");
                }

                await toUser.SendData(SendIrcReply(fromUser.Fullname, STR_PRIVMSG, toUser.Nick, null, client.Trailing));

                return null;
            }

            case MOTD:
            {
                var fromUser = Users[connection.TraceId];

                return SendIrcReply(GetMOTD(fromUser.Nick));
            }

            case QUIT:
            {
                connection.IsKeepAlive = false;

                return null;
            }
        }

        return null;
    }

    public override async Task ProcessDisconnection(ListenerSocket connection)
    {
        Users.Remove(connection.TraceId);

        await Task.Delay(0);
    }

    private byte[] CheckIfRegistrationComplete(ListenerSocket connection)
    {
        var bag = connection.DataBag;

        if (bag.ContainsKey(USER) && bag.ContainsKey(NICK))
        {
            var nick = bag[NICK].ToString();
            var user = bag[USER] as IrcUser;

            user.Nick = nick;

            Users.Add(connection.TraceId, user);

            bag.Remove(USER);

            var connectionReply = new List<(string, IrcServerReplyType, string, string[], string)> {
                (IRCD_HOSTNAME, RPL_WELCOME, nick, null, $"Welcome to the Internet Relay Network {user.Fullname}"),
                (IRCD_HOSTNAME, RPL_YOURHOST, nick, null, $"Your host is {IRCD_HOSTNAME}, running version VingtageHiveIRCd"),
                (IRCD_HOSTNAME, RPL_CREATED, nick, null, $"This server was created {StartTime}"),
                (IRCD_HOSTNAME, RPL_MYINFO, nick, new [] { IRCD_HOSTNAME, "VingtageHiveIRCd", "io", "ov" }, string.Empty),
                (IRCD_HOSTNAME, RPL_BOUNCE, nick, new [] { "CHANLIMIT=#:20", "CHANTYPES=#", "PREFIX=(ov)@+", "NETWORK=VintageHive" }, "are supported by this server"),
            };

            connectionReply.AddRange(GetMOTD(nick));

            return SendIrcReply(connectionReply);
        }

        return null;
    }

    private static List<(string, IrcServerReplyType, string, string[], string)> GetMOTD(string nick)
    {
        return new List<(string, IrcServerReplyType, string, string[], string)>
        {
            (IRCD_HOSTNAME, RPL_MOTDSTART, nick, null, $"- {IRCD_HOSTNAME} Message of the day -"),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- Welcome to VintageHive IRC Network"),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- We hope you enjoy your stay"),
            (IRCD_HOSTNAME, RPL_ENDOFMOTD, nick, null, "End of /MOTD command.")
        };
    }

    private static IrcCommand ParseIrcCommand(string input)
    {
        const string pattern = @"^(?<command>[A-Za-z0-9]+)(?:\s+(?<params>[^\s:]+))*(?:\s+:(?<trailing>.*))?$";

        var regex = new Regex(pattern);
        var match = regex.Match(input);

        if (!match.Success)
        {
            return null;
        }

        var result = new IrcCommand { Command = match.Groups["command"].Value.ToUpper() };

        foreach (var capture in match.Groups["params"].Captures.Cast<Capture>())
        {
            result.Params.Add(capture.Value);
        }

        if (match.Groups["trailing"].Success)
        {
            result.Trailing = match.Groups["trailing"].Value;
        }

        return result;
    }

    private static byte[] SendIrcReply(string host, IrcServerReplyType replyType, string nickname, string[] parameters, string trailing = "") => SendIrcReply(new List<(string, IrcServerReplyType, string, string[], string)> { (host, replyType, nickname, parameters, trailing) });

    private static byte[] SendIrcReply(List<(string, IrcServerReplyType, string, string[], string)> replies)
    {
        var sb = new StringBuilder();

        foreach (var (host, replyType, nickname, parameters, trailing) in replies)
        {
            // Add the hostname
            sb.Append($":{host} ");

            // Add the reply type or command
            if ((int)replyType < 100)
            {
                // Zero-pad numerical replies to 3 digits
                sb.Append(((int)replyType).ToString("D3"));
            }
            else if ((int)replyType >= 900)
            {
                sb.Append(replyType.ToString()[4..]);
            }
            else
            {
                // Alphabetic command like NOTICE
                sb.Append((int)replyType);
            }

            // Add the client nickname if not null or empty
            if (!string.IsNullOrEmpty(nickname))
            {
                sb.Append(' ').Append(nickname);
            }

            // Append parameters if not null
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    sb.Append(' ').Append(param);
                }
            }

            // Append the trailing message
            sb.Append(" :").Append(trailing);

            // Add CRLF at the end
            sb.Append("\r\n");
        }

        string outgoingCommand = sb.ToString();

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(IrcProxy), outgoingCommand, "");

        return Encoding.UTF8.GetBytes(outgoingCommand);
    }
}
