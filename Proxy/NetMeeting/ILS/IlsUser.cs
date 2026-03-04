// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.ILS;

/// <summary>
/// An ILS directory entry representing an online user (rtPerson).
/// Attributes are stored as a case-insensitive dictionary to handle
/// the varied attribute names from MS-TAIL (sIPAddress, sFlags, etc.).
/// </summary>
internal class IlsUser
{
    private readonly Dictionary<string, List<string>> _attributes = new(StringComparer.OrdinalIgnoreCase);

    public string Dn { get; set; }
    public Guid SessionId { get; set; }
    public DateTime ExpiresAt { get; set; }

    public string GetAttribute(string name)
    {
        if (_attributes.TryGetValue(name, out var values) && values.Count > 0)
        {
            return values[0];
        }
        return null;
    }

    public List<string> GetAttributes(string name)
    {
        if (_attributes.TryGetValue(name, out var values))
        {
            return new List<string>(values);
        }
        return new List<string>();
    }

    public bool HasAttribute(string name)
    {
        return _attributes.ContainsKey(name);
    }

    public void SetAttribute(string name, string value)
    {
        _attributes[name] = new List<string> { value };
    }

    public void SetAttributes(string name, List<string> values)
    {
        _attributes[name] = new List<string>(values);
    }

    public void AddAttributeValue(string name, string value)
    {
        if (!_attributes.TryGetValue(name, out var values))
        {
            values = new List<string>();
            _attributes[name] = values;
        }
        values.Add(value);
    }

    public void RemoveAttribute(string name)
    {
        _attributes.Remove(name);
    }

    /// <summary>
    /// Returns selected attributes in the order requested (ILS requires this).
    /// If requestedAttrs is empty, returns all attributes.
    /// </summary>
    public Dictionary<string, List<string>> GetSelectedAttributes(List<string> requestedAttrs)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (requestedAttrs == null || requestedAttrs.Count == 0)
        {
            foreach (var kvp in _attributes)
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }

        foreach (var name in requestedAttrs)
        {
            if (_attributes.TryGetValue(name, out var values))
            {
                result[name] = values;
            }
        }
        return result;
    }
}
