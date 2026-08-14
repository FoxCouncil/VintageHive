// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Msn.FileTransfer;

/// <summary>
/// A parsed <c>text/x-msmsgsinvite</c> switchboard payload: the MIME headers (MIME-Version, Content-Type) and
/// the invite key/value fields (Application-GUID, Invitation-Command/Cookie, IP-Address, Port, AuthCookie, ...).
/// Only ever constructed for a payload that actually is an msmsgsinvite; every other switchboard MSG is left as
/// raw bytes and relayed byte-identical, which is the whole flag-off / non-file-transfer compatibility contract.
/// <para>
/// Field order is preserved so a rewritten invite differs from the original in exactly the values that changed,
/// and the two structural sections (MIME headers, then a blank line, then the invite body) are kept apart the
/// way a period client emits and expects them.
/// </para>
/// </summary>
public sealed class MsnInvite
{
    /// <summary>The file-transfer Application-GUID. Only invites carrying this are the relay's business; every
    /// other application (voice clip, games, whiteboard) passes through untouched.</summary>
    public const string FileTransferGuid = "{5D3E02AB-6190-11d3-BBBB-00C04F795683}";

    const string ContentTypeToken = "text/x-msmsgsinvite";

    List<KeyValuePair<string, string>> _mimeHeaders;

    List<KeyValuePair<string, string>> _fields;

    MsnInvite() { }

    /// <summary>
    /// Parses a switchboard payload as an msmsgsinvite, or returns false (leaving it to be relayed as-is) for
    /// anything that is not one - a plain text message, an emoticon, a differently-typed invite.
    /// </summary>
    public static bool TryParse(byte[] payload, out MsnInvite invite)
    {
        invite = null;

        if (payload == null || payload.Length == 0)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(payload);

        var separator = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (separator < 0)
        {
            return false;
        }

        var mimeHeaders = ParseFields(text[..separator]);

        var contentType = Get(mimeHeaders, "Content-Type");

        if (contentType == null || !contentType.Contains(ContentTypeToken, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        invite = new MsnInvite
        {
            _mimeHeaders = mimeHeaders,
            _fields = ParseFields(text[(separator + 4)..]),
        };

        return true;
    }

    public string ApplicationGuid => Field("Application-GUID");

    public string Command => Field("Invitation-Command");

    public string Cookie => Field("Invitation-Cookie");

    public bool IsFileTransfer => string.Equals(ApplicationGuid?.Trim(), FileTransferGuid, StringComparison.OrdinalIgnoreCase);

    /// <summary>The advertised file size from an INVITE, or 0 when absent/unparsable.</summary>
    public long FileSize => long.TryParse(Field("Application-FileSize"), out var size) && size >= 0 ? size : 0;

    /// <summary>Whether this invite actually carries the peer connectivity the relay rewrites.</summary>
    public bool HasConnectivity => Field("IP-Address") != null && Field("Port") != null && Field("AuthCookie") != null;

    public string Field(string name)
    {
        return Get(_fields, name);
    }

    /// <summary>Sets an existing field in place (preserving order) or appends it. Used only by the rewrite.</summary>
    public void SetField(string name, string value)
    {
        for (var i = 0; i < _fields.Count; i++)
        {
            if (string.Equals(_fields[i].Key, name, StringComparison.OrdinalIgnoreCase))
            {
                _fields[i] = new KeyValuePair<string, string>(_fields[i].Key, value);

                return;
            }
        }

        _fields.Add(new KeyValuePair<string, string>(name, value));
    }

    /// <summary>Re-serialises to the on-wire MIME shape: the MIME headers, a blank line, the invite fields,
    /// and a trailing blank line - the layout a period client parses.</summary>
    public byte[] Serialize()
    {
        var builder = new StringBuilder();

        foreach (var header in _mimeHeaders)
        {
            builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }

        builder.Append("\r\n");

        foreach (var field in _fields)
        {
            builder.Append(field.Key).Append(": ").Append(field.Value).Append("\r\n");
        }

        builder.Append("\r\n");

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    static List<KeyValuePair<string, string>> ParseFields(string text)
    {
        var fields = new List<KeyValuePair<string, string>>();

        foreach (var line in text.Split("\r\n"))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':');

            if (colon < 0)
            {
                continue;
            }

            fields.Add(new KeyValuePair<string, string>(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }

        return fields;
    }

    static string Get(List<KeyValuePair<string, string>> fields, string name)
    {
        foreach (var field in fields)
        {
            if (string.Equals(field.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return field.Value;
            }
        }

        return null;
    }
}
