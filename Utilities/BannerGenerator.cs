// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VintageHive.Utilities;

internal static class BannerGenerator
{
    private const int BannerWidth = 194;
    private const int BannerHeight = 32;
    private const int FaviconSize = 32;
    private const int TextPadding = 4;
    private const float FontSize = 8f;
    private const int MaxCharsWithFavicon = 19;
    private const int MaxCharsNoFavicon = 23;

    private static readonly Lazy<FontFamily> _fontFamily = new(() =>
    {
        var fontData = Resources.GetStaticsResourceData("fonts.PressStart2P-Regular.ttf");

        var collection = new FontCollection();

        using var ms = new MemoryStream(fontData);

        return collection.Add(ms);
    });

    // 8 retro palettes: each is (background1, background2, text highlight)
    private static readonly (Color, Color)[] Palettes =
    {
        (Color.ParseHex("000000"), Color.ParseHex("AA00AA")),  // CGA magenta
        (Color.ParseHex("40318D"), Color.ParseHex("7869C4")),  // C64 blues
        (Color.ParseHex("FF71CE"), Color.ParseHex("01CDFE")),  // Vaporwave
        (Color.ParseHex("1A0A00"), Color.ParseHex("FF8800")),  // Amber monitor
        (Color.ParseHex("008080"), Color.ParseHex("C0C0C0")),  // Win3.1 teal
        (Color.ParseHex("000000"), Color.ParseHex("33FF33")),  // Apple II green
        (Color.ParseHex("000000"), Color.ParseHex("FFFF00")),  // Teletext
        (Color.ParseHex("AA0000"), Color.ParseHex("FFAA00")),  // EGA warm
    };

    public static byte[] Generate(string stationName, byte[] faviconBytes = null)
    {
        var seed = GetStableSeed(stationName);
        var rng = new Random(seed);

        var paletteIndex = rng.Next(Palettes.Length);
        var (color1, color2) = Palettes[paletteIndex];
        var styleIndex = rng.Next(6);

        using var image = new Image<Rgba32>(BannerWidth, BannerHeight);

        // Draw background
        DrawRetroBackground(image, color1, color2, styleIndex, rng);

        // Composite favicon if available
        var hasFavicon = false;

        if (faviconBytes != null && faviconBytes.Length > 0)
        {
            try
            {
                using var favicon = Image.Load<Rgba32>(faviconBytes);

                favicon.Mutate(x => x.Resize(FaviconSize, FaviconSize));

                image.Mutate(x => x.DrawImage(favicon, new Point(0, 0), 1f));

                hasFavicon = true;
            }
            catch
            {
                // Bad favicon data — skip it
            }
        }

        // Draw station name text
        var textX = hasFavicon ? (FaviconSize + TextPadding) : TextPadding;
        var maxChars = hasFavicon ? MaxCharsWithFavicon : MaxCharsNoFavicon;

        var displayName = stationName ?? "Unknown";

        if (displayName.Length > maxChars)
        {
            displayName = displayName[..(maxChars - 2)] + "..";
        }

        var font = _fontFamily.Value.CreateFont(FontSize, FontStyle.Regular);
        var textY = (BannerHeight - FontSize) / 2f;

        // 1px black shadow for legibility
        image.Mutate(x => x.DrawText(displayName, font, Color.Black, new PointF(textX + 1, textY + 1)));

        // White foreground text
        image.Mutate(x => x.DrawText(displayName, font, Color.White, new PointF(textX, textY)));

        using var output = new MemoryStream();

        image.SaveAsGif(output, new GifEncoder { ColorTableMode = GifColorTableMode.Local });

        return output.ToArray();
    }

    private static void DrawRetroBackground(Image<Rgba32> image, Color c1, Color c2, int style, Random rng)
    {
        var rgba1 = c1.ToPixel<Rgba32>();
        var rgba2 = c2.ToPixel<Rgba32>();

        switch (style)
        {
            case 0: // Horizontal gradient
            {
                for (var x = 0; x < BannerWidth; x++)
                {
                    var t = (float)x / (BannerWidth - 1);
                    var pixel = LerpPixel(rgba1, rgba2, t);

                    for (var y = 0; y < BannerHeight; y++)
                    {
                        image[x, y] = pixel;
                    }
                }

                break;
            }

            case 1: // Vertical stripes
            {
                var stripeWidth = 4 + rng.Next(5); // 4-8px

                for (var x = 0; x < BannerWidth; x++)
                {
                    var pixel = ((x / stripeWidth) % 2 == 0) ? rgba1 : rgba2;

                    for (var y = 0; y < BannerHeight; y++)
                    {
                        image[x, y] = pixel;
                    }
                }

                break;
            }

            case 2: // Scanlines
            {
                var scanlineColor = LerpPixel(rgba1, rgba2, 0.3f);

                for (var y = 0; y < BannerHeight; y++)
                {
                    var pixel = (y % 2 == 0) ? rgba1 : scanlineColor;

                    for (var x = 0; x < BannerWidth; x++)
                    {
                        image[x, y] = pixel;
                    }
                }

                break;
            }

            case 3: // Ordered dither (2x2 Bayer)
            {
                var bayer = new float[,] { { 0f, 0.5f }, { 0.75f, 0.25f } };

                for (var y = 0; y < BannerHeight; y++)
                {
                    for (var x = 0; x < BannerWidth; x++)
                    {
                        var t = (float)x / (BannerWidth - 1);
                        var threshold = bayer[y % 2, x % 2];
                        image[x, y] = (t > threshold) ? rgba2 : rgba1;
                    }
                }

                break;
            }

            case 4: // Diagonal gradient
            {
                var maxDist = BannerWidth + BannerHeight - 2;

                for (var y = 0; y < BannerHeight; y++)
                {
                    for (var x = 0; x < BannerWidth; x++)
                    {
                        var t = (float)(x + y) / maxDist;
                        image[x, y] = LerpPixel(rgba1, rgba2, t);
                    }
                }

                break;
            }

            case 5: // Solid with noise
            {
                for (var y = 0; y < BannerHeight; y++)
                {
                    for (var x = 0; x < BannerWidth; x++)
                    {
                        var noise = rng.Next(-20, 21);

                        image[x, y] = new Rgba32(
                            ClampByte(rgba1.R + noise),
                            ClampByte(rgba1.G + noise),
                            ClampByte(rgba1.B + noise),
                            255
                        );
                    }
                }

                break;
            }
        }
    }

    private static int GetStableSeed(string stationName)
    {
        var input = (stationName ?? "") + "|" + Environment.MachineName;
        var hash = 0;

        foreach (var c in input)
        {
            hash = (hash * 31) + c;
        }

        return hash;
    }

    private static Rgba32 LerpPixel(Rgba32 a, Rgba32 b, float t)
    {
        return new Rgba32(
            ClampByte((int)(a.R + (b.R - a.R) * t)),
            ClampByte((int)(a.G + (b.G - a.G) * t)),
            ClampByte((int)(a.B + (b.B - a.B) * t)),
            255
        );
    }

    private static byte ClampByte(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }
}
