// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;

namespace VintageHive.Proxy.Yahoo.Pager;

/// <summary>
/// The pre-YMSG Pager wire packet: a fixed 104-byte little-endian header followed by NUL-terminated content.
/// Nothing like YMSG's key/value body - this generation is entirely positional.
///
/// <code>
/// offset  0  char version[8]        "YPNS2.0\0" from the client, "YHOO2.0\0" from the server
/// offset  8  u32  len               total packet length including the header
/// offset 12  u32  service
/// offset 16  u32  connection_id
/// offset 20  u32  magic_id          the HTTP session handle
/// offset 24  u32  unknown1
/// offset 28  u32  msgtype
/// offset 32  char nick1[36]         the signed-on identity
/// offset 68  char nick2[36]         the active identity
/// offset 104 char content[]         NUL-terminated
/// </code>
///
/// Written against libyahoo 0.18.4 (<c>yahoo_sendcmd</c>, <c>yahoo_getpacket</c>), the GPL client library
/// descended from the yppro2.c prototype pager code.
/// </summary>
public sealed class YpnsPacket
{
    public const int HeaderSize = 104;

    const int VersionSize = 8;

    const int NickSize = 36;

    const int VersionOffset = 0;
    const int LengthOffset = 8;
    const int ServiceOffset = 12;
    const int ConnectionIdOffset = 16;
    const int MagicIdOffset = 20;
    const int Unknown1Offset = 24;
    const int MsgTypeOffset = 28;
    const int Nick1Offset = 32;
    const int Nick2Offset = 68;

    /// <summary>
    /// What clients write. Accepted but not required: the server does not gate on it, because a client whose
    /// dialect differs is exactly the case there is no capture for. Flip here for a YPNS1.x client.
    /// </summary>
    public const string ClientVersion = "YPNS2.0";

    /// <summary>
    /// What the server writes. The reference client resyncs its read buffer by scanning for the literal
    /// four bytes "YHOO" and never looks at the rest, so only <see cref="ServerMagic"/> is load-bearing -
    /// but the full string is one constant so a client that does check it is a one-line change.
    /// </summary>
    public const string ServerVersion = "YHOO2.0";

    /// <summary>The four bytes a client resynchronises on. Changing this breaks every client.</summary>
    public const string ServerMagic = "YHOO";

    // Period Yahoo! predates any UTF-8 flag on this protocol (YMSG grew one later as field 97). A period
    // client renders bytes in its own codepage, so text goes out as Latin-1 rather than UTF-8, where a
    // multi-byte sequence would show up as mojibake.
    internal static readonly Encoding TextEncoding = Encoding.Latin1;

    public uint Service { get; set; }

    public uint ConnectionId { get; set; }

    public uint MagicId { get; set; }

    public uint Unknown1 { get; set; }

    public uint MsgType { get; set; }

    /// <summary>The signed-on identity (nick1).</summary>
    public string RealId { get; set; } = string.Empty;

    /// <summary>The active identity (nick2).</summary>
    public string ActiveId { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Version { get; private set; } = string.Empty;

    public YpnsPacket() { }

    public YpnsPacket(uint service, string realId, string activeId, string content, uint msgType = 0)
    {
        Service = service;
        RealId = realId ?? string.Empty;
        ActiveId = activeId ?? string.Empty;
        Content = content ?? string.Empty;
        MsgType = msgType;
    }

    /// <summary>
    /// Decodes one packet from a request body, or null when the body is too short to be one.
    /// <para>
    /// THE LENGTH FIELD IS NOT TRUSTED, on purpose. The reference client hardwires <c>len</c> to 1128 on every
    /// packet it builds (its own comment: "why the )&amp;*@#$( did they hardwire the packet size") but then
    /// writes only <c>104 + strlen(content) + 1</c> bytes to the socket and sets Content-Length to that. So on
    /// this transport the declared length routinely overruns the body that actually arrived. The content extent
    /// comes from the real body length instead, which is correct for that client and for one that fills the
    /// field in honestly.
    /// </para>
    /// </summary>
    public static YpnsPacket Decode(ReadOnlySpan<byte> body)
    {
        if (body.Length < HeaderSize)
        {
            return null;
        }

        var packet = new YpnsPacket
        {
            Version = ReadFixedString(body[VersionOffset..(VersionOffset + VersionSize)]),
            Service = BinaryPrimitives.ReadUInt32LittleEndian(body[ServiceOffset..]),
            ConnectionId = BinaryPrimitives.ReadUInt32LittleEndian(body[ConnectionIdOffset..]),
            MagicId = BinaryPrimitives.ReadUInt32LittleEndian(body[MagicIdOffset..]),
            Unknown1 = BinaryPrimitives.ReadUInt32LittleEndian(body[Unknown1Offset..]),
            MsgType = BinaryPrimitives.ReadUInt32LittleEndian(body[MsgTypeOffset..]),
            RealId = ReadFixedString(body[Nick1Offset..(Nick1Offset + NickSize)]),
            ActiveId = ReadFixedString(body[Nick2Offset..(Nick2Offset + NickSize)]),
            Content = ReadFixedString(body[HeaderSize..]),
        };

        return packet;
    }

    public byte[] Encode()
    {
        var content = TextEncoding.GetBytes(Content ?? string.Empty);

        // The trailing NUL is inside the declared length: the client computes contentlen as len - HeaderSize
        // and then reads the content as a C string, so the terminator has to be accounted for.
        var buffer = new byte[HeaderSize + content.Length + 1];

        WriteFixedString(buffer.AsSpan(VersionOffset, VersionSize), ServerVersion);

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(LengthOffset), (uint)buffer.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(ServiceOffset), Service);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(ConnectionIdOffset), ConnectionId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(MagicIdOffset), MagicId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(Unknown1Offset), Unknown1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(MsgTypeOffset), MsgType);

        WriteFixedString(buffer.AsSpan(Nick1Offset, NickSize), RealId);
        WriteFixedString(buffer.AsSpan(Nick2Offset, NickSize), ActiveId);

        content.CopyTo(buffer.AsSpan(HeaderSize));

        return buffer;
    }

    // Fixed-width, NUL-padded, and truncated to fit rather than overflowing the field. A username longer than
    // the field is a malformed packet, not a reason to write past it.
    static void WriteFixedString(Span<byte> destination, string value)
    {
        destination.Clear();

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var bytes = TextEncoding.GetBytes(value);

        var length = Math.Min(bytes.Length, destination.Length - 1);

        bytes.AsSpan(0, length).CopyTo(destination);
    }

    static string ReadFixedString(ReadOnlySpan<byte> source)
    {
        var terminator = source.IndexOf((byte)0);

        if (terminator >= 0)
        {
            source = source[..terminator];
        }

        return source.IsEmpty ? string.Empty : TextEncoding.GetString(source);
    }
}
