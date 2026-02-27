// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Pop3;

internal class Pop3Proxy : Listener
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

        return await SendResponse(true, "POP3 server ready");
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var bag = connection.DataBag;

        var username = bag[Username].ToString();

        var (Command, Message) = ParseRawCommand(data, read);

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
                bag[Username] = Message;

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
                    var idx = int.Parse(Message);

                    if (idx < 1 || idx > messages.Count)
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

                var selectedIndex = int.Parse(Message);

                if (selectedIndex < 1 || selectedIndex > messages.Count)
                {
                    return await SendResponse(false, "No such message");
                }

                var message = messages[selectedIndex - 1];

                var reply = $"{message.Size} octets{EOL}{message.Data}{EOL}.";

                return await SendResponse(true, reply);
            }

            case "DELE":
            {
                var messages = bag[Messages] as List<EmailMessage>;

                var selectedIndex = int.Parse(Message);

                if (selectedIndex < 1 || selectedIndex > messages.Count)
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
                    var idx = int.Parse(Message);

                    if (idx < 1 || idx > messages.Count)
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

                var reply = $"Top of message follows{EOL}{result}{EOL}.";

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

                return await SendResponse(true, $"VintageHive POP3 proxy signing off");
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

    private (string Command, string Message) ParseRawCommand(ReadOnlySpan<byte> data, int read)
    {
        var rawData = data[..read].ToASCII().Split(" ", 2);

        var cmd = rawData[0].Trim().ToUpperInvariant();
        var msg = rawData.Length == 2 ? rawData[1].Trim() : string.Empty;

        return (cmd, msg);
    }
}
