// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;

namespace VintageHive.Proxy.NetMeeting.ILS;

/// <summary>
/// Thread-safe in-memory ILS user directory. Keyed by CN (case-insensitive).
/// Supports search with LDAP filter evaluation and TTL-based expiry.
/// </summary>
internal class IlsDirectory
{
    private readonly ConcurrentDictionary<string, IlsUser> _users = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _users.Count;

    public void AddOrUpdate(IlsUser user)
    {
        var cn = NormalizeKey(user.GetAttribute("cn") ?? ExtractCnFromDn(user.Dn));
        _users.AddOrUpdate(cn, user, (_, _) => user);
    }

    public bool Remove(string cn)
    {
        return _users.TryRemove(NormalizeKey(cn), out _);
    }

    public bool RemoveByDn(string dn)
    {
        var cn = ExtractCnFromDn(dn);
        return Remove(cn);
    }

    public void RemoveBySession(Guid sessionId)
    {
        var toRemove = _users
            .Where(kv => kv.Value.SessionId == sessionId)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _users.TryRemove(key, out _);
        }
    }

    public IlsUser Find(string cn)
    {
        return _users.TryGetValue(NormalizeKey(cn), out var user) ? user : null;
    }

    public IlsUser FindByDn(string dn)
    {
        var cn = ExtractCnFromDn(dn);
        return Find(cn);
    }

    public List<IlsUser> Search(LdapFilter filter)
    {
        return _users.Values.Where(u => filter.Evaluate(u)).ToList();
    }

    public List<IlsUser> GetAll()
    {
        return _users.Values.ToList();
    }

    public void CleanExpired()
    {
        var now = DateTime.UtcNow;
        var expired = _users
            .Where(kv => kv.Value.ExpiresAt < now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expired)
        {
            _users.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Extract the CN value from an ILS-style DN.
    /// ILS DNs are non-standard: "c=-,o=Microsoft, cn=user@email, objectClass=rtPerson"
    /// </summary>
    internal static string ExtractCnFromDn(string dn)
    {
        if (string.IsNullOrEmpty(dn))
        {
            return "";
        }

        var parts = dn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("cn=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(3).Trim();
            }
        }
        return dn;
    }

    private static string NormalizeKey(string cn)
    {
        return cn?.Trim() ?? "";
    }
}
