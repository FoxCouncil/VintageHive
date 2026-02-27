// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Printer;

public enum PrintDataFormat
{
    PostScript,
    EscP,
    IbmProPrinter,
    Pcl,
    PlainText,
    Unknown
}

public static class PrintFormatDetector
{
    public static PrintDataFormat Detect(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return PrintDataFormat.Unknown;
        }

        // PostScript: starts with %!PS
        if (data.Length >= 4 && data[0] == (byte)'%' && data[1] == (byte)'!' && data[2] == (byte)'P' && data[3] == (byte)'S')
        {
            return PrintDataFormat.PostScript;
        }

        // PostScript: might start with Ctrl-D (%04) followed by %!PS
        if (data.Length >= 5 && data[0] == 0x04 && data[1] == (byte)'%' && data[2] == (byte)'!' && data[3] == (byte)'P' && data[4] == (byte)'S')
        {
            return PrintDataFormat.PostScript;
        }

        // PCL: Universal Exit Language (ESC %-12345X) — always PCL
        if (data.Length >= 9 && data[0] == 0x1B && data[1] == (byte)'%' && data[2] == (byte)'-')
        {
            return PrintDataFormat.Pcl;
        }

        // Scan first 512 bytes for ESC sequences to classify
        int scanLimit = Math.Min(data.Length, 512);

        bool hasEscAt = false;
        bool hasEscStar = false;
        bool hasEscBang = false;
        bool hasIbmGraphics = false;
        bool hasPclCommands = false;
        bool hasEscSequences = false;

        for (int i = 0; i < scanLimit - 1; i++)
        {
            if (data[i] != 0x1B)
            {
                continue;
            }

            hasEscSequences = true;
            byte next = data[i + 1];

            switch (next)
            {
                case 0x40: // ESC @ — ESC/P initialize (unambiguous)
                {
                    hasEscAt = true;
                }
                break;

                case (byte)'*': // ESC * — ESC/P bit image
                {
                    hasEscStar = true;
                }
                break;

                case (byte)'!': // ESC ! — ESC/P master select
                {
                    hasEscBang = true;
                }
                break;

                case (byte)'K': // ESC K — IBM single density graphics
                case (byte)'L': // ESC L — IBM double density graphics
                case (byte)'Y': // ESC Y — IBM high-speed double density
                case (byte)'Z': // ESC Z — IBM quadruple density
                {
                    // Verify: next 2 bytes should be nL nH (column count)
                    if (i + 3 < scanLimit)
                    {
                        hasIbmGraphics = true;
                    }
                }
                break;

                case (byte)'&': // ESC & — PCL parameterized command
                case (byte)'(': // ESC ( — PCL parameterized command
                case (byte)')': // ESC ) — PCL parameterized command
                {
                    hasPclCommands = true;
                }
                break;
            }
        }

        // ESC @ is the definitive ESC/P indicator
        if (hasEscAt || hasEscStar || hasEscBang)
        {
            return PrintDataFormat.EscP;
        }

        // IBM ProPrinter graphics without ESC @ init
        if (hasIbmGraphics)
        {
            return PrintDataFormat.IbmProPrinter;
        }

        // PCL parameterized commands
        if (hasPclCommands)
        {
            return PrintDataFormat.Pcl;
        }

        // PCL reset (ESC E) at the very start — ambiguous with ESC/P bold,
        // but at position 0 it's almost certainly PCL reset
        if (data.Length >= 2 && data[0] == 0x1B && data[1] == (byte)'E' && !hasEscSequences)
        {
            return PrintDataFormat.Pcl;
        }

        // Unknown ESC sequences that didn't match anything specific
        if (hasEscSequences)
        {
            return PrintDataFormat.EscP; // Default ESC-based data to ESC/P
        }

        // Check for printable ASCII (plain text fallback)
        bool isPrintable = true;

        for (int i = 0; i < scanLimit; i++)
        {
            byte b = data[i];

            if (b >= 0x20 && b <= 0x7E)
            {
                continue; // Printable ASCII
            }

            if (b == 0x09 || b == 0x0A || b == 0x0D || b == 0x0C)
            {
                continue; // Tab, LF, CR, FF
            }

            isPrintable = false;
            break;
        }

        if (isPrintable)
        {
            return PrintDataFormat.PlainText;
        }

        return PrintDataFormat.Unknown;
    }
}
