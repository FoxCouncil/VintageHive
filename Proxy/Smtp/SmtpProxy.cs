// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.RegularExpressions;
using VintageHive.Network;
using static VintageHive.Proxy.Smtp.SmtpEnums;
using static VintageHive.Proxy.Smtp.SmtpEnums.SmtpResponseCodes;

namespace VintageHive.Proxy.Smtp;

internal partial class SmtpProxy : Listener
{
    private const string Username = "username";
    private const string Password = "password";

    private const string MailTo = "mailto";
    private const string MailFrom = "mailfrom";
    private const string MailFromUser = "mailfrom_user";
    private const string MailFromDomain = "mailfrom_domain";

    private const string MailData = "mail_data";

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
        postmasterThread = new Thread(new ThreadStart(PostmasterRun))
        {
            Name = "VintageHive Postmaster General",
            IsBackground = true
        };

        postmasterThread.Start();
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = true;

        return await SendResponse(ServiceReady, "stmp.hive.com ESMTP ready");
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var bag = connection.DataBag;

        var (Command, Message) = await ReadRequest(data, read, bag.ContainsKey(RequestingData));

        switch (Command)
        {
            case SmtpCommands.HELO:
            {
                bag.Add(Command.ToString(), Message);

                return await SendResponse(RequestedMailActionCompleted, $"Hello {Message}, pleased to meet you.");
            }

            case SmtpCommands.EHLO:
            {
                bag.Add(Command.ToString(), Message);

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

                bag.Add(MailFrom, email);

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

            case SmtpCommands.QUIT:
            {
                connection.IsKeepAlive = false;

                return await SendResponse(ServiceClosingTransmissionChannel, "Goodbye");
            }

            case SmtpCommands.DATA:
            {
                bag.Add(RequestingData, true);
                bag.Add(MailData, string.Empty);

                return await SendResponse(StartMailInput, "Enter mail, end with a single \".\" on a line by itself");
            }

            case SmtpCommands.RSET:
            {
                var initialHello = bag.First();

                bag.TryGetValue(Authenticated, out var username);

                bag.Clear();

                bag.Add(initialHello.Key, initialHello.Value);
                
                if (username != null)
                {
                    bag.Add(Authenticated, username);
                }

                return await SendResponse(RequestedMailActionCompleted, "OK");
            }

            case SmtpCommands.NONE:
            {
                if (bag.ContainsKey(RequestingData))
                {
                    bag[MailData] += Message;

                    if (!(bag[MailData] as string).EndsWith(EOM))
                    {
                        return null;
                    }

                    bag[MailData] = (bag[MailData] as string).Replace(EOM, string.Empty);

                    bag.Remove(RequestingData);

                    Mind.PostOfficeDb.ProcessAndInsertEmail(bag[MailFrom] as EmailAddress, bag[MailTo] as HashSet<EmailAddress>, bag[MailData] as string);

                    return await SendResponse(RequestedMailActionCompleted, "Ok, message accepted for delivery");
                }
                else if (bag.ContainsKey(RequestingUsername) && !bag.ContainsKey(RequestingPassword))
                {
                    // got username
                    bag.Add(Username, Convert.FromBase64String(Message).ToASCII());

                    bag.Remove(RequestingUsername);
                    bag.Add(RequestingPassword, true);

                    // request password
                    return await SendResponse(AuthenticationChallenge, Convert.ToBase64String("Password:".ToASCII()));
                }
                else if (!bag.ContainsKey(RequestingUsername) && bag.ContainsKey(RequestingPassword))
                {
                    // Got password
                    bag.Add(Password, Convert.FromBase64String(Message).ToASCII());

                    bag.Remove(RequestingPassword);

                    var username = bag[Username].ToString();
                    var password = bag[Password].ToString();

                    var user = Mind.Db.UserFetch(username, password);

                    if (user != null)
                    {
                        bag.Add(Authenticated, username);

                        return await SendResponse(AuthenticationSuccessful, "Authentication successful");
                    }
                    else
                    {
                        return await SendResponse(AuthenticationFailed, "Invalid Username and/or Password.");
                    }
                }

                return null;
            }

            default:
            {
                return null;
            }
        }
    }

    private async Task<byte[]> SendResponse(SmtpResponseCodes responseCode, string[] messages)
    {
        await Task.Delay(0); // LOL

        var sb = new StringBuilder();

        foreach(var message in messages)
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

    private void PostmasterRun()
    {
        var logName = GetType().Name + "Postmaster";

        Thread.Sleep(5000);

        Log.WriteLine(Log.LEVEL_INFO, logName, $"Starting VintageHive Postmaster Thread", "");

        while (postmasterThreadRunning)
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

                    Log.WriteLine(Log.LEVEL_DEBUG, logName, $"Processing {email.Id}: ({email.Size}) SUCCESSFULLY DELIVERED!", "");
                }
                else
                {
                    Mind.PostOfficeDb.DeleteEmailById(email.Id);
                    Mind.PostOfficeDb.InsertUndeliverableEmail(email.FromAddress.Full, email.ToAddress.Full);

                    Log.WriteLine(Log.LEVEL_DEBUG, logName, $"Processing {email.Id}: ({email.Size}) User {email.ToAddress} not found, rejecting and sending bounce email!", "");
                }
            }

            Thread.Sleep(1000);
        }

        Log.WriteLine(Log.LEVEL_INFO, logName, $"Exiting VintageHive Postmaster Thread", "");
    }
}
