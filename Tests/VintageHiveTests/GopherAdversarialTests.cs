// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Gopher;

namespace Adversarial.Gopher;

// Adversarial coverage for the pure Gopher parser/codec surface: GopherMenuItem.Parse/Serialize,
// TryParseProxySelector (/g/<type>/<host>:<port>/<selector>), TryParseGopherUrl, PercentDecodeLatin1,
// and the dot-stuffing menu codecs. These assert observed behavior against malformed/hostile input.

[TestClass]
public class GopherMenuItemAdversarialTests
{
    [TestMethod]
    public void Parse_TypeCharOnly_YieldsEmptyFieldsAndDefaultPort()
    {
        var item = GopherMenuItem.Parse("1");

        Assert.IsNotNull(item);
        Assert.AreEqual('1', item.Type);
        Assert.AreEqual(string.Empty, item.Display);
        Assert.AreEqual(string.Empty, item.Selector);
        Assert.AreEqual(string.Empty, item.Host);
        Assert.AreEqual(70, item.Port);
    }

    [TestMethod]
    public void Parse_NoTabs_TreatsEverythingAfterTypeAsDisplay()
    {
        var item = GopherMenuItem.Parse("1no tabs here at all");

        Assert.IsNotNull(item);
        Assert.AreEqual("no tabs here at all", item.Display);
        Assert.AreEqual(string.Empty, item.Selector);
        Assert.AreEqual(string.Empty, item.Host);
        Assert.AreEqual(70, item.Port);
    }

    [TestMethod]
    public void Parse_ExtraTabsBeyondPort_AreIgnored()
    {
        var item = GopherMenuItem.Parse("1Disp\t/sel\thost\t70\textra1\textra2");

        Assert.IsNotNull(item);
        Assert.AreEqual("Disp", item.Display);
        Assert.AreEqual("/sel", item.Selector);
        Assert.AreEqual("host", item.Host);
        Assert.AreEqual(70, item.Port);
    }

    [TestMethod]
    public void Parse_AllEmptyFields_PortFieldEmptyDefaultsTo70()
    {
        var item = GopherMenuItem.Parse("1\t\t\t");

        Assert.IsNotNull(item);
        Assert.AreEqual(string.Empty, item.Display);
        Assert.AreEqual(string.Empty, item.Selector);
        Assert.AreEqual(string.Empty, item.Host);
        Assert.AreEqual(70, item.Port);
    }

    [TestMethod]
    public void Parse_NonNumericPort_DefaultsTo70()
    {
        Assert.AreEqual(70, GopherMenuItem.Parse("1D\t/s\th\tabc")!.Port);
        Assert.AreEqual(70, GopherMenuItem.Parse("1D\t/s\th\t0x10")!.Port);
        Assert.AreEqual(70, GopherMenuItem.Parse("1D\t/s\th\t8 0")!.Port);
    }

    [TestMethod]
    public void Parse_OverflowPortField_DefaultsTo70()
    {
        // int.TryParse overflows and fails, so the port falls back to 70 rather than wrapping or throwing.
        Assert.AreEqual(70, GopherMenuItem.Parse("1D\t/s\th\t99999999999999999999")!.Port);

        // int.MaxValue + 1 overflows to the same fallback.
        Assert.AreEqual(70, GopherMenuItem.Parse("1D\t/s\th\t2147483648")!.Port);
    }

    [TestMethod]
    public void Parse_IntMaxPort_IsKept()
    {
        Assert.AreEqual(int.MaxValue, GopherMenuItem.Parse("1D\t/s\th\t2147483647")!.Port);
    }

    [TestMethod]
    public void Parse_NegativePort_IsAcceptedVerbatim()
    {
        // Observed: Parse performs no range validation, so a hostile negative port is stored as-is.
        Assert.AreEqual(-1, GopherMenuItem.Parse("1D\t/s\th\t-1")!.Port);
        Assert.AreEqual(-65535, GopherMenuItem.Parse("1D\t/s\th\t-65535")!.Port);
    }

    [TestMethod]
    public void Parse_PortWithSurroundingWhitespace_IsTrimmed()
    {
        Assert.AreEqual(8080, GopherMenuItem.Parse("1D\t/s\th\t  8080  ")!.Port);
    }

    [TestMethod]
    public void Parse_LeadingTab_ConsumesTabAsTypeAndShiftsFields()
    {
        // The first char is consumed as the item Type regardless of what it is; a leading tab therefore shifts
        // every subsequent field left by one, so the intended type/display are silently misread.
        var item = GopherMenuItem.Parse("\tfake\t(NULL)\t0");

        Assert.IsNotNull(item);
        Assert.AreEqual('\t', item.Type);
        Assert.AreEqual("fake", item.Display);
        Assert.AreEqual("(NULL)", item.Selector);
        Assert.AreEqual("0", item.Host);
        Assert.AreEqual(70, item.Port);
    }

    [TestMethod]
    public void Parse_ControlAndNonAsciiTypeChars_ArePreserved()
    {
        Assert.AreEqual('', GopherMenuItem.Parse("Ctrl\t/s\th\t70")!.Type);
        Assert.AreEqual('ÿ', GopherMenuItem.Parse("ÿHiByte\t/s\th\t70")!.Type);
    }

    [TestMethod]
    public void Serialize_DisplayContainingTab_CorruptsFieldFramingOnReparse()
    {
        // Serialize does not sanitize embedded tabs (unlike the server's AppendItem), so a tab in Display
        // injects an extra wire field. Documenting the injection vector, not endorsing it (see bugsFound).
        var item = new GopherMenuItem
        {
            Type = '1',
            Display = "evil\t/hijack\tattacker.example.com\t70",
            Selector = "/real",
            Host = "good.example.com",
            Port = 70
        };

        var reparsed = GopherMenuItem.Parse(item.Serialize());

        Assert.IsNotNull(reparsed);

        // The injected tab shifts the real selector out of the Selector slot: the attacker controls it now.
        Assert.AreEqual("evil", reparsed.Display);
        Assert.AreEqual("/hijack", reparsed.Selector);
        Assert.AreNotEqual("/real", reparsed.Selector);
    }
}

[TestClass]
public class GopherProxySelectorAdversarialTests
{
    [TestMethod]
    public void TryParseProxySelector_PrefixOnly_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_TypeWithoutSlashSeparator_IsRejected()
    {
        // rest must be "<type>/..."; "1x/host" has no slash immediately after the type char.
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1x/host:70/sel", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_EmptyAuthority_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_WhitespaceHost_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/   :70/sel", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_PortBoundaries()
    {
        Assert.IsTrue(GopherServer.TryParseProxySelector("/g/1/h:1/x", out _, out _, out var p1, out _));
        Assert.AreEqual(1, p1);

        Assert.IsTrue(GopherServer.TryParseProxySelector("/g/1/h:65535/x", out _, out _, out var pMax, out _));
        Assert.AreEqual(65535, pMax);

        // Boundary + 1 on each end is rejected.
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/h:0/x", out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/h:65536/x", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_OverflowPort_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/h:99999999999999999999/x", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_NegativePort_IsRejected()
    {
        // Parses to a negative int but fails the >= 1 range check.
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/h:-70/x", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_EmbeddedSlashInAuthority_TruncatesHostAndAbsorbsRest()
    {
        // The first '/' after the type ends the authority; a slash inside a hostile host string silently
        // truncates it and the remainder (including the ':port') becomes the selector.
        Assert.IsTrue(GopherServer.TryParseProxySelector("/g/1/ho/st:70/sel", out _, out var host, out var port, out var remoteSelector));
        Assert.AreEqual("ho", host);
        Assert.AreEqual(70, port);
        Assert.AreEqual("st:70/sel", remoteSelector);
    }

    [TestMethod]
    public void TryParseProxySelector_NoTrailingSlashAfterAuthority_EmptySelector()
    {
        Assert.IsTrue(GopherServer.TryParseProxySelector("/g/1/host:70", out _, out var host, out var port, out var remoteSelector));
        Assert.AreEqual("host", host);
        Assert.AreEqual(70, port);
        Assert.AreEqual(string.Empty, remoteSelector);
    }

    [TestMethod]
    public void TryParseProxySelector_MultipleColons_LastColonSplitsPort()
    {
        // LastIndexOf(':') is used, so an IPv6-ish authority keeps the bracketed host and reads the final group as the port.
        Assert.IsTrue(GopherServer.TryParseProxySelector("/g/1/[::1]:70/x", out _, out var host, out var port, out _));
        Assert.AreEqual("[::1]", host);
        Assert.AreEqual(70, port);
    }

    [TestMethod]
    public void TryParseProxySelector_ColonHostNoPortDigits_IsRejected()
    {
        // "host:" -> port substring is empty -> int.TryParse fails -> rejected.
        Assert.IsFalse(GopherServer.TryParseProxySelector("/g/1/host:/sel", out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseProxySelector_TypeCharCanBeSlash_WhenTripleSlash()
    {
        // "/g///host:70/" -> rest "//host:70/", rest[1] == '/', so the type char is itself '/'. Documenting the quirk.
        Assert.IsTrue(GopherServer.TryParseProxySelector("/g///host:70/sel", out var type, out var host, out _, out _));
        Assert.AreEqual('/', type);
        Assert.AreEqual("host", host);
    }

    [TestMethod]
    public void TryParseProxySelector_NonProxyPrefix_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseProxySelector("/G/1/host:70/x", out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseProxySelector("g/1/host:70/x", out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseProxySelector("  /g/1/host:70/x", out _, out _, out _, out _));
    }
}

[TestClass]
public class GopherUrlAdversarialTests
{
    [TestMethod]
    public void TryParseGopherUrl_EmptyHost_IsRejected()
    {
        Assert.IsFalse(GopherServer.TryParseGopherUrl("gopher:///1/sel", out _, out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseGopherUrl_PortOutOfRange_IsRejected()
    {
        // Uri itself refuses a port above 65535, so the whole parse fails.
        Assert.IsFalse(GopherServer.TryParseGopherUrl("gopher://host:99999/", out _, out _, out _, out _, out _));
    }

    [TestMethod]
    public void TryParseGopherUrl_ExplicitPortZero_FoldsTo70()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host:0/0/x", out _, out var port, out var type, out var selector, out _));
        Assert.AreEqual(70, port);
        Assert.AreEqual('0', type);
        Assert.AreEqual("/x", selector);
    }

    [TestMethod]
    public void TryParseGopherUrl_PathTraversalSegments_ArePassedThroughUnsanitized()
    {
        // The selector is opaque per RFC: dot segments are neither collapsed nor rejected, they reach the selector verbatim.
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host/1/a/../../etc", out _, out _, out var type, out var selector, out _));
        Assert.AreEqual('1', type);
        Assert.AreEqual("/a/../../etc", selector);
    }

    [TestMethod]
    public void TryParseGopherUrl_EncodedDotSegments_DecodeButAreNotCollapsed()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host/1/%2e%2e/%2e%2e/etc", out _, out _, out _, out var selector, out _));
        Assert.AreEqual("/../../etc", selector);
    }

    [TestMethod]
    public void TryParseGopherUrl_NullByteInSelector_DecodesToEmbeddedNul()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host/0/a%00b", out _, out _, out _, out var selector, out _));
        Assert.AreEqual("/a\0b", selector);
        Assert.IsTrue(selector.Contains('\0'));
    }

    [TestMethod]
    public void TryParseGopherUrl_HighRawOctet_DecodesAsSingleLatin1Char()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host/0/caf%FF", out _, out _, out _, out var selector, out _));
        Assert.AreEqual("/cafÿ", selector);
    }

    [TestMethod]
    public void TryParseGopherUrl_IncompletePercentEscape_SurvivesAsLiteral()
    {
        // Uri re-escapes the bare '%' to %25; PercentDecodeLatin1 then restores the literal "%4" without throwing.
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host/0/x%4", out _, out _, out _, out var selector, out _));
        Assert.AreEqual("/x%4", selector);
    }

    [TestMethod]
    public void TryParseGopherUrl_LiteralPlusInType0Selector_IsNotSpace()
    {
        // plusToSpace is false for selectors, so '+' stays a literal plus (only type-7 search terms fold '+').
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://host/0/a+b", out _, out _, out _, out var selector, out _));
        Assert.AreEqual("/a+b", selector);
    }

    [TestMethod]
    public void TryParseGopherUrl_IPv6LiteralHost_IsKeptBracketed()
    {
        Assert.IsTrue(GopherServer.TryParseGopherUrl("gopher://[::1]/1/x", out var host, out _, out _, out _, out _));
        Assert.AreEqual("[::1]", host);
    }

    [TestMethod]
    public void TryParseGopherUrl_GarbageAndWrongSchemes_AreRejected()
    {
        Assert.IsFalse(GopherServer.TryParseGopherUrl("", out _, out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseGopherUrl("://noscheme", out _, out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseGopherUrl("gophers://host/1/x", out _, out _, out _, out _, out _));
        Assert.IsFalse(GopherServer.TryParseGopherUrl("javascript:alert(1)", out _, out _, out _, out _, out _));
    }
}

[TestClass]
public class GopherPercentDecodeAdversarialTests
{
    [TestMethod]
    public void PercentDecodeLatin1_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, GopherServer.PercentDecodeLatin1(null!, false));
        Assert.AreEqual(string.Empty, GopherServer.PercentDecodeLatin1(string.Empty, true));
    }

    [TestMethod]
    public void PercentDecodeLatin1_TruncatedEscapes_StayLiteral()
    {
        Assert.AreEqual("%", GopherServer.PercentDecodeLatin1("%", false));
        Assert.AreEqual("%4", GopherServer.PercentDecodeLatin1("%4", false));
        Assert.AreEqual("ab%", GopherServer.PercentDecodeLatin1("ab%", false));
    }

    [TestMethod]
    public void PercentDecodeLatin1_NonHexDigits_StayLiteral()
    {
        Assert.AreEqual("%zz", GopherServer.PercentDecodeLatin1("%zz", false));
        Assert.AreEqual("%g0", GopherServer.PercentDecodeLatin1("%g0", false));
    }

    [TestMethod]
    public void PercentDecodeLatin1_FullByteRange_RoundTrips()
    {
        Assert.AreEqual("\0", GopherServer.PercentDecodeLatin1("%00", false));
        Assert.AreEqual("ÿ", GopherServer.PercentDecodeLatin1("%ff", false));
        Assert.AreEqual("ÿ", GopherServer.PercentDecodeLatin1("%FF", false));
        Assert.AreEqual("%", GopherServer.PercentDecodeLatin1("%25", false));
    }

    [TestMethod]
    public void PercentDecodeLatin1_MixedCaseHex_IsCaseInsensitive()
    {
        Assert.AreEqual("é", GopherServer.PercentDecodeLatin1("%e9", false));
        Assert.AreEqual("é", GopherServer.PercentDecodeLatin1("%E9", false));
        Assert.AreEqual("«", GopherServer.PercentDecodeLatin1("%aB", false));
    }

    [TestMethod]
    public void PercentDecodeLatin1_PlusHandlingDependsOnFlag()
    {
        Assert.AreEqual("a b c", GopherServer.PercentDecodeLatin1("a+b+c", true));
        Assert.AreEqual("a+b+c", GopherServer.PercentDecodeLatin1("a+b+c", false));
    }

    [TestMethod]
    public void PercentDecodeLatin1_TrailingPercentBeforeSingleHex_NoOverrun()
    {
        // "x%4" ends with an incomplete escape at the very tail; must not read past the end.
        Assert.AreEqual("x%4", GopherServer.PercentDecodeLatin1("x%4", true));
        Assert.AreEqual("x%", GopherServer.PercentDecodeLatin1("x%", true));
    }
}

[TestClass]
public class GopherCodecAdversarialTests
{
    [TestMethod]
    public void UnstuffGopherText_LoneDotMidStream_TruncatesRemainingContent()
    {
        // A '.' line is the RFC terminator, so any hostile trailer after it is dropped rather than shown.
        var doc = GopherServer.UnstuffGopherText("keep\n.\ndropped\nalso dropped");

        Assert.AreEqual("keep\r\n", doc);
    }

    [TestMethod]
    public void UnstuffGopherText_Null_EmitsSingleBlankLine()
    {
        // Observed: null coalesces to "", which splits to a single empty line that is echoed as one CRLF.
        // The codec does not special-case empty input, so it never returns a truly empty string.
        Assert.AreEqual("\r\n", GopherServer.UnstuffGopherText(null!));
    }

    [TestMethod]
    public void UnstuffGopherText_OnlyLeadingDotOfTripleDotIsStripped()
    {
        // "..." unstuffs its single leading dot to ".."; the remaining content is untouched.
        var doc = GopherServer.UnstuffGopherText("...\ntail\n.\n");

        Assert.AreEqual("..\r\ntail\r\n", doc);
    }

    [TestMethod]
    public void FormatTextDocument_SingleDotLine_IsDotStuffedNotTreatedAsTerminator()
    {
        // A content line that is just "." must be stuffed to ".." so a client does not read it as end-of-transfer.
        var wire = GopherServer.FormatTextDocument(".");

        Assert.AreEqual("..\r\n.\r\n", wire);
    }

    [TestMethod]
    public void FormatTextDocument_EmbeddedNulAndControlChars_ArePassedThrough()
    {
        var wire = GopherServer.FormatTextDocument("a\0bc");

        Assert.AreEqual("a\0bc\r\n.\r\n", wire);
    }

    [TestMethod]
    public void FinalizeMenu_ContentAfterEmbeddedTerminator_IsDropped()
    {
        // MenuLines stops at the first lone '.', so a smuggled second body is discarded, not concatenated.
        var wire = GopherServer.FinalizeMenu("iVisible\tfake\t(NULL)\t0\r\n.\r\niHidden\tfake\t(NULL)\t0\r\n");

        StringAssert.Contains(wire, "iVisible");
        Assert.IsFalse(wire.Contains("iHidden"), "Content after an embedded terminator leaked through FinalizeMenu");
    }

    [TestMethod]
    public void FinalizeMenu_BlankLinesAreDropped_AndExactlyOneTerminatorAppended()
    {
        var wire = GopherServer.FinalizeMenu("iA\tfake\t(NULL)\t0\r\n\r\n\r\niB\tfake\t(NULL)\t0\r\n");

        Assert.AreEqual(1, wire.Split(".\r\n").Length - 1);
        Assert.IsTrue(wire.EndsWith(".\r\n"));
    }
}

[TestClass]
public class GopherRewriteAdversarialTests
{
    [TestMethod]
    public void RewriteMenu_FetchableLineWithEmptyHost_IsLeftUntouched()
    {
        // No host means nothing to relay; the line must pass through instead of being rewritten to point at the proxy.
        var rewritten = GopherServer.RewriteMenu("1NoHost\t/sel\t\t70\r\n.\r\n", "10.0.0.5", 70);

        Assert.AreEqual("1NoHost\t/sel\t\t70\r\n.\r\n", rewritten);
    }

    [TestMethod]
    public void RewriteMenu_MalformedLineMissingHostAndPort_IsLeftUntouched()
    {
        // "1Bare" parses to Display="Bare" with empty host, so it is not rewritten and survives verbatim.
        var rewritten = GopherServer.RewriteMenu("1Bare\r\n.\r\n", "10.0.0.5", 70);

        Assert.AreEqual("1Bare\r\n.\r\n", rewritten);
    }

    [TestMethod]
    public void RewriteMenu_NonProxyableTypesArePreserved()
    {
        // Info (i), error (3), CSO (2), telnet (8/T) and Gopher+ (+) are never rewritten even with a real host.
        var upstream = "2CSO\t/c\tcso.example.com\t105\r\n+GopherPlus\t/p\tplus.example.com\t70\r\n.\r\n";

        var rewritten = GopherServer.RewriteMenu(upstream, "10.0.0.5", 70);

        Assert.AreEqual(upstream, rewritten);
    }

    [TestMethod]
    public void RewriteMenu_HostileHostSurvivesButProducesNonRoundTrippingSelector()
    {
        // A host field containing a slash gets folded into the proxy selector; parsing it back truncates the host.
        // This documents that rewrite does not validate the upstream host, not that the result is correct.
        var rewritten = GopherServer.RewriteMenu("1Evil\t/sel\tev/il.example.com\t70\r\n.\r\n", "10.0.0.5", 70);
        var item = GopherMenuItem.Parse(rewritten.Split("\r\n")[0]);

        Assert.IsNotNull(item);
        Assert.AreEqual("10.0.0.5", item.Host);

        GopherServer.TryParseProxySelector(item.Selector, out _, out var reparsedHost, out _, out _);
        Assert.AreEqual("ev", reparsedHost);
    }

    [TestMethod]
    public void RewriteMenu_ContentAfterTerminator_IsDropped()
    {
        var rewritten = GopherServer.RewriteMenu("1Fun\t/fun\thost.example.com\t70\r\n.\r\n1Hidden\t/x\tevil.example.com\t70\r\n", "10.0.0.5", 70);

        StringAssert.Contains(rewritten, "1Fun");
        Assert.IsFalse(rewritten.Contains("Hidden"), "Smuggled line after terminator leaked through RewriteMenu");
    }
}
