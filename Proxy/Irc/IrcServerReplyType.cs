﻿namespace VintageHive.Proxy.Irc;

public enum IrcServerReplyType
{
    // Standard replies
    RPL_WELCOME = 001,
    RPL_YOURHOST = 002,
    RPL_CREATED = 003,
    RPL_MYINFO = 004,
    RPL_BOUNCE = 005, // Also known as RPL_ISUPPORT in some implementations

    RPL_USERHOST = 302,
    RPL_ISON = 303,
    RPL_AWAY = 301,
    RPL_UNAWAY = 305,
    RPL_NOWAWAY = 306,

    RPL_WHOISUSER = 311,
    RPL_WHOISSERVER = 312,
    RPL_WHOISOPERATOR = 313,
    RPL_WHOISIDLE = 317,
    RPL_ENDOFWHOIS = 318,
    RPL_WHOISCHANNELS = 319,

    RPL_WHOREPLY = 352,
    RPL_ENDOFWHO = 315,
    RPL_LIST = 322,
    RPL_LISTEND = 323,
    RPL_CHANNELMODEIS = 324,
    RPL_NOTOPIC = 331,
    RPL_TOPIC = 332,

    RPL_NAMREPLY = 353,
    RPL_ENDOFNAMES = 366,

    RPL_MOTD = 372,
    RPL_MOTDSTART = 375,
    RPL_ENDOFMOTD = 376,

    // Error replies
    ERR_NOSUCHNICK = 401,
    ERR_NOSUCHSERVER = 402,
    ERR_NOSUCHCHANNEL = 403,
    ERR_CANNOTSENDTOCHAN = 404,
    ERR_TOOMANYCHANNELS = 405,
    ERR_UNKNOWNCOMMAND = 421,
    ERR_NOMOTD = 422,
    ERR_NONICKNAMEGIVEN = 431,
    ERR_ERRONEUSNICKNAME = 432,
    ERR_NICKNAMEINUSE = 433,
    ERR_USERNOTINCHANNEL = 441,
    ERR_NOTONCHANNEL = 442,
    ERR_USERONCHANNEL = 443,
    ERR_NOLOGIN = 444,
    ERR_SUMMONDISABLED = 445,
    ERR_USERSDISABLED = 446,
    ERR_NOTREGISTERED = 451,
    ERR_NEEDMOREPARAMS = 461,
    ERR_ALREADYREGISTRED = 462,
    ERR_PASSWDMISMATCH = 464,
    ERR_YOUREBANNEDCREEP = 465,
    ERR_YOUWILLBEBANNED = 466,
    ERR_KEYSET = 467,
    ERR_CHANNELISFULL = 471,
    ERR_UNKNOWNMODE = 472,
    ERR_INVITEONLYCHAN = 473,
    ERR_BANNEDFROMCHAN = 474,
    ERR_BADCHANNELKEY = 475,
    ERR_BADCHANMASK = 476,
    ERR_NOCHANMODES = 477,
    ERR_BANLISTFULL = 478,

    // Custom entries
    STR_NOTICE = 900,
    STR_PRIVMSG = 901,
}
