// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Utilities;

namespace Utilities;

[TestClass]
public class ExtensionsTests
{
    #region ToOnOff

    [TestMethod]
    public void ToOnOff_True_ReturnsON()
    {
        Assert.AreEqual("ON", true.ToOnOff());
    }

    [TestMethod]
    public void ToOnOff_False_ReturnsOFF()
    {
        Assert.AreEqual("OFF", false.ToOnOff());
    }

    #endregion

    #region StripHtml

    [TestMethod]
    public void StripHtml_RemovesTags()
    {
        var input = "<p>Hello <b>world</b></p>";
        var result = input.StripHtml();

        // Each tag gets replaced with a space, then the result is trimmed
        Assert.IsTrue(result.Contains("Hello"));
        Assert.IsTrue(result.Contains("world"));
        Assert.IsFalse(result.Contains("<"));
    }

    [TestMethod]
    public void StripHtml_PlainText_Unchanged()
    {
        var input = "No HTML here";
        var result = input.StripHtml();

        Assert.AreEqual("No HTML here", result);
    }

    [TestMethod]
    public void StripHtml_SelfClosingTags()
    {
        var input = "Line one<br/>Line two";
        var result = input.StripHtml();

        Assert.AreEqual("Line one Line two", result);
    }

    [TestMethod]
    public void StripHtml_Empty_ReturnsEmpty()
    {
        Assert.AreEqual("", "".StripHtml());
    }

    #endregion

    #region ReplaceNewCharsWithOldChars

    [TestMethod]
    public void ReplaceNewCharsWithOldChars_SmartQuotesToStraight()
    {
        var input = "\u2018Hello\u2019";
        var result = input.ReplaceNewCharsWithOldChars();

        Assert.AreEqual("'Hello'", result);
    }

    [TestMethod]
    public void ReplaceNewCharsWithOldChars_SmartDoubleQuotes()
    {
        var input = "\u201CHello\u201D";
        var result = input.ReplaceNewCharsWithOldChars();

        Assert.AreEqual("\"Hello\"", result);
    }

    [TestMethod]
    public void ReplaceNewCharsWithOldChars_SpanishN()
    {
        var input = "Espa\u00f1a";
        var result = input.ReplaceNewCharsWithOldChars();

        // Should preserve the same character (char 241)
        Assert.IsTrue(result.Contains((char)241));
    }

    [TestMethod]
    public void ReplaceNewCharsWithOldChars_PlainText_Unchanged()
    {
        var input = "No fancy chars here";
        var result = input.ReplaceNewCharsWithOldChars();

        Assert.AreEqual("No fancy chars here", result);
    }

    #endregion

    #region MakeHtmlAnchorLinksHappen

    [TestMethod]
    public void MakeHtmlAnchorLinksHappen_HttpUrl_WrapsInAnchor()
    {
        var input = "Visit http://example.com today";
        var result = input.MakeHtmlAnchorLinksHappen();

        Assert.IsTrue(result.Contains("<a href=\"http://example.com\">"));
        Assert.IsTrue(result.Contains("</a>"));
    }

    [TestMethod]
    public void MakeHtmlAnchorLinksHappen_HttpsUrl_WrapsInAnchor()
    {
        var input = "Visit https://secure.example.com/path today";
        var result = input.MakeHtmlAnchorLinksHappen();

        Assert.IsTrue(result.Contains("<a href=\"https://secure.example.com/path\">"));
    }

    [TestMethod]
    public void MakeHtmlAnchorLinksHappen_NoUrl_Unchanged()
    {
        var input = "Just plain text without URLs";
        var result = input.MakeHtmlAnchorLinksHappen();

        Assert.AreEqual(input, result);
    }

    #endregion

    #region ConfirmValidPath

    [TestMethod]
    public void ConfirmValidPath_ValidPath_ReturnsTrue()
    {
        Assert.IsTrue("/var/log/test.log".ConfirmValidPath());
    }

    [TestMethod]
    public void ConfirmValidPath_Empty_ReturnsFalse()
    {
        Assert.IsFalse("".ConfirmValidPath());
    }

    [TestMethod]
    public void ConfirmValidPath_Null_ReturnsFalse()
    {
        Assert.IsFalse(((string)null).ConfirmValidPath());
    }

    #endregion

    #region RegexIndexOf

    [TestMethod]
    public void RegexIndexOf_Found_ReturnsIndex()
    {
        var result = "hello world".RegexIndexOf("world");

        Assert.AreEqual(6, result);
    }

    [TestMethod]
    public void RegexIndexOf_NotFound_ReturnsNegativeOne()
    {
        var result = "hello world".RegexIndexOf("xyz");

        Assert.AreEqual(-1, result);
    }

    [TestMethod]
    public void RegexIndexOf_RegexPattern_Works()
    {
        var result = "test123abc".RegexIndexOf(@"\d+");

        Assert.AreEqual(4, result);
    }

    #endregion

    #region ComputeMD5

    [TestMethod]
    public void ComputeMD5_KnownInput_ReturnsExpectedHash()
    {
        // MD5 of empty string is well-known
        var result = "".ComputeMD5();

        Assert.AreEqual("D41D8CD98F00B204E9800998ECF8427E", result);
    }

    [TestMethod]
    public void ComputeMD5_HelloWorld()
    {
        var result = "Hello, World!".ComputeMD5();

        // MD5 of "Hello, World!" is well-known
        Assert.AreEqual("65A8E27D8879283831B664BD8B7F0AD4", result);
    }

    [TestMethod]
    public void ComputeMD5_SameInput_SameOutput()
    {
        var hash1 = "test".ComputeMD5();
        var hash2 = "test".ComputeMD5();

        Assert.AreEqual(hash1, hash2);
    }

    [TestMethod]
    public void ComputeMD5_DifferentInput_DifferentOutput()
    {
        var hash1 = "abc".ComputeMD5();
        var hash2 = "xyz".ComputeMD5();

        Assert.AreNotEqual(hash1, hash2);
    }

    #endregion

    #region ToRFC822String

    [TestMethod]
    public void ToRFC822String_FormatsCorrectly()
    {
        // Friday, January 15, 1999, 08:30:12 UTC
        var date = new DateTimeOffset(1999, 1, 15, 8, 30, 12, TimeSpan.Zero);
        var result = date.ToRFC822String();

        Assert.AreEqual("Fri, 15 Jan 1999 08:30:12 +0000", result);
    }

    [TestMethod]
    public void ToRFC822String_NegativeOffset()
    {
        var date = new DateTimeOffset(1999, 1, 15, 8, 30, 12, TimeSpan.FromHours(-5));
        var result = date.ToRFC822String();

        Assert.AreEqual("Fri, 15 Jan 1999 08:30:12 -0500", result);
    }

    [TestMethod]
    public void ToRFC822String_PositiveOffset()
    {
        // Sunday, March 7, 2004, 14:00:00 +0530
        var date = new DateTimeOffset(2004, 3, 7, 14, 0, 0, new TimeSpan(5, 30, 0));
        var result = date.ToRFC822String();

        Assert.AreEqual("Sun, 07 Mar 2004 14:00:00 +0530", result);
    }

    #endregion

    #region UTF8 / ASCII Encoding Extensions

    [TestMethod]
    public void ToUTF8_StringToBytes_RoundTrips()
    {
        var original = "Hello, UTF-8!";
        var bytes = original.ToUTF8();
        var result = bytes.ToUTF8();

        Assert.AreEqual(original, result);
    }

    [TestMethod]
    public void ToASCII_StringToBytes_RoundTrips()
    {
        var original = "Hello, ASCII!";
        var bytes = original.ToASCII();
        var result = bytes.ToASCII();

        Assert.AreEqual(original, result);
    }

    [TestMethod]
    public void ToUTF8_ReadOnlySpan_Works()
    {
        var bytes = Encoding.UTF8.GetBytes("Test");
        ReadOnlySpan<byte> span = bytes;
        var result = span.ToUTF8();

        Assert.AreEqual("Test", result);
    }

    [TestMethod]
    public void ToASCII_ReadOnlySpan_Works()
    {
        var bytes = Encoding.ASCII.GetBytes("Test");
        ReadOnlySpan<byte> span = bytes;
        var result = span.ToASCII();

        Assert.AreEqual("Test", result);
    }

    #endregion

    #region WordWrapText

    [TestMethod]
    public void WordWrapText_ShortText_SingleLine()
    {
        var input = "Hello world";
        var result = input.WordWrapText(80, 24);

        Assert.AreEqual("Hello world \r\n", result);
    }

    [TestMethod]
    public void WordWrapText_LongText_WrapsAtWidth()
    {
        var input = "The quick brown fox jumps over the lazy dog";
        var result = input.WordWrapText(20, 24);
        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            Assert.IsTrue(line.Length <= 21, $"Line too long: '{line}' ({line.Length} chars)");
        }
    }

    [TestMethod]
    public void WordWrapText_CustomNewline()
    {
        var input = "Hello world";
        var result = input.WordWrapText(80, 24, "\n");

        Assert.AreEqual("Hello world \n", result);
    }

    #endregion

    #region CleanWeatherImageUrl

    [TestMethod]
    public void CleanWeatherImageUrl_Replaces64pxUrl()
    {
        var input = "https://ssl.gstatic.com/onebox/weather/64/sunny.png";
        var result = input.CleanWeatherImageUrl();

        Assert.AreEqual("http://hive/assets/weather/sunny.jpg", result);
    }

    [TestMethod]
    public void CleanWeatherImageUrl_Replaces48pxUrl()
    {
        var input = "https://ssl.gstatic.com/onebox/weather/48/cloudy.png";
        var result = input.CleanWeatherImageUrl();

        Assert.AreEqual("http://hive/assets/weather/cloudy.jpg", result);
    }

    #endregion

    #region MemoryStream Append

    [TestMethod]
    public void Append_SingleByte_WritesToStream()
    {
        var stream = new MemoryStream();

        stream.Append(0x42);

        Assert.AreEqual(1, stream.Length);
        Assert.AreEqual(0x42, stream.ToArray()[0]);
    }

    [TestMethod]
    public void Append_ByteArray_WritesToStream()
    {
        var stream = new MemoryStream();
        var data = new byte[] { 0x01, 0x02, 0x03 };

        stream.Append(data);

        CollectionAssert.AreEqual(data, stream.ToArray());
    }

    #endregion

    #region ReadAllBytes

    [TestMethod]
    public void ReadAllBytes_MemoryStream_ReturnsArray()
    {
        var expected = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream(expected);

        var result = stream.ReadAllBytes();

        CollectionAssert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ReadAllBytes_NonMemoryStream_ReturnsArray()
    {
        // Use a stream that isn't a MemoryStream to test the CopyTo path
        var expected = new byte[] { 10, 20, 30 };
        var memStream = new MemoryStream(expected);
        var buffered = new BufferedStream(memStream);

        var result = buffered.ReadAllBytes();

        CollectionAssert.AreEqual(expected, result);
    }

    #endregion

    #region ToStringTable

    [TestMethod]
    public void ToStringTable_FormatsWithHeaders()
    {
        var data = new string[,]
        {
            { "Name", "Age" },
            { "Fox", "30" },
            { "Test", "25" }
        };

        var result = data.ToStringTable();

        Assert.IsTrue(result.Contains("Name"));
        Assert.IsTrue(result.Contains("Age"));
        Assert.IsTrue(result.Contains("Fox"));
        Assert.IsTrue(result.Contains("---")); // header separator
    }

    [TestMethod]
    public void ToStringTable_Generic_FormatsCorrectly()
    {
        var items = new[]
        {
            new { Name = "Item1", Value = 10 },
            new { Name = "Item2", Value = 20 }
        };

        var result = items.ToStringTable(
            new[] { "Name", "Value" },
            x => x.Name,
            x => x.Value
        );

        Assert.IsTrue(result.Contains("Item1"));
        Assert.IsTrue(result.Contains("Item2"));
        Assert.IsTrue(result.Contains("10"));
        Assert.IsTrue(result.Contains("20"));
    }

    #endregion

    #region HostContains

    [TestMethod]
    public void HostContains_MatchingPattern_ReturnsTrue()
    {
        var uri = new Uri("http://www.example.com/path");

        Assert.IsTrue(uri.HostContains("example"));
    }

    [TestMethod]
    public void HostContains_NonMatchingPattern_ReturnsFalse()
    {
        var uri = new Uri("http://www.example.com/path");

        Assert.IsFalse(uri.HostContains("other"));
    }

    [TestMethod]
    public void HostContains_NullUri_ReturnsFalse()
    {
        Assert.IsFalse(((Uri)null).HostContains("test"));
    }

    [TestMethod]
    public void HostContains_NullPattern_Throws()
    {
        var uri = new Uri("http://example.com");

        Assert.ThrowsExactly<ArgumentNullException>(() => uri.HostContains(null));
    }

    #endregion
}
