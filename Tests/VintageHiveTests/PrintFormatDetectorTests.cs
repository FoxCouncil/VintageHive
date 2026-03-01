// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Printer;

namespace Printer;

[TestClass]
public class PrintFormatDetectorTests
{
    #region Null / Empty

    [TestMethod]
    public void Detect_Null_ReturnsUnknown()
    {
        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(null));
    }

    [TestMethod]
    public void Detect_Empty_ReturnsUnknown()
    {
        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(Array.Empty<byte>()));
    }

    #endregion

    #region PostScript

    [TestMethod]
    public void Detect_PostScript_Standard()
    {
        // %!PS-Adobe-3.0
        var data = Encoding.ASCII.GetBytes("%!PS-Adobe-3.0\n%%Creator: Test");

        Assert.AreEqual(PrintDataFormat.PostScript, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PostScript_MinimalHeader()
    {
        // Just the minimum %!PS
        var data = Encoding.ASCII.GetBytes("%!PS\n");

        Assert.AreEqual(PrintDataFormat.PostScript, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PostScript_WithCtrlD()
    {
        // Ctrl-D (\x04) followed by %!PS
        var data = new byte[] { 0x04, (byte)'%', (byte)'!', (byte)'P', (byte)'S', (byte)'\n' };

        Assert.AreEqual(PrintDataFormat.PostScript, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region PCL

    [TestMethod]
    public void Detect_Pcl_UniversalExitLanguage()
    {
        // ESC %-12345X is the PCL Universal Exit Language sequence
        var data = new byte[] { 0x1B, (byte)'%', (byte)'-', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'X' };

        Assert.AreEqual(PrintDataFormat.Pcl, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_Pcl_ParameterizedEscAmpersand()
    {
        // ESC & — PCL parameterized command
        var data = new byte[64];
        data[0] = (byte)'@'; // some leading byte
        data[10] = 0x1B;
        data[11] = (byte)'&';

        Assert.AreEqual(PrintDataFormat.Pcl, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_Pcl_ParameterizedEscOpenParen()
    {
        // ESC ( — PCL parameterized command
        var data = new byte[64];
        data[10] = 0x1B;
        data[11] = (byte)'(';

        Assert.AreEqual(PrintDataFormat.Pcl, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_Pcl_ParameterizedEscCloseParen()
    {
        // ESC ) — PCL parameterized command
        var data = new byte[64];
        data[10] = 0x1B;
        data[11] = (byte)')';

        Assert.AreEqual(PrintDataFormat.Pcl, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_Pcl_ResetAtStart_FallsToEscP()
    {
        // ESC E at position 0 — the scan loop detects it as an ESC sequence first,
        // so it falls through to the EscP default for unknown ESC sequences.
        // The PCL reset check requires !hasEscSequences which is already true.
        var data = new byte[] { 0x1B, (byte)'E', (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region ESC/P

    [TestMethod]
    public void Detect_EscP_InitializeCommand()
    {
        // ESC @ — ESC/P initialize printer
        var data = new byte[64];
        data[0] = 0x1B;
        data[1] = 0x40; // @

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_EscP_BitImageCommand()
    {
        // ESC * — ESC/P bit image mode
        var data = new byte[64];
        data[0] = 0x1B;
        data[1] = (byte)'*';

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_EscP_MasterSelectCommand()
    {
        // ESC ! — ESC/P master select
        var data = new byte[64];
        data[0] = 0x1B;
        data[1] = (byte)'!';

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_EscP_UnknownEscSequenceFallback()
    {
        // Unknown ESC sequence defaults to ESC/P
        var data = new byte[64];
        data[10] = 0x1B;
        data[11] = (byte)'Q'; // Not recognized specifically

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region IBM ProPrinter

    [TestMethod]
    public void Detect_IbmProPrinter_SingleDensityGraphics()
    {
        // ESC K nL nH — IBM single density graphics
        var data = new byte[64];
        data[10] = 0x1B;
        data[11] = (byte)'K';
        data[12] = 0x10; // nL
        data[13] = 0x00; // nH

        Assert.AreEqual(PrintDataFormat.IbmProPrinter, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_IbmProPrinter_DoubleDensityGraphics()
    {
        // ESC L nL nH — IBM double density graphics
        var data = new byte[64];
        data[10] = 0x1B;
        data[11] = (byte)'L';
        data[12] = 0x20; // nL
        data[13] = 0x00; // nH

        Assert.AreEqual(PrintDataFormat.IbmProPrinter, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_IbmProPrinter_HighSpeedDoubleDensity()
    {
        // ESC Y nL nH — IBM high-speed double density
        var data = new byte[64];
        data[10] = 0x1B;
        data[11] = (byte)'Y';
        data[12] = 0x10;
        data[13] = 0x00;

        Assert.AreEqual(PrintDataFormat.IbmProPrinter, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_IbmProPrinter_QuadrupleDensity()
    {
        // ESC Z nL nH — IBM quadruple density
        var data = new byte[64];
        data[10] = 0x1B;
        data[11] = (byte)'Z';
        data[12] = 0x40;
        data[13] = 0x00;

        Assert.AreEqual(PrintDataFormat.IbmProPrinter, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region PlainText

    [TestMethod]
    public void Detect_PlainText_SimpleAscii()
    {
        var data = Encoding.ASCII.GetBytes("Hello, World!\r\n");

        Assert.AreEqual(PrintDataFormat.PlainText, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PlainText_WithTabs()
    {
        var data = Encoding.ASCII.GetBytes("Column1\tColumn2\tColumn3\r\n");

        Assert.AreEqual(PrintDataFormat.PlainText, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PlainText_WithFormFeed()
    {
        // Form feed (0x0C) is allowed in plain text
        var data = new byte[] { (byte)'P', (byte)'a', (byte)'g', (byte)'e', (byte)'1', 0x0C, (byte)'P', (byte)'a', (byte)'g', (byte)'e', (byte)'2' };

        Assert.AreEqual(PrintDataFormat.PlainText, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region Unknown / Binary

    [TestMethod]
    public void Detect_Unknown_BinaryGarbage()
    {
        // Non-printable bytes that don't match any signature
        var data = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_Unknown_SingleByte()
    {
        // Single non-printable byte
        var data = new byte[] { 0xFF };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Detect_EscP_TakesPriorityOverIbmGraphics()
    {
        // When both ESC @ (ESC/P init) and ESC K (IBM graphics) are present,
        // ESC/P should win since ESC @ is checked first
        var data = new byte[64];
        data[0] = 0x1B;
        data[1] = 0x40; // ESC @ — ESC/P init
        data[10] = 0x1B;
        data[11] = (byte)'K'; // ESC K — IBM graphics
        data[12] = 0x10;
        data[13] = 0x00;

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PostScript_CheckedBeforeEscSequences()
    {
        // PostScript header checked before scanning for ESC sequences
        var data = Encoding.ASCII.GetBytes("%!PS-Adobe-3.0\nsome data");

        Assert.AreEqual(PrintDataFormat.PostScript, PrintFormatDetector.Detect(data));
    }

    #endregion
}
