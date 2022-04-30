using HtmlAgilityPack;
using System.Diagnostics;
using System.Text.RegularExpressions;
using VintageHive.Utilities;

namespace VintageHive;

public static class Extensions
{
    private const string HtmlTagAllowList = "a|ol|ul|li|br|small|font|b|strong|i|em|blockquote|h1|h2|h3|h4|h5|h6";

    public static string ReplaceNewCharsWithOldChars(this string input)
    {
        input = input.Replace("‘", "'");    
        input = input.Replace("’", "'");  
        input = input.Replace('“', '"'); 
        input = input.Replace('”', '"');
        input = input.Replace("ñ", new string((char)241, 1));
        input = input.Replace("Ñ", new string((char)209, 1));

        return input;
    }

    public static string SanitizeHtml(this string html)
    {
        var stringPattern = @"</?(?(?=" + HtmlTagAllowList + @")notag|[a-zA-Z0-9]+)(?:\s[a-zA-Z0-9\-]+=?(?:(["",']?).*?\1?)?)*\s*/?>";

        return Regex.Replace(html, stringPattern, " ");
    }

    public static string ToOnOff(this bool boolean)
    {
        return boolean ? "ON" : "OFF";
    }

    public static string StripHtml(this string input)
    {
        return Regex.Replace(input, "<.*?>", " ").Trim();
    }

    public static void ReplaceTextById(this HtmlDocument doc, string id, string text)
    {
        if (doc == null)
        {
            return;
        }

        var el = doc.GetElementById(id);

        if (el == null)
        {
            throw new ArgumentNullException("id");
        }

        el.InnerHtml = text;
    }

    public static HtmlNode GetElementById(this HtmlDocument doc, string id)
    {
        if (doc == null)
        {
            return null;
        }

        return doc.DocumentNode.SelectSingleNode("//*[@id='" + id + "']");
    }

    public static void LoadVirtual(this HtmlDocument doc, string path)
    {
        if (Debugger.IsAttached)
        {
            doc.Load(Path.Combine("../../../Statics/", path));
        }
        else
        {
            doc.LoadHtml(Resources.GetStaticsResourceString("control/index.html"));
        }
    }

    public static bool HostContains(this Uri uri, string searchPattern)
    {
        if (uri == null)
        {
            return false;
        }

        if (searchPattern == null)
        {
            throw new ArgumentNullException(nameof(searchPattern));
        }

        if (uri.Host == null)
        {
            return false;
        }

        return uri.Host.Contains(searchPattern);
    }

    public static int RegexIndexOf(this string str, string pattern)
    {
        var m = Regex.Match(str, pattern);

        return m.Success ? m.Index : -1;
    }
}
