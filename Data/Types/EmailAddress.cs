using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VintageHive.Data.Types;

public partial class EmailAddress
{
    [GeneratedRegex(@"<(?<user>[^@]+)@(?<domain>[^>]+)>", RegexOptions.Compiled)]
    private static partial Regex RegexFullEmail();

    [GeneratedRegex(@"(?<user>[^@]+)@(?<domain>[^>]+)", RegexOptions.Compiled)]
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
