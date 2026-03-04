// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.AppSharing;

/// <summary>
/// Microsoft NetMeeting Application Sharing (S20) protocol constants.
///
/// S20 packets ride directly on MCS SendData (T.125), little-endian binary.
/// Transport: TCP:1503 → TPKT → X.224 → MCS SendData → S20 packet
///
/// S20 is separate from OMNET — it uses its own MCS channels for
/// screen sharing, remote control, and application sharing.
///
/// The protocol has 8 packet types (Version/Type field in each header):
///   - Control packets (CREATE..COLLISION) carry a leading length field.
///   - S20_DATA carries application sharing payloads with their own sub-header.
/// </summary>
internal static class AppSharingConstants
{
    // ──────────────────────────────────────────────────────────
    //  S20 packet Version/Type values (uint16 LE)
    // ──────────────────────────────────────────────────────────

    public const ushort S20_CREATE = 0x0031;
    public const ushort S20_JOIN = 0x0032;
    public const ushort S20_RESPOND = 0x0033;
    public const ushort S20_DELETE = 0x0034;
    public const ushort S20_LEAVE = 0x0035;
    public const ushort S20_END = 0x0036;
    public const ushort S20_DATA = 0x0037;
    public const ushort S20_COLLISION = 0x0038;

    /// <summary>Number of defined S20 packet types.</summary>
    public const int S20_TYPE_COUNT = 8;

    // ──────────────────────────────────────────────────────────
    //  S20_DATA stream types
    // ──────────────────────────────────────────────────────────

    /// <summary>Updates stream — screen updates, bitmap data.</summary>
    public const byte STREAM_UPDATES = 0x01;

    /// <summary>Miscellaneous stream — control, sync, etc.</summary>
    public const byte STREAM_MISC = 0x02;

    /// <summary>Input stream — mouse/keyboard events.</summary>
    public const byte STREAM_INPUT = 0x03;

    // ──────────────────────────────────────────────────────────
    //  S20_DATA datatype values
    // ──────────────────────────────────────────────────────────

    /// <summary>Update PDU — screen/bitmap updates.</summary>
    public const byte DT_UP = 0x02;

    /// <summary>Font Handler PDU — font list exchange.</summary>
    public const byte DT_FH = 0x03;

    /// <summary>Confirm Active PDU — capabilities acknowledgement.</summary>
    public const byte DT_CA = 0x07;

    /// <summary>Hosting Entity Tracker — host session management.</summary>
    public const byte DT_HET = 0x0A;

    /// <summary>Shared Window List — window/app sharing list.</summary>
    public const byte DT_SWL = 0x0B;

    /// <summary>Application Viewer — remote viewer control.</summary>
    public const byte DT_AV = 0x0C;

    /// <summary>Cursor Manager — shared cursor updates.</summary>
    public const byte DT_CM = 0x14;

    /// <summary>Bitmap Cache — persistent cache management.</summary>
    public const byte DT_BC = 0x15;

    /// <summary>Synchronize PDU.</summary>
    public const byte DT_SYNC = 0x1F;

    /// <summary>Control PDU — cooperation, detach, grant.</summary>
    public const byte DT_CTRL = 0x20;

    /// <summary>Input PDU — mouse and keyboard events.</summary>
    public const byte DT_INPUT = 0x2C;

    /// <summary>Demand Active PDU — capabilities exchange.</summary>
    public const byte DT_DA = 0x31;

    // ──────────────────────────────────────────────────────────
    //  S20_DATA compression types
    // ──────────────────────────────────────────────────────────

    /// <summary>No compression.</summary>
    public const byte COMPRESS_NONE = 0x00;

    /// <summary>PKZIP/deflate compression.</summary>
    public const byte COMPRESS_PKZIP = 0x01;

    /// <summary>Persistent dictionary compression.</summary>
    public const byte COMPRESS_PERSIST = 0x02;

    // ──────────────────────────────────────────────────────────
    //  CPCALLCAPS capability set identifiers
    // ──────────────────────────────────────────────────────────

    public const ushort CAPS_GENERAL = 0x0001;
    public const ushort CAPS_BITMAP = 0x0002;
    public const ushort CAPS_ORDER = 0x0003;
    public const ushort CAPS_BMPCACHE = 0x0004;
    public const ushort CAPS_CONTROL = 0x0005;
    public const ushort CAPS_ACTIVATION = 0x0007;
    public const ushort CAPS_POINTER = 0x0008;

    /// <summary>Number of capability sets in a standard CPCALLCAPS exchange.</summary>
    public const int CAPS_SET_COUNT = 7;

    /// <summary>Total size of a CPCALLCAPS structure in bytes.</summary>
    public const int CPCALLCAPS_SIZE = 204;

    // ──────────────────────────────────────────────────────────
    //  S20 session states
    // ──────────────────────────────────────────────────────────

    /// <summary>Initial state — no sharing session exists.</summary>
    public const int STATE_IDLE = 0;

    /// <summary>CREATE sent, awaiting RESPOND from peers.</summary>
    public const int STATE_PENDING = 1;

    /// <summary>Active sharing session established.</summary>
    public const int STATE_SHARING = 2;

    /// <summary>Session is being torn down.</summary>
    public const int STATE_ENDING = 3;

    // ──────────────────────────────────────────────────────────
    //  S20_RESPOND result codes
    // ──────────────────────────────────────────────────────────

    /// <summary>Join request accepted.</summary>
    public const uint RESPOND_POSITIVE = 0x00000001;

    /// <summary>Join request denied.</summary>
    public const uint RESPOND_NEGATIVE = 0x00000000;

    // ──────────────────────────────────────────────────────────
    //  Constraints
    // ──────────────────────────────────────────────────────────

    /// <summary>Minimum S20 control packet size (length + version/type).</summary>
    public const int MIN_CONTROL_PACKET = 4;

    /// <summary>Minimum S20_DATA header size (no payload).</summary>
    public const int S20_DATA_HEADER_SIZE = 16;

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>Return a friendly name for an S20 packet type.</summary>
    public static string PacketTypeName(ushort type)
    {
        return type switch
        {
            S20_CREATE => "S20_CREATE",
            S20_JOIN => "S20_JOIN",
            S20_RESPOND => "S20_RESPOND",
            S20_DELETE => "S20_DELETE",
            S20_LEAVE => "S20_LEAVE",
            S20_END => "S20_END",
            S20_DATA => "S20_DATA",
            S20_COLLISION => "S20_COLLISION",
            _ => $"S20(0x{type:X4})"
        };
    }

    /// <summary>Return a friendly name for an S20_DATA datatype.</summary>
    public static string DatatypeName(byte dt)
    {
        return dt switch
        {
            DT_UP => "Update",
            DT_FH => "FontHandler",
            DT_CA => "ConfirmActive",
            DT_HET => "HostEntity",
            DT_SWL => "SharedWindowList",
            DT_AV => "AppViewer",
            DT_CM => "CursorManager",
            DT_BC => "BitmapCache",
            DT_SYNC => "Synchronize",
            DT_CTRL => "Control",
            DT_INPUT => "Input",
            DT_DA => "DemandActive",
            _ => $"DT(0x{dt:X2})"
        };
    }

    /// <summary>Return a friendly name for a stream type.</summary>
    public static string StreamName(byte stream)
    {
        return stream switch
        {
            STREAM_UPDATES => "Updates",
            STREAM_MISC => "Misc",
            STREAM_INPUT => "Input",
            _ => $"Stream(0x{stream:X2})"
        };
    }

    /// <summary>Return a friendly name for a capability set.</summary>
    public static string CapsName(ushort capsId)
    {
        return capsId switch
        {
            CAPS_GENERAL => "General",
            CAPS_BITMAP => "Bitmap",
            CAPS_ORDER => "Order",
            CAPS_BMPCACHE => "BitmapCache",
            CAPS_CONTROL => "Control",
            CAPS_ACTIVATION => "Activation",
            CAPS_POINTER => "Pointer",
            _ => $"Caps(0x{capsId:X4})"
        };
    }

    /// <summary>True if the type value is a known S20 packet type.</summary>
    public static bool IsS20Packet(ushort type)
    {
        return type >= S20_CREATE && type <= S20_COLLISION;
    }

    /// <summary>True if the type is a control packet (has leading length field).</summary>
    public static bool IsControlPacket(ushort type)
    {
        return type >= S20_CREATE && type <= S20_COLLISION && type != S20_DATA;
    }
}
