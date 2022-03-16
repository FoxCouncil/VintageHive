using HtmlAgilityPack;
using System.Diagnostics;
using System.Text.RegularExpressions;
using VintageHive.Utilities;

namespace VintageHive;

public static class Extensions
{
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
