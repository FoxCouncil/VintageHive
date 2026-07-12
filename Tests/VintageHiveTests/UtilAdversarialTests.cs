// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;
using VintageHive.Data.Types;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace Adversarial2.Util;

[TestClass]
public class UtilAdversarialTests
{
    #region StripHtml - malformed / unbalanced / control chars

    [TestMethod]
    public void StripHtml_Null_ThrowsArgumentNullException()
    {
        // Regex.Replace throws on a null subject; a hostile null must not be silently swallowed
        Assert.ThrowsExactly<ArgumentNullException>(() => ((string)null!).StripHtml());
    }

    [TestMethod]
    public void StripHtml_UnbalancedOpenBracket_NoClose_SurvivesUntouched()
    {
        // "<.*?>" requires a closing '>', so a lone unterminated tag is NOT stripped
        var result = "hello <div".StripHtml();

        Assert.AreEqual("hello <div", result);
    }

    [TestMethod]
    public void StripHtml_EmptyTag_BecomesSpace()
    {
        var result = "a<>b".StripHtml();

        Assert.AreEqual("a b", result);
    }

    [TestMethod]
    public void StripHtml_TagSpanningNewline_IsStripped()
    {
        // RegexOptions.Singleline makes '.' match '\n', so a tag containing a newline no longer
        // evades the stripper - closing a sanitization bypass for newline-laden markup.
        var input = "x<a\nb>y";
        var result = input.StripHtml();

        Assert.AreEqual("x y", result);
    }

    [TestMethod]
    public void StripHtml_NestedAngleBrackets_LazyMatchLeavesTrailer()
    {
        // Lazy "<.*?>" matches "<a<b>" then stops; the dangling "c>" is left behind
        var result = "<a<b>c>".StripHtml();

        Assert.AreEqual("c>", result);
    }

    [TestMethod]
    public void StripHtml_OnlyOpenBrackets_SurviveAfterTrim()
    {
        var result = "<<<".StripHtml();

        Assert.AreEqual("<<<", result);
    }

    [TestMethod]
    public void StripHtml_NullByteInsideTag_IsStripped()
    {
        // A NUL is a normal character to '.', so the tag matches and is removed
        var result = "a<\0>b".StripHtml();

        Assert.AreEqual("a b", result);
    }

    [TestMethod]
    public void StripHtml_ManyTags_ReducedToTextWithInnerSpaces()
    {
        var result = "<p><b>Hi</b></p>".StripHtml();

        // Four tags => four spaces collapsed around "Hi", outer trim removes the flanks
        Assert.AreEqual("Hi", result);
    }

    #endregion

    #region ReplaceNewCharsWithOldChars - null / mixed / boundary

    [TestMethod]
    public void ReplaceNewCharsWithOldChars_Null_ThrowsNullReferenceException()
    {
        // Instance String.Replace on a null receiver dereferences null
        Assert.ThrowsExactly<NullReferenceException>(() => ((string)null!).ReplaceNewCharsWithOldChars());
    }

    [TestMethod]
    public void ReplaceNewCharsWithOldChars_Empty_ReturnsEmpty()
    {
        Assert.AreEqual("", "".ReplaceNewCharsWithOldChars());
    }

    [TestMethod]
    public void ReplaceNewCharsWithOldChars_AllSpecialCharsAtOnce_AllReplaced()
    {
        // Both smart single quotes, both smart double quotes, and both n-tildes in one payload
        var input = "‘’“”ñÑ";
        var result = input.ReplaceNewCharsWithOldChars();

        Assert.AreEqual("''\"\"" + (char)241 + (char)209, result);
    }

    [TestMethod]
    public void ReplaceNewCharsWithOldChars_AlreadyStraightQuotes_Unchanged()
    {
        var input = "'plain' \"quotes\"";
        var result = input.ReplaceNewCharsWithOldChars();

        Assert.AreEqual("'plain' \"quotes\"", result);
    }

    #endregion

    #region RegexIndexOf - null subject / hostile pattern

    [TestMethod]
    public void RegexIndexOf_NullSubject_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ((string)null!).RegexIndexOf("abc"));
    }

    [TestMethod]
    public void RegexIndexOf_MalformedPattern_ThrowsRegexParseException()
    {
        // An unbalanced group in the pattern surfaces as a parse exception, not a swallowed -1
        Assert.ThrowsExactly<RegexParseException>(() => "some text".RegexIndexOf("(unterminated"));
    }

    [TestMethod]
    public void RegexIndexOf_EmptyPattern_MatchesAtZero()
    {
        // Empty pattern matches the zero-width start position
        Assert.AreEqual(0, "abc".RegexIndexOf(""));
    }

    #endregion

    #region RemoveBOM - null safe / repeated / interior BOM

    [TestMethod]
    public void RemoveBOM_Null_ReturnsNull()
    {
        Assert.IsNull(((string)null!).RemoveBOM());
    }

    [TestMethod]
    public void RemoveBOM_MultipleLeadingBOMs_AllTrimmed()
    {
        var input = "﻿﻿﻿data";
        var result = input.RemoveBOM();

        Assert.AreEqual("data", result);
    }

    [TestMethod]
    public void RemoveBOM_InteriorBOM_NotRemoved()
    {
        // Only leading BOMs are trimmed; an interior BOM stays put
        var input = "data﻿more";
        var result = input.RemoveBOM();

        Assert.AreEqual("data﻿more", result);
    }

    #endregion

    #region WordWrapText - whitespace-only / degenerate width

    [TestMethod]
    public void WordWrapText_WhitespaceOnly_ReturnsEmpty()
    {
        // RemoveEmptyEntries drops every token, so nothing is emitted
        var result = "     ".WordWrapText(80, 24);

        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void WordWrapText_EmptyString_ReturnsEmpty()
    {
        Assert.AreEqual("", "".WordWrapText(80, 24));
    }

    #endregion

    #region HttpUtilities.AddOrUpdate - null key / value-type key path

    [TestMethod]
    public void AddOrUpdate_NullKey_Throws()
    {
        // ContainsKey(null) on a string-keyed dictionary rejects the null key
        var dict = new Dictionary<string, string>();

        Assert.ThrowsExactly<ArgumentNullException>(() => dict.AddOrUpdate(null!, "value"));
    }

    [TestMethod]
    public void AddOrUpdate_NullDictTakesPrecedenceOverNullValue()
    {
        // The null-dict guard is checked before the null-value guard
        Dictionary<string, string> dict = null!;

        Assert.ThrowsExactly<ArgumentNullException>(() => dict!.AddOrUpdate(null!, null!));
    }

    [TestMethod]
    public void AddOrUpdate_ValueTypeKey_UpdateOverwrites()
    {
        var dict = new Dictionary<int, string> { { 7, "old" } };

        dict.AddOrUpdate(7, "new");

        Assert.AreEqual(1, dict.Count);
        Assert.AreEqual("new", dict[7]);
    }

    #endregion

    #region HttpVerbs / HttpVersions - case sensitivity

    [TestMethod]
    public void HttpVerbs_LowercaseVerb_NotMatched()
    {
        // Verb matching is case-sensitive; a lowercase hostile verb is not recognized
        Assert.IsFalse(HttpVerbs.Contains("get"));
    }

    [TestMethod]
    public void HttpVersions_UnknownVersion_NotMatched()
    {
        Assert.IsFalse(HttpVersions.Contains("HTTP/2.0"));
        Assert.IsFalse(HttpVersions.Contains("HTTP/0.9"));
    }

    #endregion

    #region EmailAddress(string) - unanchored / multi-@ / empty parts

    [TestMethod]
    public void EmailCtor_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new EmailAddress((string)null!));
    }

    [TestMethod]
    public void EmailCtor_MultipleAt_Throws()
    {
        // The domain capture now excludes '@' and the pattern is anchored, so a second '@' can no
        // longer be swallowed into the domain; the whole string fails to validate.
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("a@b@c.com"));
    }

    [TestMethod]
    public void EmailCtor_LeadingAtOnly_Throws()
    {
        // No non-@ char precedes the @, so [^@]+ cannot anchor
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("@domain.com"));
    }

    [TestMethod]
    public void EmailCtor_TrailingAtEmptyDomain_Throws()
    {
        // Domain [^>]+ requires at least one char
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("user@"));
    }

    [TestMethod]
    public void EmailCtor_BareAt_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("@"));
    }

    [TestMethod]
    public void EmailCtor_GreaterThanTrailer_Throws()
    {
        // A '>' still terminates the domain capture, but the anchored pattern now requires the whole
        // string to be the address, so the '>garbage' trailer makes it fail rather than truncate.
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("user@domain>garbage"));
    }

    [TestMethod]
    public void EmailCtor_SurroundingText_Rejected()
    {
        // The anchored regex no longer absorbs leading/trailing prose into user/domain.
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("hello user@example.com world"));
    }

    [TestMethod]
    public void EmailCtor_WhitespaceOnlyParts_Rejected()
    {
        // Whitespace is now excluded from both captures, so blank parts no longer "validate".
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("  @  "));
    }

    [TestMethod]
    public void EmailCtor_NewlineInDomain_Rejected()
    {
        // A CRLF-bearing domain is now rejected instead of silently carrying control chars through.
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("u@d\r\nomain"));
    }

    #endregion

    #region EmailAddress.ParseFromSmtp - nested brackets / injection / greedy domain

    [TestMethod]
    public void ParseFromSmtp_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EmailAddress.ParseFromSmtp(null!));
    }

    [TestMethod]
    public void ParseFromSmtp_NestedBrackets_ExtraBracketLeaksIntoUser()
    {
        // The outer '<' anchors; the inner '<' is a legal [^@] char and becomes part of the user
        var email = EmailAddress.ParseFromSmtp("<<a@b.com>>");

        Assert.AreEqual("<a", email.User);
        Assert.AreEqual("b.com", email.Domain);
    }

    [TestMethod]
    public void ParseFromSmtp_MultipleAtInsideBrackets_Throws()
    {
        // Domain now excludes '@', so a second '@' before the closing '>' means no bracketed address
        // matches and the malformed command is rejected.
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("MAIL FROM:<a@b@c.com>"));
    }

    [TestMethod]
    public void ParseFromSmtp_TrailingGarbageAfterClose_FirstMatchWins()
    {
        var email = EmailAddress.ParseFromSmtp("<a@b.com> junk <c@d.net>");

        Assert.AreEqual("a", email.User);
        Assert.AreEqual("b.com", email.Domain);
    }

    [TestMethod]
    public void ParseFromSmtp_NoAtInsideBrackets_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("<justtext>"));
    }

    [TestMethod]
    public void ParseFromSmtp_EmptyBrackets_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("<>"));
    }

    [TestMethod]
    public void ParseFromSmtp_EmptyUserInsideBrackets_Throws()
    {
        // [^@]+ needs at least one char before the @
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("<@b.com>"));
    }

    [TestMethod]
    public void ParseFromSmtp_CrlfInjectionInDomain_Rejected()
    {
        // The CRLF between angle brackets now breaks the domain capture, so the SMTP header-injection
        // payload no longer parses into a valid address.
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("<a@b.com\r\nHELO evil>"));
    }

    #endregion
}