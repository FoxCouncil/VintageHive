// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.Whiteboard;

/// <summary>
/// A point in workspace coordinates.
/// Range: x,y ∈ [-21845, 43690]
/// </summary>
internal class WorkspacePoint
{
    public int X { get; init; }
    public int Y { get; init; }

    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// A relative point delta used in PointList arrays.
/// </summary>
internal class PointDelta
{
    public int Dx { get; init; }
    public int Dy { get; init; }

    public override string ToString() => $"Δ({Dx},{Dy})";
}

/// <summary>
/// Color value for pen/fill attributes.
/// </summary>
internal class WorkspaceColor
{
    /// <summary>Color type: 0=palette, 1=RGB, 2=transparent.</summary>
    public int Type { get; init; }

    /// <summary>Palette index (if Type == palette).</summary>
    public int PaletteIndex { get; init; }

    /// <summary>RGB components (if Type == RGB). Each 0-255.</summary>
    public byte R { get; init; }
    public byte G { get; init; }
    public byte B { get; init; }
}

/// <summary>
/// Drawing attribute (pen color, fill color, thickness, nib, line style, etc).
/// </summary>
internal class DrawingAttribute
{
    /// <summary>Attribute type (CHOICE index from WhiteboardConstants.ATTR_*).</summary>
    public int Type { get; init; }

    /// <summary>Color value (for ATTR_PEN_COLOR, ATTR_FILL_COLOR).</summary>
    public WorkspaceColor Color { get; init; }

    /// <summary>Pen thickness 1-255 (for ATTR_PEN_THICKNESS).</summary>
    public int PenThickness { get; init; }

    /// <summary>Pen nib type (for ATTR_PEN_NIB): 0=circular, 1=square.</summary>
    public int PenNib { get; init; }

    /// <summary>Line style (for ATTR_LINE_STYLE): 0=solid, etc.</summary>
    public int LineStyle { get; init; }

    /// <summary>Highlight flag (for ATTR_HIGHLIGHT).</summary>
    public bool Highlight { get; init; }

    /// <summary>View state (for ATTR_VIEW_STATE): 0=unselected, 1=selected, 2=focused.</summary>
    public int ViewState { get; init; }

    /// <summary>Z-order value (for ATTR_Z_ORDER).</summary>
    public int ZOrder { get; init; }
}

/// <summary>
/// T.126 DrawingCreatePDU — creates a drawing object on the whiteboard.
/// </summary>
internal class DrawingCreatePdu
{
    /// <summary>Optional object handle (32-bit).</summary>
    public uint? Handle { get; init; }

    /// <summary>Workspace handle for destination.</summary>
    public uint WorkspaceHandle { get; init; }

    /// <summary>Plane index within the workspace.</summary>
    public int PlaneIndex { get; init; }

    /// <summary>Drawing type: point, openPolyLine, closedPolyLine, rectangle, ellipse.</summary>
    public int DrawingType { get; init; }

    /// <summary>Drawing attributes (pen color, thickness, etc).</summary>
    public DrawingAttribute[] Attributes { get; init; }

    /// <summary>Anchor point in workspace coordinates.</summary>
    public WorkspacePoint AnchorPoint { get; init; }

    /// <summary>Point deltas relative to anchor (or previous point).</summary>
    public PointDelta[] Points { get; init; }

    /// <summary>Point encoding precision: 0=Diff4, 1=Diff8, 2=Diff16.</summary>
    public int PointListType { get; init; }
}

/// <summary>
/// T.126 DrawingDeletePDU — deletes a drawing object.
/// </summary>
internal class DrawingDeletePdu
{
    public uint Handle { get; init; }
}

/// <summary>
/// T.126 DrawingEditPDU — modifies an existing drawing object.
/// </summary>
internal class DrawingEditPdu
{
    public uint Handle { get; init; }
    public DrawingAttribute[] AttributeEdits { get; init; }
    public WorkspacePoint AnchorPointEdit { get; init; }
    public PointDelta[] PointListEdits { get; init; }
    public int? PointListType { get; init; }
}

/// <summary>
/// T.126 WorkspaceCreatePDU — creates a new workspace (whiteboard page).
/// </summary>
internal class WorkspaceCreatePdu
{
    public uint WorkspaceHandle { get; init; }
    public int AppRosterInstance { get; init; }
    public bool Synchronized { get; init; }
    public bool AcceptKeyboardEvents { get; init; }
    public bool AcceptPointingDeviceEvents { get; init; }
    public int WorkspaceWidth { get; init; }
    public int WorkspaceHeight { get; init; }
}

/// <summary>
/// Decoded SIPDU envelope.
/// </summary>
internal class SipduMessage
{
    /// <summary>SIPDU CHOICE index.</summary>
    public int Type { get; init; }

    /// <summary>DrawingCreatePDU (if Type == SIPDU_DRAWING_CREATE).</summary>
    public DrawingCreatePdu DrawingCreate { get; init; }

    /// <summary>DrawingDeletePDU (if Type == SIPDU_DRAWING_DELETE).</summary>
    public DrawingDeletePdu DrawingDelete { get; init; }

    /// <summary>DrawingEditPDU (if Type == SIPDU_DRAWING_EDIT).</summary>
    public DrawingEditPdu DrawingEdit { get; init; }

    /// <summary>WorkspaceCreatePDU (if Type == SIPDU_WORKSPACE_CREATE).</summary>
    public WorkspaceCreatePdu WorkspaceCreate { get; init; }

    /// <summary>Raw bytes for SIPDU types not fully decoded.</summary>
    public byte[] RawData { get; init; }

    public override string ToString() => WhiteboardConstants.SipduName(Type);
}

/// <summary>
/// T.126 SIPDU PER codec — encodes and decodes whiteboard drawing primitives.
///
/// Uses ASN.1 ALIGNED PER (ITU-T X.691) encoding via the existing
/// PerEncoder/PerDecoder infrastructure.
/// </summary>
internal static class WhiteboardCodec
{
    // ──────────────────────────────────────────────────────────
    //  Detection
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Peek at the SIPDU CHOICE index from PER-encoded data.
    /// Returns -1 if the data is too short or invalid.
    /// </summary>
    public static int PeekSipduType(byte[] data)
    {
        if (data == null || data.Length < 1)
        {
            return -1;
        }

        try
        {
            var dec = new PerDecoder(data);
            var hasExtensions = dec.ReadExtensionBit();

            if (!hasExtensions)
            {
                // Root CHOICE: constrained to 0..34
                return (int)dec.ReadConstrainedWholeNumber(0, WhiteboardConstants.SIPDU_ROOT_COUNT - 1);
            }

            // Extension — we don't handle these yet
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Encode
    // ──────────────────────────────────────────────────────────

    /// <summary>Encode a DrawingCreatePDU as a T.126 SIPDU.</summary>
    public static byte[] EncodeDrawingCreate(DrawingCreatePdu pdu)
    {
        var enc = new PerEncoder();

        // SIPDU CHOICE: extensible, 35 root alternatives
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(WhiteboardConstants.SIPDU_DRAWING_CREATE, WhiteboardConstants.SIPDU_ROOT_COUNT, extensible: false, isExtension: false);

        // DrawingCreatePDU SEQUENCE (extensible, with optional fields)
        enc.WriteExtensionBit(false);

        // Optional bitmap: handle, attributes, rotation, sampleRate, nonStandardParameters
        var hasHandle = pdu.Handle.HasValue;
        var hasAttrs = pdu.Attributes != null && pdu.Attributes.Length > 0;
        enc.WriteOptionalBitmap(hasHandle, hasAttrs, false, false, false);

        // drawingHandle OPTIONAL
        if (hasHandle)
        {
            // Handle ::= INTEGER(0..4294967295) — unconstrained in practice, use 32 bits
            enc.WriteConstrainedWholeNumber(pdu.Handle.Value, 0, uint.MaxValue);
        }

        // destinationAddress: DrawingDestinationAddress CHOICE
        // softCopyAnnotationPlane SEQUENCE { workspaceHandle, plane }
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(1, 2, extensible: false, isExtension: false); // softCopyAnnotationPlane

        // workspaceHandle
        enc.WriteConstrainedWholeNumber(pdu.WorkspaceHandle, 0, uint.MaxValue);

        // plane (INTEGER 0..255)
        enc.WriteConstrainedWholeNumber(pdu.PlaneIndex, 0, 255);

        // drawingType CHOICE (extensible, 6 root)
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(pdu.DrawingType, WhiteboardConstants.DRAWING_TYPE_ROOT_COUNT, extensible: false, isExtension: false);
        // Most drawing types are NULL — no additional data needed

        // attributes OPTIONAL SET OF DrawingAttribute
        if (hasAttrs)
        {
            EncodeDrawingAttributes(enc, pdu.Attributes);
        }

        // anchorPoint: WorkspacePoint
        EncodeWorkspacePoint(enc, pdu.AnchorPoint);

        // pointList CHOICE (3 root)
        EncodePointList(enc, pdu.PointListType, pdu.Points);

        return enc.ToArray();
    }

    /// <summary>Encode a DrawingDeletePDU as a T.126 SIPDU.</summary>
    public static byte[] EncodeDrawingDelete(DrawingDeletePdu pdu)
    {
        var enc = new PerEncoder();

        // SIPDU CHOICE
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(WhiteboardConstants.SIPDU_DRAWING_DELETE, WhiteboardConstants.SIPDU_ROOT_COUNT, extensible: false, isExtension: false);

        // DrawingDeletePDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // Optional bitmap: nonStandardParameters only
        enc.WriteOptionalBitmap(false);

        // drawingHandle
        enc.WriteConstrainedWholeNumber(pdu.Handle, 0, uint.MaxValue);

        return enc.ToArray();
    }

    /// <summary>Encode a DrawingEditPDU as a T.126 SIPDU.</summary>
    public static byte[] EncodeDrawingEdit(DrawingEditPdu pdu)
    {
        var enc = new PerEncoder();

        // SIPDU CHOICE: extensible, 35 root alternatives
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(WhiteboardConstants.SIPDU_DRAWING_EDIT, WhiteboardConstants.SIPDU_ROOT_COUNT, extensible: false, isExtension: false);

        // DrawingEditPDU SEQUENCE (extensible, with optional fields)
        enc.WriteExtensionBit(false);

        // Optional bitmap: attributeEdits, anchorPointEdit, pointListEdits, nonStandardParameters
        var hasAttrs = pdu.AttributeEdits != null && pdu.AttributeEdits.Length > 0;
        var hasAnchor = pdu.AnchorPointEdit != null;
        var hasPoints = pdu.PointListEdits != null && pdu.PointListEdits.Length > 0;
        enc.WriteOptionalBitmap(hasAttrs, hasAnchor, hasPoints, false);

        // drawingHandle (REQUIRED)
        enc.WriteConstrainedWholeNumber(pdu.Handle, 0, uint.MaxValue);

        // attributeEdits OPTIONAL SET OF DrawingAttribute
        if (hasAttrs)
        {
            EncodeDrawingAttributes(enc, pdu.AttributeEdits);
        }

        // anchorPointEdit OPTIONAL WorkspacePoint
        if (hasAnchor)
        {
            EncodeWorkspacePoint(enc, pdu.AnchorPointEdit);
        }

        // pointListEdits OPTIONAL PointList CHOICE
        if (hasPoints)
        {
            EncodePointList(enc, pdu.PointListType ?? WhiteboardConstants.POINT_LIST_DIFF16, pdu.PointListEdits);
        }

        return enc.ToArray();
    }

    /// <summary>Encode a WorkspaceCreatePDU as a T.126 SIPDU.</summary>
    public static byte[] EncodeWorkspaceCreate(WorkspaceCreatePdu pdu)
    {
        var enc = new PerEncoder();

        // SIPDU CHOICE
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(WhiteboardConstants.SIPDU_WORKSPACE_CREATE, WhiteboardConstants.SIPDU_ROOT_COUNT, extensible: false, isExtension: false);

        // WorkspaceCreatePDU SEQUENCE (extensible, has extension "refresh")
        enc.WriteExtensionBit(false);

        // Optional bitmap: protectedPlaneAccessList, workspaceAttributes,
        //   planeParameters, viewParameters, nonStandardParameters
        enc.WriteOptionalBitmap(false, false, false, false, false);

        // workspaceIdentifier: WorkspaceIdentifier CHOICE
        // activeWorkspace Handle — use workspace handle
        enc.WriteChoiceIndex(0, 2, extensible: false, isExtension: false);
        enc.WriteConstrainedWholeNumber(pdu.WorkspaceHandle, 0, uint.MaxValue);

        // appRosterInstance INTEGER(0..65535)
        enc.WriteConstrainedWholeNumber(pdu.AppRosterInstance, 0, 65535);

        // synchronized BOOLEAN
        enc.WriteBoolean(pdu.Synchronized);

        // acceptKeyboardEvents BOOLEAN
        enc.WriteBoolean(pdu.AcceptKeyboardEvents);

        // acceptPointingDeviceEvents BOOLEAN
        enc.WriteBoolean(pdu.AcceptPointingDeviceEvents);

        // workspaceSize: WorkspaceSize
        // width INTEGER(1..65535), height INTEGER(1..65535)
        enc.WriteConstrainedWholeNumber(pdu.WorkspaceWidth, 1, 65535);
        enc.WriteConstrainedWholeNumber(pdu.WorkspaceHeight, 1, 65535);

        return enc.ToArray();
    }

    // ──────────────────────────────────────────────────────────
    //  Decode
    // ──────────────────────────────────────────────────────────

    /// <summary>Decode a T.126 SIPDU from PER-encoded bytes.</summary>
    public static SipduMessage DecodeSipdu(byte[] data)
    {
        if (data == null || data.Length < 1)
        {
            throw new ArgumentException("SIPDU data too short");
        }

        var dec = new PerDecoder(data);

        // SIPDU CHOICE (extensible, 35 root)
        var hasExtensions = dec.ReadExtensionBit();
        if (hasExtensions)
        {
            // Extension — return raw
            return new SipduMessage { Type = -1, RawData = data };
        }

        var sipduType = (int)dec.ReadConstrainedWholeNumber(0, WhiteboardConstants.SIPDU_ROOT_COUNT - 1);

        return sipduType switch
        {
            WhiteboardConstants.SIPDU_DRAWING_CREATE => DecodeDrawingCreate(dec, data),
            WhiteboardConstants.SIPDU_DRAWING_DELETE => DecodeDrawingDelete(dec, data),
            WhiteboardConstants.SIPDU_DRAWING_EDIT => DecodeDrawingEdit(dec, data),
            WhiteboardConstants.SIPDU_WORKSPACE_CREATE => DecodeWorkspaceCreate(dec, data),
            _ => new SipduMessage { Type = sipduType, RawData = data }
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Internal decode helpers
    // ──────────────────────────────────────────────────────────

    private static SipduMessage DecodeDrawingCreate(PerDecoder dec, byte[] rawData)
    {
        // DrawingCreatePDU SEQUENCE (extensible)
        var seqExtended = dec.ReadExtensionBit();

        // Optional bitmap: handle, attributes, rotation, sampleRate, nonStandardParameters
        var optionals = dec.ReadOptionalBitmap(5);

        // drawingHandle OPTIONAL
        uint? handle = null;
        if (optionals[0])
        {
            handle = (uint)dec.ReadConstrainedWholeNumber(0, uint.MaxValue);
        }

        // destinationAddress: DrawingDestinationAddress CHOICE (extensible)
        var destExtended = dec.ReadExtensionBit();
        var destChoice = (int)dec.ReadConstrainedWholeNumber(0, 1);

        uint workspaceHandle = 0;
        var planeIndex = 0;

        if (destChoice == 1) // softCopyAnnotationPlane
        {
            workspaceHandle = (uint)dec.ReadConstrainedWholeNumber(0, uint.MaxValue);
            planeIndex = (int)dec.ReadConstrainedWholeNumber(0, 255);
        }
        else // softCopyImagePlane
        {
            workspaceHandle = (uint)dec.ReadConstrainedWholeNumber(0, uint.MaxValue);
        }

        // drawingType CHOICE (extensible, 6 root)
        var dtExtended = dec.ReadExtensionBit();
        var drawingType = (int)dec.ReadConstrainedWholeNumber(0,
            WhiteboardConstants.DRAWING_TYPE_ROOT_COUNT - 1);

        // attributes OPTIONAL SET OF DrawingAttribute
        DrawingAttribute[] attributes = null;
        if (optionals[1])
        {
            attributes = DecodeDrawingAttributes(dec);
        }

        // anchorPoint: WorkspacePoint
        var anchor = DecodeWorkspacePoint(dec);

        // Skip rotation (optional[2]) and sampleRate (optional[3]) if present
        // These are rarely used; skip for basic interop

        // pointList CHOICE
        var (pointListType, points) = DecodePointList(dec);

        return new SipduMessage
        {
            Type = WhiteboardConstants.SIPDU_DRAWING_CREATE,
            DrawingCreate = new DrawingCreatePdu
            {
                Handle = handle,
                WorkspaceHandle = workspaceHandle,
                PlaneIndex = planeIndex,
                DrawingType = drawingType,
                Attributes = attributes,
                AnchorPoint = anchor,
                Points = points,
                PointListType = pointListType
            },
            RawData = rawData
        };
    }

    private static SipduMessage DecodeDrawingDelete(PerDecoder dec, byte[] rawData)
    {
        // DrawingDeletePDU SEQUENCE (extensible)
        dec.ReadExtensionBit();

        // Optional bitmap: nonStandardParameters
        dec.ReadOptionalBitmap(1);

        // drawingHandle
        var handle = (uint)dec.ReadConstrainedWholeNumber(0, uint.MaxValue);

        return new SipduMessage
        {
            Type = WhiteboardConstants.SIPDU_DRAWING_DELETE,
            DrawingDelete = new DrawingDeletePdu { Handle = handle },
            RawData = rawData
        };
    }

    private static SipduMessage DecodeDrawingEdit(PerDecoder dec, byte[] rawData)
    {
        // DrawingEditPDU SEQUENCE (extensible)
        var seqExtended = dec.ReadExtensionBit();

        // Optional bitmap: attributeEdits, anchorPointEdit, pointListEdits, nonStandardParameters
        var optionals = dec.ReadOptionalBitmap(4);

        // drawingHandle (REQUIRED)
        var handle = (uint)dec.ReadConstrainedWholeNumber(0, uint.MaxValue);

        // attributeEdits OPTIONAL
        DrawingAttribute[] attributeEdits = null;
        if (optionals[0])
        {
            attributeEdits = DecodeDrawingAttributes(dec);
        }

        // anchorPointEdit OPTIONAL
        WorkspacePoint anchorPointEdit = null;
        if (optionals[1])
        {
            anchorPointEdit = DecodeWorkspacePoint(dec);
        }

        // pointListEdits OPTIONAL
        PointDelta[] pointListEdits = null;
        int? pointListType = null;
        if (optionals[2])
        {
            var (plt, pts) = DecodePointList(dec);
            pointListType = plt;
            pointListEdits = pts;
        }

        return new SipduMessage
        {
            Type = WhiteboardConstants.SIPDU_DRAWING_EDIT,
            DrawingEdit = new DrawingEditPdu
            {
                Handle = handle,
                AttributeEdits = attributeEdits,
                AnchorPointEdit = anchorPointEdit,
                PointListEdits = pointListEdits,
                PointListType = pointListType
            },
            RawData = rawData
        };
    }

    private static SipduMessage DecodeWorkspaceCreate(PerDecoder dec, byte[] rawData)
    {
        // WorkspaceCreatePDU SEQUENCE (extensible, has "refresh" extension)
        var seqExtended = dec.ReadExtensionBit();

        // Optional bitmap: protectedPlaneAccessList, workspaceAttributes,
        //   planeParameters, viewParameters, nonStandardParameters
        dec.ReadOptionalBitmap(5);

        // workspaceIdentifier CHOICE (0=activeWorkspace, 1=archiveWorkspace)
        var idChoice = (int)dec.ReadConstrainedWholeNumber(0, 1);
        var workspaceHandle = (uint)dec.ReadConstrainedWholeNumber(0, uint.MaxValue);

        // appRosterInstance
        var appRoster = (int)dec.ReadConstrainedWholeNumber(0, 65535);

        // synchronized, acceptKeyboardEvents, acceptPointingDeviceEvents
        var sync = dec.ReadBoolean();
        var keyboard = dec.ReadBoolean();
        var pointing = dec.ReadBoolean();

        // workspaceSize: width, height
        var width = (int)dec.ReadConstrainedWholeNumber(1, 65535);
        var height = (int)dec.ReadConstrainedWholeNumber(1, 65535);

        return new SipduMessage
        {
            Type = WhiteboardConstants.SIPDU_WORKSPACE_CREATE,
            WorkspaceCreate = new WorkspaceCreatePdu
            {
                WorkspaceHandle = workspaceHandle,
                AppRosterInstance = appRoster,
                Synchronized = sync,
                AcceptKeyboardEvents = keyboard,
                AcceptPointingDeviceEvents = pointing,
                WorkspaceWidth = width,
                WorkspaceHeight = height
            },
            RawData = rawData
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Shared PER encode/decode for sub-structures
    // ──────────────────────────────────────────────────────────

    private static void EncodeWorkspacePoint(PerEncoder enc, WorkspacePoint point)
    {
        enc.WriteConstrainedWholeNumber(point.X, WhiteboardConstants.COORD_MIN, WhiteboardConstants.COORD_MAX);
        enc.WriteConstrainedWholeNumber(point.Y, WhiteboardConstants.COORD_MIN, WhiteboardConstants.COORD_MAX);
    }

    private static WorkspacePoint DecodeWorkspacePoint(PerDecoder dec)
    {
        var x = (int)dec.ReadConstrainedWholeNumber(WhiteboardConstants.COORD_MIN, WhiteboardConstants.COORD_MAX);
        var y = (int)dec.ReadConstrainedWholeNumber(WhiteboardConstants.COORD_MIN, WhiteboardConstants.COORD_MAX);
        return new WorkspacePoint { X = x, Y = y };
    }

    private static void EncodePointList(PerEncoder enc, int pointListType, PointDelta[] points)
    {
        var count = points?.Length ?? 0;

        // PointList CHOICE (3 root, not extensible)
        enc.WriteChoiceIndex(pointListType, WhiteboardConstants.POINT_LIST_ROOT_COUNT, extensible: false, isExtension: false);

        // SEQUENCE OF (SIZE 0..255)
        enc.WriteConstrainedWholeNumber(count, 0, 255);

        for (var i = 0; i < count; i++)
        {
            var p = points[i];

            switch (pointListType)
            {
                case WhiteboardConstants.POINT_LIST_DIFF4:
                {
                    enc.WriteConstrainedWholeNumber(p.Dx, -8, 7);
                    enc.WriteConstrainedWholeNumber(p.Dy, -8, 7);
                }
                break;

                case WhiteboardConstants.POINT_LIST_DIFF8:
                {
                    enc.WriteConstrainedWholeNumber(p.Dx, -128, 127);
                    enc.WriteConstrainedWholeNumber(p.Dy, -128, 127);
                }
                break;

                case WhiteboardConstants.POINT_LIST_DIFF16:
                {
                    enc.WriteConstrainedWholeNumber(p.Dx, -32768, 32767);
                    enc.WriteConstrainedWholeNumber(p.Dy, -32768, 32767);
                }
                break;
            }
        }
    }

    private static (int pointListType, PointDelta[] points) DecodePointList(PerDecoder dec)
    {
        var pointListType = (int)dec.ReadConstrainedWholeNumber(0, WhiteboardConstants.POINT_LIST_ROOT_COUNT - 1);

        var count = (int)dec.ReadConstrainedWholeNumber(0, 255);
        var points = new PointDelta[count];

        for (var i = 0; i < count; i++)
        {
            int dx, dy;

            switch (pointListType)
            {
                case WhiteboardConstants.POINT_LIST_DIFF4:
                {
                    dx = (int)dec.ReadConstrainedWholeNumber(-8, 7);
                    dy = (int)dec.ReadConstrainedWholeNumber(-8, 7);
                }
                break;

                case WhiteboardConstants.POINT_LIST_DIFF8:
                {
                    dx = (int)dec.ReadConstrainedWholeNumber(-128, 127);
                    dy = (int)dec.ReadConstrainedWholeNumber(-128, 127);
                }
                break;

                case WhiteboardConstants.POINT_LIST_DIFF16:
                default:
                {
                    dx = (int)dec.ReadConstrainedWholeNumber(-32768, 32767);
                    dy = (int)dec.ReadConstrainedWholeNumber(-32768, 32767);
                }
                break;
            }

            points[i] = new PointDelta { Dx = dx, Dy = dy };
        }

        return (pointListType, points);
    }

    private static void EncodeDrawingAttributes(PerEncoder enc, DrawingAttribute[] attrs)
    {
        // SET OF DrawingAttribute — length-determinant first
        enc.WriteConstrainedLengthDeterminant(attrs.Length, 1, 256);

        foreach (var attr in attrs)
        {
            // DrawingAttribute CHOICE (extensible, 9 root)
            enc.WriteExtensionBit(false);
            enc.WriteChoiceIndex(attr.Type, WhiteboardConstants.ATTR_ROOT_COUNT, extensible: false, isExtension: false);

            switch (attr.Type)
            {
                case WhiteboardConstants.ATTR_PEN_COLOR:
                case WhiteboardConstants.ATTR_FILL_COLOR:
                {
                    EncodeWorkspaceColor(enc, attr.Color);
                }
                break;

                case WhiteboardConstants.ATTR_PEN_THICKNESS:
                {
                    enc.WriteConstrainedWholeNumber(attr.PenThickness, 1, 255);
                }
                break;

                case WhiteboardConstants.ATTR_PEN_NIB:
                {
                    enc.WriteExtensionBit(false);
                    enc.WriteChoiceIndex(attr.PenNib, WhiteboardConstants.PEN_NIB_ROOT_COUNT, extensible: false, isExtension: false);
                }
                break;

                case WhiteboardConstants.ATTR_LINE_STYLE:
                {
                    enc.WriteExtensionBit(false);
                    enc.WriteChoiceIndex(attr.LineStyle, WhiteboardConstants.LINE_STYLE_ROOT_COUNT, extensible: false, isExtension: false);
                }
                break;

                case WhiteboardConstants.ATTR_HIGHLIGHT:
                {
                    enc.WriteBoolean(attr.Highlight);
                }
                break;

                case WhiteboardConstants.ATTR_VIEW_STATE:
                {
                    enc.WriteEnumerated(attr.ViewState,
                        WhiteboardConstants.VIEW_STATE_ROOT_COUNT, extensible: true, isExtension: false);
                }
                break;

                case WhiteboardConstants.ATTR_Z_ORDER:
                {
                    // ZOrder ::= INTEGER(0..65535)
                    enc.WriteConstrainedWholeNumber(attr.ZOrder, 0, 65535);
                }
                break;
            }
        }
    }

    private static DrawingAttribute[] DecodeDrawingAttributes(PerDecoder dec)
    {
        var count = (int)dec.ReadConstrainedLengthDeterminant(1, 256);
        var attrs = new DrawingAttribute[count];

        for (var i = 0; i < count; i++)
        {
            dec.ReadExtensionBit(); // DrawingAttribute CHOICE extension bit
            var attrType = (int)dec.ReadConstrainedWholeNumber(0,
                WhiteboardConstants.ATTR_ROOT_COUNT - 1);

            var attr = new DrawingAttribute { Type = attrType };

            switch (attrType)
            {
                case WhiteboardConstants.ATTR_PEN_COLOR:
                case WhiteboardConstants.ATTR_FILL_COLOR:
                {
                    attr = new DrawingAttribute
                    {
                        Type = attrType,
                        Color = DecodeWorkspaceColor(dec)
                    };
                }
                break;

                case WhiteboardConstants.ATTR_PEN_THICKNESS:
                {
                    attr = new DrawingAttribute
                    {
                        Type = attrType,
                        PenThickness = (int)dec.ReadConstrainedWholeNumber(1, 255)
                    };
                }
                break;

                case WhiteboardConstants.ATTR_PEN_NIB:
                {
                    dec.ReadExtensionBit();
                    attr = new DrawingAttribute
                    {
                        Type = attrType,
                        PenNib = (int)dec.ReadConstrainedWholeNumber(0,
                            WhiteboardConstants.PEN_NIB_ROOT_COUNT - 1)
                    };
                }
                break;

                case WhiteboardConstants.ATTR_LINE_STYLE:
                {
                    dec.ReadExtensionBit();
                    attr = new DrawingAttribute
                    {
                        Type = attrType,
                        LineStyle = (int)dec.ReadConstrainedWholeNumber(0,
                            WhiteboardConstants.LINE_STYLE_ROOT_COUNT - 1)
                    };
                }
                break;

                case WhiteboardConstants.ATTR_HIGHLIGHT:
                {
                    attr = new DrawingAttribute
                    {
                        Type = attrType,
                        Highlight = dec.ReadBoolean()
                    };
                }
                break;

                case WhiteboardConstants.ATTR_VIEW_STATE:
                {
                    attr = new DrawingAttribute
                    {
                        Type = attrType,
                        ViewState = dec.ReadEnumerated(
                            WhiteboardConstants.VIEW_STATE_ROOT_COUNT, extensible: true)
                    };
                }
                break;

                case WhiteboardConstants.ATTR_Z_ORDER:
                {
                    attr = new DrawingAttribute
                    {
                        Type = attrType,
                        ZOrder = (int)dec.ReadConstrainedWholeNumber(0, 65535)
                    };
                }
                break;
            }

            attrs[i] = attr;
        }

        return attrs;
    }

    private static void EncodeWorkspaceColor(PerEncoder enc, WorkspaceColor color)
    {
        // WorkspaceColor CHOICE (extensible, 3 root)
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(color.Type,
            WhiteboardConstants.COLOR_ROOT_COUNT, extensible: false, isExtension: false);

        switch (color.Type)
        {
            case WhiteboardConstants.COLOR_PALETTE_INDEX:
            {
                enc.WriteConstrainedWholeNumber(color.PaletteIndex, 0, 255);
            }
            break;

            case WhiteboardConstants.COLOR_RGB:
            {
                // ColorRGB SEQUENCE { r, g, b } each OCTET (0-255)
                enc.WriteConstrainedWholeNumber(color.R, 0, 255);
                enc.WriteConstrainedWholeNumber(color.G, 0, 255);
                enc.WriteConstrainedWholeNumber(color.B, 0, 255);
            }
            break;

            case WhiteboardConstants.COLOR_TRANSPARENT:
            {
                // NULL — nothing to encode
            }
            break;
        }
    }

    private static WorkspaceColor DecodeWorkspaceColor(PerDecoder dec)
    {
        dec.ReadExtensionBit(); // CHOICE extension
        var colorType = (int)dec.ReadConstrainedWholeNumber(0,
            WhiteboardConstants.COLOR_ROOT_COUNT - 1);

        return colorType switch
        {
            WhiteboardConstants.COLOR_PALETTE_INDEX => new WorkspaceColor
            {
                Type = colorType,
                PaletteIndex = (int)dec.ReadConstrainedWholeNumber(0, 255)
            },
            WhiteboardConstants.COLOR_RGB => new WorkspaceColor
            {
                Type = colorType,
                R = (byte)dec.ReadConstrainedWholeNumber(0, 255),
                G = (byte)dec.ReadConstrainedWholeNumber(0, 255),
                B = (byte)dec.ReadConstrainedWholeNumber(0, 255)
            },
            _ => new WorkspaceColor { Type = colorType } // transparent or unknown
        };
    }
}
