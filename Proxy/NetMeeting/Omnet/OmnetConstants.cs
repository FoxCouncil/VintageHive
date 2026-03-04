// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.Omnet;

/// <summary>
/// NetMeeting Object Manager (OMNET) constants.
///
/// OMNET manages shared data objects (worksets) over MCS channels.
/// Used by whiteboard (T.126), file transfer (T.127), and application sharing.
///
/// All OMNET messages ride inside MCS SendData PDUs on the T.120 stack:
///   TCP:1503 → TPKT → X.224 → MCS SendData → OMNET message
///
/// All multi-byte integer fields are little-endian per MS-MNPR.
/// </summary>
internal static class OmnetConstants
{
    // ──────────────────────────────────────────────────────────
    //  Message types (2-byte LE values)
    // ──────────────────────────────────────────────────────────

    // Joiner messages
    public const ushort MSG_HELLO = 0x000A;
    public const ushort MSG_WELCOME = 0x000B;

    // Lock messages
    public const ushort MSG_LOCK_REQ = 0x0015;
    public const ushort MSG_LOCK_GRANT = 0x0016;
    public const ushort MSG_LOCK_DENY = 0x0017;
    public const ushort MSG_UNLOCK = 0x0018;
    public const ushort MSG_LOCK_NOTIFY = 0x0019;

    // Workset group send messages
    public const ushort MSG_WSGROUP_SEND_REQ = 0x001E;
    public const ushort MSG_WSGROUP_SEND_MIDWAY = 0x001F;
    public const ushort MSG_WSGROUP_SEND_COMPLETE = 0x0020;
    public const ushort MSG_WSGROUP_SEND_DENY = 0x0021;

    // Operation messages
    public const ushort MSG_WORKSET_CLEAR = 0x0028;
    public const ushort MSG_WORKSET_NEW = 0x0029;
    public const ushort MSG_WORKSET_CATCHUP = 0x0030;
    public const ushort MSG_OBJECT_ADD = 0x0032;
    public const ushort MSG_OBJECT_CATCHUP = 0x0033;
    public const ushort MSG_OBJECT_REPLACE = 0x0034;
    public const ushort MSG_OBJECT_UPDATE = 0x0035;
    public const ushort MSG_OBJECT_DELETE = 0x0036;
    public const ushort MSG_OBJECT_MOVE = 0x0037;
    public const ushort MSG_MORE_DATA = 0x0046;

    // ──────────────────────────────────────────────────────────
    //  Compression capabilities (bitwise OR in HELLO/WELCOME)
    // ──────────────────────────────────────────────────────────

    public const uint CAPS_PKW_COMPRESSION = 0x00000002;
    public const uint CAPS_NO_COMPRESSION = 0x00000004;

    /// <summary>Standard caps length in HELLO/WELCOME messages.</summary>
    public const uint CAPS_LENGTH = 0x00000004;

    // ──────────────────────────────────────────────────────────
    //  Object position constants
    // ──────────────────────────────────────────────────────────

    public const byte POSITION_LAST = 0x01;
    public const byte POSITION_FIRST = 0x02;

    // ──────────────────────────────────────────────────────────
    //  WSGROUP_INFO magic stamp
    // ──────────────────────────────────────────────────────────

    /// <summary>"OMWI" as a little-endian uint32 (0x4F=O, 0x4D=M, 0x57=W, 0x49=I).</summary>
    public const uint WSGROUP_INFO_STAMP = 0x49574D4F;

    /// <summary>Fixed size of WSGROUP_INFO (excluding the 4-byte length prefix).</summary>
    public const int WSGROUP_INFO_SIZE = 60;

    // ──────────────────────────────────────────────────────────
    //  Fixed message sizes
    // ──────────────────────────────────────────────────────────

    /// <summary>Joiner messages (HELLO, WELCOME): 12 bytes.</summary>
    public const int JOINER_SIZE = 12;

    /// <summary>Lock messages: 12 bytes.</summary>
    public const int LOCK_SIZE = 12;

    /// <summary>Workset group send messages: 20 bytes.</summary>
    public const int WSGROUP_SEND_SIZE = 20;

    /// <summary>Common operation header: 24 bytes.</summary>
    public const int OPERATION_HEADER_SIZE = 24;

    /// <summary>WORKSET_CLEAR: 16 bytes (truncated op header).</summary>
    public const int WORKSET_CLEAR_SIZE = 16;

    /// <summary>OBJECT_ADD header: 36 bytes (op header + 3 size fields).</summary>
    public const int OBJECT_ADD_HEADER_SIZE = 36;

    /// <summary>OBJECT_REPLACE/UPDATE header: 32 bytes.</summary>
    public const int OBJECT_REPLACE_HEADER_SIZE = 32;

    /// <summary>OBJECT_CATCHUP header: 60 bytes.</summary>
    public const int OBJECT_CATCHUP_HEADER_SIZE = 60;

    /// <summary>MORE_DATA header: 8 bytes.</summary>
    public const int MORE_DATA_HEADER_SIZE = 8;

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>Return a friendly name for an OMNET message type.</summary>
    public static string MessageName(ushort type)
    {
        return type switch
        {
            MSG_HELLO => "HELLO",
            MSG_WELCOME => "WELCOME",
            MSG_LOCK_REQ => "LOCK_REQ",
            MSG_LOCK_GRANT => "LOCK_GRANT",
            MSG_LOCK_DENY => "LOCK_DENY",
            MSG_UNLOCK => "UNLOCK",
            MSG_LOCK_NOTIFY => "LOCK_NOTIFY",
            MSG_WSGROUP_SEND_REQ => "WSGROUP_SEND_REQ",
            MSG_WSGROUP_SEND_MIDWAY => "WSGROUP_SEND_MIDWAY",
            MSG_WSGROUP_SEND_COMPLETE => "WSGROUP_SEND_COMPLETE",
            MSG_WSGROUP_SEND_DENY => "WSGROUP_SEND_DENY",
            MSG_WORKSET_CLEAR => "WORKSET_CLEAR",
            MSG_WORKSET_NEW => "WORKSET_NEW",
            MSG_WORKSET_CATCHUP => "WORKSET_CATCHUP",
            MSG_OBJECT_ADD => "OBJECT_ADD",
            MSG_OBJECT_CATCHUP => "OBJECT_CATCHUP",
            MSG_OBJECT_REPLACE => "OBJECT_REPLACE",
            MSG_OBJECT_UPDATE => "OBJECT_UPDATE",
            MSG_OBJECT_DELETE => "OBJECT_DELETE",
            MSG_OBJECT_MOVE => "OBJECT_MOVE",
            MSG_MORE_DATA => "MORE_DATA",
            _ => $"OMNET(0x{type:X4})"
        };
    }

    /// <summary>Check if a message type is a joiner message.</summary>
    public static bool IsJoinerMessage(ushort type)
    {
        return type == MSG_HELLO || type == MSG_WELCOME;
    }

    /// <summary>Check if a message type is a lock message.</summary>
    public static bool IsLockMessage(ushort type)
    {
        return type >= MSG_LOCK_REQ && type <= MSG_LOCK_NOTIFY;
    }

    /// <summary>Check if a message type is a workset group send message.</summary>
    public static bool IsWsGroupSendMessage(ushort type)
    {
        return type >= MSG_WSGROUP_SEND_REQ && type <= MSG_WSGROUP_SEND_DENY;
    }

    /// <summary>Check if a message type carries variable-length object data.</summary>
    public static bool HasObjectData(ushort type)
    {
        return type == MSG_OBJECT_ADD ||
               type == MSG_OBJECT_CATCHUP ||
               type == MSG_OBJECT_REPLACE ||
               type == MSG_OBJECT_UPDATE ||
               type == MSG_MORE_DATA;
    }
}
