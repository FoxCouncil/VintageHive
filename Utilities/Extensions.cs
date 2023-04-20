﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using HtmlAgilityPack;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.RegularExpressions;
using VintageHive.Proxy.Security;

namespace VintageHive.Utilities;

public static class Extensions
{
    private const string HtmlTagAllowList = "a|ol|ul|li|br|small|font|b|strong|i|em|blockquote|h1|h2|h3|h4|h5|h6";

    public static string MakeHtmlAnchorLinksHappen(this string text)
    {
        return Regex.Replace(text, @"((https?|ftp)://(?:www\.|(?!www))[^\s.]+\.\S{2,}|www\.\S+\.\S{2,})", m => m.Groups[2].Success ?
           $"<a href=\"{m.Groups[1].Value}\">{m.Groups[1].Value}</a>" :
           $"<a href=\"http://{m.Groups[1].Value}\">{m.Groups[1].Value}</a>");
    }

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

    public static bool ConfirmValidPath(this string path)
    {
        return !string.IsNullOrEmpty(path) && path.IndexOfAny(Path.GetInvalidPathChars()) <= 0;
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
            doc.LoadHtml(Resources.GetStaticsResourceString(path));
        }

        doc.ProcessPartials();
    }

    public static void ProcessPartials(this HtmlDocument doc)
    {
        if (doc == null)
        {
            return;
        }

        var nodes = doc.DocumentNode.SelectNodes("//*[@partial]");

        if (nodes == null)
        {
            return;
        }

        foreach (var node in nodes)
        {
            var partial = node.GetAttributeValue("partial", "");

            if (string.IsNullOrEmpty(partial) || !partial.EndsWith(".html") && !Resources.Statics.ContainsKey(partial))
            {
                continue;
            }

            var partialDoc = new HtmlDocument();

            partialDoc.LoadVirtual($"partials/{partial}");

            node.InnerHtml = partialDoc.DocumentNode.InnerHtml;
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

    public static Dictionary<string, string> ToDictionary(this NameValueCollection nvc)
    {
        var dict = new Dictionary<string, string>();

        if (nvc != null)
        {
            foreach (string key in nvc.AllKeys)
            {
                if (key == null)
                {
                    dict.Add(nvc.ToString(), string.Empty);
                }
                else
                {
                    dict.Add(key, nvc[key]);
                }
            }
        }

        return dict;
    }

    public static byte[] ReadAllBytes(this Stream instream)
    {
        if (instream is MemoryStream stream)
        {
            return stream.ToArray();
        }

        using var memoryStream = new MemoryStream();

        instream.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }

    public static async Task CopyToSslAsync(this Stream input, SslStream output)
    {
        var data = new byte[32768];
        int read;

        while ((read = input.Read(data, 0, data.Length)) > 0)
        {
            await output.WriteRawAsync(data, read);
        }
    }

    public static string CleanWeatherImageUrl(this string url)
    {
        return url.Replace("https://ssl.gstatic.com/onebox/weather/64/", "http://hive/assets/weather/")
            .Replace("https://ssl.gstatic.com/onebox/weather/48/", "http://hive/assets/weather/")
            .Replace(".png", ".jpg");
    }

    public static void Append(this MemoryStream stream, byte value)
    {
        stream.Append(new[] { value });
    }

    public static void Append(this MemoryStream stream, byte[] values)
    {
        stream.Write(values, 0, values.Length);
    }
}
