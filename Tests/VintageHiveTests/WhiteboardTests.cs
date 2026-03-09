// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.Omnet;
using VintageHive.Proxy.NetMeeting.Whiteboard;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  Whiteboard Constants tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class WhiteboardConstantsTests
{
    [TestMethod]
    public void SipduRootCount_Is35()
    {
        Assert.AreEqual(35, WhiteboardConstants.SIPDU_ROOT_COUNT);
    }

    [TestMethod]
    public void SipduName_KnownTypes()
    {
        Assert.AreEqual("DrawingCreate", WhiteboardConstants.SipduName(WhiteboardConstants.SIPDU_DRAWING_CREATE));
        Assert.AreEqual("DrawingDelete", WhiteboardConstants.SipduName(WhiteboardConstants.SIPDU_DRAWING_DELETE));
        Assert.AreEqual("DrawingEdit", WhiteboardConstants.SipduName(WhiteboardConstants.SIPDU_DRAWING_EDIT));
        Assert.AreEqual("WorkspaceCreate", WhiteboardConstants.SipduName(WhiteboardConstants.SIPDU_WORKSPACE_CREATE));
        Assert.AreEqual("BitmapCreate", WhiteboardConstants.SipduName(WhiteboardConstants.SIPDU_BITMAP_CREATE));
        Assert.AreEqual("TextCreate", WhiteboardConstants.SipduName(WhiteboardConstants.SIPDU_TEXT_CREATE));
        Assert.AreEqual("NonStandard", WhiteboardConstants.SipduName(WhiteboardConstants.SIPDU_NON_STANDARD));
    }

    [TestMethod]
    public void SipduName_Unknown_ReturnsGeneric()
    {
        Assert.IsTrue(WhiteboardConstants.SipduName(99).Contains("99"));
    }

    [TestMethod]
    public void DrawingTypeName_KnownTypes()
    {
        Assert.AreEqual("point", WhiteboardConstants.DrawingTypeName(WhiteboardConstants.DRAWING_POINT));
        Assert.AreEqual("openPolyLine", WhiteboardConstants.DrawingTypeName(WhiteboardConstants.DRAWING_OPEN_POLYLINE));
        Assert.AreEqual("rectangle", WhiteboardConstants.DrawingTypeName(WhiteboardConstants.DRAWING_RECTANGLE));
        Assert.AreEqual("ellipse", WhiteboardConstants.DrawingTypeName(WhiteboardConstants.DRAWING_ELLIPSE));
    }

    [TestMethod]
    public void CoordinateRange_MatchesT126Spec()
    {
        Assert.AreEqual(-21845, WhiteboardConstants.COORD_MIN);
        Assert.AreEqual(43690, WhiteboardConstants.COORD_MAX);
    }
}

// ──────────────────────────────────────────────────────────
//  DrawingCreatePDU encode/decode tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class DrawingCreateTests
{
    [TestMethod]
    public void DrawingCreate_Point_RoundTrip()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_POINT,
            AnchorPoint = new WorkspacePoint { X = 100, Y = 200 },
            Points = Array.Empty<PointDelta>(),
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        Assert.IsTrue(data.Length > 0);

        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        Assert.AreEqual(WhiteboardConstants.SIPDU_DRAWING_CREATE, sipdu.Type);
        Assert.IsNotNull(sipdu.DrawingCreate);

        var dc = sipdu.DrawingCreate;
        Assert.AreEqual(WhiteboardConstants.DRAWING_POINT, dc.DrawingType);
        Assert.AreEqual(100, dc.AnchorPoint.X);
        Assert.AreEqual(200, dc.AnchorPoint.Y);
        Assert.AreEqual(0, dc.Points.Length);
    }

    [TestMethod]
    public void DrawingCreate_OpenPolyLine_WithDiff8Points()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_OPEN_POLYLINE,
            AnchorPoint = new WorkspacePoint { X = 50, Y = 50 },
            Points = new[]
            {
                new PointDelta { Dx = 10, Dy = -5 },
                new PointDelta { Dx = 20, Dy = 15 },
                new PointDelta { Dx = -30, Dy = 0 }
            },
            PointListType = WhiteboardConstants.POINT_LIST_DIFF8
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        var dc = sipdu.DrawingCreate;

        Assert.AreEqual(WhiteboardConstants.DRAWING_OPEN_POLYLINE, dc.DrawingType);
        Assert.AreEqual(50, dc.AnchorPoint.X);
        Assert.AreEqual(50, dc.AnchorPoint.Y);
        Assert.AreEqual(3, dc.Points.Length);
        Assert.AreEqual(10, dc.Points[0].Dx);
        Assert.AreEqual(-5, dc.Points[0].Dy);
        Assert.AreEqual(20, dc.Points[1].Dx);
        Assert.AreEqual(15, dc.Points[1].Dy);
        Assert.AreEqual(-30, dc.Points[2].Dx);
        Assert.AreEqual(0, dc.Points[2].Dy);
    }

    [TestMethod]
    public void DrawingCreate_Rectangle_WithDiff16Points()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_RECTANGLE,
            AnchorPoint = new WorkspacePoint { X = 0, Y = 0 },
            Points = new[]
            {
                new PointDelta { Dx = 500, Dy = 300 }
            },
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        var dc = sipdu.DrawingCreate;

        Assert.AreEqual(WhiteboardConstants.DRAWING_RECTANGLE, dc.DrawingType);
        Assert.AreEqual(1, dc.Points.Length);
        Assert.AreEqual(500, dc.Points[0].Dx);
        Assert.AreEqual(300, dc.Points[0].Dy);
    }

    [TestMethod]
    public void DrawingCreate_Ellipse_RoundTrip()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_ELLIPSE,
            AnchorPoint = new WorkspacePoint { X = 200, Y = 150 },
            Points = new[]
            {
                new PointDelta { Dx = 100, Dy = 75 }
            },
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(WhiteboardConstants.DRAWING_ELLIPSE, dc(sipdu).DrawingType);
        Assert.AreEqual(200, dc(sipdu).AnchorPoint.X);
        Assert.AreEqual(150, dc(sipdu).AnchorPoint.Y);
    }

    [TestMethod]
    public void DrawingCreate_WithHandle_RoundTrip()
    {
        var pdu = new DrawingCreatePdu
        {
            Handle = 12345u,
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_POINT,
            AnchorPoint = new WorkspacePoint { X = 0, Y = 0 },
            Points = Array.Empty<PointDelta>(),
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(12345u, sipdu.DrawingCreate.Handle);
    }

    [TestMethod]
    public void DrawingCreate_WithAttributes_RoundTrip()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_OPEN_POLYLINE,
            Attributes = new[]
            {
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_COLOR,
                    Color = new WorkspaceColor
                    {
                        Type = WhiteboardConstants.COLOR_RGB,
                        R = 255, G = 0, B = 0
                    }
                },
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_THICKNESS,
                    PenThickness = 3
                }
            },
            AnchorPoint = new WorkspacePoint { X = 10, Y = 20 },
            Points = new[] { new PointDelta { Dx = 50, Dy = 50 } },
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        var dc = sipdu.DrawingCreate;

        Assert.IsNotNull(dc.Attributes);
        Assert.AreEqual(2, dc.Attributes.Length);

        // Pen color
        Assert.AreEqual(WhiteboardConstants.ATTR_PEN_COLOR, dc.Attributes[0].Type);
        Assert.AreEqual(WhiteboardConstants.COLOR_RGB, dc.Attributes[0].Color.Type);
        Assert.AreEqual((byte)255, dc.Attributes[0].Color.R);
        Assert.AreEqual((byte)0, dc.Attributes[0].Color.G);
        Assert.AreEqual((byte)0, dc.Attributes[0].Color.B);

        // Pen thickness
        Assert.AreEqual(WhiteboardConstants.ATTR_PEN_THICKNESS, dc.Attributes[1].Type);
        Assert.AreEqual(3, dc.Attributes[1].PenThickness);
    }

    [TestMethod]
    public void DrawingCreate_Diff4Points_RoundTrip()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_OPEN_POLYLINE,
            AnchorPoint = new WorkspacePoint { X = 100, Y = 100 },
            Points = new[]
            {
                new PointDelta { Dx = 5, Dy = -3 },
                new PointDelta { Dx = -8, Dy = 7 },
                new PointDelta { Dx = 0, Dy = 0 }
            },
            PointListType = WhiteboardConstants.POINT_LIST_DIFF4
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        var dc = sipdu.DrawingCreate;

        Assert.AreEqual(WhiteboardConstants.POINT_LIST_DIFF4, dc.PointListType);
        Assert.AreEqual(3, dc.Points.Length);
        Assert.AreEqual(5, dc.Points[0].Dx);
        Assert.AreEqual(-3, dc.Points[0].Dy);
        Assert.AreEqual(-8, dc.Points[1].Dx);
        Assert.AreEqual(7, dc.Points[1].Dy);
    }

    [TestMethod]
    public void DrawingCreate_NegativeCoordinates_RoundTrip()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_POINT,
            AnchorPoint = new WorkspacePoint
            {
                X = WhiteboardConstants.COORD_MIN,
                Y = WhiteboardConstants.COORD_MAX
            },
            Points = Array.Empty<PointDelta>(),
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(WhiteboardConstants.COORD_MIN, sipdu.DrawingCreate.AnchorPoint.X);
        Assert.AreEqual(WhiteboardConstants.COORD_MAX, sipdu.DrawingCreate.AnchorPoint.Y);
    }

    [TestMethod]
    public void DrawingCreate_AllAttributeTypes_RoundTrip()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_OPEN_POLYLINE,
            Attributes = new[]
            {
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_COLOR,
                    Color = new WorkspaceColor { Type = WhiteboardConstants.COLOR_TRANSPARENT }
                },
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_FILL_COLOR,
                    Color = new WorkspaceColor
                    {
                        Type = WhiteboardConstants.COLOR_PALETTE_INDEX,
                        PaletteIndex = 42
                    }
                },
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_THICKNESS,
                    PenThickness = 1
                },
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_NIB,
                    PenNib = WhiteboardConstants.PEN_NIB_SQUARE
                },
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_LINE_STYLE,
                    LineStyle = WhiteboardConstants.LINE_STYLE_DASHED
                },
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_HIGHLIGHT,
                    Highlight = true
                },
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_Z_ORDER,
                    ZOrder = 100
                }
            },
            AnchorPoint = new WorkspacePoint { X = 0, Y = 0 },
            Points = Array.Empty<PointDelta>(),
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        var attrs = sipdu.DrawingCreate.Attributes;

        Assert.AreEqual(7, attrs.Length);

        // Transparent pen color
        Assert.AreEqual(WhiteboardConstants.COLOR_TRANSPARENT, attrs[0].Color.Type);

        // Palette fill color
        Assert.AreEqual(WhiteboardConstants.COLOR_PALETTE_INDEX, attrs[1].Color.Type);
        Assert.AreEqual(42, attrs[1].Color.PaletteIndex);

        // Pen thickness
        Assert.AreEqual(1, attrs[2].PenThickness);

        // Square nib
        Assert.AreEqual(WhiteboardConstants.PEN_NIB_SQUARE, attrs[3].PenNib);

        // Dashed line
        Assert.AreEqual(WhiteboardConstants.LINE_STYLE_DASHED, attrs[4].LineStyle);

        // Highlight
        Assert.IsTrue(attrs[5].Highlight);

        // Z-order
        Assert.AreEqual(100, attrs[6].ZOrder);
    }

    private static DrawingCreatePdu dc(SipduMessage msg) => msg.DrawingCreate;
}

// ──────────────────────────────────────────────────────────
//  DrawingDeletePDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class DrawingDeleteTests
{
    [TestMethod]
    public void DrawingDelete_RoundTrip()
    {
        var pdu = new DrawingDeletePdu { Handle = 99999 };
        var data = WhiteboardCodec.EncodeDrawingDelete(pdu);

        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        Assert.AreEqual(WhiteboardConstants.SIPDU_DRAWING_DELETE, sipdu.Type);
        Assert.AreEqual(99999u, sipdu.DrawingDelete.Handle);
    }

    [TestMethod]
    public void DrawingDelete_ZeroHandle()
    {
        var pdu = new DrawingDeletePdu { Handle = 0 };
        var data = WhiteboardCodec.EncodeDrawingDelete(pdu);

        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        Assert.AreEqual(0u, sipdu.DrawingDelete.Handle);
    }

    [TestMethod]
    public void DrawingDelete_MaxHandle()
    {
        var pdu = new DrawingDeletePdu { Handle = uint.MaxValue };
        var data = WhiteboardCodec.EncodeDrawingDelete(pdu);

        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        Assert.AreEqual(uint.MaxValue, sipdu.DrawingDelete.Handle);
    }
}

// ──────────────────────────────────────────────────────────
//  DrawingEditPDU encode/decode tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class DrawingEditTests
{
    [TestMethod]
    public void DrawingEdit_AttributesOnly_RoundTrip()
    {
        var pdu = new DrawingEditPdu
        {
            Handle = 100,
            AttributeEdits = new[]
            {
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_COLOR,
                    Color = new WorkspaceColor
                    {
                        Type = WhiteboardConstants.COLOR_RGB,
                        R = 0, G = 255, B = 0
                    }
                }
            }
        };

        var data = WhiteboardCodec.EncodeDrawingEdit(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(WhiteboardConstants.SIPDU_DRAWING_EDIT, sipdu.Type);
        Assert.IsNotNull(sipdu.DrawingEdit);
        Assert.AreEqual(100u, sipdu.DrawingEdit.Handle);
        Assert.IsNotNull(sipdu.DrawingEdit.AttributeEdits);
        Assert.AreEqual(1, sipdu.DrawingEdit.AttributeEdits.Length);
        Assert.AreEqual((byte)255, sipdu.DrawingEdit.AttributeEdits[0].Color.G);
        Assert.IsNull(sipdu.DrawingEdit.AnchorPointEdit);
        Assert.IsNull(sipdu.DrawingEdit.PointListEdits);
    }

    [TestMethod]
    public void DrawingEdit_AnchorPointOnly_RoundTrip()
    {
        var pdu = new DrawingEditPdu
        {
            Handle = 200,
            AnchorPointEdit = new WorkspacePoint { X = 500, Y = -100 }
        };

        var data = WhiteboardCodec.EncodeDrawingEdit(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(200u, sipdu.DrawingEdit.Handle);
        Assert.IsNotNull(sipdu.DrawingEdit.AnchorPointEdit);
        Assert.AreEqual(500, sipdu.DrawingEdit.AnchorPointEdit.X);
        Assert.AreEqual(-100, sipdu.DrawingEdit.AnchorPointEdit.Y);
        Assert.IsNull(sipdu.DrawingEdit.AttributeEdits);
    }

    [TestMethod]
    public void DrawingEdit_PointListOnly_RoundTrip()
    {
        var pdu = new DrawingEditPdu
        {
            Handle = 300,
            PointListEdits = new[]
            {
                new PointDelta { Dx = 10, Dy = -20 },
                new PointDelta { Dx = 30, Dy = 40 }
            },
            PointListType = WhiteboardConstants.POINT_LIST_DIFF8
        };

        var data = WhiteboardCodec.EncodeDrawingEdit(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(300u, sipdu.DrawingEdit.Handle);
        Assert.IsNotNull(sipdu.DrawingEdit.PointListEdits);
        Assert.AreEqual(2, sipdu.DrawingEdit.PointListEdits.Length);
        Assert.AreEqual(10, sipdu.DrawingEdit.PointListEdits[0].Dx);
        Assert.AreEqual(-20, sipdu.DrawingEdit.PointListEdits[0].Dy);
        Assert.AreEqual(WhiteboardConstants.POINT_LIST_DIFF8, sipdu.DrawingEdit.PointListType);
    }

    [TestMethod]
    public void DrawingEdit_AllFields_RoundTrip()
    {
        var pdu = new DrawingEditPdu
        {
            Handle = 400,
            AttributeEdits = new[]
            {
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_THICKNESS,
                    PenThickness = 5
                }
            },
            AnchorPointEdit = new WorkspacePoint { X = 1000, Y = 2000 },
            PointListEdits = new[]
            {
                new PointDelta { Dx = -50, Dy = 50 }
            },
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingEdit(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        var edit = sipdu.DrawingEdit;
        Assert.AreEqual(400u, edit.Handle);
        Assert.AreEqual(1, edit.AttributeEdits.Length);
        Assert.AreEqual(5, edit.AttributeEdits[0].PenThickness);
        Assert.AreEqual(1000, edit.AnchorPointEdit.X);
        Assert.AreEqual(2000, edit.AnchorPointEdit.Y);
        Assert.AreEqual(1, edit.PointListEdits.Length);
        Assert.AreEqual(-50, edit.PointListEdits[0].Dx);
    }

    [TestMethod]
    public void DrawingEdit_HandleOnly_NoEdits()
    {
        var pdu = new DrawingEditPdu
        {
            Handle = 500
        };

        var data = WhiteboardCodec.EncodeDrawingEdit(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(500u, sipdu.DrawingEdit.Handle);
        Assert.IsNull(sipdu.DrawingEdit.AttributeEdits);
        Assert.IsNull(sipdu.DrawingEdit.AnchorPointEdit);
        Assert.IsNull(sipdu.DrawingEdit.PointListEdits);
    }

    [TestMethod]
    public void PeekSipduType_DrawingEdit()
    {
        var pdu = new DrawingEditPdu { Handle = 1 };
        var data = WhiteboardCodec.EncodeDrawingEdit(pdu);
        Assert.AreEqual(WhiteboardConstants.SIPDU_DRAWING_EDIT, WhiteboardCodec.PeekSipduType(data));
    }
}

// ──────────────────────────────────────────────────────────
//  WorkspaceCreatePDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class WorkspaceCreateTests
{
    [TestMethod]
    public void WorkspaceCreate_RoundTrip()
    {
        var pdu = new WorkspaceCreatePdu
        {
            WorkspaceHandle = 1,
            AppRosterInstance = 0,
            Synchronized = true,
            AcceptKeyboardEvents = false,
            AcceptPointingDeviceEvents = true,
            WorkspaceWidth = 800,
            WorkspaceHeight = 600
        };

        var data = WhiteboardCodec.EncodeWorkspaceCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(WhiteboardConstants.SIPDU_WORKSPACE_CREATE, sipdu.Type);
        var ws = sipdu.WorkspaceCreate;
        Assert.AreEqual(1u, ws.WorkspaceHandle);
        Assert.AreEqual(0, ws.AppRosterInstance);
        Assert.IsTrue(ws.Synchronized);
        Assert.IsFalse(ws.AcceptKeyboardEvents);
        Assert.IsTrue(ws.AcceptPointingDeviceEvents);
        Assert.AreEqual(800, ws.WorkspaceWidth);
        Assert.AreEqual(600, ws.WorkspaceHeight);
    }

    [TestMethod]
    public void WorkspaceCreate_MaxDimensions()
    {
        var pdu = new WorkspaceCreatePdu
        {
            WorkspaceHandle = 1,
            WorkspaceWidth = 65535,
            WorkspaceHeight = 65535
        };

        var data = WhiteboardCodec.EncodeWorkspaceCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(65535, sipdu.WorkspaceCreate.WorkspaceWidth);
        Assert.AreEqual(65535, sipdu.WorkspaceCreate.WorkspaceHeight);
    }

    [TestMethod]
    public void WorkspaceCreate_MinDimensions()
    {
        var pdu = new WorkspaceCreatePdu
        {
            WorkspaceHandle = 1,
            WorkspaceWidth = 1,
            WorkspaceHeight = 1
        };

        var data = WhiteboardCodec.EncodeWorkspaceCreate(pdu);
        var sipdu = WhiteboardCodec.DecodeSipdu(data);

        Assert.AreEqual(1, sipdu.WorkspaceCreate.WorkspaceWidth);
        Assert.AreEqual(1, sipdu.WorkspaceCreate.WorkspaceHeight);
    }
}

// ──────────────────────────────────────────────────────────
//  SIPDU type detection tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class SipduDetectionTests
{
    [TestMethod]
    public void PeekSipduType_DrawingCreate()
    {
        var pdu = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            DrawingType = WhiteboardConstants.DRAWING_POINT,
            AnchorPoint = new WorkspacePoint { X = 0, Y = 0 },
            Points = Array.Empty<PointDelta>(),
            PointListType = WhiteboardConstants.POINT_LIST_DIFF16
        };

        var data = WhiteboardCodec.EncodeDrawingCreate(pdu);
        Assert.AreEqual(WhiteboardConstants.SIPDU_DRAWING_CREATE, WhiteboardCodec.PeekSipduType(data));
    }

    [TestMethod]
    public void PeekSipduType_DrawingDelete()
    {
        var pdu = new DrawingDeletePdu { Handle = 1 };
        var data = WhiteboardCodec.EncodeDrawingDelete(pdu);
        Assert.AreEqual(WhiteboardConstants.SIPDU_DRAWING_DELETE, WhiteboardCodec.PeekSipduType(data));
    }

    [TestMethod]
    public void PeekSipduType_WorkspaceCreate()
    {
        var pdu = new WorkspaceCreatePdu
        {
            WorkspaceHandle = 1,
            WorkspaceWidth = 800,
            WorkspaceHeight = 600
        };

        var data = WhiteboardCodec.EncodeWorkspaceCreate(pdu);
        Assert.AreEqual(WhiteboardConstants.SIPDU_WORKSPACE_CREATE, WhiteboardCodec.PeekSipduType(data));
    }

    [TestMethod]
    public void PeekSipduType_Null_ReturnsNegative1()
    {
        Assert.AreEqual(-1, WhiteboardCodec.PeekSipduType(null));
        Assert.AreEqual(-1, WhiteboardCodec.PeekSipduType(Array.Empty<byte>()));
    }

    [TestMethod]
    public void DecodeSipdu_UnknownType_ReturnsRawData()
    {
        // Encode a bitmap abort (index 4) — not fully decoded
        var enc = new VintageHive.Proxy.NetMeeting.Asn1.PerEncoder();
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(WhiteboardConstants.SIPDU_BITMAP_ABORT, WhiteboardConstants.SIPDU_ROOT_COUNT, extensible: false, isExtension: false);
        // Write some dummy data
        enc.WriteConstrainedWholeNumber(42, 0, uint.MaxValue);
        var data = enc.ToArray();

        var sipdu = WhiteboardCodec.DecodeSipdu(data);
        Assert.AreEqual(WhiteboardConstants.SIPDU_BITMAP_ABORT, sipdu.Type);
        Assert.IsNotNull(sipdu.RawData);
    }
}

// ──────────────────────────────────────────────────────────
//  Full stack integration: T.126 → OMNET → MCS
// ──────────────────────────────────────────────────────────

[TestClass]
public class WhiteboardStackTests
{
    [TestMethod]
    public void DrawingCreate_InOmnetObjectAdd_OverMcs_RoundTrips()
    {
        // 1. Create a T.126 drawing
        var drawing = new DrawingCreatePdu
        {
            WorkspaceHandle = 1,
            PlaneIndex = 0,
            DrawingType = WhiteboardConstants.DRAWING_OPEN_POLYLINE,
            Attributes = new[]
            {
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_COLOR,
                    Color = new WorkspaceColor
                    {
                        Type = WhiteboardConstants.COLOR_RGB,
                        R = 0, G = 0, B = 255
                    }
                },
                new DrawingAttribute
                {
                    Type = WhiteboardConstants.ATTR_PEN_THICKNESS,
                    PenThickness = 2
                }
            },
            AnchorPoint = new WorkspacePoint { X = 100, Y = 100 },
            Points = new[]
            {
                new PointDelta { Dx = 50, Dy = 0 },
                new PointDelta { Dx = 0, Dy = 50 },
                new PointDelta { Dx = -50, Dy = 0 }
            },
            PointListType = WhiteboardConstants.POINT_LIST_DIFF8
        };

        // 2. PER-encode to SIPDU bytes
        var sipduData = WhiteboardCodec.EncodeDrawingCreate(drawing);

        // 3. Wrap in OMNET OBJECT_ADD
        var stamp = new OmnetSeqStamp { GenNumber = 1, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 1, Creator = 1001 };
        var omnetPacket = OmnetCodec.EncodeObjectAdd(1001, 2, 0,
            OmnetConstants.POSITION_LAST, stamp, objId, sipduData);

        // 4. Wrap in MCS SendDataRequest
        var mcsPacket = VintageHive.Proxy.NetMeeting.T120.McsCodec.EncodeSendDataRequest(
            1001, 7, VintageHive.Proxy.NetMeeting.T120.McsConstants.PRIORITY_HIGH, omnetPacket);

        // 5. Unwrap: MCS → OMNET → T.126
        var mcsPdu = VintageHive.Proxy.NetMeeting.T120.McsCodec.DecodeDomainPdu(mcsPacket);
        Assert.IsTrue(OmnetCodec.IsOmnetMessage(mcsPdu.UserData));

        var omnetMsg = OmnetCodec.Decode(mcsPdu.UserData);
        Assert.AreEqual(OmnetConstants.MSG_OBJECT_ADD, omnetMsg.MessageType);
        Assert.IsNotNull(omnetMsg.Data);

        var sipdu = WhiteboardCodec.DecodeSipdu(omnetMsg.Data);
        Assert.AreEqual(WhiteboardConstants.SIPDU_DRAWING_CREATE, sipdu.Type);

        var dc = sipdu.DrawingCreate;
        Assert.AreEqual(WhiteboardConstants.DRAWING_OPEN_POLYLINE, dc.DrawingType);
        Assert.AreEqual(100, dc.AnchorPoint.X);
        Assert.AreEqual(100, dc.AnchorPoint.Y);
        Assert.AreEqual(3, dc.Points.Length);
        Assert.AreEqual(50, dc.Points[0].Dx);

        // Verify attributes survived the full stack
        Assert.AreEqual(2, dc.Attributes.Length);
        Assert.AreEqual((byte)255, dc.Attributes[0].Color.B);
        Assert.AreEqual(2, dc.Attributes[1].PenThickness);
    }

    [TestMethod]
    public void WorkspaceCreate_InOmnetObjectAdd_RoundTrips()
    {
        // Workspace creation also rides OMNET
        var ws = new WorkspaceCreatePdu
        {
            WorkspaceHandle = 1,
            AppRosterInstance = 0,
            Synchronized = true,
            AcceptKeyboardEvents = true,
            AcceptPointingDeviceEvents = true,
            WorkspaceWidth = 1024,
            WorkspaceHeight = 768
        };

        var sipduData = WhiteboardCodec.EncodeWorkspaceCreate(ws);

        var stamp = new OmnetSeqStamp { GenNumber = 1, NodeId = 1001 };
        var objId = new OmnetObjectId { Sequence = 1, Creator = 1001 };
        var omnetPacket = OmnetCodec.EncodeObjectAdd(1001, 2, 0,
            OmnetConstants.POSITION_LAST, stamp, objId, sipduData);

        var omnetMsg = OmnetCodec.Decode(omnetPacket);
        var sipdu = WhiteboardCodec.DecodeSipdu(omnetMsg.Data);

        Assert.AreEqual(WhiteboardConstants.SIPDU_WORKSPACE_CREATE, sipdu.Type);
        Assert.AreEqual(1024, sipdu.WorkspaceCreate.WorkspaceWidth);
        Assert.AreEqual(768, sipdu.WorkspaceCreate.WorkspaceHeight);
    }
}
