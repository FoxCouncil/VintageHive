// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VintageHive.Utilities;

namespace VintageHive.Proxy.Printer;

internal class EscPosRenderer
{
    const int RENDER_DPI = 360;
    const float PAGE_WIDTH_INCHES = 8.5f;
    const float PAGE_HEIGHT_INCHES = 11.0f;
    const int PAGE_WIDTH_DOTS = (int)(PAGE_WIDTH_INCHES * RENDER_DPI);   // 3060
    const int PAGE_HEIGHT_DOTS = (int)(PAGE_HEIGHT_INCHES * RENDER_DPI); // 3960

    // Printer state
    float _headX;
    float _headY;
    float _leftMargin;
    float _rightMargin;
    float _lineSpacing;
    int _charWidth;
    bool _bold;
    bool _italic;
    bool _underline;
    bool _ibmMode;

    // Pages
    readonly List<Image<Rgba32>> _pages = [];
    Image<Rgba32> _currentPage;

    // Font - loaded from embedded resource (same as BannerGenerator)
    static readonly Lazy<FontFamily> _fontFamily = new(() =>
    {
        var fontData = Resources.GetStaticsResourceData("fonts.PressStart2P-Regular.ttf");
        var collection = new FontCollection();
        using var ms = new MemoryStream(fontData);
        return collection.Add(ms);
    });

    public static async Task<byte[]> RenderToPdfAsync(byte[] data, bool ibmMode, string spoolerPath, int jobId)
    {
        var renderer = new EscPosRenderer { _ibmMode = ibmMode };
        renderer.Reset();
        renderer.Process(data);

        if (renderer._pages.Count == 0)
        {
            return null;
        }

        var psPath = $"{spoolerPath}{jobId}_escp.ps";
        var pdfPath = $"{spoolerPath}{jobId}_escp.pdf";

        // Write PostScript with embedded monochrome image data
        using (var psStream = VFS.FileWrite(psPath))
        {
            renderer.WritePagesAsPostScript(psStream);
            await psStream.FlushAsync();
        }

        // Convert PS to PDF via GhostScript
        GhostScriptNative.ConvertPsToPdf(VFS.GetFullPath(psPath), VFS.GetFullPath(pdfPath));

        var pdfData = await VFS.FileReadDataAsync(pdfPath);

        // Cleanup temp files
        VFS.FileDelete(psPath);
        VFS.FileDelete(pdfPath);

        // Dispose page images
        foreach (var page in renderer._pages)
        {
            page.Dispose();
        }

        return pdfData;
    }

    void Reset()
    {
        foreach (var page in _pages)
        {
            page.Dispose();
        }

        _pages.Clear();
        _headX = 0;
        _headY = 0;
        _leftMargin = 0;
        _rightMargin = PAGE_WIDTH_DOTS;
        _lineSpacing = RENDER_DPI / 6f;  // 1/6 inch default
        _charWidth = RENDER_DPI / 10;    // 10 CPI default
        _bold = false;
        _italic = false;
        _underline = false;
        NewPage();
    }

    void NewPage()
    {
        _currentPage = new Image<Rgba32>(PAGE_WIDTH_DOTS, PAGE_HEIGHT_DOTS, SixLabors.ImageSharp.Color.White);
        _pages.Add(_currentPage);
        _headX = _leftMargin;
        _headY = 0;
    }

    void CheckPageBreak()
    {
        if (_headY + _lineSpacing > PAGE_HEIGHT_DOTS)
        {
            NewPage();
        }
    }

    #region Main Processing Loop

    void Process(byte[] data)
    {
        int i = 0;

        while (i < data.Length)
        {
            byte b = data[i];

            switch (b)
            {
                case 0x1B: // ESC
                {
                    i = ProcessEscape(data, i);
                }
                break;

                case 0x0D: // CR
                {
                    _headX = _leftMargin;
                    i++;
                }
                break;

                case 0x0A: // LF
                {
                    _headY += _lineSpacing;
                    CheckPageBreak();
                    i++;
                }
                break;

                case 0x0C: // FF
                {
                    NewPage();
                    i++;
                }
                break;

                case 0x09: // HT (horizontal tab — advance to next 8-char stop)
                {
                    int tabStops = (int)(_headX / _charWidth / 8) + 1;
                    _headX = tabStops * 8 * _charWidth;
                    i++;
                }
                break;

                case 0x08: // BS (backspace)
                {
                    _headX = Math.Max(_leftMargin, _headX - _charWidth);
                    i++;
                }
                break;

                default:
                {
                    if (b >= 0x20)
                    {
                        PrintCharacter(b);
                    }

                    i++;
                }
                break;
            }
        }
    }

    #endregion

    #region Escape Sequence Dispatch

    int ProcessEscape(byte[] data, int pos)
    {
        if (pos + 1 >= data.Length)
        {
            return pos + 1;
        }

        byte cmd = data[pos + 1];

        switch (cmd)
        {
            case 0x40: // ESC @ — Initialize printer
            {
                Reset();
                return pos + 2;
            }

            // ---- Bit image commands (ESC/P) ----
            case (byte)'*': // ESC * m nL nH d... — Select bit image
            {
                return ProcessBitImageEscP(data, pos);
            }

            // ---- Bit image commands (IBM ProPrinter) ----
            case (byte)'K': // ESC K nL nH d... — Single density 60 DPI
            {
                return ProcessBitImageIbm(data, pos, 60);
            }

            case (byte)'L': // ESC L nL nH d... — Double density 120 DPI
            {
                return ProcessBitImageIbm(data, pos, 120);
            }

            case (byte)'Y': // ESC Y nL nH d... — High-speed double 120 DPI
            {
                return ProcessBitImageIbm(data, pos, 120);
            }

            case (byte)'Z': // ESC Z nL nH d... — Quadruple density 240 DPI
            {
                return ProcessBitImageIbm(data, pos, 240);
            }

            // ---- Vertical motion ----
            case (byte)'J': // ESC J n — Advance print position vertically
            {
                if (pos + 2 < data.Length)
                {
                    // ESC/P: n/180 inch, IBM: n/216 inch
                    float units = _ibmMode ? 216f : 180f;
                    _headY += data[pos + 2] * (RENDER_DPI / units);
                    CheckPageBreak();
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'j': // ESC j n — Reverse feed n/180 inch (ESC/P only)
            {
                if (pos + 2 < data.Length)
                {
                    _headY -= data[pos + 2] * (RENDER_DPI / 180f);
                    _headY = Math.Max(0, _headY);
                    return pos + 3;
                }

                return pos + 2;
            }

            // ---- Horizontal motion ----
            case (byte)'$': // ESC $ nL nH — Set absolute horizontal position (1/60 inch units)
            {
                if (pos + 3 < data.Length)
                {
                    int n = data[pos + 2] + data[pos + 3] * 256;
                    _headX = _leftMargin + n * (RENDER_DPI / 60f);
                    return pos + 4;
                }

                return pos + 2;
            }

            case (byte)'\\': // ESC \ nL nH — Set relative horizontal position (1/120 inch units)
            {
                if (pos + 3 < data.Length)
                {
                    int n = data[pos + 2] + data[pos + 3] * 256;

                    if (n > 32767)
                    {
                        n -= 65536; // Signed 16-bit
                    }

                    _headX += n * (RENDER_DPI / 120f);
                    _headX = Math.Max(_leftMargin, _headX);
                    return pos + 4;
                }

                return pos + 2;
            }

            // ---- Margins ----
            case (byte)'l': // ESC l n — Set left margin (character columns)
            {
                if (pos + 2 < data.Length)
                {
                    _leftMargin = data[pos + 2] * _charWidth;
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'Q': // ESC Q n — Set right margin (character columns)
            {
                if (pos + 2 < data.Length)
                {
                    _rightMargin = data[pos + 2] * _charWidth;

                    if (_rightMargin <= 0 || _rightMargin > PAGE_WIDTH_DOTS)
                    {
                        _rightMargin = PAGE_WIDTH_DOTS;
                    }

                    return pos + 3;
                }

                return pos + 2;
            }

            // ---- Line spacing ----
            case (byte)'0': // ESC 0 — Set line spacing to 1/8 inch
            {
                _lineSpacing = RENDER_DPI / 8f;
                return pos + 2;
            }

            case (byte)'1': // ESC 1 — Set line spacing to 7/72 inch
            {
                _lineSpacing = RENDER_DPI * 7f / 72f;
                return pos + 2;
            }

            case (byte)'2': // ESC 2 — Set line spacing to 1/6 inch (default)
            {
                _lineSpacing = RENDER_DPI / 6f;
                return pos + 2;
            }

            case (byte)'A': // ESC A n — Set line spacing to n/72 inch (IBM) or n/60 inch (ESC/P)
            {
                if (pos + 2 < data.Length)
                {
                    float units = _ibmMode ? 72f : 60f;
                    _lineSpacing = data[pos + 2] * (RENDER_DPI / units);
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'3': // ESC 3 n — Set line spacing to n/180 inch (ESC/P) or n/216 inch (IBM)
            {
                if (pos + 2 < data.Length)
                {
                    float units = _ibmMode ? 216f : 180f;
                    _lineSpacing = data[pos + 2] * (RENDER_DPI / units);
                    return pos + 3;
                }

                return pos + 2;
            }

            // ---- Text attributes ----
            case (byte)'E': // ESC E — Select bold
            {
                _bold = true;
                return pos + 2;
            }

            case (byte)'F': // ESC F — Cancel bold
            {
                _bold = false;
                return pos + 2;
            }

            case (byte)'4': // ESC 4 — Select italic
            {
                _italic = true;
                return pos + 2;
            }

            case (byte)'5': // ESC 5 — Cancel italic
            {
                _italic = false;
                return pos + 2;
            }

            case (byte)'-': // ESC - n — Underline on/off
            {
                if (pos + 2 < data.Length)
                {
                    _underline = data[pos + 2] == 1;
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'G': // ESC G — Select double-strike (treat as bold)
            {
                _bold = true;
                return pos + 2;
            }

            case (byte)'H': // ESC H — Cancel double-strike
            {
                _bold = false;
                return pos + 2;
            }

            // ---- Master select ----
            case (byte)'!': // ESC ! n — Master select
            {
                if (pos + 2 < data.Length)
                {
                    byte n = data[pos + 2];

                    _bold = (n & 0x08) != 0;
                    _italic = (n & 0x40) != 0;
                    _underline = (n & 0x80) != 0;

                    if ((n & 0x01) != 0)
                    {
                        _charWidth = RENDER_DPI / 12; // 12 CPI (Elite)
                    }
                    else if ((n & 0x04) != 0)
                    {
                        _charWidth = RENDER_DPI * 10 / 170; // ~17 CPI (Condensed, approximate)
                    }
                    else
                    {
                        _charWidth = RENDER_DPI / 10; // 10 CPI (Pica)
                    }

                    if ((n & 0x20) != 0)
                    {
                        _charWidth *= 2; // Double-wide
                    }

                    return pos + 3;
                }

                return pos + 2;
            }

            // ---- Character pitch ----
            case (byte)'P': // ESC P — Select 10 CPI
            {
                _charWidth = RENDER_DPI / 10;
                return pos + 2;
            }

            case (byte)'M': // ESC M — Select 12 CPI (Elite)
            {
                _charWidth = RENDER_DPI / 12;
                return pos + 2;
            }

            case (byte)'g': // ESC g — Select 15 CPI
            {
                _charWidth = RENDER_DPI / 15;
                return pos + 2;
            }

            case (byte)'W': // ESC W n — Double-wide on/off
            {
                if (pos + 2 < data.Length)
                {
                    if (data[pos + 2] == 1)
                    {
                        _charWidth *= 2;
                    }

                    return pos + 3;
                }

                return pos + 2;
            }

            // ---- Typeface (consume but ignore — we only have one font) ----
            case (byte)'k': // ESC k n — Select typeface
            {
                if (pos + 2 < data.Length)
                {
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'x': // ESC x n — Select NLQ/Draft mode
            {
                if (pos + 2 < data.Length)
                {
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'p': // ESC p n — Proportional mode on/off
            {
                if (pos + 2 < data.Length)
                {
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'t': // ESC t n — Select character table
            {
                if (pos + 2 < data.Length)
                {
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'R': // ESC R n — Select international character set
            {
                if (pos + 2 < data.Length)
                {
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'C': // ESC C n — Set page length in lines
            {
                if (pos + 2 < data.Length)
                {
                    // ESC C 0 n — set page length in inches
                    if (data[pos + 2] == 0 && pos + 3 < data.Length)
                    {
                        return pos + 4;
                    }

                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'N': // ESC N n — Set bottom margin
            {
                if (pos + 2 < data.Length)
                {
                    return pos + 3;
                }

                return pos + 2;
            }

            case (byte)'O': // ESC O — Cancel bottom margin
            {
                return pos + 2;
            }

            default:
            {
                // Unknown escape sequence — skip 2 bytes and hope for the best
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(EscPosRenderer), $"Unknown ESC sequence: ESC 0x{cmd:X2} at position {pos}");
                return pos + 2;
            }
        }
    }

    #endregion

    #region Bit Image Rendering

    int ProcessBitImageEscP(byte[] data, int pos)
    {
        // ESC * m nL nH d1 d2 ...
        if (pos + 4 >= data.Length)
        {
            return data.Length;
        }

        byte m = data[pos + 2];
        int numColumns = data[pos + 3] + data[pos + 4] * 256;

        int bytesPerColumn;
        int horizontalDpi;
        int verticalDpi;

        switch (m)
        {
            case 0:  // Single density, 8-pin, 60 DPI
            {
                horizontalDpi = 60; verticalDpi = 60; bytesPerColumn = 1;
            }
            break;

            case 1:  // Double density, 8-pin, 120 DPI
            {
                horizontalDpi = 120; verticalDpi = 60; bytesPerColumn = 1;
            }
            break;

            case 2:  // Double density, 8-pin, 120 DPI (double speed, same resolution)
            {
                horizontalDpi = 120; verticalDpi = 60; bytesPerColumn = 1;
            }
            break;

            case 3:  // Quadruple density, 8-pin, 240 DPI
            {
                horizontalDpi = 240; verticalDpi = 60; bytesPerColumn = 1;
            }
            break;

            case 32: // CRT I, 24-pin, 60 DPI
            {
                horizontalDpi = 60; verticalDpi = 180; bytesPerColumn = 3;
            }
            break;

            case 33: // CRT II, 24-pin, 120 DPI
            {
                horizontalDpi = 120; verticalDpi = 180; bytesPerColumn = 3;
            }
            break;

            case 38: // CRT III, 24-pin, 90 DPI
            {
                horizontalDpi = 90; verticalDpi = 180; bytesPerColumn = 3;
            }
            break;

            case 39: // Triple density, 24-pin, 180 DPI
            {
                horizontalDpi = 180; verticalDpi = 180; bytesPerColumn = 3;
            }
            break;

            case 40: // Hex density, 24-pin, 360 DPI
            {
                horizontalDpi = 360; verticalDpi = 180; bytesPerColumn = 3;
            }
            break;

            default: // Unknown mode — treat as single density 8-pin
            {
                horizontalDpi = 60; verticalDpi = 60; bytesPerColumn = 1;
            }
            break;
        }

        int dataStart = pos + 5;
        int dataLength = numColumns * bytesPerColumn;
        int dataEnd = Math.Min(dataStart + dataLength, data.Length);

        RenderBitImage(data, dataStart, dataEnd, numColumns, bytesPerColumn, horizontalDpi, verticalDpi);

        return dataEnd;
    }

    int ProcessBitImageIbm(byte[] data, int pos, int horizontalDpi)
    {
        // ESC K/L/Y/Z nL nH d1 d2 ... — always 8-pin (1 byte per column)
        if (pos + 3 >= data.Length)
        {
            return data.Length;
        }

        int numColumns = data[pos + 2] + data[pos + 3] * 256;

        int dataStart = pos + 4;
        int dataLength = numColumns;
        int dataEnd = Math.Min(dataStart + dataLength, data.Length);

        RenderBitImage(data, dataStart, dataEnd, numColumns, 1, horizontalDpi, 60);

        return dataEnd;
    }

    void RenderBitImage(byte[] data, int dataStart, int dataEnd, int numColumns, int bytesPerColumn, int horizontalDpi, int verticalDpi)
    {
        float xScale = (float)RENDER_DPI / horizontalDpi;
        float yScale = (float)RENDER_DPI / verticalDpi;
        int dotSize = Math.Max(1, (int)Math.Ceiling(Math.Min(xScale, yScale)));

        for (int col = 0; col < numColumns; col++)
        {
            int colDataStart = dataStart + col * bytesPerColumn;

            if (colDataStart >= dataEnd)
            {
                break;
            }

            int pixelX = (int)(_headX + col * xScale);

            for (int byteIdx = 0; byteIdx < bytesPerColumn && (colDataStart + byteIdx) < dataEnd; byteIdx++)
            {
                byte d = data[colDataStart + byteIdx];

                for (int bit = 0; bit < 8; bit++)
                {
                    if ((d & (0x80 >> bit)) != 0)
                    {
                        int pin = byteIdx * 8 + bit;
                        int pixelY = (int)(_headY + pin * yScale);
                        PlotDot(pixelX, pixelY, dotSize);
                    }
                }
            }
        }

        // Advance head past the image data (horizontal only; no vertical advance)
        _headX += numColumns * xScale;
    }

    void PlotDot(int x, int y, int size)
    {
        for (int dx = 0; dx < size; dx++)
        {
            for (int dy = 0; dy < size; dy++)
            {
                int px = x + dx;
                int py = y + dy;

                if (px >= 0 && px < PAGE_WIDTH_DOTS && py >= 0 && py < PAGE_HEIGHT_DOTS)
                {
                    _currentPage[px, py] = new Rgba32(0, 0, 0, 255);
                }
            }
        }
    }

    #endregion

    #region Text Rendering

    void PrintCharacter(byte ch)
    {
        // Word-wrap: if character won't fit, advance to next line
        if (_headX + _charWidth > _rightMargin)
        {
            _headX = _leftMargin;
            _headY += _lineSpacing;
            CheckPageBreak();
        }

        var font = CreateFont();
        var options = new RichTextOptions(font)
        {
            Origin = new System.Numerics.Vector2(_headX, _headY),
            Dpi = RENDER_DPI
        };

        _currentPage.Mutate(ctx =>
        {
            ctx.DrawText(options, ((char)ch).ToString(), SixLabors.ImageSharp.Color.Black);
        });

        // If underline is active, draw a line under the character
        if (_underline)
        {
            float underlineY = _headY + _lineSpacing - 2;
            var pen = Pens.Solid(SixLabors.ImageSharp.Color.Black, 2);

            _currentPage.Mutate(ctx =>
            {
                ctx.DrawLine(pen, new PointF(_headX, underlineY), new PointF(_headX + _charWidth, underlineY));
            });
        }

        _headX += _charWidth;
    }

    Font CreateFont()
    {
        // Calculate point size from character width at render DPI
        // charWidth dots at RENDER_DPI DPI = charWidth/RENDER_DPI inches = charWidth/RENDER_DPI * 72 points
        // PressStart2P is a square pixel font, so height ~= width
        float pointSize = _charWidth * 72f / RENDER_DPI;

        // Clamp to reasonable range
        pointSize = Math.Max(4f, Math.Min(pointSize, 36f));

        return _fontFamily.Value.CreateFont(pointSize, FontStyle.Regular);
    }

    #endregion

    #region PostScript Output

    void WritePagesAsPostScript(FileStream stream)
    {
        using var writer = new StreamWriter(stream, Encoding.ASCII);

        writer.WriteLine("%!PS-Adobe-3.0");
        writer.WriteLine($"%%Pages: {_pages.Count}");
        writer.WriteLine("%%EndComments");

        for (int pageNum = 0; pageNum < _pages.Count; pageNum++)
        {
            var page = _pages[pageNum];

            writer.WriteLine($"%%Page: {pageNum + 1} {pageNum + 1}");
            writer.WriteLine($"<< /PageSize [{PAGE_WIDTH_INCHES * 72} {PAGE_HEIGHT_INCHES * 72}] >> setpagedevice");
            writer.WriteLine("gsave");

            // Scale so that (0,0)-(1,1) maps to full page in PostScript points
            writer.WriteLine($"{PAGE_WIDTH_INCHES * 72} {PAGE_HEIGHT_INCHES * 72} scale");

            // Set paint color to black
            writer.WriteLine("0 setgray");

            // Image parameters: width height polarity matrix datasrc imagemask
            int width = page.Width;
            int height = page.Height;
            int bytesPerRow = (width + 7) / 8;

            writer.WriteLine($"{width} {height} true [{width} 0 0 -{height} 0 {height}]");
            writer.WriteLine($"{{currentfile {bytesPerRow} string readhexstring pop}}");
            writer.WriteLine("imagemask");

            // Write monochrome pixel data as hex strings, row by row
            page.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var sb = new System.Text.StringBuilder(bytesPerRow * 2);

                    for (int byteX = 0; byteX < bytesPerRow; byteX++)
                    {
                        byte b = 0;

                        for (int bit = 0; bit < 8; bit++)
                        {
                            int x = byteX * 8 + bit;

                            if (x < width)
                            {
                                var pixel = row[x];

                                // Threshold: if dark enough, it's a printed dot
                                if ((pixel.R + pixel.G + pixel.B) / 3 < 128)
                                {
                                    b |= (byte)(0x80 >> bit);
                                }
                            }
                        }

                        sb.Append(b.ToString("x2"));
                    }

                    writer.WriteLine(sb.ToString());
                }
            });

            writer.WriteLine("grestore");
            writer.WriteLine("showpage");
        }

        writer.WriteLine("%%EOF");
    }

    #endregion
}
