// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;
using VintageHive.Network;
using static VintageHive.Proxy.Smtp.SmtpEnums;
using static VintageHive.Proxy.Smtp.SmtpEnums.SmtpResponseCodes;

namespace VintageHive.Proxy.Smtp;

public partial class SmtpProxy : Listener
{
    private const string Username = "username";
    private const string Password = "password";

    private const string MailTo = "mailto";
    private const string MailFrom = "mailfrom";
    private const string MailFromUser = "mailfrom_user";
    private const string MailFromDomain = "mailfrom_domain";

    private const string MailData = "mail_data";

    // Bound an unauthenticated DATA stream so it can't exhaust memory on the default 0.0.0.0 bind
    private const int MaxMessageBytes = 32 * 1024 * 1024;

    private const string RequestingUsername = "requesting_username";
    private const string RequestingPassword = "requesting_password";
    private const string RequestingData = "requesting_data";

    private const string Authenticated = "auth";

    [GeneratedRegex(@"<(?<user>[^@]+)@(?<domain>[^>]+)>", RegexOptions.Compiled)]
    private static partial Regex RegexEmail();

    private Thread postmasterThread;
    private bool postmasterThreadRunning = true;

    public SmtpProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
    }

    public void StartPostmaster()
    {
        if (postmasterThread != null)
        {
            return;
        }

        postmasterThread = new Thread(new ThreadStart(PostmasterRun))
        {
            Name = $"{Mind.ProductName} Postmaster General",
            IsBackground = true
        };

        postmasterThread.Start();
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = true;

        return await SendResponse(ServiceReady, $"{HiveDomains.Smtp} ESMTP ready");
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        const string LineBufferKey = "_smtp_linebuf";
        const int MaxLineBytes = 8 * 1024;

        var bag = connection.DataBag;

        // In DATA mode the payload is the message body (accumulated until EOM by the NONE case) - pass it straight
        // through without line-splitting. In command mode, buffer to CRLF so a split command is reassembled and
        // pipelined commands are all answered instead of only the first.
        if (bag.ContainsKey(RequestingData))
        {
            return await ProcessRequestInner(connection, data, read);
        }

        var prev = bag.TryGetValue(LineBufferKey, out var lb) ? lb as string : string.Empty;
        var buffer = prev + Encoding.ASCII.GetString(data, 0, read);

        var responses = new List<byte>();

        int start = 0, nl;

        while ((nl = buffer.IndexOf('\n', start)) != -1)
        {
            var line = buffer[start..nl].TrimEnd('\r');

            start = nl + 1;

            var lineBytes = Encoding.ASCII.GetBytes(line);

            var resp = await ProcessRequestInner(connection, lineBytes, lineBytes.Length);

            if (resp != null)
            {
                responses.AddRange(resp);
            }

            if (!connection.IsKeepAlive)
            {
                bag[LineBufferKey] = string.Empty;

                return responses.Count > 0 ? responses.ToArray() : null;
            }

            // If that command (DATA) switched us into body mode, the rest of the buffer is message body
            if (bag.ContainsKey(RequestingData))
            {
                bag[LineBufferKey] = string.Empty;

                if (start < buffer.Length)
                {
                    var bodyBytes = Encoding.ASCII.GetBytes(buffer[start..]);

                    var bodyResp = await ProcessRequestInner(connection, bodyBytes, bodyBytes.Length);

                    if (bodyResp != null)
                    {
                        responses.AddRange(bodyResp);
                    }
                }

                return responses.Count > 0 ? responses.ToArray() : null;
            }
        }

        var remainder = buffer[start..];

        bag[LineBufferKey] = remainder.Length > MaxLineBytes ? string.Empty : remainder;

        return responses.Count > 0 ? responses.ToArray() : null;
    }

    private async Task<byte[]> ProcessRequestInner(ListenerSocket connection, byte[] data, int read)
    {
        var bag = connection.DataBag;

        var (Command, Message) = await ReadRequest(data, read, bag.ContainsKey(RequestingData));

        switch (Command)
        {
            case SmtpCommands.HELO:
            {
                bag[Command.ToString()] = Message;

                return await SendResponse(RequestedMailActionCompleted, $"Hello {Message}, pleased to meet you.");
            }

            case SmtpCommands.EHLO:
            {
                bag[Command.ToString()] = Message;

                var helloResponse = $"Hello {Message}, pleased to meet you. ESMTP features supported.";

                var responseArray = new string[] { helloResponse, "AUTH LOGIN" };

                return await SendResponse(RequestedMailActionCompleted, responseArray);
            }

            case SmtpCommands.MAIL:
            {
                if (!bag.ContainsKey(Authenticated))
                {
                    return await SendResponse(AuthenticationRequired, "Authentication required before sending mail.");
                }

                var email = EmailAddress.ParseFromSmtp(Message);

                if (email.User != bag[Authenticated].ToString())
                {
                    return await SendResponse(MailboxUnavailable, "Cannot relay email for another user!");
                }

                bag[MailFrom] = email;

                return await SendResponse(RequestedMailActionCompleted, $"<{email.Full}> sender accepted");
            }

            case SmtpCommands.RCPT:
            {
                if (!bag.ContainsKey(Authenticated))
                {
                    return await SendResponse(AuthenticationRequired, "Authentication required before sending mail.");
                }

                var email = EmailAddress.ParseFromSmtp(Message);

                if (!bag.ContainsKey(MailTo))
                {
                    bag.Add(MailTo, new HashSet<EmailAddress>());
                    (bag[MailTo] as HashSet<EmailAddress>).Add(email);
                }
                else
                {
                    (bag[MailTo] as HashSet<EmailAddress>).Add(email);
                }

                return await SendResponse(RequestedMailActionCompleted, $"<{email.Full}> recipient accepted");
            }

            case SmtpCommands.AUTH:
            {
                if (!bag.ContainsKey(RequestingUsername) && !bag.ContainsKey(RequestingPassword))
                {
                    bag.Add(RequestingUsername, true);

                    return await SendResponse(AuthenticationChallenge, Convert.ToBase64String("Username:".ToASCII()));
                }

                return null;
            }

            case SmtpCommands.NOOP:
            {
                return await SendResponse(RequestedMailActionCompleted, "OK");
            }

            case SmtpCommands.HELP:
            {
                return await SendResponse(SmtpResponseCodes.CommandNotImplemented, $"{Mind.ProductName} SMTP server. For help, visit http://{HiveDomains.Intranet}/help/email.html");
            }

            case SmtpCommands.VRFY:
            {
                return await SendResponse(CannotVerifyUser, "Cannot VRFY user, but will accept message and attempt delivery");
            }

            case SmtpCommands.QUIT:
            {
                connection.IsKeepAlive = false;

                return await SendResponse(ServiceClosingTransmissionChannel, "Goodbye");
            }

            case SmtpCommands.DATA:
            {
                // Reject DATA before a transaction is set up - otherwise delivery later NREs on the missing MAIL FROM
                if (!bag.ContainsKey(MailFrom) || !bag.ContainsKey(MailTo))
                {
                    return await SendResponse(BadSequenceOfCommands, "Need MAIL FROM and RCPT TO before DATA");
                }

                bag[RequestingData] = true;
                bag[MailData] = string.Empty;

                return await SendResponse(StartMailInput, "Enter mail, end with a single \".\" on a line by itself");
            }

            case SmtpCommands.RSET:
            {
                bag.TryGetValue(Authenticated, out var username);

                if (bag.Count > 0)
                {
                    var initialHello = bag.First();

                    bag.Clear();

                    bag.Add(initialHello.Key, initialHello.Value);
                }

                if (username != null)
                {
                    bag[Authenticated] = username;
                }

                return await SendResponse(RequestedMailActionCompleted, "OK");
            }

            case SmtpCommands.NONE:
            {
                if (bag.ContainsKey(RequestingData))
                {
                    var mailData = (bag[MailData] as string) + Message;

                    if (mailData.Length > MaxMessageBytes)
                    {
                        bag.Remove(RequestingData);
                        bag.Remove(MailData);
                        bag.Remove(MailFrom);
                        bag.Remove(MailTo);

                        return await SendResponse(ExceededStorageAllocation, "Message exceeds the maximum allowed size");
                    }

                    bag[MailData] = mailData;

                    if (!mailData.EndsWith(EOM))
                    {
                        return null;
                    }

                    // Strip ONLY the trailing terminator (Replace(EOM) mangled interior sequences), then reverse
                    // SMTP dot-stuffing so IMAP FETCH doesn't expose ".." corruption on lines that began with a dot.
                    var message = UnstuffDots(mailData[..^EOM.Length]);

                    bag.Remove(RequestingData);
                    bag.Remove(MailData);

                    Mind.PostOfficeDb.ProcessAndInsertEmail(bag[MailFrom] as EmailAddress, bag[MailTo] as HashSet<EmailAddress>, message);

                    bag.Remove(MailFrom);
                    bag.Remove(MailTo);

                    return await SendResponse(RequestedMailActionCompleted, "Ok, message accepted for delivery");
                }
                else if (bag.ContainsKey(RequestingUsername) && !bag.ContainsKey(RequestingPassword))
                {
                    // RFC 4954: a bare '*' cancels AUTH, and any non-base64 line is a protocol error.
                    // Either way, abort cleanly instead of letting Convert.FromBase64String throw and
                    // tear the session down.
                    if (Message == "*" || !TryDecodeBase64(Message, out var decodedUsername))
                    {
                        bag.Remove(RequestingUsername);

                        return await SendResponse(SyntaxError, "Authentication aborted");
                    }

                    // got username
                    bag[Username] = decodedUsername;

                    bag.Remove(RequestingUsername);
                    bag.Add(RequestingPassword, true);

                    // request password
                    return await SendResponse(AuthenticationChallenge, Convert.ToBase64String("Password:".ToASCII()));
                }
                else if (!bag.ContainsKey(RequestingUsername) && bag.ContainsKey(RequestingPassword))
                {
                    if (Message == "*" || !TryDecodeBase64(Message, out var decodedPassword))
                    {
                        bag.Remove(RequestingPassword);

                        return await SendResponse(SyntaxError, "Authentication aborted");
                    }

                    // Got password
                    bag[Password] = decodedPassword;

                    bag.Remove(RequestingPassword);

                    var username = bag[Username].ToString();
                    var password = bag[Password].ToString();

                    var user = Mind.Db.UserFetch(username, password);

                    if (user != null)
                    {
                        bag[Authenticated] = username;

                        return await SendResponse(AuthenticationSuccessful, "Authentication successful");
                    }
                    else
                    {
                        return await SendResponse(AuthenticationFailed, "Invalid Username and/or Password.");
                    }
                }

                // An unrecognized but non-empty command must get a 500 (RFC 5321 4.2.1), else a conformant
                // client hangs waiting for a reply that never comes. Empty/whitespace lines stay ignored.
                if (!string.IsNullOrWhiteSpace(Message))
                {
                    return await SendResponse(SyntaxError, "Syntax error, command unrecognized");
                }

                return null;
            }

            default:
            {
                return await SendResponse(SyntaxError, "Syntax error, command unrecognized");
            }
        }
    }

    private async Task<byte[]> SendResponse(SmtpResponseCodes responseCode, string[] messages)
    {
        await Task.Delay(0); // LOL

        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            if (message != messages.Last())
            {
                sb.Append($"{(int)responseCode}-{message}{EOL}");
            }
            else
            {
                sb.Append($"{(int)responseCode} {message}{EOL}");
            }
        }

        return sb.ToString().ToASCII();
    }

    private async Task<byte[]> SendResponse(SmtpResponseCodes responseCode, string message)
    {
        await Task.Delay(0); // LOL

        return $"{(int)responseCode} {message}{EOL}".ToASCII();
    }

    private async Task<(SmtpCommands Command, string Message)> ReadRequest(byte[] data, int read, bool isData = false)
    {
        await Task.Delay(0); // LOL

        var rawCommand = data[..read].ToASCII();
        var parsedCommand = rawCommand.Split(' ', 2);

        if (!isData && Enum.TryParse(typeof(SmtpCommands), parsedCommand[0], true, out var command))
        {
            if (parsedCommand.Length == 1)
            {
                return ((SmtpCommands)command, string.Empty);
            }
            else
            {
                return ((SmtpCommands)command, parsedCommand[1].TrimEnd());
            }
        }
        else
        {
            return (SmtpCommands.NONE, rawCommand);
        }
    }

    // Reverse SMTP dot-stuffing: a line that began with '.' was transmitted as '..'
    private static string UnstuffDots(string data)
    {
        if (data.StartsWith(".."))
        {
            data = data[1..];
        }

        return data.Replace("\r\n..", "\r\n.");
    }

    // Decode a base64 AUTH LOGIN line without throwing on malformed input (a hostile or aborting client).
    private static bool TryDecodeBase64(string value, out string decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value).ToASCII();

            return true;
        }
        catch (FormatException)
        {
            decoded = null;

            return false;
        }
    }

    private void PostmasterRun()
    {
        var logName = GetType().Name + "Postmaster";

        Thread.Sleep(5000);

        Log.WriteLine(Log.LEVEL_INFO, logName, $"Starting VintageHive Postmaster Thread", "");

        while (postmasterThreadRunning)
        {
          try
          {
            var emailsToProcess = Mind.PostOfficeDb.GetUndeliveredEmails();

            if (emailsToProcess.Count != 0) Log.WriteLine(Log.LEVEL_DEBUG, logName, $"Detected {emailsToProcess.Count} undelivered emails to process.", "");

            foreach (var email in emailsToProcess)
            {
                Log.WriteLine(Log.LEVEL_DEBUG, logName, $"Processing {email.Id}: ({email.Size}) <{email.FromAddress}> -> <{email.ToAddress}>", "");

                var toUser = email.ToAddress.User;

                if (Mind.Db.UserExistsByUsername(toUser))
                {
                    Mind.PostOfficeDb.MarkEmailAsDelivered(email.Id);

                    var inbox = Mind.PostOfficeDb.GetMailboxByName(toUser, "INBOX");

                    if (inbox == null)
                    {
                        Mind.PostOfficeDb.CreateDefaultMailboxes(toUser);
                        inbox = Mind.PostOfficeDb.GetMailboxByName(toUser, "INBOX");
                    }

                    if (inbox != null)
                    {
                        Mind.PostOfficeDb.AssignMessageToMailbox(email.Id, inbox.Value.Id);
                    }

                    Log.WriteLine(Log.LEVEL_DEBUG, logName, $"Processing {email.Id}: ({email.Size}) SUCCESSFULLY DELIVERED!", "");
                }
                else
                {
                    Mind.PostOfficeDb.DeleteEmailById(email.Id);
                    Mind.PostOfficeDb.InsertUndeliverableEmail(email.FromAddress.Full, email.ToAddress.Full);

                    Log.WriteLine(Log.LEVEL_DEBUG, logName, $"Processing {email.Id}: ({email.Size}) User {email.ToAddress} not found, rejecting and sending bounce email!", "");
                }
            }
          }
          catch (Exception ex)
          {
            // A rare SqliteException (disk I/O, corruption, external lock) used to escape and kill the process
            Log.WriteException(logName, ex, "");
          }

            Thread.Sleep(1000);
        }

        Log.WriteLine(Log.LEVEL_INFO, logName, $"Exiting VintageHive Postmaster Thread", "");
    }
}
