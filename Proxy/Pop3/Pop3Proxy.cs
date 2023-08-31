using VintageHive.Network;

namespace VintageHive.Proxy.Pop3;

internal class Pop3Proxy : Listener
{
    private const string EOL = "\r\n";

    private const string Authenticated = "auth";
    private const string Username = "username";
    private const string Messages = "messages";

    public Pop3Proxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = true;

        connection.DataBag[Authenticated] = false;
        connection.DataBag[Username] = string.Empty;

        return await SendResponse(true, "POP3 server ready");
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var bag = connection.DataBag;

        var username = bag[Username].ToString();

        var (Command, Message) = ParseRawCommand(data, read);

        switch (Command)
        {
            case "USER":
            {
                bag[Username] = Message;

                return await SendResponse(true, "User name accepted, password pleas");
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
                var messages = connection.DataBag[Messages] as List<EmailMessage>;

                return await SendResponse(true, $"{messages.Count} {messages.Sum(x => x.Size)}");
            }

            case "LIST":
            {
                var messages = connection.DataBag[Messages] as List<EmailMessage>;
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
                var messages = connection.DataBag[Messages] as List<EmailMessage>;

                var selectedIndex = int.Parse(Message);

                var message = messages[selectedIndex-1];

                var reply = $"{message.Size} octets{EOL}{message.Data}{EOL}.";

                return await SendResponse(true, reply);
            }

            case "DELE":
            {
                var messages = connection.DataBag[Messages] as List<EmailMessage>;

                var selectedIndex = int.Parse(Message);

                var message = messages[selectedIndex - 1];

                Mind.PostOfficeDb.DeleteEmailById(message.Id);

                return await SendResponse(true, $"Message {Message} deleted");
            }

            case "QUIT":
            {
                connection.IsKeepAlive = false;

                return await SendResponse(true, $"VintageHive POP3 proxy signing off");
            }
        }

        return null;
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

        var cmd = rawData[0].Trim();
        var msg = rawData.Length == 2 ? rawData[1].Trim() : string.Empty;

        return (cmd, msg);
    }
}
