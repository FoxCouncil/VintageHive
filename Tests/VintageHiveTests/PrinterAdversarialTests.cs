// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// Adversarial coverage for the pure printer parsers: PrintFormatDetector.Detect (magic-number
// boundaries, truncated signatures, 512-byte scan window, binary/high-bit data) and
// LpdProxy.ParseControlFile (malformed control lines, duplicate/out-of-order/unknown codes,
// CR-only delimiters, embedded control bytes, non-ASCII). Happy paths live in
// PrintFormatDetectorTests / LpdControlFileTests and are not repeated here.

using System.Text;
using VintageHive.Proxy.Printer;

namespace Adversarial2.Printer;

[TestClass]
public class PrinterAdversarialTests
{
    private static (string user, string job, string host, string src) ParseControl(byte[] data)
    {
        string user = null!, job = null!, host = null!, src = null!;
        LpdProxy.ParseControlFile(data, ref user, ref job, ref host, ref src);
        return (user, job, host, src);
    }

    private static (string user, string job, string host, string src) ParseControl(string ascii)
    {
        return ParseControl(Encoding.ASCII.GetBytes(ascii));
    }

    #region PrintFormatDetector - truncated / boundary magic numbers

    [TestMethod]
    public void Detect_PostScript_ThreeBytePrefix_NotEnoughForMagic()
    {
        // "%!P" is one byte short of the 4-byte %!PS magic. It is all printable, so it degrades
        // to PlainText rather than PostScript.
        var data = new byte[] { (byte)'%', (byte)'!', (byte)'P' };

        Assert.AreEqual(PrintDataFormat.PlainText, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PostScript_ExactlyFourBytes_MagicOnly()
    {
        // Minimum length that satisfies the >= 4 guard, nothing after the magic.
        var data = new byte[] { (byte)'%', (byte)'!', (byte)'P', (byte)'S' };

        Assert.AreEqual(PrintDataFormat.PostScript, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PostScript_CtrlD_FourBytes_TooShortForVariant()
    {
        // 0x04 %!P (4 bytes) fails the >= 5 Ctrl-D variant guard. The leading 0x04 is a control
        // byte, so the printable-ASCII fallback also fails -> Unknown.
        var data = new byte[] { 0x04, (byte)'%', (byte)'!', (byte)'P' };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_CtrlD_ThenPlainText_IsUnknown_NotPostScript()
    {
        // Ctrl-D not followed by the %!PS magic. 0x04 is non-printable -> Unknown.
        var data = new byte[] { 0x04, (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PclUel_EightBytes_OneShort_FallsToEscP()
    {
        // ESC %-12345 is 8 bytes: one short of the >= 9 UEL guard. The scan loop then sees ESC
        // followed by '%' (an unhandled escape) and defaults unknown-escape data to ESC/P.
        var data = new byte[] { 0x1B, (byte)'%', (byte)'-', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5' };

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PclUel_MissingMinus_IsNotPcl()
    {
        // ESC % without the '-' third byte is not the UEL. '%' is an unhandled escape -> EscP.
        var data = new byte[] { 0x1B, (byte)'%', (byte)'+', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'X' };

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_PclUel_TakesPriorityOverEscAtInit()
    {
        // UEL is matched by an early return before the ESC-sequence scan, so it wins even when an
        // ESC @ (ESC/P init) also appears later in the buffer.
        var data = new byte[] { 0x1B, (byte)'%', (byte)'-', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'X', 0x1B, 0x40 };

        Assert.AreEqual(PrintDataFormat.Pcl, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region PrintFormatDetector - IBM graphics length verification

    [TestMethod]
    public void Detect_IbmGraphics_TruncatedColumnCount_FallsToEscP()
    {
        // ESC K needs the i+3 < scanLimit room for the nL nH column count. With only ESC K nL
        // (3 bytes) that verification fails, so it drops to the unknown-escape ESC/P default.
        var data = new byte[] { 0x1B, (byte)'K', 0x10 };

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_IbmGraphics_ExactlyEnoughRoom_AtStart()
    {
        // ESC K nL nH = 4 bytes at offset 0 satisfies i+3 < scanLimit (0+3 < 4) -> IBM.
        var data = new byte[] { 0x1B, (byte)'K', 0x10, 0x00 };

        Assert.AreEqual(PrintDataFormat.IbmProPrinter, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region PrintFormatDetector - ESC scan window edges

    [TestMethod]
    public void Detect_TrailingEscByte_IsNeverScanned_TextBecomesUnknown()
    {
        // The scan loop runs i < scanLimit - 1, so an ESC that is the final byte is never paired
        // with a following byte and never flags an escape. The printable check then trips on it,
        // flipping otherwise-plain text to Unknown.
        var data = new byte[] { (byte)'A', (byte)'B', 0x1B };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_SingleEscByte_IsUnknown()
    {
        // A lone ESC: too short for every magic guard, never scanned, not printable.
        var data = new byte[] { 0x1B };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_SingleEscInOtherwisePlainText_ReclassifiesAsEscP()
    {
        // One stray ESC (followed by an unhandled letter) is enough to pull a text document out of
        // the PlainText bucket into ESC/P.
        var data = Encoding.ASCII.GetBytes("Hello\x1BQ world this is text");

        Assert.AreEqual(PrintDataFormat.EscP, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_EscSequenceBeyond512_IsIgnored()
    {
        // Only the first 512 bytes are scanned for escapes. An ESC @ at offset 600 is invisible,
        // so a buffer that is otherwise printable stays PlainText.
        var data = new byte[640];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)'X';
        }

        data[600] = 0x1B;
        data[601] = 0x40; // ESC @ that should have meant ESC/P, but it is past the scan window

        Assert.AreEqual(PrintDataFormat.PlainText, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_BinaryBeyond512_IsIgnored_StillPlainText()
    {
        // The printable-ASCII check also stops at 512 bytes. Garbage past that window does not
        // count against classification.
        var data = new byte[600];

        for (int i = 0; i < 512; i++)
        {
            data[i] = (byte)'A';
        }

        for (int i = 512; i < data.Length; i++)
        {
            data[i] = 0xFF; // non-printable, but never examined
        }

        Assert.AreEqual(PrintDataFormat.PlainText, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region PrintFormatDetector - binary / non-ASCII where text expected

    [TestMethod]
    public void Detect_HighBitLatin1_IsUnknown()
    {
        // Latin-1 "cafe" with an accented e (0xE9) is not 7-bit printable and has no escape -> Unknown.
        var data = new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_EmbeddedNullInText_IsUnknown()
    {
        // A NUL byte in the middle of otherwise printable text fails the printable check -> Unknown.
        var data = new byte[] { (byte)'H', (byte)'i', 0x00, (byte)'t', (byte)'h', (byte)'e', (byte)'r', (byte)'e' };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_DelCharacter_IsUnknown()
    {
        // 0x7F (DEL) is just above the printable range (0x20-0x7E) and is not a whitelisted control.
        var data = new byte[] { (byte)'o', (byte)'k', 0x7F };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    [TestMethod]
    public void Detect_BellControlChar_IsUnknown()
    {
        // 0x07 (BEL) is a control char that is not in the tab/lf/cr/ff whitelist.
        var data = new byte[] { (byte)'a', 0x07, (byte)'b' };

        Assert.AreEqual(PrintDataFormat.Unknown, PrintFormatDetector.Detect(data));
    }

    #endregion

    #region LpdProxy.ParseControlFile - malformed / hostile control lines

    [TestMethod]
    public void ParseControl_DuplicateUserFields_LastWins()
    {
        var (user, _, _, _) = ParseControl("Pfox\nPimpostor\n");

        Assert.AreEqual("impostor", user);
    }

    [TestMethod]
    public void ParseControl_OutOfOrderFields_StillParsed()
    {
        // Field order is irrelevant; each line stands alone.
        var (user, job, host, src) = ParseControl("Nreport.ps\nJThe Job\nPfox\nHmyhost\n");

        Assert.AreEqual("fox", user);
        Assert.AreEqual("The Job", job);
        Assert.AreEqual("myhost", host);
        Assert.AreEqual("report.ps", src);
    }

    [TestMethod]
    public void ParseControl_NoTrailingNewline_LastLineStillParsed()
    {
        // A hostile/lazy client that omits the final LF: the last field is still recovered.
        var (user, _, _, _) = ParseControl("Pfox");

        Assert.AreEqual("fox", user);
    }

    [TestMethod]
    public void ParseControl_CrOnlyDelimiters_CollapseIntoOneValueWithEmbeddedCr()
    {
        // Split is on '\n' only. CR-only "line endings" never split, so the whole payload becomes a
        // single P-value; only the trailing CR is trimmed. The embedded CR survives inside the value.
        var (user, _, _, _) = ParseControl("Pfox\rHmyhost\r");

        Assert.AreEqual("fox\rHmyhost", user);
    }

    [TestMethod]
    public void ParseControl_CodeThenCrOnly_YieldsEmptyStringNotNull()
    {
        // "H\r\n": the line "H\r" is length 2, so it is parsed; the value trims to "" (empty),
        // which is distinct from the null you get for a bare "H\n" (length 1, skipped).
        var (_, _, host, _) = ParseControl("H\r\nPfox\n");

        Assert.AreEqual(string.Empty, host);
    }

    [TestMethod]
    public void ParseControl_BareCodeNoValue_LeavesFieldNull()
    {
        // "P\n" splits to "P" (length 1) which is skipped entirely -> user stays null.
        var (user, _, _, _) = ParseControl("P\n");

        Assert.IsNull(user);
    }

    [TestMethod]
    public void ParseControl_LowercaseCode_IsNotTreatedAsUser()
    {
        // 'p' (print with pr) is a distinct consumed-but-ignored code from 'P' (user name). Case matters.
        var (user, _, _, _) = ParseControl("pfox\n");

        Assert.IsNull(user);
    }

    [TestMethod]
    public void ParseControl_UnknownCodeLetters_AreIgnored()
    {
        // Codes with no switch arm (X, Z, digit) fall through and set nothing.
        var (user, job, host, src) = ParseControl("Xhello\nZworld\n9nine\n");

        Assert.IsNull(user);
        Assert.IsNull(job);
        Assert.IsNull(host);
        Assert.IsNull(src);
    }

    [TestMethod]
    public void ParseControl_EmbeddedNullInValue_IsPreserved()
    {
        // The parser does not sanitize control bytes inside a value; a NUL rides along in the string.
        var (user, _, _, _) = ParseControl(new byte[] { (byte)'P', (byte)'a', 0x00, (byte)'b', (byte)'\n' });

        Assert.AreEqual("a\0b", user);
    }

    [TestMethod]
    public void ParseControl_NonAsciiBytes_DecodeLossilyToQuestionMark()
    {
        // Encoding.ASCII maps every byte > 0x7F to '?'. A high-bit host name is silently mangled.
        var (_, _, host, _) = ParseControl(new byte[] { (byte)'H', 0xE9, 0xFF, (byte)'\n' });

        Assert.AreEqual("??", host);
    }

    [TestMethod]
    public void ParseControl_BlankLinesBetweenFields_AreDropped()
    {
        // RemoveEmptyEntries discards the empties produced by consecutive newlines.
        var (user, _, host, _) = ParseControl("Pfox\n\n\n\nHmyhost\n");

        Assert.AreEqual("fox", user);
        Assert.AreEqual("myhost", host);
    }

    [TestMethod]
    public void ParseControl_ValueWithInternalSpacesAndTabs_Preserved()
    {
        // Only a trailing CR is trimmed; interior whitespace and a trailing tab are kept verbatim.
        var (_, job, _, _) = ParseControl("J  spaced  out\tjob\t\n");

        Assert.AreEqual("  spaced  out\tjob\t", job);
    }

    [TestMethod]
    public void ParseControl_NullData_ThrowsArgumentNull()
    {
        // ParseControlFile forwards straight into Encoding.ASCII.GetString(byte[]), which rejects
        // null. The socket path never supplies null, so this documents the raw contract.
        string user = null!, job = null!, host = null!, src = null!;

        Assert.ThrowsExactly<ArgumentNullException>(() => LpdProxy.ParseControlFile(null!, ref user, ref job, ref host, ref src));
    }

    #endregion
}
