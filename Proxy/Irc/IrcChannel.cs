// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace VintageHive.Proxy.Irc;

internal class IrcChannel
{
    public string Name { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string TopicSetBy { get; set; } = string.Empty;

    public DateTime TopicSetAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public HashSet<char> Modes { get; set; } = new() { 'n', 't' };

    public string Key { get; set; } = string.Empty;

    public int UserLimit { get; set; } = 0;

    public bool IsPersisted { get; set; }

    public ConcurrentDictionary<string, IrcUser> Members { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Operators { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Voiced { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> BanMasks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> InviteList { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IrcChannel(string name)
    {
        Name = name;
    }

    public async Task BroadcastAsync(byte[] data, IrcUser except = null)
    {
        foreach (var member in Members.Values.ToList())
        {
            if (except != null && member.Nick.Equals(except.Nick, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await member.SendData(data);
            }
            catch
            {
                // Connection may have dropped
            }
        }
    }

    public bool IsOperator(string nick)
    {
        return Operators.Contains(nick);
    }

    public bool IsVoiced(string nick)
    {
        return Voiced.Contains(nick);
    }

    public bool IsBanned(string fullname)
    {
        foreach (var mask in BanMasks.ToList())
        {
            if (MatchWildcard(fullname, mask))
            {
                return true;
            }
        }

        return false;
    }

    public bool CanSendMessage(IrcUser user)
    {
        if (IsOperator(user.Nick) || IsVoiced(user.Nick))
        {
            return true;
        }

        if (IsBanned(user.Fullname))
        {
            return false;
        }

        if (Modes.Contains('n') && !Members.ContainsKey(user.Nick))
        {
            return false;
        }

        return true;
    }

    public string GetNamesPrefix(string nick)
    {
        if (IsOperator(nick))
        {
            return "@";
        }

        if (IsVoiced(nick))
        {
            return "+";
        }

        return "";
    }

    private static bool MatchWildcard(string input, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
