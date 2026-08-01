// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.ILS;

/// <summary>
/// LDAP search filter tree. Parsed from BER-encoded filter in SearchRequest.
/// Handles ILS-specific % wildcard (MS-TAIL protocol violation).
/// </summary>
internal abstract class LdapFilter
{
    public abstract bool Evaluate(IlsUser user);

    /// <summary>
    /// How deep a filter tree may nest. Each and/or/not level costs about two bytes on the wire against a
    /// 1 MB message cap, so an unbounded parser could be driven tens of thousands of levels deep by a single
    /// SearchRequest - and StackOverflowException cannot be caught in .NET, so that took the whole process
    /// down rather than just the connection. Real filters are a handful of levels at most; NetMeeting's are
    /// flatter still. Evaluate() recurses over the same tree, so capping the parse bounds both.
    /// </summary>
    public const int MaxNestingDepth = 32;

    public static LdapFilter Parse(BerDecoder decoder)
    {
        return Parse(decoder, 0);
    }

    private static LdapFilter Parse(BerDecoder decoder, int depth)
    {
        if (depth > MaxNestingDepth)
        {
            throw new ApplicationException($"LDAP filter nested deeper than {MaxNestingDepth} levels");
        }

        var tag = decoder.PeekTag();

        switch (tag.RawByte)
        {
            case LdapConstants.FILTER_AND:
            {
                decoder.ReadTag();
                var length = decoder.ReadLength();
                var children = decoder.Slice(length);
                var filters = new List<LdapFilter>();
                while (children.HasData)
                {
                    filters.Add(Parse(children, depth + 1));
                }
                return new AndFilter(filters);
            }

            case LdapConstants.FILTER_OR:
            {
                decoder.ReadTag();
                var length = decoder.ReadLength();
                var children = decoder.Slice(length);
                var filters = new List<LdapFilter>();
                while (children.HasData)
                {
                    filters.Add(Parse(children, depth + 1));
                }
                return new OrFilter(filters);
            }

            case LdapConstants.FILTER_NOT:
            {
                decoder.ReadTag();
                var length = decoder.ReadLength();
                var child = decoder.Slice(length);
                return new NotFilter(Parse(child, depth + 1));
            }

            case LdapConstants.FILTER_EQUALITY:
            {
                decoder.ReadTag();
                var length = decoder.ReadLength();
                var body = decoder.Slice(length);
                var attr = body.ReadString();
                var value = body.ReadString();
                return new EqualityFilter(attr, value);
            }

            case LdapConstants.FILTER_SUBSTRINGS:
            {
                decoder.ReadTag();
                var length = decoder.ReadLength();
                var body = decoder.Slice(length);
                var attr = body.ReadString();
                var subSeq = body.ReadSequence();

                string initial = null;
                string final_ = null;
                var any = new List<string>();

                while (subSeq.HasData)
                {
                    var subTag = subSeq.ReadTag();
                    var subLen = subSeq.ReadLength();
                    var subVal = Encoding.UTF8.GetString(subSeq.ReadBytes(subLen));

                    switch (subTag.RawByte)
                    {
                        case LdapConstants.SUBSTRING_INITIAL:
                        {
                            initial = subVal;
                        }
                        break;

                        case LdapConstants.SUBSTRING_ANY:
                        {
                            any.Add(subVal);
                        }
                        break;

                        case LdapConstants.SUBSTRING_FINAL:
                        {
                            final_ = subVal;
                        }
                        break;
                    }
                }
                return new SubstringFilter(attr, initial, any, final_);
            }

            case LdapConstants.FILTER_PRESENT:
            {
                decoder.ReadTag();
                var length = decoder.ReadLength();
                var attr = Encoding.UTF8.GetString(decoder.ReadBytes(length));
                return new PresentFilter(attr);
            }

            default:
            {
                // Unknown filter type - skip and match everything
                decoder.Skip();
                return new PresentFilter("objectClass");
            }
        }
    }
}

internal class AndFilter : LdapFilter
{
    public IReadOnlyList<LdapFilter> Children { get; }

    public AndFilter(List<LdapFilter> children)
    {
        Children = children;
    }

    public override bool Evaluate(IlsUser user)
    {
        foreach (var child in Children)
        {
            if (!child.Evaluate(user))
            {
                return false;
            }
        }
        return true;
    }
}

internal class OrFilter : LdapFilter
{
    public IReadOnlyList<LdapFilter> Children { get; }

    public OrFilter(List<LdapFilter> children)
    {
        Children = children;
    }

    public override bool Evaluate(IlsUser user)
    {
        foreach (var child in Children)
        {
            if (child.Evaluate(user))
            {
                return true;
            }
        }
        return false;
    }
}

internal class NotFilter : LdapFilter
{
    public LdapFilter Child { get; }

    public NotFilter(LdapFilter child)
    {
        Child = child;
    }

    public override bool Evaluate(IlsUser user)
    {
        return !Child.Evaluate(user);
    }
}

internal class EqualityFilter : LdapFilter
{
    public string Attribute { get; }
    public string Value { get; }

    public EqualityFilter(string attribute, string value)
    {
        Attribute = attribute;
        Value = value;
    }

    public override bool Evaluate(IlsUser user)
    {
        // ILS uses % as wildcard (MS-TAIL protocol violation)
        if (Value == "%" || Value == "*")
        {
            return user.HasAttribute(Attribute);
        }

        // Multi-valued attribute: match if ANY value equals
        var values = user.GetAttributes(Attribute);
        return values.Any(v => string.Equals(v, Value, StringComparison.OrdinalIgnoreCase));
    }
}

internal class SubstringFilter : LdapFilter
{
    public string Attribute { get; }
    public string Initial { get; }
    public IReadOnlyList<string> Any { get; }
    public string Final { get; }

    public SubstringFilter(string attribute, string initial, List<string> any, string final_)
    {
        Attribute = attribute;
        Initial = initial;
        Any = any;
        Final = final_;
    }

    public override bool Evaluate(IlsUser user)
    {
        var values = user.GetAttributes(Attribute);
        return values.Any(v => MatchesSubstring(v));
    }

    private bool MatchesSubstring(string value)
    {
        if (value == null)
        {
            return false;
        }

        var pos = 0;

        if (Initial != null)
        {
            if (!value.StartsWith(Initial, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            pos = Initial.Length;
        }

        foreach (var any in Any)
        {
            var idx = value.IndexOf(any, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return false;
            }
            pos = idx + any.Length;
        }

        if (Final != null)
        {
            if (!value.EndsWith(Final, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (pos > value.Length - Final.Length)
            {
                return false;
            }
        }

        return true;
    }
}

internal class PresentFilter : LdapFilter
{
    public string Attribute { get; }

    public PresentFilter(string attribute)
    {
        Attribute = attribute;
    }

    public override bool Evaluate(IlsUser user)
    {
        return user.HasAttribute(Attribute);
    }
}
