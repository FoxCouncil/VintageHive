// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;

namespace VintageHive.Data.Types;

public partial class EmailAddress
{
    // Excluding whitespace from both captures closes an SMTP header/command injection vector: an
    // address like <a@b.com\r\nHELO evil> must not smuggle CRLF into the parsed domain.
    [GeneratedRegex(@"<(?<user>[^@\s>]+)@(?<domain>[^@\s>]+)>", RegexOptions.Compiled)]
    private static partial Regex RegexFullEmail();

    // Anchored so the bare constructor validates the whole string instead of absorbing surrounding prose.
    [GeneratedRegex(@"^(?<user>[^@\s>]+)@(?<domain>[^@\s>]+)$", RegexOptions.Compiled)]
    private static partial Regex RegexEmail();

    public string User { get; private set; }

    public string Domain { get; private set; }

    public string Full => $"{User}@{Domain}";

    public EmailAddress(string user, string domain)
    {
        User = user;
        Domain = domain;
    }

    public EmailAddress(string email)
    {
        var match = RegexEmail().Match(email);

        if (!match.Success)
        {
            throw new FormatException("Invalid email format.");
        }

        User = match.Groups["user"].Value;
        Domain = match.Groups["domain"].Value;
    }

    public override string ToString()
    {
        return Full;
    }

    public static EmailAddress ParseFromSmtp(string message)
    {
        var match = RegexFullEmail().Match(message);

        if (!match.Success)
        {
            throw new FormatException("Invalid email format.");
        }

        return new EmailAddress(match.Groups["user"].Value, match.Groups["domain"].Value);
    }
}
