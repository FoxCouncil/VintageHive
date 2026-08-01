// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Pop3;

public class Pop3Proxy : Listener
{
    private const string EOL = "\r\n";

    private const string Authenticated = "auth";
    private const string Username = "username";
    private const string Messages = "messages";
    private const string PendingDeletions = "pending_deletions";

    public Pop3Proxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = true;

        connection.DataBag[Authenticated] = false;
        connection.DataBag[Username] = string.Empty;
        connection.DataBag[PendingDeletions] = new HashSet<int>();

        return await SendResponse(true, $"pop3.{MailDomains.Primary} POP3 server ready");
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        const string LineBufferKey = "_pop3_linebuf";
        const int MaxLineBytes = 8 * 1024;

        // Buffer to CRLF and loop: a client that pipelines commands in one packet, or whose command is split
        // across TCP reads, was previously misparsed (only the first line handled; later tags never answered).
        // The carry-buffer mechanics live in LineBuffer, shared with the other line-based protocols; see its
        // remarks for why they share the mechanics but each keep their own control flow.
        var buffer = LineBuffer.Open(connection, LineBufferKey, data, read, MaxLineBytes);

        var responses = new List<byte>();

        while (buffer.TryReadLine(out var line))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var resp = await ProcessCommandLine(connection, line);

            if (resp != null)
            {
                responses.AddRange(resp);
            }

            if (!connection.IsKeepAlive)
            {
                buffer.Clear();

                return responses.Count > 0 ? responses.ToArray() : null;
            }
        }

        buffer.Save();

        return responses.Count > 0 ? responses.ToArray() : null;
    }

    private async Task<byte[]> ProcessCommandLine(ListenerSocket connection, string commandLine)
    {
        var bag = connection.DataBag;

        var username = bag[Username].ToString();

        var (Command, Message) = ParseCommandLine(commandLine);

        // Auth guard for commands that require authentication
        switch (Command)
        {
            case "STAT":
            case "LIST":
            case "RETR":
            case "DELE":
            case "UIDL":
            case "TOP":
            case "RSET":
            case "NOOP":
            {
                if (!(bool)bag[Authenticated])
                {
                    return await SendResponse(false, "Not authenticated");
                }

                break;
            }
        }

        switch (Command)
        {
            case "USER":
            {
                // Real clients configured with the full email address send USER fred@domain. Resolve the
                // domain against the hosted list and authenticate on the local part - the verbatim string
                // used to miss the user table and surface as "Invalid password". A foreign or malformed
                // domain is rejected here, as itself, not left to fail as a password error.
                if (!MailDomains.TryResolveLogin(Message, out var localPart, out _))
                {
                    return await SendResponse(false, "Mailbox domain not hosted here");
                }

                bag[Username] = localPart;

                return await SendResponse(true, "User name accepted, password please");
            }

            case "PASS":
            {
                var password = Message;

                var user = Mind.Db.UserFetch(username, password);

                if (user != null)
                {
                    connection.DataBag[Authenticated] = true;
                    connection.DataBag[Messages] = Mind.PostOfficeDb.GetDeliveredEmailsForUser(username);

                    return await SendResponse(true, "Mailbox locked and ready");
                }
                else
                {
                    return await SendResponse(false, "Invalid password");
                }
            }

            case "STAT":
            {
                var messages = bag[Messages] as List<EmailMessage>;

                return await SendResponse(true, $"{messages.Count} {messages.Sum(x => x.Size)}");
            }

            case "LIST":
            {
                var messages = bag[Messages] as List<EmailMessage>;

                if (!string.IsNullOrEmpty(Message))
                {
                    if (!int.TryParse(Message, out var idx) || idx < 1 || idx > messages.Count)
                    {
                        return await SendResponse(false, "No such message");
                    }

                    return await SendResponse(true, $"{idx} {messages[idx - 1].Size}");
                }

                var listOfMessages = new StringBuilder();

                var index = 1;

                foreach (var message in messages)
                {
                    listOfMessages.Append($"{index} {message.Size}{EOL}");

                    index++;
                }

                listOfMessages.Append('.');

                var reply = $"{messages.Count} messages ({messages.Sum(x => x.Size)} octets){EOL}{listOfMessages}";

                return await SendResponse(true, reply);
            }

            case "RETR":
            {
                var messages = bag[Messages] as List<EmailMessage>;

                if (!int.TryParse(Message, out var selectedIndex) || selectedIndex < 1 || selectedIndex > messages.Count)
                {
                    return await SendResponse(false, "No such message");
                }

                var message = messages[selectedIndex - 1];

                var reply = $"{message.Size} octets{EOL}{StuffDots(message.Data)}{EOL}.";

                return await SendResponse(true, reply);
            }

            case "DELE":
            {
                var messages = bag[Messages] as List<EmailMessage>;

                if (!int.TryParse(Message, out var selectedIndex) || selectedIndex < 1 || selectedIndex > messages.Count)
                {
                    return await SendResponse(false, "No such message");
                }

                var pending = bag[PendingDeletions] as HashSet<int>;

                pending.Add(selectedIndex);

                return await SendResponse(true, $"Message {Message} deleted");
            }

            case "UIDL":
            {
                var messages = bag[Messages] as List<EmailMessage>;

                if (!string.IsNullOrEmpty(Message))
                {
                    if (!int.TryParse(Message, out var idx) || idx < 1 || idx > messages.Count)
                    {
                        return await SendResponse(false, "No such message");
                    }

                    return await SendResponse(true, $"{idx} {messages[idx - 1].Id}");
                }

                var uidlList = new StringBuilder();

                var index = 1;

                foreach (var message in messages)
                {
                    uidlList.Append($"{index} {message.Id}{EOL}");

                    index++;
                }

                uidlList.Append('.');

                var reply = $"{messages.Count} messages{EOL}{uidlList}";

                return await SendResponse(true, reply);
            }

            case "TOP":
            {
                var messages = bag[Messages] as List<EmailMessage>;

                var parts = Message.Split(' ', 2);

                if (parts.Length < 2 || !int.TryParse(parts[0], out var msgNum) || !int.TryParse(parts[1], out var lineCount))
                {
                    return await SendResponse(false, "Invalid arguments");
                }

                if (msgNum < 1 || msgNum > messages.Count)
                {
                    return await SendResponse(false, "No such message");
                }

                var message = messages[msgNum - 1];
                var messageData = message.Data;

                var headerEndIdx = messageData.IndexOf("\r\n\r\n", StringComparison.Ordinal);

                string result;

                if (headerEndIdx == -1)
                {
                    result = messageData;
                }
                else
                {
                    var headers = messageData[..(headerEndIdx + 4)];
                    var body = messageData[(headerEndIdx + 4)..];
                    var bodyLines = body.Split(EOL);
                    var limitedBody = string.Join(EOL, bodyLines.Take(lineCount));

                    result = headers + limitedBody;
                }

                var reply = $"Top of message follows{EOL}{StuffDots(result)}{EOL}.";

                return await SendResponse(true, reply);
            }

            case "RSET":
            {
                var pending = bag[PendingDeletions] as HashSet<int>;

                pending.Clear();

                return await SendResponse(true, "Maildrop has been reset");
            }

            case "NOOP":
            {
                return await SendResponse(true, "");
            }

            case "CAPA":
            {
                var capabilities = new StringBuilder();

                capabilities.Append($"Capability list follows{EOL}");
                capabilities.Append($"USER{EOL}");
                capabilities.Append($"UIDL{EOL}");
                capabilities.Append($"TOP{EOL}");
                capabilities.Append($"RESP-CODES{EOL}");
                capabilities.Append('.');

                return await SendResponse(true, capabilities.ToString());
            }

            case "QUIT":
            {
                if ((bool)bag[Authenticated])
                {
                    var pending = bag[PendingDeletions] as HashSet<int>;
                    var messages = bag[Messages] as List<EmailMessage>;

                    foreach (var idx in pending.OrderByDescending(x => x))
                    {
                        if (idx >= 1 && idx <= messages.Count)
                        {
                            Mind.PostOfficeDb.DeleteEmailById(messages[idx - 1].Id);
                        }
                    }
                }

                connection.IsKeepAlive = false;

                return await SendResponse(true, $"{Mind.ProductName} POP3 proxy signing off");
            }
        }

        return await SendResponse(false, "Unknown command");
    }

    private async Task<byte[]> SendResponse(bool success, string message)
    {
        await Task.Delay(0); // LOL

        if (!message.EndsWith(EOL))
        {
            message += EOL;
        }

        return $"{(success ? "+OK" : "-ERR")} {message}".ToASCII();
    }

    // RFC 1939 3: in a multi-line reply, any line starting with '.' is sent with an extra '.' prepended, so the
    // lone '.' that terminates the reply cannot be confused with message content. SMTP ingest deliberately
    // strips this on the way in (SmtpProxy.UnstuffDots), so without the matching stuff on the way out a stored
    // body line of "." truncates the download and a line beginning with "." silently loses a character. The
    // exact inverse of UnstuffDots, and the reason IMAP needs no equivalent is that it frames with literals.
    internal static string StuffDots(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return data;
        }

        if (data.StartsWith('.'))
        {
            data = "." + data;
        }

        return data.Replace($"{EOL}.", $"{EOL}..");
    }

    private static (string Command, string Message) ParseCommandLine(string line)
    {
        var rawData = line.Split(" ", 2);

        var cmd = rawData[0].Trim().ToUpperInvariant();
        var msg = rawData.Length == 2 ? rawData[1].Trim() : string.Empty;

        return (cmd, msg);
    }
}
