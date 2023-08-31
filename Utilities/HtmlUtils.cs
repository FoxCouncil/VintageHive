// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using HtmlAgilityPack;

namespace VintageHive.Utilities;

internal static class HtmlUtils
{
    internal static void NormalizeAnchorLinks(HtmlDocument articleDocument)
    {
        var anchorNodes = articleDocument.DocumentNode.SelectNodes("//a");

        if (anchorNodes != null)
        {
            foreach (var node in anchorNodes)
            {
                var href = node.GetAttributeValue("href", "");

                if (!href.Any() || href[0] == '#')
                {
                    continue;
                }

                href = $"/viewer.html?url={WebUtility.UrlDecode(href)}";

                node.SetAttributeValue("href", href);

                node.SetAttributeValue("target", "");
            }
        }
    }

    internal static void NormalizeImages(HtmlDocument articleDocument)
    {
        var imgNodes = articleDocument.DocumentNode.SelectNodes("//img");

        if (imgNodes != null)
        {
            foreach (var node in imgNodes)
            {
                var img = node.GetAttributeValue("src", "");

                if (string.IsNullOrEmpty(img))
                {
                    img = node.GetAttributeValue("data-src", "");
                }

                if (string.IsNullOrEmpty(img))
                {
                    img = node.GetAttributeValue("data-src-medium", "");
                }

                var imgUri = new Uri(img.StartsWith("//") ? $"https:{img}" : img);

                var imageLinkNode = HtmlNode.CreateNode($"<a href=\"/viewer.html?url={Uri.EscapeDataString(imgUri.ToString())}\"><img src=\"http://api.hive.com/image/fetch?url={Uri.EscapeDataString(imgUri.ToString())}\" border=\"0\"></a>");

                if (node.ParentNode.Name == "picture")
                {
                    var pictureEl = node.ParentNode;

                    pictureEl.ParentNode.InsertAfter(imageLinkNode, pictureEl);

                    pictureEl.Remove();
                }

                node.ParentNode.InsertAfter(imageLinkNode, node);

                node.Remove();
            }
        }
    }
}
