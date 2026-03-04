// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.Whiteboard;

/// <summary>
/// T.126 Still Image Protocol (whiteboard) constants.
///
/// T.126 SIPDUs ride inside OMNET OBJECT_ADD/REPLACE/UPDATE data payloads,
/// PER-encoded (ASN.1 ALIGNED variant).
///
/// The top-level SIPDU is a CHOICE with 35 root alternatives.
/// </summary>
internal static class WhiteboardConstants
{
    // ──────────────────────────────────────────────────────────
    //  SIPDU CHOICE indices (root alternatives)
    // ──────────────────────────────────────────────────────────

    public const int SIPDU_ROOT_COUNT = 35;

    public const int SIPDU_ARCHIVE_ACKNOWLEDGE = 0;
    public const int SIPDU_ARCHIVE_CLOSE = 1;
    public const int SIPDU_ARCHIVE_ERROR = 2;
    public const int SIPDU_ARCHIVE_OPEN = 3;
    public const int SIPDU_BITMAP_ABORT = 4;
    public const int SIPDU_BITMAP_CHECKPOINT = 5;
    public const int SIPDU_BITMAP_CREATE = 6;
    public const int SIPDU_BITMAP_CREATE_CONTINUE = 7;
    public const int SIPDU_BITMAP_DELETE = 8;
    public const int SIPDU_BITMAP_EDIT = 9;
    public const int SIPDU_CONDUCTOR_PRIVILEGE_GRANT = 10;
    public const int SIPDU_CONDUCTOR_PRIVILEGE_REQUEST = 11;
    public const int SIPDU_DRAWING_CREATE = 12;
    public const int SIPDU_DRAWING_DELETE = 13;
    public const int SIPDU_DRAWING_EDIT = 14;
    public const int SIPDU_FONT = 15;
    public const int SIPDU_NON_STANDARD = 16;
    public const int SIPDU_REMOTE_EVENT_PERMISSION_GRANT = 17;
    public const int SIPDU_REMOTE_EVENT_PERMISSION_REQUEST = 18;
    public const int SIPDU_REMOTE_KEYBOARD_EVENT = 19;
    public const int SIPDU_REMOTE_POINTING_DEVICE_EVENT = 20;
    public const int SIPDU_REMOTE_PRINT = 21;
    public const int SIPDU_TEXT_CREATE = 22;
    public const int SIPDU_TEXT_DELETE = 23;
    public const int SIPDU_TEXT_EDIT = 24;
    public const int SIPDU_VIDEO_WINDOW_CREATE = 25;
    public const int SIPDU_VIDEO_WINDOW_DELETE = 26;
    public const int SIPDU_VIDEO_WINDOW_EDIT = 27;
    public const int SIPDU_WORKSPACE_CREATE = 28;
    public const int SIPDU_WORKSPACE_CREATE_ACKNOWLEDGE = 29;
    public const int SIPDU_WORKSPACE_DELETE = 30;
    public const int SIPDU_WORKSPACE_EDIT = 31;
    public const int SIPDU_WORKSPACE_PLANE_COPY = 32;
    public const int SIPDU_WORKSPACE_READY = 33;
    public const int SIPDU_WORKSPACE_REFRESH_STATUS = 34;

    // ──────────────────────────────────────────────────────────
    //  DrawingType CHOICE (5 root alternatives, extensible)
    // ──────────────────────────────────────────────────────────

    public const int DRAWING_TYPE_ROOT_COUNT = 6;

    public const int DRAWING_POINT = 0;
    public const int DRAWING_OPEN_POLYLINE = 1;
    public const int DRAWING_CLOSED_POLYLINE = 2;
    public const int DRAWING_RECTANGLE = 3;
    public const int DRAWING_ELLIPSE = 4;
    public const int DRAWING_NON_STANDARD = 5;

    // ──────────────────────────────────────────────────────────
    //  DrawingAttribute CHOICE (9 root alternatives, extensible)
    // ──────────────────────────────────────────────────────────

    public const int ATTR_ROOT_COUNT = 9;

    public const int ATTR_PEN_COLOR = 0;
    public const int ATTR_FILL_COLOR = 1;
    public const int ATTR_PEN_THICKNESS = 2;
    public const int ATTR_PEN_NIB = 3;
    public const int ATTR_LINE_STYLE = 4;
    public const int ATTR_HIGHLIGHT = 5;
    public const int ATTR_VIEW_STATE = 6;
    public const int ATTR_Z_ORDER = 7;
    public const int ATTR_NON_STANDARD = 8;

    // ──────────────────────────────────────────────────────────
    //  PenNib CHOICE (3 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int PEN_NIB_ROOT_COUNT = 3;
    public const int PEN_NIB_CIRCULAR = 0;
    public const int PEN_NIB_SQUARE = 1;
    public const int PEN_NIB_NON_STANDARD = 2;

    // ──────────────────────────────────────────────────────────
    //  LineStyle CHOICE (7 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int LINE_STYLE_ROOT_COUNT = 7;
    public const int LINE_STYLE_SOLID = 0;
    public const int LINE_STYLE_DASHED = 1;
    public const int LINE_STYLE_DOTTED = 2;
    public const int LINE_STYLE_DASH_DOT = 3;
    public const int LINE_STYLE_DASH_DOT_DOT = 4;
    public const int LINE_STYLE_TWO_TONE = 5;
    public const int LINE_STYLE_NON_STANDARD = 6;

    // ──────────────────────────────────────────────────────────
    //  PointList CHOICE (3 root alternatives)
    // ──────────────────────────────────────────────────────────

    public const int POINT_LIST_ROOT_COUNT = 3;
    public const int POINT_LIST_DIFF4 = 0;  // x,y: -8..7 (4 bits each)
    public const int POINT_LIST_DIFF8 = 1;  // x,y: -128..127 (8 bits each)
    public const int POINT_LIST_DIFF16 = 2; // x,y: -32768..32767 (16 bits each)

    // ──────────────────────────────────────────────────────────
    //  WorkspacePoint coordinate range
    // ──────────────────────────────────────────────────────────

    public const int COORD_MIN = -21845;
    public const int COORD_MAX = 43690;

    // ──────────────────────────────────────────────────────────
    //  ViewState enumeration (3 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int VIEW_STATE_ROOT_COUNT = 3;
    public const int VIEW_STATE_UNSELECTED = 0;
    public const int VIEW_STATE_SELECTED = 1;
    public const int VIEW_STATE_FOCUSED = 2;

    // ──────────────────────────────────────────────────────────
    //  WorkspaceColor CHOICE (3 root, extensible)
    // ──────────────────────────────────────────────────────────

    public const int COLOR_ROOT_COUNT = 3;
    public const int COLOR_PALETTE_INDEX = 0;
    public const int COLOR_RGB = 1;
    public const int COLOR_TRANSPARENT = 2;

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>Return a friendly name for a SIPDU CHOICE index.</summary>
    public static string SipduName(int index)
    {
        return index switch
        {
            SIPDU_DRAWING_CREATE => "DrawingCreate",
            SIPDU_DRAWING_DELETE => "DrawingDelete",
            SIPDU_DRAWING_EDIT => "DrawingEdit",
            SIPDU_BITMAP_CREATE => "BitmapCreate",
            SIPDU_BITMAP_CREATE_CONTINUE => "BitmapCreateContinue",
            SIPDU_BITMAP_DELETE => "BitmapDelete",
            SIPDU_BITMAP_EDIT => "BitmapEdit",
            SIPDU_TEXT_CREATE => "TextCreate",
            SIPDU_TEXT_DELETE => "TextDelete",
            SIPDU_TEXT_EDIT => "TextEdit",
            SIPDU_WORKSPACE_CREATE => "WorkspaceCreate",
            SIPDU_WORKSPACE_DELETE => "WorkspaceDelete",
            SIPDU_WORKSPACE_EDIT => "WorkspaceEdit",
            SIPDU_WORKSPACE_READY => "WorkspaceReady",
            SIPDU_NON_STANDARD => "NonStandard",
            SIPDU_FONT => "Font",
            _ => $"SIPDU({index})"
        };
    }

    /// <summary>Return a friendly name for a DrawingType CHOICE index.</summary>
    public static string DrawingTypeName(int index)
    {
        return index switch
        {
            DRAWING_POINT => "point",
            DRAWING_OPEN_POLYLINE => "openPolyLine",
            DRAWING_CLOSED_POLYLINE => "closedPolyLine",
            DRAWING_RECTANGLE => "rectangle",
            DRAWING_ELLIPSE => "ellipse",
            DRAWING_NON_STANDARD => "nonStandard",
            _ => $"DrawingType({index})"
        };
    }
}
