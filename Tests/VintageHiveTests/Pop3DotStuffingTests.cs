// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// RETR and TOP built their multiline replies as "{...}{EOL}{message.Data}{EOL}." with no dot-stuffing, while
// SMTP ingest deliberately UN-stuffs on the way in (SmtpProxy.UnstuffDots). That asymmetry is the bug: a
// stored body line that is exactly "." terminates the download early, and a line beginning with "." arrives
// one character short. IMAP's equivalent path is fine because it frames with literals rather than a lone dot,
// which left POP3 as the only unguarded one.
//
// The property that matters is the round trip: what a client reconstructs by un-stuffing the reply has to be
// byte-identical to what was stored.

using System.Net;
using System.Text;
using Mail;
using VintageHive;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Pop3;

namespace Adversarial5.Pop3DotStuffing;

[TestClass]
public class Pop3DotStuffingTests
{
    const string Password = "stuffed";

    private static Pop3Proxy _proxy = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        MailTestEnv.Ensure();

        _proxy = new Pop3Proxy(IPAddress.Loopback, 0);
    }

    // What an RFC 1939 client does to a multiline reply: strip exactly one leading dot from EVERY line that
    // begins with one, not just from doubled dots.
    //
    // This distinction is the whole test. SmtpProxy.UnstuffDots is the lenient form - it only unstuffs ".." -
    // which is safe on ingest because a compliant sender only ever transmits stuffed lines. Modelling the
    // client that way here made ABodyLineStartingWithADot a false green: an unstuffed ".signature" line was
    // left alone by the lenient reader and the assertion passed against a server that had not stuffed at all.
    // A real client strips that dot, which is precisely how the corruption shows up.
    private static string Unstuff(string data)
    {
        var lines = data.Split("\r\n");

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('.'))
            {
                lines[i] = lines[i][1..];
            }
        }

        return string.Join("\r\n", lines);
    }

    // Pulls the message body out of a RETR reply: everything between the +OK status line and the lone dot that
    // terminates it. Deliberately stops at the FIRST lone dot, because that is exactly what a real client does
    // and therefore what the truncation bug looked like from the client's side.
    private static string BodyOf(string reply)
    {
        var firstLineEnd = reply.IndexOf("\r\n", StringComparison.Ordinal);

        Assert.IsTrue(firstLineEnd > 0, $"RETR reply had no status line:\n{reply}");

        var rest = reply[(firstLineEnd + 2)..];

        var terminator = rest.IndexOf("\r\n.\r\n", StringComparison.Ordinal);

        if (terminator < 0)
        {
            Assert.IsTrue(rest.EndsWith("\r\n."), $"RETR reply was never terminated by a lone dot:\n{reply}");

            return rest[..^3];
        }

        return rest[..terminator];
    }

    private static async Task<string> RetrieveLatest(string account, string marker)
    {
        var conn = new ListenerSocket();

        await _proxy.ProcessConnection(conn);

        await MailTestEnv.Cmd(_proxy, conn, $"USER {account}");

        var pass = await MailTestEnv.Cmd(_proxy, conn, $"PASS {Password}");

        StringAssert.StartsWith(pass, "+OK", $"Could not sign in as {account}: {pass}");

        var messages = Mind.PostOfficeDb.GetDeliveredEmailsForUser(account);

        var index = messages.FindIndex(x => x.Data.Contains(marker));

        Assert.IsTrue(index >= 0, "The seeded message was not in the mailbox, so this test never reached RETR.");

        return await MailTestEnv.Cmd(_proxy, conn, $"RETR {index + 1}");
    }

    // Seeds one message straight into the store, the same way SMTP's DATA handler does after un-stuffing.
    private static string Seed(string account, string body)
    {
        var marker = $"X-Marker: {Guid.NewGuid()}";

        var raw = $"Subject: dot test\r\n{marker}\r\n\r\n{body}";

        Mind.PostOfficeDb.ProcessAndInsertEmail(
            new EmailAddress($"sender@{MailDomains.Primary}"),
            new HashSet<EmailAddress> { new($"{account}@{MailDomains.Primary}") },
            raw);

        // Insert lands the row with delivery = 0; POP3 only ever lists delivery = 1, so run the promotion the
        // real mail loop does before the mailbox is readable.
        foreach (var undelivered in Mind.PostOfficeDb.GetUndeliveredEmails())
        {
            Mind.PostOfficeDb.MarkEmailAsDelivered(undelivered.Id);
        }

        return marker;
    }

    private static void EnsureUser(string account)
    {
        if (!Mind.Db.UserExistsByUsername(account))
        {
            Mind.Db.UserCreate(account, Password);
        }
    }

    // The truncation case. A body line that is exactly "." used to end the client's download right there,
    // silently losing everything after it.
    [TestMethod]
    [Timeout(20000)]
    public async Task ABodyLineThatIsOnlyADot_DoesNotTruncateTheDownload()
    {
        const string account = "dotone";

        EnsureUser(account);

        var body = "first line\r\n.\r\nlast line";

        var marker = Seed(account, body);

        var reply = await RetrieveLatest(account, marker);

        var received = Unstuff(BodyOf(reply));

        StringAssert.Contains(received, "last line", "The download stopped at a body line containing only a dot, so everything after it was lost.");
        StringAssert.Contains(received, body, "The round trip did not reproduce the stored body.");
    }

    // The corruption case. A line merely STARTING with a dot used to arrive one character short.
    [TestMethod]
    [Timeout(20000)]
    public async Task ABodyLineStartingWithADot_KeepsItsFirstCharacter()
    {
        const string account = "dottwo";

        EnsureUser(account);

        var body = "intro\r\n.signature line\r\nouttro";

        var marker = Seed(account, body);

        var reply = await RetrieveLatest(account, marker);

        var received = Unstuff(BodyOf(reply));

        StringAssert.Contains(received, ".signature line", "A body line beginning with a dot lost its leading character in transit.");
        StringAssert.Contains(received, body, "The round trip did not reproduce the stored body.");
    }

    // The stuffing itself, as the exact inverse of SmtpProxy.UnstuffDots.
    [TestMethod]
    public void StuffingIsTheInverseOfUnstuffing()
    {
        foreach (var original in new[]
        {
            ".",
            "..",
            ".leading",
            "plain",
            "a\r\n.\r\nb",
            "a\r\n.hidden\r\nb",
            "a\r\n..already\r\nb",
            "trailing\r\n.",
            string.Empty,
        })
        {
            Assert.AreEqual(original, Unstuff(Pop3Proxy.StuffDots(original)), $"Round trip changed '{original.Replace("\r\n", "<CRLF>")}'.");
        }
    }

    [TestMethod]
    public void StuffingLeavesOrdinaryContentAlone()
    {
        Assert.AreEqual("hello\r\nworld", Pop3Proxy.StuffDots("hello\r\nworld"));
        Assert.AreEqual("a.b", Pop3Proxy.StuffDots("a.b"), "A dot that is not at the start of a line must not be touched.");
        Assert.IsNull(Pop3Proxy.StuffDots(null));
    }
}
