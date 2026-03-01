// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;

namespace UsenetCurator.Parsing;

internal static class MboxParser
{
    /// <summary>
    /// Splits an mbox stream into individual raw message strings.
    /// Messages are delimited by lines starting with "From " at column 0.
    /// Yields messages lazily to avoid loading the entire mbox into memory.
    /// </summary>
    public static IEnumerable<string> SplitMessages(Stream mboxStream, int maxMessages = int.MaxValue)
    {
        var count = 0;

        using var reader = new StreamReader(mboxStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var messageBuilder = new StringBuilder(8192);
        var inMessage = false;

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();

            if (line == null)
            {
                break;
            }

            if (IsFromLine(line))
            {
                if (inMessage && messageBuilder.Length > 0)
                {
                    yield return messageBuilder.ToString();

                    count++;

                    if (count >= maxMessages)
                    {
                        yield break;
                    }

                    messageBuilder.Clear();
                }

                inMessage = true;

                continue;
            }

            if (inMessage)
            {
                // Un-escape mbox "From " escaping (>From -> From)
                if (line.StartsWith(">From "))
                {
                    line = line[1..];
                }

                messageBuilder.AppendLine(line);
            }
        }

        // Yield the last message
        if (inMessage && messageBuilder.Length > 0)
        {
            yield return messageBuilder.ToString();
        }
    }

    private static bool IsFromLine(string line)
    {
        // Mbox "From " separator: starts with "From " followed by an address and date
        // Example: "From user@host.com Thu Jun  1 05:28:25 1989"
        if (!line.StartsWith("From "))
        {
            return false;
        }

        // Must have at least "From x" (more than just "From ")
        if (line.Length < 6)
        {
            return false;
        }

        // Quick heuristic: real "From " lines have a space after the address
        // and typically contain a date. Headers like "From: user@host" use a colon.
        // We check there's no colon right after "From" (that would be a header).
        return true;
    }
}
