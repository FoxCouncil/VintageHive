// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Globalization;
using System.Text.RegularExpressions;
using VintageHive.Network;

namespace VintageHive.Proxy.Imap;

public partial class ImapProxy : Listener
{
    private const string EOL = "\r\n";
    private const string SessionKey = "imap_session";

    private const string Capability = "IMAP4rev1 AUTH=LOGIN";

    // Bound an APPEND literal so a hostile size can't exhaust memory (mirrors SMTP's DATA cap).
    private const int MaxAppendBytes = 32 * 1024 * 1024;

    [GeneratedRegex(@"^(\S+)\s+(\S+)(?:\s+(.*))?$", RegexOptions.Compiled)]
    private static partial Regex CommandRegex();

    // RFC 3501 6.3.11: mailbox [(flags)] [date-time] {size}. Anchored, and the size digit count is
    // capped so a 19+ digit literal can't overflow the long parse downstream.
    [GeneratedRegex(@"^(?<mailbox>""[^""]*""|[^\s(]+)(?:\s+\((?<flags>[^)]*)\))?(?:\s+(?<date>""[^""]*""))?\s+\{(?<size>\d{1,18})(?<nonsync>\+)?\}$", RegexOptions.Compiled)]
    private static partial Regex AppendArgsRegex();

    // Envelope-column lifts for APPEND (^ binds per-line under Multiline; value stops at the line end).
    [GeneratedRegex(@"^From:[ \t]*(?<value>[^\r\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FromHeaderRegex();

    [GeneratedRegex(@"^To:[ \t]*(?<value>[^\r\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ToHeaderRegex();

    [GeneratedRegex(@"^Subject:[ \t]*(?<value>[^\r\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SubjectHeaderRegex();

    public ImapProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = true;

        connection.DataBag[SessionKey] = new ImapSession();

        return BuildResponse($"* OK imap.{MailDomains.Primary} {Mind.ProductName} IMAP4rev1 server ready{EOL}");
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        await Task.Delay(0);

        const string LineBufferKey = "_imap_linebuf";
        const int MaxLineBytes = 16 * 1024;

        var session = connection.DataBag[SessionKey] as ImapSession;

        // Buffer to CRLF and loop so pipelined commands are all answered and a command split across TCP reads
        // is reassembled instead of misparsed (its tag would otherwise never be answered - the client stalls).
        var prev = connection.DataBag.TryGetValue(LineBufferKey, out var b) ? b as string : string.Empty;
        var buffer = prev + Encoding.ASCII.GetString(data, 0, read);

        var responses = new List<byte>();

        while (buffer.Length > 0)
        {
            // An armed APPEND literal is byte-counted message data full of CRLFs - drain it here so
            // it never reaches the line splitter below.
            if (session.Append != null)
            {
                var take = Math.Min(session.Append.Remaining, buffer.Length);

                session.Append.Data.Append(buffer, 0, take);
                session.Append.Remaining -= take;

                buffer = buffer[take..];

                if (session.Append.Remaining > 0)
                {
                    break;
                }

                responses.AddRange(FinalizeAppend(session));

                continue;
            }

            var idx = buffer.IndexOf('\n');

            if (idx == -1)
            {
                break;
            }

            var line = buffer[..idx].TrimEnd('\r');

            buffer = buffer[(idx + 1)..];

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var resp = ProcessCommandLine(connection, line);

            if (resp != null)
            {
                responses.AddRange(resp);
            }

            if (!connection.IsKeepAlive)
            {
                connection.DataBag[LineBufferKey] = string.Empty;

                return responses.Count > 0 ? responses.ToArray() : null;
            }
        }

        connection.DataBag[LineBufferKey] = buffer.Length > MaxLineBytes ? string.Empty : buffer;

        return responses.Count > 0 ? responses.ToArray() : null;
    }

    private byte[] ProcessCommandLine(ListenerSocket connection, string rawLine)
    {
        var session = connection.DataBag[SessionKey] as ImapSession;

        // Mid-AUTHENTICATE, lines are SASL challenge responses (raw base64 or a lone "*"), not tagged
        // commands - intercept before the command grammar gets a chance to misread them.
        if (session.AuthExchange != ImapAuthExchange.None)
        {
            return HandleAuthenticateResponse(rawLine, session);
        }

        var match = CommandRegex().Match(rawLine);

        if (!match.Success)
        {
            return BuildResponse($"* BAD Invalid command format{EOL}");
        }

        var tag = match.Groups[1].Value;
        var command = match.Groups[2].Value.ToUpperInvariant();
        var args = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

        var lineBytes = Encoding.ASCII.GetBytes(rawLine);

        return command switch
        {
            "CAPABILITY" => HandleCapability(tag),
            "NOOP" => HandleNoop(tag, session),
            "LOGOUT" => HandleLogout(tag, session, connection),
            "LOGIN" => HandleLogin(tag, args, session),
            "AUTHENTICATE" => HandleAuthenticate(tag, args, session),
            "SELECT" => HandleSelect(tag, args, session, readOnly: false),
            "EXAMINE" => HandleSelect(tag, args, session, readOnly: true),
            "CREATE" => HandleCreate(tag, args, session),
            "DELETE" => HandleDelete(tag, args, session),
            "RENAME" => HandleRename(tag, args, session),
            "SUBSCRIBE" => HandleSubscribe(tag, args, session),
            "UNSUBSCRIBE" => HandleUnsubscribe(tag, args, session),
            "LIST" => HandleList(tag, args, session),
            "LSUB" => HandleLsub(tag, args, session),
            "STATUS" => HandleStatus(tag, args, session),
            "APPEND" => HandleAppend(tag, args, session, connection, lineBytes, lineBytes.Length),
            "CLOSE" => HandleClose(tag, session),
            "EXPUNGE" => HandleExpunge(tag, session),
            "SEARCH" => HandleSearch(tag, args, session, useUid: false),
            "FETCH" => HandleFetch(tag, args, session, useUid: false),
            "STORE" => HandleStore(tag, args, session, useUid: false),
            "COPY" => HandleCopy(tag, args, session, useUid: false),
            "UID" => HandleUid(tag, args, session, connection, lineBytes, lineBytes.Length),
            _ => BuildResponse($"{tag} BAD Unknown command{EOL}")
        };
    }

    #region Any State Commands

    private byte[] HandleCapability(string tag)
    {
        var sb = new StringBuilder();

        sb.Append($"* CAPABILITY {Capability}{EOL}");
        sb.Append($"{tag} OK CAPABILITY completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleNoop(string tag, ImapSession session)
    {
        var sb = new StringBuilder();

        if (session.State == ImapState.Selected)
        {
            var status = Mind.PostOfficeDb.GetMailboxStatus(session.SelectedMailboxId);

            sb.Append($"* {status.MessageCount} EXISTS{EOL}");
            sb.Append($"* {status.Recent} RECENT{EOL}");
        }

        sb.Append($"{tag} OK NOOP completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleLogout(string tag, ImapSession session, ListenerSocket connection)
    {
        session.State = ImapState.Logout;
        connection.IsKeepAlive = false;

        var sb = new StringBuilder();

        sb.Append($"* BYE {Mind.ProductName} IMAP4rev1 server logging out{EOL}");
        sb.Append($"{tag} OK LOGOUT completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    #endregion

    #region Not Authenticated State Commands

    private byte[] HandleLogin(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.NotAuthenticated)
        {
            return BuildResponse($"{tag} BAD Already authenticated{EOL}");
        }

        var (username, password) = ParseTwoArgs(args);

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return BuildResponse($"{tag} BAD Missing username or password{EOL}");
        }

        // Same seam as POP3 USER and SMTP AUTH: a qualified login resolves to its local part, a
        // foreign or malformed domain is rejected as such instead of surfacing as a password failure.
        if (!MailDomains.TryResolveLogin(username, out var localPart, out _))
        {
            return BuildResponse($"{tag} NO Mailbox domain not hosted here{EOL}");
        }

        var user = Mind.Db.UserFetch(localPart, password);

        if (user == null)
        {
            return BuildResponse($"{tag} NO LOGIN failed{EOL}");
        }

        session.State = ImapState.Authenticated;
        session.Username = user.Username;

        Mind.PostOfficeDb.CreateDefaultMailboxes(session.Username);

        return BuildResponse($"{tag} OK LOGIN completed{EOL}");
    }

    // RFC 3501 6.2.2. CAPABILITY advertises AUTH=LOGIN, and SASL-preferring clients (curl among them)
    // issue AUTHENTICATE before ever considering the plain LOGIN command - this used to fall through
    // to "BAD Unknown command" and the client gave up without sending credentials.
    private byte[] HandleAuthenticate(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.NotAuthenticated)
        {
            return BuildResponse($"{tag} BAD Already authenticated{EOL}");
        }

        var parts = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return BuildResponse($"{tag} BAD Missing authentication mechanism{EOL}");
        }

        // SASL-IR (RFC 4959) is not advertised, so an initial response argument is a protocol error.
        if (parts.Length > 1)
        {
            return BuildResponse($"{tag} BAD Initial response not supported{EOL}");
        }

        if (parts[0].ToUpperInvariant() != "LOGIN")
        {
            return BuildResponse($"{tag} NO Unsupported authentication mechanism{EOL}");
        }

        session.AuthExchange = ImapAuthExchange.WantUsername;
        session.AuthTag = tag;
        session.AuthUsername = string.Empty;

        return BuildResponse($"+ {ToBase64("Username:")}{EOL}");
    }

    private byte[] HandleAuthenticateResponse(string line, ImapSession session)
    {
        var tag = session.AuthTag;

        // RFC 3501 6.2.2: a bare "*" cancels the exchange, and any non-base64 line is a protocol
        // error. Abort cleanly either way (same hardening as the SMTP AUTH path) instead of letting
        // Convert.FromBase64String throw and tear the session down.
        if (line == "*" || !TryDecodeBase64(line, out var decoded))
        {
            ResetAuthExchange(session);

            return BuildResponse($"{tag} BAD Authentication aborted{EOL}");
        }

        if (session.AuthExchange == ImapAuthExchange.WantUsername)
        {
            // Hosted-domain seam, same as LOGIN: fred@hosted resolves to fred, a foreign or malformed
            // domain is rejected as such - never stripped, never surfaced as a password failure.
            if (!MailDomains.TryResolveLogin(decoded, out var localPart, out _))
            {
                ResetAuthExchange(session);

                return BuildResponse($"{tag} NO Mailbox domain not hosted here{EOL}");
            }

            session.AuthUsername = localPart;
            session.AuthExchange = ImapAuthExchange.WantPassword;

            return BuildResponse($"+ {ToBase64("Password:")}{EOL}");
        }

        var username = session.AuthUsername;

        ResetAuthExchange(session);

        var user = Mind.Db.UserFetch(username, decoded);

        if (user == null)
        {
            return BuildResponse($"{tag} NO AUTHENTICATE failed{EOL}");
        }

        session.State = ImapState.Authenticated;
        session.Username = user.Username;

        Mind.PostOfficeDb.CreateDefaultMailboxes(session.Username);

        return BuildResponse($"{tag} OK AUTHENTICATE completed{EOL}");
    }

    private static void ResetAuthExchange(ImapSession session)
    {
        session.AuthExchange = ImapAuthExchange.None;
        session.AuthTag = string.Empty;
        session.AuthUsername = string.Empty;
    }

    private static string ToBase64(string value)
    {
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(value));
    }

    // Decode a base64 SASL response without throwing on malformed input (a hostile or aborting client).
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

    #endregion

    #region Authenticated State Commands

    private byte[] HandleSelect(string tag, string args, ImapSession session, bool readOnly)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var mailboxName = UnquoteMailboxName(args.Trim());

        var mailbox = Mind.PostOfficeDb.GetMailboxByName(session.Username, mailboxName);

        if (mailbox == null)
        {
            return BuildResponse($"{tag} NO Mailbox does not exist{EOL}");
        }

        session.SelectedMailboxId = mailbox.Value.Id;
        session.SelectedMailbox = mailbox.Value.Name;
        session.SelectedReadOnly = readOnly;
        session.UidValidity = mailbox.Value.UidValidity;
        session.State = ImapState.Selected;

        var status = Mind.PostOfficeDb.GetMailboxStatus(session.SelectedMailboxId);
        session.Messages = Mind.PostOfficeDb.GetMessagesForMailbox(session.SelectedMailboxId);
        session.UidNext = status.UidNext;

        var sb = new StringBuilder();

        sb.Append($"* {status.MessageCount} EXISTS{EOL}");
        sb.Append($"* {status.Recent} RECENT{EOL}");

        if (status.Unseen > 0)
        {
            var firstUnseen = session.Messages.FindIndex(m => !m.Flags.Contains(ImapMessageFlag.Seen)) + 1;

            if (firstUnseen > 0)
            {
                sb.Append($"* OK [UNSEEN {firstUnseen}] First unseen message{EOL}");
            }
        }

        sb.Append($"* OK [UIDVALIDITY {session.UidValidity}] UIDs valid{EOL}");
        sb.Append($"* OK [UIDNEXT {session.UidNext}] Predicted next UID{EOL}");
        sb.Append($"* FLAGS ({string.Join(" ", ImapMessageFlag.AllFlags)}){EOL}");
        sb.Append($"* OK [PERMANENTFLAGS ({string.Join(" ", ImapMessageFlag.AllPermanentFlags)} \\*)] Permanent flags{EOL}");

        var accessMode = readOnly ? "READ-ONLY" : "READ-WRITE";
        var cmdName = readOnly ? "EXAMINE" : "SELECT";

        sb.Append($"{tag} OK [{accessMode}] {cmdName} completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleCreate(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var name = UnquoteMailboxName(args.Trim());

        if (string.IsNullOrEmpty(name))
        {
            return BuildResponse($"{tag} BAD Missing mailbox name{EOL}");
        }

        var existing = Mind.PostOfficeDb.GetMailboxByName(session.Username, name);

        if (existing != null)
        {
            return BuildResponse($"{tag} NO Mailbox already exists{EOL}");
        }

        Mind.PostOfficeDb.CreateMailbox(session.Username, name);

        return BuildResponse($"{tag} OK CREATE completed{EOL}");
    }

    private byte[] HandleDelete(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var name = UnquoteMailboxName(args.Trim());

        if (name.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResponse($"{tag} NO Cannot delete INBOX{EOL}");
        }

        if (!Mind.PostOfficeDb.DeleteMailbox(session.Username, name))
        {
            return BuildResponse($"{tag} NO Mailbox does not exist{EOL}");
        }

        return BuildResponse($"{tag} OK DELETE completed{EOL}");
    }

    private byte[] HandleRename(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var (oldName, newName) = ParseTwoArgs(args);

        oldName = UnquoteMailboxName(oldName);
        newName = UnquoteMailboxName(newName);

        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
        {
            return BuildResponse($"{tag} BAD Missing arguments{EOL}");
        }

        if (oldName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResponse($"{tag} NO Cannot rename INBOX{EOL}");
        }

        if (!Mind.PostOfficeDb.RenameMailbox(session.Username, oldName, newName))
        {
            return BuildResponse($"{tag} NO Rename failed{EOL}");
        }

        return BuildResponse($"{tag} OK RENAME completed{EOL}");
    }

    private byte[] HandleSubscribe(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var name = UnquoteMailboxName(args.Trim());

        Mind.PostOfficeDb.SubscribeMailbox(session.Username, name);

        return BuildResponse($"{tag} OK SUBSCRIBE completed{EOL}");
    }

    private byte[] HandleUnsubscribe(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var name = UnquoteMailboxName(args.Trim());

        Mind.PostOfficeDb.UnsubscribeMailbox(session.Username, name);

        return BuildResponse($"{tag} OK UNSUBSCRIBE completed{EOL}");
    }

    private byte[] HandleList(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var (reference, pattern) = ParseTwoArgs(args);

        reference = UnquoteMailboxName(reference);
        pattern = UnquoteMailboxName(pattern);

        if (string.IsNullOrEmpty(pattern))
        {
            var sb2 = new StringBuilder();

            sb2.Append($"* LIST (\\Noselect) \"/\" \"\"{EOL}");
            sb2.Append($"{tag} OK LIST completed{EOL}");

            return BuildResponse(sb2.ToString());
        }

        var mailboxes = Mind.PostOfficeDb.GetMailboxesForUser(session.Username);

        var sb = new StringBuilder();

        foreach (var mbox in mailboxes)
        {
            if (MatchesMailboxPattern(mbox.Name, pattern))
            {
                var flags = GetMailboxFlags(mbox.Name);

                sb.Append($"* LIST ({flags}) \"/\" \"{mbox.Name}\"{EOL}");
            }
        }

        sb.Append($"{tag} OK LIST completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleLsub(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var (reference, pattern) = ParseTwoArgs(args);

        reference = UnquoteMailboxName(reference);
        pattern = UnquoteMailboxName(pattern);

        var mailboxes = Mind.PostOfficeDb.GetSubscribedMailboxes(session.Username);

        var sb = new StringBuilder();

        foreach (var mbox in mailboxes)
        {
            if (MatchesMailboxPattern(mbox.Name, pattern))
            {
                var flags = GetMailboxFlags(mbox.Name);

                sb.Append($"* LSUB ({flags}) \"/\" \"{mbox.Name}\"{EOL}");
            }
        }

        sb.Append($"{tag} OK LSUB completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleStatus(string tag, string args, ImapSession session)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var parenIdx = args.IndexOf('(');

        if (parenIdx == -1)
        {
            return BuildResponse($"{tag} BAD Missing status data items{EOL}");
        }

        var mailboxName = UnquoteMailboxName(args[..parenIdx].Trim());
        var itemsStr = args[parenIdx..].Trim().Trim('(', ')');
        var items = itemsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var mailbox = Mind.PostOfficeDb.GetMailboxByName(session.Username, mailboxName);

        if (mailbox == null)
        {
            return BuildResponse($"{tag} NO Mailbox does not exist{EOL}");
        }

        var status = Mind.PostOfficeDb.GetMailboxStatus(mailbox.Value.Id);

        var resultItems = new List<string>();

        foreach (var item in items)
        {
            switch (item.ToUpperInvariant())
            {
                case "MESSAGES":
                {
                    resultItems.Add($"MESSAGES {status.MessageCount}");
                    break;
                }

                case "RECENT":
                {
                    resultItems.Add($"RECENT {status.Recent}");
                    break;
                }

                case "UIDNEXT":
                {
                    resultItems.Add($"UIDNEXT {status.UidNext}");
                    break;
                }

                case "UIDVALIDITY":
                {
                    resultItems.Add($"UIDVALIDITY {status.UidValidity}");
                    break;
                }

                case "UNSEEN":
                {
                    resultItems.Add($"UNSEEN {status.Unseen}");
                    break;
                }
            }
        }

        var sb = new StringBuilder();

        sb.Append($"* STATUS \"{mailboxName}\" ({string.Join(" ", resultItems)}){EOL}");
        sb.Append($"{tag} OK STATUS completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    // RFC 3501 6.3.11. Outlook Express uploads every sent message here ("Sent Items"), so the old
    // "NO APPEND not supported" stub raised an 0x800CCCD2 error dialog on each send.
    private byte[] HandleAppend(string tag, string args, ImapSession session, ListenerSocket connection, byte[] data, int read)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        var match = AppendArgsRegex().Match(args.Trim());

        if (!match.Success)
        {
            return BuildResponse($"{tag} BAD Invalid APPEND arguments{EOL}");
        }

        var size = long.Parse(match.Groups["size"].Value);

        if (size > MaxAppendBytes)
        {
            // Refused BEFORE the continuation, so a compliant client never sends the payload.
            return BuildResponse($"{tag} NO Message exceeds the maximum allowed size{EOL}");
        }

        var mailboxName = UnquoteMailboxName(match.Groups["mailbox"].Value.Trim());

        var mailbox = Mind.PostOfficeDb.GetMailboxByName(session.Username, mailboxName);

        if (mailbox == null)
        {
            // TRYCREATE invites the client to CREATE the mailbox and retry the APPEND (OE does).
            return BuildResponse($"{tag} NO [TRYCREATE] Mailbox does not exist{EOL}");
        }

        session.Append = new ImapAppendState
        {
            Tag = tag,
            MailboxId = mailbox.Value.Id,
            Flags = match.Groups["flags"].Success ? match.Groups["flags"].Value.Trim() : string.Empty,
            InternalDate = ParseInternalDate(match.Groups["date"].Value) ?? DateTime.Now,
            Remaining = (int)size
        };

        // {n+} is a LITERAL+ non-synchronizing literal: the client is already sending the payload
        // and expects no continuation line.
        if (match.Groups["nonsync"].Success)
        {
            return null;
        }

        return BuildResponse($"+ Ready for literal data{EOL}");
    }

    // Complete an APPEND whose literal has fully arrived. The envelope columns are lifted from the
    // message headers, and MUST be stored as clean addr-specs - the PostOffice readers rehydrate
    // them with the throwing EmailAddress constructor, so a raw "Name <a@b>" header would break
    // every later read of the mailbox.
    private byte[] FinalizeAppend(ImapSession session)
    {
        var append = session.Append;

        session.Append = null;

        var message = append.Data.ToString();

        var from = ExtractAddressHeader(message, FromHeaderRegex());
        var to = ExtractAddressHeader(message, ToHeaderRegex());

        var subjectMatch = SubjectHeaderRegex().Match(message);
        var subject = subjectMatch.Success ? subjectMatch.Groups["value"].Value.Trim() : string.Empty;

        Mind.PostOfficeDb.AppendMessage(append.MailboxId, append.Flags, append.InternalDate, message, from, to, subject);

        var sb = new StringBuilder();

        // Appending into the currently selected mailbox must refresh the session cache and announce
        // the new EXISTS count, or the client's view (and sequence numbers) go stale.
        if (session.State == ImapState.Selected && session.SelectedMailboxId == append.MailboxId)
        {
            RefreshMessages(session);

            var status = Mind.PostOfficeDb.GetMailboxStatus(append.MailboxId);

            session.UidNext = status.UidNext;

            sb.Append($"* {status.MessageCount} EXISTS{EOL}");
        }

        sb.Append($"{append.Tag} OK APPEND completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private static string ExtractAddressHeader(string message, Regex headerRegex)
    {
        var match = headerRegex.Match(message);

        if (match.Success)
        {
            var raw = match.Groups["value"].Value.Trim();

            // "Fox <fox@hive.com>" first, then a bare addr-spec (taking the first of a comma list).
            if (EmailAddress.TryParseFromSmtp(raw, out var bracketed))
            {
                return bracketed.Full;
            }

            if (EmailAddress.TryParse(raw.Split(',')[0].Trim(), out var bare))
            {
                return bare.Full;
            }
        }

        return "unknown@" + MailDomains.Primary;
    }

    // IMAP internal date: "dd-MMM-yyyy HH:mm:ss +ZZZZ" (quoted, day possibly space-padded). Lenient:
    // an unparseable date falls back to now rather than failing the whole APPEND.
    private static DateTime? ParseInternalDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim().Trim('"').Trim();

        // .NET's zzz specifier wants a colon in the offset; IMAP sends -0800.
        if (text.Length > 5 && (text[^5] == '+' || text[^5] == '-') && char.IsAsciiDigit(text[^4]))
        {
            text = text[..^2] + ":" + text[^2..];
        }

        if (DateTimeOffset.TryParseExact(text, "d-MMM-yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var withZone))
        {
            return withZone.LocalDateTime;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var loose))
        {
            return loose;
        }

        return null;
    }

    #endregion

    #region Selected State Commands

    private byte[] HandleClose(string tag, ImapSession session)
    {
        if (session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO No mailbox selected{EOL}");
        }

        if (!session.SelectedReadOnly)
        {
            Mind.PostOfficeDb.ExpungeDeleted(session.SelectedMailboxId);
        }

        session.State = ImapState.Authenticated;
        session.SelectedMailbox = string.Empty;
        session.SelectedMailboxId = 0;
        session.Messages.Clear();

        return BuildResponse($"{tag} OK CLOSE completed{EOL}");
    }

    private byte[] HandleExpunge(string tag, ImapSession session)
    {
        if (session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO No mailbox selected{EOL}");
        }

        if (session.SelectedReadOnly)
        {
            return BuildResponse($"{tag} NO Mailbox is read-only{EOL}");
        }

        var expunged = Mind.PostOfficeDb.ExpungeDeleted(session.SelectedMailboxId);

        var sb = new StringBuilder();

        foreach (var seqNum in expunged)
        {
            sb.Append($"* {seqNum} EXPUNGE{EOL}");
        }

        session.Messages = Mind.PostOfficeDb.GetMessagesForMailbox(session.SelectedMailboxId);

        sb.Append($"{tag} OK EXPUNGE completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleSearch(string tag, string args, ImapSession session, bool useUid)
    {
        if (session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO No mailbox selected{EOL}");
        }

        RefreshMessages(session);

        var criteria = args.Trim().ToUpperInvariant();
        var results = new List<int>();

        for (var i = 0; i < session.Messages.Count; i++)
        {
            var msg = session.Messages[i];
            var seqNum = i + 1;

            if (MatchesSearchCriteria(msg, criteria))
            {
                results.Add(useUid ? msg.Uid : seqNum);
            }
        }

        var sb = new StringBuilder();

        sb.Append($"* SEARCH{(results.Count > 0 ? " " + string.Join(" ", results) : "")}{EOL}");
        sb.Append($"{tag} OK SEARCH completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleFetch(string tag, string args, ImapSession session, bool useUid)
    {
        if (session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO No mailbox selected{EOL}");
        }

        RefreshMessages(session);

        var spaceIdx = args.IndexOf(' ');

        if (spaceIdx == -1)
        {
            return BuildResponse($"{tag} BAD Missing fetch arguments{EOL}");
        }

        var sequenceSet = args[..spaceIdx];
        var fetchItems = args[(spaceIdx + 1)..].Trim();

        fetchItems = ExpandFetchMacro(fetchItems);

        var indices = ResolveSequenceSet(sequenceSet, session.Messages, useUid);

        var sb = new StringBuilder();

        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= session.Messages.Count)
            {
                continue;
            }

            var msg = session.Messages[idx];
            var seqNum = idx + 1;

            var fetchResult = BuildFetchResponse(msg, seqNum, fetchItems, session, useUid);

            sb.Append($"* {seqNum} FETCH ({fetchResult}){EOL}");
        }

        sb.Append($"{tag} OK FETCH completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleStore(string tag, string args, ImapSession session, bool useUid)
    {
        if (session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO No mailbox selected{EOL}");
        }

        if (session.SelectedReadOnly)
        {
            return BuildResponse($"{tag} NO Mailbox is read-only{EOL}");
        }

        RefreshMessages(session);

        var parts = args.Split(' ', 3);

        if (parts.Length < 3)
        {
            return BuildResponse($"{tag} BAD Missing STORE arguments{EOL}");
        }

        var sequenceSet = parts[0];
        var action = parts[1].ToUpperInvariant();
        var flagsStr = parts[2].Trim().Trim('(', ')');

        var indices = ResolveSequenceSet(sequenceSet, session.Messages, useUid);
        var silent = action.Contains(".SILENT");

        var sb = new StringBuilder();

        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= session.Messages.Count)
            {
                continue;
            }

            var msg = session.Messages[idx];
            var seqNum = idx + 1;

            if (action.StartsWith("+FLAGS"))
            {
                Mind.PostOfficeDb.AddMessageFlags(msg.Id, session.SelectedMailboxId, flagsStr);
            }
            else if (action.StartsWith("-FLAGS"))
            {
                Mind.PostOfficeDb.RemoveMessageFlags(msg.Id, session.SelectedMailboxId, flagsStr);
            }
            else if (action.StartsWith("FLAGS"))
            {
                Mind.PostOfficeDb.SetMessageFlags(msg.Id, session.SelectedMailboxId, flagsStr);
            }

            if (!silent)
            {
                var newFlags = Mind.PostOfficeDb.GetMessageFlags(msg.Id, session.SelectedMailboxId);

                sb.Append($"* {seqNum} FETCH (FLAGS ({newFlags})){EOL}");
            }
        }

        RefreshMessages(session);

        sb.Append($"{tag} OK STORE completed{EOL}");

        return BuildResponse(sb.ToString());
    }

    private byte[] HandleCopy(string tag, string args, ImapSession session, bool useUid)
    {
        if (session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO No mailbox selected{EOL}");
        }

        RefreshMessages(session);

        var spaceIdx = args.IndexOf(' ');

        if (spaceIdx == -1)
        {
            return BuildResponse($"{tag} BAD Missing COPY arguments{EOL}");
        }

        var sequenceSet = args[..spaceIdx];
        var targetName = UnquoteMailboxName(args[(spaceIdx + 1)..].Trim());

        var target = Mind.PostOfficeDb.GetMailboxByName(session.Username, targetName);

        if (target == null)
        {
            return BuildResponse($"{tag} NO [TRYCREATE] Mailbox does not exist{EOL}");
        }

        var indices = ResolveSequenceSet(sequenceSet, session.Messages, useUid);

        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= session.Messages.Count)
            {
                continue;
            }

            Mind.PostOfficeDb.CopyMessageToMailbox(session.Messages[idx].Id, target.Value.Id);
        }

        return BuildResponse($"{tag} OK COPY completed{EOL}");
    }

    #endregion

    #region UID Command

    private byte[] HandleUid(string tag, string args, ImapSession session, ListenerSocket connection, byte[] data, int read)
    {
        if (session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO No mailbox selected{EOL}");
        }

        var spaceIdx = args.IndexOf(' ');

        if (spaceIdx == -1)
        {
            return BuildResponse($"{tag} BAD Missing UID subcommand{EOL}");
        }

        var subCmd = args[..spaceIdx].ToUpperInvariant();
        var subArgs = args[(spaceIdx + 1)..];

        return subCmd switch
        {
            "FETCH" => HandleFetch(tag, subArgs, session, useUid: true),
            "STORE" => HandleStore(tag, subArgs, session, useUid: true),
            "COPY" => HandleCopy(tag, subArgs, session, useUid: true),
            "SEARCH" => HandleSearch(tag, subArgs, session, useUid: true),
            _ => BuildResponse($"{tag} BAD Unknown UID subcommand{EOL}")
        };
    }

    #endregion

    #region Helper Methods

    private static byte[] BuildResponse(string response)
    {
        return Encoding.ASCII.GetBytes(response);
    }

    private void RefreshMessages(ImapSession session)
    {
        session.Messages = Mind.PostOfficeDb.GetMessagesForMailbox(session.SelectedMailboxId);
    }

    private static (string First, string Second) ParseTwoArgs(string args)
    {
        args = args.Trim();

        if (string.IsNullOrEmpty(args))
        {
            return (string.Empty, string.Empty);
        }

        string first;
        string remainder;

        if (args.StartsWith('"'))
        {
            var endQuote = args.IndexOf('"', 1);

            if (endQuote == -1)
            {
                return (args, string.Empty);
            }

            first = args[1..endQuote];
            remainder = args[(endQuote + 1)..].TrimStart();
        }
        else
        {
            var spaceIdx = args.IndexOf(' ');

            if (spaceIdx == -1)
            {
                return (args, string.Empty);
            }

            first = args[..spaceIdx];
            remainder = args[(spaceIdx + 1)..].TrimStart();
        }

        string second;

        if (remainder.StartsWith('"'))
        {
            var endQuote = remainder.IndexOf('"', 1);

            second = endQuote == -1 ? remainder[1..] : remainder[1..endQuote];
        }
        else
        {
            second = remainder.Split(' ', 2)[0];
        }

        return (first, second);
    }

    private static string UnquoteMailboxName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (name.StartsWith('"') && name.EndsWith('"') && name.Length >= 2)
        {
            return name[1..^1];
        }

        return name;
    }

    private static string GetMailboxFlags(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "INBOX" => @"\HasNoChildren",
            "SENT" => @"\Sent \HasNoChildren",
            "DRAFTS" => @"\Drafts \HasNoChildren",
            "TRASH" => @"\Trash \HasNoChildren",
            _ => @"\HasNoChildren"
        };
    }

    private static bool MatchesMailboxPattern(string name, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
        {
            return true;
        }

        if (pattern == "%")
        {
            return !name.Contains('/');
        }

        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("%", "[^/]*") + "$";

        return Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase);
    }

    private static bool MatchesSearchCriteria(EmailMessage msg, string criteria)
    {
        if (string.IsNullOrEmpty(criteria) || criteria == "ALL")
        {
            return true;
        }

        if (criteria == "SEEN")
        {
            return msg.Flags.Contains(ImapMessageFlag.Seen);
        }

        if (criteria == "UNSEEN")
        {
            return !msg.Flags.Contains(ImapMessageFlag.Seen);
        }

        if (criteria == "DELETED")
        {
            return msg.Flags.Contains(ImapMessageFlag.Deleted);
        }

        if (criteria == "UNDELETED")
        {
            return !msg.Flags.Contains(ImapMessageFlag.Deleted);
        }

        if (criteria == "FLAGGED")
        {
            return msg.Flags.Contains(ImapMessageFlag.Flagged);
        }

        if (criteria == "UNFLAGGED")
        {
            return !msg.Flags.Contains(ImapMessageFlag.Flagged);
        }

        if (criteria == "ANSWERED")
        {
            return msg.Flags.Contains(ImapMessageFlag.Answered);
        }

        if (criteria == "UNANSWERED")
        {
            return !msg.Flags.Contains(ImapMessageFlag.Answered);
        }

        if (criteria == "DRAFT")
        {
            return msg.Flags.Contains(ImapMessageFlag.Draft);
        }

        if (criteria == "RECENT")
        {
            return msg.Flags.Contains(ImapMessageFlag.Recent);
        }

        // Multi-word criteria
        if (criteria.StartsWith("FROM "))
        {
            var searchTerm = criteria[5..].Trim().Trim('"').ToUpperInvariant();

            return msg.FromAddress.Full.ToUpperInvariant().Contains(searchTerm);
        }

        if (criteria.StartsWith("TO "))
        {
            var searchTerm = criteria[3..].Trim().Trim('"').ToUpperInvariant();

            return msg.ToAddress.Full.ToUpperInvariant().Contains(searchTerm);
        }

        if (criteria.StartsWith("SUBJECT "))
        {
            var searchTerm = criteria[8..].Trim().Trim('"').ToUpperInvariant();

            return (msg.Subject ?? "").ToUpperInvariant().Contains(searchTerm);
        }

        // Default: match all for unrecognized criteria
        return true;
    }

    private static List<int> ResolveSequenceSet(string sequenceSet, List<EmailMessage> messages, bool useUid)
    {
        var result = new List<int>();

        if (messages.Count == 0)
        {
            return result;
        }

        var parts = sequenceSet.Split(',');

        foreach (var part in parts)
        {
            if (part.Contains(':'))
            {
                var range = part.Split(':', 2);

                int startVal;
                int endVal;

                // TryParse (was int.Parse - a non-numeric range threw FormatException and 500'd the handler)
                if (range[0] == "*")
                {
                    startVal = useUid ? messages[^1].Uid : messages.Count;
                }
                else if (!int.TryParse(range[0], out startVal))
                {
                    continue;
                }

                if (range[1] == "*")
                {
                    endVal = useUid ? messages[^1].Uid : messages.Count;
                }
                else if (!int.TryParse(range[1], out endVal))
                {
                    continue;
                }

                if (startVal > endVal)
                {
                    (startVal, endVal) = (endVal, startVal);
                }

                if (useUid)
                {
                    for (var i = 0; i < messages.Count; i++)
                    {
                        if (messages[i].Uid >= startVal && messages[i].Uid <= endVal)
                        {
                            result.Add(i);
                        }
                    }
                }
                else
                {
                    // Clamp to [1, count] BEFORE looping so a huge/overflow endVal can't pin the CPU
                    var from = Math.Max(1, startVal);
                    var to = Math.Min(messages.Count, endVal);

                    for (var seq = from; seq <= to; seq++)
                    {
                        result.Add(seq - 1);
                    }
                }
            }
            else
            {
                if (part == "*")
                {
                    result.Add(messages.Count - 1);
                }
                else if (int.TryParse(part, out var num))
                {
                    if (useUid)
                    {
                        var idx = messages.FindIndex(m => m.Uid == num);

                        if (idx >= 0)
                        {
                            result.Add(idx);
                        }
                    }
                    else
                    {
                        if (num >= 1 && num <= messages.Count)
                        {
                            result.Add(num - 1);
                        }
                    }
                }
            }
        }

        return result.Distinct().OrderBy(x => x).ToList();
    }

    private static string ExpandFetchMacro(string items)
    {
        return items.Trim() switch
        {
            "ALL" => "FLAGS INTERNALDATE RFC822.SIZE ENVELOPE",
            "FAST" => "FLAGS INTERNALDATE RFC822.SIZE",
            "FULL" => "FLAGS INTERNALDATE RFC822.SIZE ENVELOPE BODY",
            _ => items
        };
    }

    private string BuildFetchResponse(EmailMessage msg, int seqNum, string fetchItems, ImapSession session, bool useUid)
    {
        var results = new List<string>();

        var items = ParseFetchItems(fetchItems);

        foreach (var item in items)
        {
            var upperItem = item.ToUpperInvariant();

            if (upperItem == "FLAGS")
            {
                results.Add($"FLAGS ({msg.Flags})");
            }
            else if (upperItem == "INTERNALDATE")
            {
                results.Add($"INTERNALDATE \"{msg.Date.ToString("dd-MMM-yyyy HH:mm:ss zz00", CultureInfo.InvariantCulture)}\"");
            }
            else if (upperItem == "RFC822.SIZE")
            {
                results.Add($"RFC822.SIZE {msg.Size}");
            }
            else if (upperItem == "UID")
            {
                results.Add($"UID {msg.Uid}");
            }
            else if (upperItem == "ENVELOPE")
            {
                results.Add($"ENVELOPE ({BuildEnvelope(msg)})");
            }
            // RFC 3501 7.4.2: every response data-item below is named after the REQUEST item, with
            // .PEEK stripped (BODY.PEEK is request-only syntax and must never appear in a response).
            // Answering BODY[] with an RFC822 item made Outlook Express render populated folders as
            // empty - it could not match the response to anything it asked for.
            else if (upperItem == "RFC822" || upperItem == "BODY[]" || upperItem.StartsWith("BODY.PEEK[]"))
            {
                var bodyData = msg.Data ?? string.Empty;

                var responseName = upperItem == "RFC822" ? "RFC822" : "BODY[]";

                results.Add($"{responseName} {{{bodyData.Length}}}{EOL}{bodyData}");

                // Mark as seen unless PEEK
                if (!upperItem.Contains("PEEK") && !session.SelectedReadOnly)
                {
                    Mind.PostOfficeDb.AddMessageFlags(msg.Id, session.SelectedMailboxId, ImapMessageFlag.Seen);
                }
            }
            else if (upperItem == "RFC822.HEADER" || upperItem == "BODY[HEADER]" || upperItem.StartsWith("BODY.PEEK[HEADER]"))
            {
                var headerData = ExtractHeaders(msg.Data ?? string.Empty);

                var responseName = upperItem == "RFC822.HEADER" ? "RFC822.HEADER" : "BODY[HEADER]";

                results.Add($"{responseName} {{{headerData.Length}}}{EOL}{headerData}");
            }
            else if (upperItem == "RFC822.TEXT" || upperItem == "BODY[TEXT]" || upperItem.StartsWith("BODY.PEEK[TEXT]"))
            {
                var bodyText = ExtractBody(msg.Data ?? string.Empty);

                var responseName = upperItem == "RFC822.TEXT" ? "RFC822.TEXT" : "BODY[TEXT]";

                results.Add($"{responseName} {{{bodyText.Length}}}{EOL}{bodyText}");

                if (!upperItem.Contains("PEEK") && !session.SelectedReadOnly)
                {
                    Mind.PostOfficeDb.AddMessageFlags(msg.Id, session.SelectedMailboxId, ImapMessageFlag.Seen);
                }
            }
            else if (upperItem.StartsWith("BODY[HEADER.FIELDS") || upperItem.StartsWith("BODY.PEEK[HEADER.FIELDS"))
            {
                var headerFieldsStart = upperItem.IndexOf('(');
                var headerFieldsEnd = upperItem.IndexOf(')');

                if (headerFieldsStart >= 0 && headerFieldsEnd > headerFieldsStart)
                {
                    var fieldNames = upperItem[(headerFieldsStart + 1)..headerFieldsEnd].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var headerData = ExtractSpecificHeaders(msg.Data ?? string.Empty, fieldNames);

                    // Strip only the .PEEK; the client's original field-list casing is echoed back so
                    // its request/response matcher gets a byte-identical item name.
                    var responseName = upperItem.StartsWith("BODY.PEEK[") ? string.Concat("BODY[", item.AsSpan("BODY.PEEK[".Length)) : item;

                    results.Add($"{responseName} {{{headerData.Length}}}{EOL}{headerData}");
                }
            }
            else if (upperItem == "BODYSTRUCTURE" || upperItem == "BODY")
            {
                results.Add($"BODYSTRUCTURE (\"TEXT\" \"PLAIN\" (\"CHARSET\" \"US-ASCII\") NIL NIL \"7BIT\" {msg.Size} {CountLines(msg.Data ?? string.Empty)})");
            }
        }

        if (useUid && !results.Any(r => r.StartsWith("UID ")))
        {
            results.Insert(0, $"UID {msg.Uid}");
        }

        return string.Join(" ", results);
    }

    private static List<string> ParseFetchItems(string items)
    {
        var result = new List<string>();
        var trimmed = items.Trim().Trim('(', ')');

        var i = 0;

        while (i < trimmed.Length)
        {
            if (trimmed[i] == ' ')
            {
                i++;
                continue;
            }

            var start = i;

            while (i < trimmed.Length && trimmed[i] != ' ')
            {
                if (trimmed[i] == '[')
                {
                    while (i < trimmed.Length && trimmed[i] != ']')
                    {
                        i++;
                    }

                    if (i < trimmed.Length)
                    {
                        i++;
                    }
                }
                else
                {
                    i++;
                }
            }

            var token = trimmed[start..i];

            if (!string.IsNullOrWhiteSpace(token))
            {
                result.Add(token);
            }
        }

        return result;
    }

    private static string BuildEnvelope(EmailMessage msg)
    {
        var date = QuoteString(msg.Date.ToString("R"));
        var subject = QuoteString(msg.Subject ?? "");
        var from = BuildAddressList(msg.FromAddress);
        var to = BuildAddressList(msg.ToAddress);

        return $"{date} {subject} {from} {from} {from} {to} NIL NIL NIL NIL";
    }

    private static string BuildAddressList(EmailAddress addr)
    {
        if (addr == null)
        {
            return "NIL";
        }

        return $"((\"{addr.User}\" NIL \"{addr.User}\" \"{addr.Domain}\"))";
    }

    private static string QuoteString(string value)
    {
        if (value == null)
        {
            return "NIL";
        }

        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    // Tolerate-read, emit-strict: legacy rows synthesized on a Linux host (pre-CRLF-fix bounces)
    // are LF-only, which the CRLF-keyed extraction below would otherwise serve as headerless mush.
    // Canonicalizing is byte-identical for well-formed CRLF messages.
    private static string NormalizeCrlf(string data)
    {
        return data.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private static string ExtractHeaders(string data)
    {
        var normalized = NormalizeCrlf(data);

        var idx = normalized.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (idx == -1)
        {
            return normalized + "\r\n";
        }

        return normalized[..(idx + 4)];
    }

    private static string ExtractBody(string data)
    {
        var normalized = NormalizeCrlf(data);

        var idx = normalized.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (idx == -1)
        {
            return string.Empty;
        }

        return normalized[(idx + 4)..];
    }

    private static string ExtractSpecificHeaders(string data, string[] fieldNames)
    {
        var headers = ExtractHeaders(data);
        var result = new StringBuilder();
        var lines = headers.Split("\r\n");

        var includeNext = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                if (includeNext)
                {
                    result.Append(line + "\r\n");
                }

                continue;
            }

            includeNext = false;

            var colonIdx = line.IndexOf(':');

            if (colonIdx > 0)
            {
                var headerName = line[..colonIdx].Trim();

                if (fieldNames.Any(f => f.Equals(headerName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Append(line + "\r\n");
                    includeNext = true;
                }
            }
        }

        result.Append("\r\n");

        return result.ToString();
    }

    private static int CountLines(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return 0;
        }

        var count = 1;

        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    #endregion
}
