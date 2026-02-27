// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using VintageHive.Network;

using static VintageHive.Proxy.Irc.IrcServerReplyType;
using static VintageHive.Proxy.Irc.IrcCommand;

namespace VintageHive.Proxy.Irc;

internal class IrcProxy : Listener
{
    #region Constants & Fields

    private const string IRCD_HOSTNAME = "irc.hive.com";
    private const string IRCD_VERSION = "VintageHiveIRCd";
    private const int FLOOD_MAX_MESSAGES = 5;
    private static readonly TimeSpan FloodWindow = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<Guid, IrcUser> Users = new();
    private readonly ConcurrentDictionary<string, IrcChannel> Channels = new(StringComparer.OrdinalIgnoreCase);

    private DateTime StartTime;

    #endregion

    #region Constructor & Public API

    public IrcProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
        StartTime = DateTime.UtcNow;
    }

    public int UserCount => Users.Count;

    public int ChannelCount => Channels.Count;

    public IEnumerable<(string Name, int MemberCount, string Topic)> GetChannelStats()
    {
        foreach (var channel in Channels.Values)
        {
            yield return (channel.Name, channel.Members.Count, channel.Topic);
        }
    }

    public void InitChannels()
    {
        if (Mind.IrcDb == null)
        {
            return;
        }

        var savedChannels = Mind.IrcDb.GetAllChannels();

        foreach (var record in savedChannels)
        {
            var channel = new IrcChannel(record.Name)
            {
                Topic = record.Topic,
                TopicSetBy = record.TopicSetBy,
                TopicSetAt = record.TopicSetAt,
                Key = record.Key,
                UserLimit = record.UserLimit,
                IsPersisted = true
            };

            channel.Modes.Clear();

            foreach (var c in record.Modes)
            {
                channel.Modes.Add(c);
            }

            var bans = Mind.IrcDb.GetBans(record.Name);

            foreach (var ban in bans)
            {
                channel.BanMasks.Add(ban.Mask);
            }

            Channels[record.Name] = channel;
        }

        if (!Channels.ContainsKey("#hive"))
        {
            var hive = new IrcChannel("#hive")
            {
                Topic = "Welcome to VintageHive IRC!",
                TopicSetBy = IRCD_HOSTNAME,
                TopicSetAt = DateTime.UtcNow,
                IsPersisted = true
            };

            Channels["#hive"] = hive;
            PersistChannel(hive);
        }

        if (!Channels.ContainsKey("#vintage"))
        {
            var vintage = new IrcChannel("#vintage")
            {
                Topic = "All things retro computing",
                TopicSetBy = IRCD_HOSTNAME,
                TopicSetAt = DateTime.UtcNow,
                IsPersisted = true
            };

            Channels["#vintage"] = vintage;
            PersistChannel(vintage);
        }
    }

    #endregion

    #region Lifecycle

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        await Task.Delay(0);

        connection.IsKeepAlive = true;

        return SendIrcReply(new List<(string, IrcServerReplyType, string, string[], string)>
        {
            (IRCD_HOSTNAME, STR_NOTICE, "AUTH", null, "*** Looking up your hostname..."),
            (IRCD_HOSTNAME, STR_NOTICE, "AUTH", null, "*** Found your hostname"),
            (IRCD_HOSTNAME, STR_NOTICE, "AUTH", null, "*** Checking ident..."),
            (IRCD_HOSTNAME, STR_NOTICE, "AUTH", null, "*** No ident response; using ~username"),
        });
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var input = data[..read].ToUTF8().TrimEnd('\r', '\n');

        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var responses = new List<byte>();

        foreach (var line in lines)
        {
            var client = ParseIrcCommand(line.TrimEnd('\r'));

            if (client == null)
            {
                continue;
            }

            var response = await HandleCommand(connection, client);

            if (response != null)
            {
                responses.AddRange(response);
            }

            if (!connection.IsKeepAlive)
            {
                break;
            }
        }

        return responses.Count > 0 ? responses.ToArray() : null;
    }

    public override async Task ProcessDisconnection(ListenerSocket connection)
    {
        if (Users.TryRemove(connection.TraceId, out var user))
        {
            await RemoveUserFromAllChannels(user, "Connection closed");
        }

        await Task.CompletedTask;
    }

    #endregion

    #region Command Router

    private async Task<byte[]> HandleCommand(ListenerSocket connection, IrcCommand client)
    {
        var isRegistered = Users.TryGetValue(connection.TraceId, out var user);

        if (!isRegistered)
        {
            switch (client.Command)
            {
                case PASS:
                {
                    return HandlePass(connection, client);
                }

                case USER:
                {
                    return HandleUser(connection, client);
                }

                case NICK:
                {
                    return HandleNickRegistration(connection, client);
                }

                case QUIT:
                {
                    connection.IsKeepAlive = false;
                    return null;
                }

                case PING:
                {
                    var token = client.Params.Count > 0 ? client.Params[0] : (client.Trailing ?? IRCD_HOSTNAME);
                    return SendIrcReply(IRCD_HOSTNAME, STR_PONG, IRCD_HOSTNAME, null, token);
                }

                default:
                {
                    return SendIrcReply(IRCD_HOSTNAME, ERR_NOTREGISTERED, "*", null, "You have not registered");
                }
            }
        }

        if (IsFlooding(user))
        {
            return null;
        }

        user.LastMessageAt = DateTime.UtcNow;

        switch (client.Command)
        {
            case PRIVMSG:
            case NOTICE:
            {
                return await HandlePrivmsg(user, client);
            }

            case JOIN:
            {
                return await HandleJoin(user, client);
            }

            case PART:
            {
                return await HandlePart(user, client);
            }

            case TOPIC:
            {
                return await HandleTopic(user, client);
            }

            case LIST:
            {
                return HandleList(user, client);
            }

            case NAMES:
            {
                return HandleNames(user, client);
            }

            case WHO:
            {
                return HandleWho(user, client);
            }

            case WHOIS:
            {
                return HandleWhois(user, client);
            }

            case AWAY:
            {
                return HandleAway(user, client);
            }

            case ISON:
            {
                return HandleIson(user, client);
            }

            case USERHOST:
            {
                return HandleUserhost(user, client);
            }

            case MODE:
            {
                return await HandleMode(user, client);
            }

            case KICK:
            {
                return await HandleKick(user, client);
            }

            case INVITE:
            {
                return await HandleInvite(user, client);
            }

            case NICK:
            {
                return await HandleNickChange(user, client);
            }

            case QUIT:
            {
                return await HandleQuit(connection, user, client);
            }

            case PING:
            {
                return HandlePing(user, client);
            }

            case PONG:
            {
                return HandlePong(user, client);
            }

            case MOTD:
            {
                return SendIrcReply(GetMOTD(user.Nick));
            }

            case USER:
            case PASS:
            {
                return SendIrcReply(IRCD_HOSTNAME, ERR_ALREADYREGISTRED, user.Nick, null, "You may not reregister");
            }

            default:
            {
                return SendIrcReply(IRCD_HOSTNAME, ERR_UNKNOWNCOMMAND, user.Nick, new[] { client.Command }, "Unknown command");
            }
        }
    }

    #endregion

    #region Registration Commands

    private byte[] HandlePass(ListenerSocket connection, IrcCommand client)
    {
        if (client.Params.Count == 0 && string.IsNullOrEmpty(client.Trailing))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, "*", new[] { PASS }, "Not enough parameters");
        }

        var password = client.Params.Count > 0 ? client.Params[0] : client.Trailing;

        connection.DataBag[PASS] = password;

        return null;
    }

    private byte[] HandleUser(ListenerSocket connection, IrcCommand client)
    {
        if (client.Params.Count < 1)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, "*", new[] { USER }, "Not enough parameters");
        }

        if (connection.DataBag.ContainsKey(USER))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_ALREADYREGISTRED, "*", null, "You may not reregister");
        }

        var ircUser = new IrcUser(connection)
        {
            Username = client.Params[0],
            Hostname = connection.RemoteIP,
            Realname = client.Trailing ?? client.Params[0]
        };

        connection.DataBag[USER] = ircUser;

        return CheckIfRegistrationComplete(connection);
    }

    private byte[] HandleNickRegistration(ListenerSocket connection, IrcCommand client)
    {
        var nick = client.Params.Count > 0 ? client.Params[0] : client.Trailing;

        if (string.IsNullOrWhiteSpace(nick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NONICKNAMEGIVEN, "*", null, "No nickname given");
        }

        if (!IsValidNick(nick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_ERRONEUSNICKNAME, "*", new[] { nick }, "Erroneous nickname");
        }

        if (Users.Values.Any(x => x.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase)))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NICKNAMEINUSE, "*", new[] { nick }, "Nickname is already in use");
        }

        connection.DataBag[NICK] = nick;

        return CheckIfRegistrationComplete(connection);
    }

    private byte[] CheckIfRegistrationComplete(ListenerSocket connection)
    {
        var bag = connection.DataBag;

        if (!bag.ContainsKey(USER) || !bag.ContainsKey(NICK))
        {
            return null;
        }

        var nick = bag[NICK].ToString();
        var user = bag[USER] as IrcUser;

        // PASS authentication
        if (bag.ContainsKey(PASS))
        {
            var password = bag[PASS].ToString();
            var hiveUser = Mind.Db.UserFetch(nick, password);

            if (hiveUser == null)
            {
                var errorReply = SendIrcReply(IRCD_HOSTNAME, ERR_PASSWDMISMATCH, nick, null, "Password incorrect");
                connection.IsKeepAlive = false;
                return errorReply;
            }

            user.IsAuthenticated = true;
        }
        else
        {
            // No PASS: check if nick is registered — if so, reject
            if (Mind.Db.UserExistsByUsername(nick))
            {
                return SendIrcReply(IRCD_HOSTNAME, ERR_NICKNAMEINUSE, "*", new[] { nick }, "Nickname is registered. Use PASS to authenticate.");
            }
        }

        user.Nick = nick;
        user.ConnectedAt = DateTime.UtcNow;
        user.LastMessageAt = DateTime.UtcNow;

        Users[connection.TraceId] = user;

        bag.Remove(USER);
        bag.Remove(NICK);
        bag.Remove(PASS);

        var replies = new List<(string, IrcServerReplyType, string, string[], string)>
        {
            (IRCD_HOSTNAME, RPL_WELCOME, nick, null, $"Welcome to the Internet Relay Network {user.Fullname}"),
            (IRCD_HOSTNAME, RPL_YOURHOST, nick, null, $"Your host is {IRCD_HOSTNAME}, running version {IRCD_VERSION}"),
            (IRCD_HOSTNAME, RPL_CREATED, nick, null, $"This server was created {StartTime:R}"),
            (IRCD_HOSTNAME, RPL_MYINFO, nick, new[] { IRCD_HOSTNAME, IRCD_VERSION, "io", "ovntikl" }, ""),
            (IRCD_HOSTNAME, RPL_BOUNCE, nick, new[] { "CHANLIMIT=#:20", "CHANTYPES=#", "PREFIX=(ov)@+", "NETWORK=VintageHive", "CASEMAPPING=ascii" }, "are supported by this server"),
        };

        replies.AddRange(GetMOTD(nick));

        return SendIrcReply(replies);
    }

    #endregion

    #region Channel Commands

    private async Task<byte[]> HandleJoin(IrcUser user, IrcCommand client)
    {
        if (client.Params.Count == 0 && string.IsNullOrEmpty(client.Trailing))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, user.Nick, new[] { JOIN }, "Not enough parameters");
        }

        var channelNames = (client.Params.Count > 0 ? client.Params[0] : client.Trailing).Split(',');
        var keys = client.Params.Count > 1 ? client.Params[1].Split(',') : Array.Empty<string>();
        var allReplies = new List<byte>();

        for (var i = 0; i < channelNames.Length; i++)
        {
            var channelName = channelNames[i].Trim();

            if (!channelName.StartsWith('#'))
            {
                allReplies.AddRange(SendIrcReply(IRCD_HOSTNAME, ERR_BADCHANMASK, user.Nick, new[] { channelName }, "Bad Channel Mask"));
                continue;
            }

            if (user.Channels.Count >= 20)
            {
                allReplies.AddRange(SendIrcReply(IRCD_HOSTNAME, ERR_TOOMANYCHANNELS, user.Nick, new[] { channelName }, "You have joined too many channels"));
                continue;
            }

            var channel = Channels.GetOrAdd(channelName, name => new IrcChannel(name));

            // Check banned
            if (channel.IsBanned(user.Fullname))
            {
                allReplies.AddRange(SendIrcReply(IRCD_HOSTNAME, ERR_BANNEDFROMCHAN, user.Nick, new[] { channelName }, "Cannot join channel (+b)"));
                continue;
            }

            // Check invite-only
            if (channel.Modes.Contains('i') && !channel.InviteList.Contains(user.Nick))
            {
                allReplies.AddRange(SendIrcReply(IRCD_HOSTNAME, ERR_INVITEONLYCHAN, user.Nick, new[] { channelName }, "Cannot join channel (+i)"));
                continue;
            }

            // Check key
            if (channel.Modes.Contains('k') && !string.IsNullOrEmpty(channel.Key))
            {
                var providedKey = i < keys.Length ? keys[i] : "";

                if (providedKey != channel.Key)
                {
                    allReplies.AddRange(SendIrcReply(IRCD_HOSTNAME, ERR_BADCHANNELKEY, user.Nick, new[] { channelName }, "Cannot join channel (+k)"));
                    continue;
                }
            }

            // Check user limit
            if (channel.Modes.Contains('l') && channel.UserLimit > 0 && channel.Members.Count >= channel.UserLimit)
            {
                allReplies.AddRange(SendIrcReply(IRCD_HOSTNAME, ERR_CHANNELISFULL, user.Nick, new[] { channelName }, "Cannot join channel (+l)"));
                continue;
            }

            // Already in channel
            if (channel.Members.ContainsKey(user.Nick))
            {
                continue;
            }

            // Join the channel
            channel.Members[user.Nick] = user;
            user.Channels.Add(channelName);

            // Remove from invite list if present
            channel.InviteList.Remove(user.Nick);

            // If first user and channel is new, make them operator
            if (channel.Members.Count == 1 && !channel.IsPersisted)
            {
                channel.Operators.Add(user.Nick);
            }

            // Broadcast JOIN to other members
            var joinMessage = SendIrcReply(user.Fullname, STR_JOIN, null, null, channelName);
            await channel.BroadcastAsync(joinMessage, except: user);

            // Build response for the joining user
            var replies = new List<(string, IrcServerReplyType, string, string[], string)>();

            // The JOIN message to the joiner
            replies.Add((user.Fullname, STR_JOIN, null, null, channelName));

            // Topic
            if (!string.IsNullOrEmpty(channel.Topic))
            {
                replies.Add((IRCD_HOSTNAME, RPL_TOPIC, user.Nick, new[] { channelName }, channel.Topic));
                replies.Add((IRCD_HOSTNAME, RPL_TOPICWHOTIME, user.Nick, new[] { channelName, channel.TopicSetBy, ((DateTimeOffset)channel.TopicSetAt).ToUnixTimeSeconds().ToString() }, null));
            }
            else
            {
                replies.Add((IRCD_HOSTNAME, RPL_NOTOPIC, user.Nick, new[] { channelName }, "No topic is set"));
            }

            // Names
            var namesList = string.Join(" ", channel.Members.Keys.Select(n => channel.GetNamesPrefix(n) + n));
            replies.Add((IRCD_HOSTNAME, RPL_NAMREPLY, user.Nick, new[] { "=", channelName }, namesList));
            replies.Add((IRCD_HOSTNAME, RPL_ENDOFNAMES, user.Nick, new[] { channelName }, "End of /NAMES list."));

            allReplies.AddRange(SendIrcReply(replies));
        }

        return allReplies.Count > 0 ? allReplies.ToArray() : null;
    }

    private async Task<byte[]> HandlePart(IrcUser user, IrcCommand client)
    {
        if (client.Params.Count == 0)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, user.Nick, new[] { PART }, "Not enough parameters");
        }

        var channelNames = client.Params[0].Split(',');
        var message = client.Trailing ?? "Leaving";
        var allReplies = new List<byte>();

        foreach (var channelName in channelNames)
        {
            var trimmedName = channelName.Trim();

            if (!Channels.TryGetValue(trimmedName, out var channel))
            {
                allReplies.AddRange(SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHCHANNEL, user.Nick, new[] { trimmedName }, "No such channel"));
                continue;
            }

            if (!channel.Members.ContainsKey(user.Nick))
            {
                allReplies.AddRange(SendIrcReply(IRCD_HOSTNAME, ERR_NOTONCHANNEL, user.Nick, new[] { trimmedName }, "You're not on that channel"));
                continue;
            }

            // Broadcast PART to all members (including the user leaving)
            var partMessage = SendIrcReply(user.Fullname, STR_PART, trimmedName, null, message);
            await channel.BroadcastAsync(partMessage, except: user);

            // Remove from channel
            channel.Members.TryRemove(user.Nick, out _);
            channel.Operators.Remove(user.Nick);
            channel.Voiced.Remove(user.Nick);
            user.Channels.Remove(trimmedName);

            // Add PART to sender's response
            allReplies.AddRange(partMessage);

            // Clean up empty non-persisted channels
            if (channel.Members.IsEmpty && !channel.IsPersisted)
            {
                Channels.TryRemove(trimmedName, out _);
            }
        }

        return allReplies.Count > 0 ? allReplies.ToArray() : null;
    }

    private async Task<byte[]> HandleTopic(IrcUser user, IrcCommand client)
    {
        if (client.Params.Count == 0)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, user.Nick, new[] { TOPIC }, "Not enough parameters");
        }

        var channelName = client.Params[0];

        if (!Channels.TryGetValue(channelName, out var channel))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHCHANNEL, user.Nick, new[] { channelName }, "No such channel");
        }

        if (!channel.Members.ContainsKey(user.Nick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NOTONCHANNEL, user.Nick, new[] { channelName }, "You're not on that channel");
        }

        // Query topic
        if (client.Trailing == null && client.Params.Count == 1)
        {
            if (!string.IsNullOrEmpty(channel.Topic))
            {
                return SendIrcReply(new List<(string, IrcServerReplyType, string, string[], string)>
                {
                    (IRCD_HOSTNAME, RPL_TOPIC, user.Nick, new[] { channelName }, channel.Topic),
                    (IRCD_HOSTNAME, RPL_TOPICWHOTIME, user.Nick, new[] { channelName, channel.TopicSetBy, ((DateTimeOffset)channel.TopicSetAt).ToUnixTimeSeconds().ToString() }, null),
                });
            }
            else
            {
                return SendIrcReply(IRCD_HOSTNAME, RPL_NOTOPIC, user.Nick, new[] { channelName }, "No topic is set");
            }
        }

        // Set topic
        if (channel.Modes.Contains('t') && !channel.IsOperator(user.Nick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_CHANOPRIVSNEEDED, user.Nick, new[] { channelName }, "You're not channel operator");
        }

        channel.Topic = client.Trailing ?? "";
        channel.TopicSetBy = user.Nick;
        channel.TopicSetAt = DateTime.UtcNow;

        PersistChannel(channel);

        // Broadcast topic change to all members (including setter)
        var topicMessage = SendIrcReply(user.Fullname, STR_TOPIC, channelName, null, channel.Topic);
        await channel.BroadcastAsync(topicMessage, except: user);

        return topicMessage;
    }

    private byte[] HandleList(IrcUser user, IrcCommand client)
    {
        var replies = new List<(string, IrcServerReplyType, string, string[], string)>();

        if (client.Params.Count > 0)
        {
            // List specific channels
            foreach (var channelName in client.Params[0].Split(','))
            {
                if (Channels.TryGetValue(channelName.Trim(), out var channel))
                {
                    replies.Add((IRCD_HOSTNAME, RPL_LIST, user.Nick, new[] { channel.Name, channel.Members.Count.ToString() }, channel.Topic));
                }
            }
        }
        else
        {
            // List all channels
            foreach (var channel in Channels.Values)
            {
                replies.Add((IRCD_HOSTNAME, RPL_LIST, user.Nick, new[] { channel.Name, channel.Members.Count.ToString() }, channel.Topic));
            }
        }

        replies.Add((IRCD_HOSTNAME, RPL_LISTEND, user.Nick, null, "End of /LIST"));

        return SendIrcReply(replies);
    }

    private byte[] HandleNames(IrcUser user, IrcCommand client)
    {
        var replies = new List<(string, IrcServerReplyType, string, string[], string)>();

        if (client.Params.Count > 0)
        {
            foreach (var channelName in client.Params[0].Split(','))
            {
                if (Channels.TryGetValue(channelName.Trim(), out var channel))
                {
                    var namesList = string.Join(" ", channel.Members.Keys.Select(n => channel.GetNamesPrefix(n) + n));
                    replies.Add((IRCD_HOSTNAME, RPL_NAMREPLY, user.Nick, new[] { "=", channel.Name }, namesList));
                }

                replies.Add((IRCD_HOSTNAME, RPL_ENDOFNAMES, user.Nick, new[] { channelName.Trim() }, "End of /NAMES list."));
            }
        }
        else
        {
            foreach (var channel in Channels.Values)
            {
                var namesList = string.Join(" ", channel.Members.Keys.Select(n => channel.GetNamesPrefix(n) + n));
                replies.Add((IRCD_HOSTNAME, RPL_NAMREPLY, user.Nick, new[] { "=", channel.Name }, namesList));
            }

            replies.Add((IRCD_HOSTNAME, RPL_ENDOFNAMES, user.Nick, new[] { "*" }, "End of /NAMES list."));
        }

        return SendIrcReply(replies);
    }

    #endregion

    #region Messaging Commands

    private async Task<byte[]> HandlePrivmsg(IrcUser user, IrcCommand client)
    {
        if (client.Params.Count == 0)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, user.Nick, new[] { client.Command }, "Not enough parameters");
        }

        var target = client.Params[0];
        var message = client.Trailing ?? "";
        var isNotice = client.Command == NOTICE;
        var replyType = isNotice ? STR_NOTICE : STR_PRIVMSG;

        if (target.StartsWith('#'))
        {
            // Channel message
            if (!Channels.TryGetValue(target, out var channel))
            {
                return SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHCHANNEL, user.Nick, new[] { target }, "No such channel");
            }

            if (!channel.CanSendMessage(user))
            {
                return SendIrcReply(IRCD_HOSTNAME, ERR_CANNOTSENDTOCHAN, user.Nick, new[] { target }, "Cannot send to channel");
            }

            var msgData = SendIrcReply(user.Fullname, replyType, target, null, message);
            await channel.BroadcastAsync(msgData, except: user);

            return null;
        }
        else
        {
            // User message
            var toUser = Users.Values.FirstOrDefault(x => x.Nick.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (toUser == null)
            {
                return SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHNICK, user.Nick, new[] { target }, "No such nick/channel");
            }

            await toUser.SendData(SendIrcReply(user.Fullname, replyType, toUser.Nick, null, message));

            // RPL_AWAY for PRIVMSG only (not NOTICE)
            if (!isNotice && toUser.IsAway)
            {
                return SendIrcReply(IRCD_HOSTNAME, RPL_AWAY, user.Nick, new[] { toUser.Nick }, toUser.AwayMessage);
            }

            return null;
        }
    }

    #endregion

    #region Query Commands

    private byte[] HandleWho(IrcUser user, IrcCommand client)
    {
        var replies = new List<(string, IrcServerReplyType, string, string[], string)>();
        var target = client.Params.Count > 0 ? client.Params[0] : "*";

        if (target.StartsWith('#'))
        {
            if (Channels.TryGetValue(target, out var channel))
            {
                foreach (var member in channel.Members.Values.ToList())
                {
                    var flags = (member.IsAway ? "G" : "H") + channel.GetNamesPrefix(member.Nick);
                    replies.Add((IRCD_HOSTNAME, RPL_WHOREPLY, user.Nick, new[] { target, member.Username, member.Hostname, IRCD_HOSTNAME, member.Nick, flags }, $"0 {member.Realname}"));
                }
            }
        }
        else
        {
            // WHO for a specific nick
            var targetUser = Users.Values.FirstOrDefault(x => x.Nick.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (targetUser != null)
            {
                var chanName = targetUser.Channels.FirstOrDefault() ?? "*";
                var flags = (targetUser.IsAway ? "G" : "H");

                if (chanName != "*" && Channels.TryGetValue(chanName, out var chan))
                {
                    flags += chan.GetNamesPrefix(targetUser.Nick);
                }

                replies.Add((IRCD_HOSTNAME, RPL_WHOREPLY, user.Nick, new[] { chanName, targetUser.Username, targetUser.Hostname, IRCD_HOSTNAME, targetUser.Nick, flags }, $"0 {targetUser.Realname}"));
            }
        }

        replies.Add((IRCD_HOSTNAME, RPL_ENDOFWHO, user.Nick, new[] { target }, "End of /WHO list."));

        return SendIrcReply(replies);
    }

    private byte[] HandleWhois(IrcUser user, IrcCommand client)
    {
        if (client.Params.Count == 0)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, user.Nick, new[] { WHOIS }, "Not enough parameters");
        }

        var targetNick = client.Params.Count > 1 ? client.Params[1] : client.Params[0];
        var targetUser = Users.Values.FirstOrDefault(x => x.Nick.Equals(targetNick, StringComparison.OrdinalIgnoreCase));

        if (targetUser == null)
        {
            return SendIrcReply(new List<(string, IrcServerReplyType, string, string[], string)>
            {
                (IRCD_HOSTNAME, ERR_NOSUCHNICK, user.Nick, new[] { targetNick }, "No such nick/channel"),
                (IRCD_HOSTNAME, RPL_ENDOFWHOIS, user.Nick, new[] { targetNick }, "End of /WHOIS list."),
            });
        }

        var replies = new List<(string, IrcServerReplyType, string, string[], string)>
        {
            (IRCD_HOSTNAME, RPL_WHOISUSER, user.Nick, new[] { targetUser.Nick, targetUser.Username, targetUser.Hostname, "*" }, targetUser.Realname),
            (IRCD_HOSTNAME, RPL_WHOISSERVER, user.Nick, new[] { targetUser.Nick, IRCD_HOSTNAME }, "VintageHive IRC Network"),
        };

        // Channels
        var channelList = new List<string>();

        foreach (var chanName in targetUser.Channels.ToList())
        {
            if (Channels.TryGetValue(chanName, out var chan))
            {
                channelList.Add(chan.GetNamesPrefix(targetUser.Nick) + chan.Name);
            }
        }

        if (channelList.Count > 0)
        {
            replies.Add((IRCD_HOSTNAME, RPL_WHOISCHANNELS, user.Nick, new[] { targetUser.Nick }, string.Join(" ", channelList)));
        }

        // Idle time
        var idleSeconds = (int)(DateTime.UtcNow - targetUser.LastMessageAt).TotalSeconds;
        var signonTime = ((DateTimeOffset)targetUser.ConnectedAt).ToUnixTimeSeconds().ToString();
        replies.Add((IRCD_HOSTNAME, RPL_WHOISIDLE, user.Nick, new[] { targetUser.Nick, idleSeconds.ToString(), signonTime }, "seconds idle, signon time"));

        if (targetUser.IsAway)
        {
            replies.Add((IRCD_HOSTNAME, RPL_AWAY, user.Nick, new[] { targetUser.Nick }, targetUser.AwayMessage));
        }

        if (targetUser.IsAuthenticated)
        {
            replies.Add((IRCD_HOSTNAME, RPL_WHOISOPERATOR, user.Nick, new[] { targetUser.Nick }, "is a registered user"));
        }

        replies.Add((IRCD_HOSTNAME, RPL_ENDOFWHOIS, user.Nick, new[] { targetUser.Nick }, "End of /WHOIS list."));

        return SendIrcReply(replies);
    }

    private byte[] HandleAway(IrcUser user, IrcCommand client)
    {
        if (string.IsNullOrEmpty(client.Trailing))
        {
            user.IsAway = false;
            user.AwayMessage = string.Empty;

            return SendIrcReply(IRCD_HOSTNAME, RPL_UNAWAY, user.Nick, null, "You are no longer marked as being away");
        }
        else
        {
            user.IsAway = true;
            user.AwayMessage = client.Trailing;

            return SendIrcReply(IRCD_HOSTNAME, RPL_NOWAWAY, user.Nick, null, "You have been marked as being away");
        }
    }

    private byte[] HandleIson(IrcUser user, IrcCommand client)
    {
        var onlineNicks = new List<string>();

        foreach (var nick in client.Params)
        {
            if (Users.Values.Any(x => x.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase)))
            {
                onlineNicks.Add(nick);
            }
        }

        // Also check trailing (some clients put nicks there)
        if (!string.IsNullOrEmpty(client.Trailing))
        {
            foreach (var nick in client.Trailing.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Users.Values.Any(x => x.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase)))
                {
                    onlineNicks.Add(nick);
                }
            }
        }

        return SendIrcReply(IRCD_HOSTNAME, RPL_ISON, user.Nick, null, string.Join(" ", onlineNicks));
    }

    private byte[] HandleUserhost(IrcUser user, IrcCommand client)
    {
        var entries = new List<string>();

        foreach (var nick in client.Params)
        {
            var targetUser = Users.Values.FirstOrDefault(x => x.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase));

            if (targetUser != null)
            {
                var awayFlag = targetUser.IsAway ? "-" : "+";
                entries.Add($"{targetUser.Nick}={awayFlag}~{targetUser.Username}@{targetUser.Hostname}");
            }
        }

        return SendIrcReply(IRCD_HOSTNAME, RPL_USERHOST, user.Nick, null, string.Join(" ", entries));
    }

    #endregion

    #region Channel Management Commands

    private async Task<byte[]> HandleMode(IrcUser user, IrcCommand client)
    {
        if (client.Params.Count == 0)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, user.Nick, new[] { MODE }, "Not enough parameters");
        }

        var target = client.Params[0];

        if (target.StartsWith('#'))
        {
            return await HandleChannelMode(user, client);
        }
        else
        {
            return HandleUserMode(user, client);
        }
    }

    private async Task<byte[]> HandleChannelMode(IrcUser user, IrcCommand client)
    {
        var channelName = client.Params[0];

        if (!Channels.TryGetValue(channelName, out var channel))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHCHANNEL, user.Nick, new[] { channelName }, "No such channel");
        }

        // No mode string — show current modes
        if (client.Params.Count == 1)
        {
            var modeStr = "+" + string.Concat(channel.Modes.OrderBy(c => c));
            var modeParams = new List<string> { channelName, modeStr };

            if (channel.Modes.Contains('k') && !string.IsNullOrEmpty(channel.Key))
            {
                modeParams.Add(channel.Key);
            }

            if (channel.Modes.Contains('l') && channel.UserLimit > 0)
            {
                modeParams.Add(channel.UserLimit.ToString());
            }

            return SendIrcReply(new List<(string, IrcServerReplyType, string, string[], string)>
            {
                (IRCD_HOSTNAME, RPL_CHANNELMODEIS, user.Nick, modeParams.ToArray(), null),
                (IRCD_HOSTNAME, RPL_CREATIONTIME, user.Nick, new[] { channelName, ((DateTimeOffset)channel.CreatedAt).ToUnixTimeSeconds().ToString() }, null),
            });
        }

        var modeString = client.Params[1];
        var paramIdx = 2;
        var adding = true;
        var addedModes = new StringBuilder();
        var removedModes = new StringBuilder();
        var addedParams = new List<string>();
        var removedParams = new List<string>();

        foreach (var c in modeString)
        {
            if (c == '+')
            {
                adding = true;
                continue;
            }

            if (c == '-')
            {
                adding = false;
                continue;
            }

            switch (c)
            {
                case 'o':
                case 'v':
                {
                    if (!channel.IsOperator(user.Nick))
                    {
                        return SendIrcReply(IRCD_HOSTNAME, ERR_CHANOPRIVSNEEDED, user.Nick, new[] { channelName }, "You're not channel operator");
                    }

                    if (paramIdx >= client.Params.Count)
                    {
                        break;
                    }

                    var targetNick = client.Params[paramIdx++];

                    if (!channel.Members.ContainsKey(targetNick))
                    {
                        return SendIrcReply(IRCD_HOSTNAME, ERR_USERNOTINCHANNEL, user.Nick, new[] { targetNick, channelName }, "They aren't on that channel");
                    }

                    var set = c == 'o' ? channel.Operators : channel.Voiced;

                    if (adding)
                    {
                        set.Add(targetNick);
                        addedModes.Append(c);
                        addedParams.Add(targetNick);
                    }
                    else
                    {
                        set.Remove(targetNick);
                        removedModes.Append(c);
                        removedParams.Add(targetNick);
                    }

                    break;
                }

                case 'b':
                {
                    if (paramIdx >= client.Params.Count)
                    {
                        // List bans
                        var banReplies = new List<(string, IrcServerReplyType, string, string[], string)>();

                        foreach (var ban in channel.BanMasks.ToList())
                        {
                            banReplies.Add((IRCD_HOSTNAME, RPL_BANLIST, user.Nick, new[] { channelName, ban }, null));
                        }

                        banReplies.Add((IRCD_HOSTNAME, RPL_ENDOFBANLIST, user.Nick, new[] { channelName }, "End of channel ban list"));

                        return SendIrcReply(banReplies);
                    }

                    if (!channel.IsOperator(user.Nick))
                    {
                        return SendIrcReply(IRCD_HOSTNAME, ERR_CHANOPRIVSNEEDED, user.Nick, new[] { channelName }, "You're not channel operator");
                    }

                    var mask = client.Params[paramIdx++];

                    if (adding)
                    {
                        channel.BanMasks.Add(mask);
                        Mind.IrcDb?.AddBan(channelName, mask, user.Nick);
                        addedModes.Append(c);
                        addedParams.Add(mask);
                    }
                    else
                    {
                        channel.BanMasks.Remove(mask);
                        Mind.IrcDb?.RemoveBan(channelName, mask);
                        removedModes.Append(c);
                        removedParams.Add(mask);
                    }

                    break;
                }

                case 'k':
                {
                    if (!channel.IsOperator(user.Nick))
                    {
                        return SendIrcReply(IRCD_HOSTNAME, ERR_CHANOPRIVSNEEDED, user.Nick, new[] { channelName }, "You're not channel operator");
                    }

                    if (adding)
                    {
                        if (paramIdx >= client.Params.Count)
                        {
                            break;
                        }

                        channel.Key = client.Params[paramIdx++];
                        channel.Modes.Add('k');
                        addedModes.Append(c);
                        addedParams.Add(channel.Key);
                    }
                    else
                    {
                        channel.Key = string.Empty;
                        channel.Modes.Remove('k');
                        removedModes.Append(c);
                        removedParams.Add("*");
                    }

                    PersistChannel(channel);

                    break;
                }

                case 'l':
                {
                    if (!channel.IsOperator(user.Nick))
                    {
                        return SendIrcReply(IRCD_HOSTNAME, ERR_CHANOPRIVSNEEDED, user.Nick, new[] { channelName }, "You're not channel operator");
                    }

                    if (adding)
                    {
                        if (paramIdx >= client.Params.Count)
                        {
                            break;
                        }

                        if (int.TryParse(client.Params[paramIdx++], out var limit))
                        {
                            channel.UserLimit = limit;
                            channel.Modes.Add('l');
                            addedModes.Append(c);
                            addedParams.Add(limit.ToString());
                        }
                    }
                    else
                    {
                        channel.UserLimit = 0;
                        channel.Modes.Remove('l');
                        removedModes.Append(c);
                    }

                    PersistChannel(channel);

                    break;
                }

                case 'n':
                case 't':
                case 'i':
                {
                    if (!channel.IsOperator(user.Nick))
                    {
                        return SendIrcReply(IRCD_HOSTNAME, ERR_CHANOPRIVSNEEDED, user.Nick, new[] { channelName }, "You're not channel operator");
                    }

                    if (adding)
                    {
                        channel.Modes.Add(c);
                        addedModes.Append(c);
                    }
                    else
                    {
                        channel.Modes.Remove(c);
                        removedModes.Append(c);
                    }

                    PersistChannel(channel);

                    break;
                }

                default:
                {
                    return SendIrcReply(IRCD_HOSTNAME, ERR_UNKNOWNMODE, user.Nick, new[] { c.ToString() }, "is unknown mode char to me");
                }
            }
        }

        // Broadcast mode changes
        var modeChange = new StringBuilder();
        var allParams = new List<string>();

        if (addedModes.Length > 0)
        {
            modeChange.Append('+').Append(addedModes);
            allParams.AddRange(addedParams);
        }

        if (removedModes.Length > 0)
        {
            modeChange.Append('-').Append(removedModes);
            allParams.AddRange(removedParams);
        }

        if (modeChange.Length > 0)
        {
            var broadcastParams = new List<string> { modeChange.ToString() };
            broadcastParams.AddRange(allParams);

            var modeMessage = SendIrcReply(user.Fullname, STR_MODE, channelName, broadcastParams.ToArray(), null);
            await channel.BroadcastAsync(modeMessage);
        }

        return null;
    }

    private byte[] HandleUserMode(IrcUser user, IrcCommand client)
    {
        var targetNick = client.Params[0];

        if (!targetNick.Equals(user.Nick, StringComparison.OrdinalIgnoreCase))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_USERSDISABLED, user.Nick, null, "Cannot change mode for other users");
        }

        // No mode string — show current modes
        if (client.Params.Count == 1)
        {
            var modeStr = "+" + string.Concat(user.Modes.OrderBy(c => c));
            return SendIrcReply(IRCD_HOSTNAME, STR_MODE, user.Nick, new[] { modeStr }, null);
        }

        var modeString = client.Params[1];
        var adding = true;

        foreach (var c in modeString)
        {
            if (c == '+')
            {
                adding = true;
                continue;
            }

            if (c == '-')
            {
                adding = false;
                continue;
            }

            switch (c)
            {
                case 'i':
                {
                    if (adding)
                    {
                        user.Modes.Add('i');
                    }
                    else
                    {
                        user.Modes.Remove('i');
                    }

                    break;
                }
            }
        }

        var resultMode = "+" + string.Concat(user.Modes.OrderBy(ch => ch));

        return SendIrcReply(user.Fullname, STR_MODE, user.Nick, new[] { resultMode }, null);
    }

    private async Task<byte[]> HandleKick(IrcUser user, IrcCommand client)
    {
        if (client.Params.Count < 2)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, user.Nick, new[] { KICK }, "Not enough parameters");
        }

        var channelName = client.Params[0];
        var targetNick = client.Params[1];
        var reason = client.Trailing ?? user.Nick;

        if (!Channels.TryGetValue(channelName, out var channel))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHCHANNEL, user.Nick, new[] { channelName }, "No such channel");
        }

        if (!channel.IsOperator(user.Nick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_CHANOPRIVSNEEDED, user.Nick, new[] { channelName }, "You're not channel operator");
        }

        if (!channel.Members.TryGetValue(targetNick, out var targetUser))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_USERNOTINCHANNEL, user.Nick, new[] { targetNick, channelName }, "They aren't on that channel");
        }

        // Broadcast KICK to all members
        var kickMessage = SendIrcReply(user.Fullname, STR_KICK, channelName, new[] { targetNick }, reason);
        await channel.BroadcastAsync(kickMessage);

        // Remove target from channel
        channel.Members.TryRemove(targetNick, out _);
        channel.Operators.Remove(targetNick);
        channel.Voiced.Remove(targetNick);
        targetUser.Channels.Remove(channelName);

        return null;
    }

    private async Task<byte[]> HandleInvite(IrcUser user, IrcCommand client)
    {
        if (client.Params.Count < 2)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NEEDMOREPARAMS, user.Nick, new[] { INVITE }, "Not enough parameters");
        }

        var targetNick = client.Params[0];
        var channelName = client.Params[1];

        if (!Channels.TryGetValue(channelName, out var channel))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHCHANNEL, user.Nick, new[] { channelName }, "No such channel");
        }

        if (!channel.Members.ContainsKey(user.Nick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NOTONCHANNEL, user.Nick, new[] { channelName }, "You're not on that channel");
        }

        if (channel.Modes.Contains('i') && !channel.IsOperator(user.Nick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_CHANOPRIVSNEEDED, user.Nick, new[] { channelName }, "You're not channel operator");
        }

        if (channel.Members.ContainsKey(targetNick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_USERONCHANNEL, user.Nick, new[] { targetNick, channelName }, "is already on channel");
        }

        var targetUser = Users.Values.FirstOrDefault(x => x.Nick.Equals(targetNick, StringComparison.OrdinalIgnoreCase));

        if (targetUser == null)
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NOSUCHNICK, user.Nick, new[] { targetNick }, "No such nick/channel");
        }

        // Add to invite list
        channel.InviteList.Add(targetNick);

        // Notify the target
        var inviteMessage = SendIrcReply(user.Fullname, STR_INVITE, targetNick, new[] { channelName }, null);
        await targetUser.SendData(inviteMessage);

        return SendIrcReply(IRCD_HOSTNAME, RPL_INVITING, user.Nick, new[] { targetNick, channelName }, null);
    }

    #endregion

    #region Session Commands

    private async Task<byte[]> HandleNickChange(IrcUser user, IrcCommand client)
    {
        var newNick = client.Params.Count > 0 ? client.Params[0] : client.Trailing;

        if (string.IsNullOrWhiteSpace(newNick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NONICKNAMEGIVEN, user.Nick, null, "No nickname given");
        }

        if (!IsValidNick(newNick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_ERRONEUSNICKNAME, user.Nick, new[] { newNick }, "Erroneous nickname");
        }

        if (newNick.Equals(user.Nick, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Check if nick is in use by another connected user
        if (Users.Values.Any(x => !ReferenceEquals(x, user) && x.Nick.Equals(newNick, StringComparison.OrdinalIgnoreCase)))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NICKNAMEINUSE, user.Nick, new[] { newNick }, "Nickname is already in use");
        }

        // Check if nick is registered to someone else
        if (!user.IsAuthenticated && Mind.Db.UserExistsByUsername(newNick))
        {
            return SendIrcReply(IRCD_HOSTNAME, ERR_NICKNAMEINUSE, user.Nick, new[] { newNick }, "Nickname is registered. Use PASS to authenticate.");
        }

        var oldNick = user.Nick;
        var oldFullname = user.Fullname;

        // Update nick in all channels
        foreach (var channelName in user.Channels.ToList())
        {
            if (Channels.TryGetValue(channelName, out var channel))
            {
                channel.Members.TryRemove(oldNick, out _);
                channel.Members[newNick] = user;

                if (channel.Operators.Remove(oldNick))
                {
                    channel.Operators.Add(newNick);
                }

                if (channel.Voiced.Remove(oldNick))
                {
                    channel.Voiced.Add(newNick);
                }
            }
        }

        user.Nick = newNick;

        // Broadcast to all users in shared channels (deduplicated)
        var nickMessage = SendIrcReply(oldFullname, STR_NICK, null, null, newNick);
        var notified = new HashSet<Guid>();
        notified.Add(user.ListenerSocket.TraceId);

        foreach (var channelName in user.Channels.ToList())
        {
            if (Channels.TryGetValue(channelName, out var channel))
            {
                foreach (var member in channel.Members.Values.ToList())
                {
                    if (notified.Add(member.ListenerSocket.TraceId))
                    {
                        try
                        {
                            await member.SendData(nickMessage);
                        }
                        catch
                        {
                            // Connection may have dropped
                        }
                    }
                }
            }
        }

        return nickMessage;
    }

    private async Task<byte[]> HandleQuit(ListenerSocket connection, IrcUser user, IrcCommand client)
    {
        var message = !string.IsNullOrEmpty(client.Trailing) ? client.Trailing : "Leaving";

        await RemoveUserFromAllChannels(user, message);

        Users.TryRemove(connection.TraceId, out _);

        connection.IsKeepAlive = false;

        return Encoding.UTF8.GetBytes($"ERROR :Closing Link: {user.Hostname} (Quit: {message})\r\n");
    }

    private byte[] HandlePing(IrcUser user, IrcCommand client)
    {
        var token = client.Params.Count > 0 ? client.Params[0] : (client.Trailing ?? IRCD_HOSTNAME);

        return SendIrcReply(IRCD_HOSTNAME, STR_PONG, IRCD_HOSTNAME, null, token);
    }

    private byte[] HandlePong(IrcUser user, IrcCommand client)
    {
        user.LastMessageAt = DateTime.UtcNow;

        return null;
    }

    #endregion

    #region MOTD

    private static List<(string, IrcServerReplyType, string, string[], string)> GetMOTD(string nick)
    {
        return new List<(string, IrcServerReplyType, string, string[], string)>
        {
            (IRCD_HOSTNAME, RPL_MOTDSTART, nick, null, $"- {IRCD_HOSTNAME} Message of the day -"),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "-  __     ___       _                  _   _ _           "),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, @"-  \ \   / (_)_ __ | |_ __ _  __ _  ___| | | (_)_   ___  "),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, @"-   \ \ / /| | '_ \| __/ _` |/ _` |/ _ \ |_| | \ \ / / _ \"),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, @"-    \ V / | | | | | || (_| | (_| |  __/  _  | |\ V /  __/"),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, @"-     \_/  |_|_| |_|\__\__,_|\__, |\___|_| |_|_| \_/ \___|"),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, @"-                             |___/                        "),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- "),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- Welcome to the VintageHive IRC Network!"),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- "),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- Rules: Be excellent to each other."),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- "),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- Default channels: #hive, #vintage"),
            (IRCD_HOSTNAME, RPL_MOTD, nick, null, "- "),
            (IRCD_HOSTNAME, RPL_ENDOFMOTD, nick, null, "End of /MOTD command."),
        };
    }

    #endregion

    #region Flood Protection

    private bool IsFlooding(IrcUser user)
    {
        var now = DateTime.UtcNow;

        if (now - user.LastMessageAt > FloodWindow)
        {
            user.MessageCount = 1;
            return false;
        }

        user.MessageCount++;

        return user.MessageCount > FLOOD_MAX_MESSAGES;
    }

    #endregion

    #region Cleanup & Persistence

    private async Task RemoveUserFromAllChannels(IrcUser user, string quitMessage)
    {
        var quitMsg = SendIrcReply(user.Fullname, STR_QUIT, null, null, quitMessage);
        var notified = new HashSet<Guid>();

        foreach (var channelName in user.Channels.ToList())
        {
            if (Channels.TryGetValue(channelName, out var channel))
            {
                channel.Members.TryRemove(user.Nick, out _);
                channel.Operators.Remove(user.Nick);
                channel.Voiced.Remove(user.Nick);

                foreach (var member in channel.Members.Values.ToList())
                {
                    if (notified.Add(member.ListenerSocket.TraceId))
                    {
                        try
                        {
                            await member.SendData(quitMsg);
                        }
                        catch
                        {
                            // Connection may have dropped
                        }
                    }
                }

                if (channel.Members.IsEmpty && !channel.IsPersisted)
                {
                    Channels.TryRemove(channelName, out _);
                }
            }
        }

        user.Channels.Clear();
    }

    private void PersistChannel(IrcChannel channel)
    {
        channel.IsPersisted = true;

        Mind.IrcDb?.SaveChannel(
            channel.Name,
            channel.Topic,
            channel.TopicSetBy,
            channel.TopicSetAt,
            string.Concat(channel.Modes),
            channel.Key,
            channel.UserLimit
        );
    }

    #endregion

    #region IRC Protocol Helpers

    private static bool IsValidNick(string nick)
    {
        if (string.IsNullOrEmpty(nick) || nick.Length > 30)
        {
            return false;
        }

        return Regex.IsMatch(nick, @"^[A-Za-z\[\]\\`_\^{|}][A-Za-z0-9\[\]\\`_\^{|}\-]*$");
    }

    private static IrcCommand ParseIrcCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        const string pattern = @"^(?::(?<prefix>[^\s]+)\s+)?(?<command>[A-Za-z0-9]+)(?:\s+(?<params>[^\s:]+))*(?:\s+:(?<trailing>.*))?$";

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

    private static byte[] SendIrcReply(string host, IrcServerReplyType replyType, string nickname, string[] parameters, string trailing)
    {
        return SendIrcReply(new List<(string, IrcServerReplyType, string, string[], string)>
        {
            (host, replyType, nickname, parameters, trailing)
        });
    }

    private static byte[] SendIrcReply(List<(string, IrcServerReplyType, string, string[], string)> replies)
    {
        var sb = new StringBuilder();

        foreach (var (host, replyType, nickname, parameters, trailing) in replies)
        {
            sb.Append($":{host} ");

            if ((int)replyType < 100)
            {
                sb.Append(((int)replyType).ToString("D3"));
            }
            else if ((int)replyType >= 900)
            {
                sb.Append(replyType.ToString()[4..]);
            }
            else
            {
                sb.Append((int)replyType);
            }

            if (!string.IsNullOrEmpty(nickname))
            {
                sb.Append(' ').Append(nickname);
            }

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    sb.Append(' ').Append(param);
                }
            }

            if (trailing != null)
            {
                sb.Append(" :").Append(trailing);
            }

            sb.Append("\r\n");
        }

        string outgoingCommand = sb.ToString();

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(IrcProxy), outgoingCommand, "");

        return Encoding.UTF8.GetBytes(outgoingCommand);
    }

    #endregion
}
