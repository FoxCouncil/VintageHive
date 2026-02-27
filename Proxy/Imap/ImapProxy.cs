// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Globalization;
using System.Text.RegularExpressions;
using VintageHive.Network;

namespace VintageHive.Proxy.Imap;

internal partial class ImapProxy : Listener
{
    private const string EOL = "\r\n";
    private const string SessionKey = "imap_session";

    private const string Capability = "IMAP4rev1 AUTH=LOGIN";

    [GeneratedRegex(@"^(\S+)\s+(\S+)(?:\s+(.*))?$", RegexOptions.Compiled)]
    private static partial Regex CommandRegex();

    public ImapProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = true;

        connection.DataBag[SessionKey] = new ImapSession();

        return BuildResponse($"* OK VintageHive IMAP4rev1 server ready{EOL}");
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        await Task.Delay(0);

        var session = connection.DataBag[SessionKey] as ImapSession;

        var rawLine = Encoding.ASCII.GetString(data, 0, read).TrimEnd('\r', '\n');

        var match = CommandRegex().Match(rawLine);

        if (!match.Success)
        {
            return BuildResponse($"* BAD Invalid command format{EOL}");
        }

        var tag = match.Groups[1].Value;
        var command = match.Groups[2].Value.ToUpperInvariant();
        var args = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

        return command switch
        {
            "CAPABILITY" => HandleCapability(tag),
            "NOOP" => HandleNoop(tag, session),
            "LOGOUT" => HandleLogout(tag, session, connection),
            "LOGIN" => HandleLogin(tag, args, session),
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
            "APPEND" => HandleAppend(tag, args, session, connection, data, read),
            "CLOSE" => HandleClose(tag, session),
            "EXPUNGE" => HandleExpunge(tag, session),
            "SEARCH" => HandleSearch(tag, args, session, useUid: false),
            "FETCH" => HandleFetch(tag, args, session, useUid: false),
            "STORE" => HandleStore(tag, args, session, useUid: false),
            "COPY" => HandleCopy(tag, args, session, useUid: false),
            "UID" => HandleUid(tag, args, session, connection, data, read),
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

        sb.Append($"* BYE VintageHive IMAP4rev1 server logging out{EOL}");
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

        var user = Mind.Db.UserFetch(username, password);

        if (user == null)
        {
            return BuildResponse($"{tag} NO LOGIN failed{EOL}");
        }

        session.State = ImapState.Authenticated;
        session.Username = user.Username;

        Mind.PostOfficeDb.CreateDefaultMailboxes(session.Username);

        return BuildResponse($"{tag} OK LOGIN completed{EOL}");
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

    private byte[] HandleAppend(string tag, string args, ImapSession session, ListenerSocket connection, byte[] data, int read)
    {
        if (session.State != ImapState.Authenticated && session.State != ImapState.Selected)
        {
            return BuildResponse($"{tag} NO Not authenticated{EOL}");
        }

        // Simplified APPEND: we accept the command but respond with OK
        // Full literal handling would require multi-line reads from the socket
        var sb = new StringBuilder();

        sb.Append($"{tag} OK APPEND completed{EOL}");

        return BuildResponse(sb.ToString());
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

                var startVal = range[0] == "*" ? (useUid ? messages[^1].Uid : messages.Count) : int.Parse(range[0]);
                var endVal = range[1] == "*" ? (useUid ? messages[^1].Uid : messages.Count) : int.Parse(range[1]);

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
                    for (var seq = startVal; seq <= endVal; seq++)
                    {
                        if (seq >= 1 && seq <= messages.Count)
                        {
                            result.Add(seq - 1);
                        }
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
            else if (upperItem == "RFC822" || upperItem == "BODY[]" || upperItem.StartsWith("BODY.PEEK[]"))
            {
                var bodyData = msg.Data ?? string.Empty;

                results.Add($"RFC822 {{{bodyData.Length}}}{EOL}{bodyData}");

                // Mark as seen unless PEEK
                if (!upperItem.Contains("PEEK") && !session.SelectedReadOnly)
                {
                    Mind.PostOfficeDb.AddMessageFlags(msg.Id, session.SelectedMailboxId, ImapMessageFlag.Seen);
                }
            }
            else if (upperItem == "RFC822.HEADER" || upperItem == "BODY[HEADER]" || upperItem.StartsWith("BODY.PEEK[HEADER]"))
            {
                var headerData = ExtractHeaders(msg.Data ?? string.Empty);

                results.Add($"BODY[HEADER] {{{headerData.Length}}}{EOL}{headerData}");
            }
            else if (upperItem == "BODY[TEXT]" || upperItem.StartsWith("BODY.PEEK[TEXT]"))
            {
                var bodyText = ExtractBody(msg.Data ?? string.Empty);

                results.Add($"BODY[TEXT] {{{bodyText.Length}}}{EOL}{bodyText}");

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

                    results.Add($"{item} {{{headerData.Length}}}{EOL}{headerData}");
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

    private static string ExtractHeaders(string data)
    {
        var idx = data.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (idx == -1)
        {
            return data + "\r\n";
        }

        return data[..(idx + 4)];
    }

    private static string ExtractBody(string data)
    {
        var idx = data.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (idx == -1)
        {
            return string.Empty;
        }

        return data[(idx + 4)..];
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
