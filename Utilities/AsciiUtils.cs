// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities;

public static class AsciiUtils
{
    public static string ConvertToAsciiArt(Image<Rgba32> image, int maxWidth, int maxHeight)
    {
        // Calculate the aspect ratio of the image
        double aspectRatio = (double)image.Width / (double)image.Height;

        // Calculate the width and height of the output image based on the aspect ratio and the maximum width and height
        int outputWidth = (int)Math.Min(maxWidth, aspectRatio * maxHeight);
        int outputHeight = (int)Math.Min(maxHeight, maxHeight / aspectRatio);

        // Resize the image to the output width and height
        Image<Rgba32> resizedImage = image.Clone(img => img.Resize(outputWidth, outputHeight));

        // Create a StringBuilder to store the ASCII art
        var asciiArt = new StringBuilder();

        // Define the characters to use for different levels of brightness
        char[] asciiChars = { '@', '#', '8', '&', 'o', ':', '*', '.', ' ' };

        // Loop through each row of pixels in the image
        for (int y = 0; y < outputHeight; y++)
        {
            // Loop through each column of pixels in the image
            for (int x = 0; x < outputWidth; x++)
            {
                // Get the color of the current pixel
                Rgba32 pixelColor = resizedImage[x, y];

                // Calculate the brightness of the current pixel
                double brightness = (0.299 * pixelColor.R + 0.587 * pixelColor.G + 0.114 * pixelColor.B) / 255;

                // Choose the character to represent the current pixel based on its brightness
                int index = (int)Math.Round(brightness * (asciiChars.Length - 1));
                char asciiChar = asciiChars[index];

                // Append the ASCII character to the StringBuilder
                asciiArt.Append(asciiChar);
            }

            // Append a newline character to the StringBuilder to start a new row
            //asciiArt.Append(Environment.NewLine);
            asciiArt.Append("\r\n");
        }

        // Remove blank lines above and below the parsed artwork.
        string[] lines = asciiArt.ToString().Split("\r\n", StringSplitOptions.None);
        string output = string.Join("\r\n", lines.Where(line => !string.IsNullOrWhiteSpace(line)));

        // Return the ASCII art as a string
        return $"{output}\r\n";
    }
}
