// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.T120;

/// <summary>
/// X.224 (ISO 8073) Class 0 transport PDU parser/builder.
/// Used by T.120 data conferencing over TCP port 1503.
///
/// PDU types:
///   Connection Request (CR)  = 0xE0
///   Connection Confirm (CC)  = 0xD0
///   Data Transfer (DT)       = 0xF0
///   Disconnect Request (DR)  = 0x80
///
/// Class 0 header format:
///   Byte 0: Length Indicator (LI) — length of header excluding LI byte
///   Byte 1: PDU type (upper nibble)
///   Bytes 2+: type-specific fields
/// </summary>
internal class X224Message
{
    // ──────────────────────────────────────────────────────────
    //  PDU type constants (upper nibble of byte 1)
    // ──────────────────────────────────────────────────────────

    public const byte TYPE_CR = 0xE0;
    public const byte TYPE_CC = 0xD0;
    public const byte TYPE_DT = 0xF0;
    public const byte TYPE_DR = 0x80;

    /// <summary>PDU type byte.</summary>
    public byte Type { get; init; }

    /// <summary>Destination reference (CR/CC/DR).</summary>
    public ushort DstRef { get; init; }

    /// <summary>Source reference (CR/CC/DR).</summary>
    public ushort SrcRef { get; init; }

    /// <summary>Class and options byte (CR/CC). Class 0 = 0x00.</summary>
    public byte ClassOptions { get; init; }

    /// <summary>EOT flag for DT PDU (bit 7 of byte 2).</summary>
    public bool Eot { get; init; }

    /// <summary>Variable parameters (CR/CC) or user data (DT).</summary>
    public byte[] Data { get; init; }

    // ──────────────────────────────────────────────────────────
    //  Parse
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Parse an X.224 PDU from a TPKT payload.
    /// </summary>
    public static X224Message Parse(byte[] payload)
    {
        if (payload == null || payload.Length < 2)
        {
            throw new ArgumentException("X.224 payload too short");
        }

        var li = payload[0];
        var type = (byte)(payload[1] & 0xF0);

        switch (type)
        {
            case TYPE_CR:
            case TYPE_CC:
            {
                // CR/CC: LI, type, DST-REF (2), SRC-REF (2), class/options
                if (li < 6 || payload.Length < 7)
                {
                    throw new ArgumentException($"X.224 CR/CC too short: LI={li}, len={payload.Length}");
                }

                var dstRef = (ushort)((payload[2] << 8) | payload[3]);
                var srcRef = (ushort)((payload[4] << 8) | payload[5]);
                var classOpts = payload[6];

                // Variable parameters are inside the header (bytes 7..LI)
                byte[] varParams = null;
                var varParamLen = li - 6; // LI counts 6 fixed bytes + variable params

                if (varParamLen > 0 && payload.Length >= 7 + varParamLen)
                {
                    varParams = new byte[varParamLen];
                    Array.Copy(payload, 7, varParams, 0, varParamLen);
                }

                return new X224Message
                {
                    Type = type,
                    DstRef = dstRef,
                    SrcRef = srcRef,
                    ClassOptions = classOpts,
                    Data = varParams
                };
            }

            case TYPE_DT:
            {
                // DT: LI (=2), type (0xF0), EOT|NR
                if (payload.Length < 3)
                {
                    throw new ArgumentException("X.224 DT too short");
                }

                var eot = (payload[2] & 0x80) != 0;

                // User data follows the header
                var headerEnd = 1 + li;
                byte[] userData = null;

                if (payload.Length > headerEnd)
                {
                    userData = new byte[payload.Length - headerEnd];
                    Array.Copy(payload, headerEnd, userData, 0, userData.Length);
                }

                return new X224Message
                {
                    Type = TYPE_DT,
                    Eot = eot,
                    Data = userData
                };
            }

            case TYPE_DR:
            {
                // DR: LI, type, DST-REF (2), SRC-REF (2), reason
                if (payload.Length < 7)
                {
                    throw new ArgumentException("X.224 DR too short");
                }

                var dstRef = (ushort)((payload[2] << 8) | payload[3]);
                var srcRef = (ushort)((payload[4] << 8) | payload[5]);

                return new X224Message
                {
                    Type = TYPE_DR,
                    DstRef = dstRef,
                    SrcRef = srcRef,
                    ClassOptions = payload.Length > 6 ? payload[6] : (byte)0
                };
            }

            default:
            {
                throw new NotSupportedException($"Unknown X.224 PDU type: 0x{type:X2}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Build
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Build a Connection Request PDU.
    /// </summary>
    public static byte[] BuildConnectionRequest(ushort srcRef = 0, byte classOptions = 0x00,
        byte[] cookie = null)
    {
        var cookieLen = cookie?.Length ?? 0;
        var li = 6 + cookieLen; // fixed header = 6 bytes after LI
        var pdu = new byte[1 + li];

        pdu[0] = (byte)li;
        pdu[1] = TYPE_CR;
        pdu[2] = 0; pdu[3] = 0; // DST-REF = 0
        pdu[4] = (byte)(srcRef >> 8); pdu[5] = (byte)(srcRef & 0xFF);
        pdu[6] = classOptions;

        if (cookie != null)
        {
            Array.Copy(cookie, 0, pdu, 7, cookie.Length);
        }

        return pdu;
    }

    /// <summary>
    /// Build a Connection Confirm PDU.
    /// </summary>
    public static byte[] BuildConnectionConfirm(ushort dstRef = 0, ushort srcRef = 0,
        byte classOptions = 0x00)
    {
        var pdu = new byte[7];

        pdu[0] = 6; // LI = 6
        pdu[1] = TYPE_CC;
        pdu[2] = (byte)(dstRef >> 8); pdu[3] = (byte)(dstRef & 0xFF);
        pdu[4] = (byte)(srcRef >> 8); pdu[5] = (byte)(srcRef & 0xFF);
        pdu[6] = classOptions;

        return pdu;
    }

    /// <summary>
    /// Build a Data Transfer PDU wrapping user data.
    /// </summary>
    public static byte[] BuildDataTransfer(byte[] userData, bool eot = true)
    {
        var dataLen = userData?.Length ?? 0;
        var pdu = new byte[3 + dataLen];

        pdu[0] = 2; // LI = 2 (type + EOT/NR)
        pdu[1] = TYPE_DT;
        pdu[2] = (byte)(eot ? 0x80 : 0x00); // EOT flag

        if (userData != null)
        {
            Array.Copy(userData, 0, pdu, 3, userData.Length);
        }

        return pdu;
    }

    /// <summary>
    /// Build a Disconnect Request PDU.
    /// </summary>
    public static byte[] BuildDisconnectRequest(ushort dstRef = 0, ushort srcRef = 0,
        byte reason = 0)
    {
        var pdu = new byte[7];

        pdu[0] = 6;
        pdu[1] = TYPE_DR;
        pdu[2] = (byte)(dstRef >> 8); pdu[3] = (byte)(dstRef & 0xFF);
        pdu[4] = (byte)(srcRef >> 8); pdu[5] = (byte)(srcRef & 0xFF);
        pdu[6] = reason;

        return pdu;
    }

    /// <summary>Return a friendly type name.</summary>
    public static string TypeName(byte type)
    {
        return type switch
        {
            TYPE_CR => "ConnectionRequest",
            TYPE_CC => "ConnectionConfirm",
            TYPE_DT => "DataTransfer",
            TYPE_DR => "DisconnectRequest",
            _ => $"X224(0x{type:X2})"
        };
    }
}
