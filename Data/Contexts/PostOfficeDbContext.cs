// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace VintageHive.Data.Contexts;

public class PostOfficeDbContext : DbContextBase
{
    private const string TABLE_EMAILS  = "emails";
    private const string TABLE_MAILBOXES = "mailboxes";
    private const string TABLE_MESSAGE_MAILBOX = "message_mailbox";

    private static readonly string[] DefaultMailboxNames = ["INBOX", "Sent", "Drafts", "Trash"];

    public PostOfficeDbContext() : base()
    {
        CreateTable(TABLE_EMAILS, "id INTEGER PRIMARY KEY AUTOINCREMENT, delivery INTEGER, fromAddress TEXT, toAddress TEXT, subject TEXT, date DATETIME, size INTEGER, data TEXT");
        CreateTable(TABLE_MAILBOXES, "id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT COLLATE NOCASE, name TEXT, subscribed INTEGER DEFAULT 1, uidvalidity INTEGER, UNIQUE(username, name)");
        CreateTable(TABLE_MESSAGE_MAILBOX, "id INTEGER PRIMARY KEY AUTOINCREMENT, email_id INTEGER, mailbox_id INTEGER, uid INTEGER, flags TEXT DEFAULT '', FOREIGN KEY(email_id) REFERENCES emails(id), FOREIGN KEY(mailbox_id) REFERENCES mailboxes(id)");
    }

    public void ProcessAndInsertEmail(EmailAddress from, HashSet<EmailAddress> toAddresses, string data)
    {
        var subject = Regex.Match(data, @"Subject: (.*?)\r\n").Groups[1].Value;
        var rawDate = Regex.Match(data, @"Date: (.*?)\r\n").Groups[1].Value;

        var date = DateTime.Parse(rawDate);

        WithContext(context =>
        {
            foreach (var to in toAddresses)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = $"INSERT INTO {TABLE_EMAILS} (delivery, fromAddress, toAddress, subject, date, size, data) VALUES(@delivery, @from, @to, @subject, @date, @size, @data)";

                insertCommand.Parameters.Add(new SqliteParameter("@delivery", value: 0));
                insertCommand.Parameters.Add(new SqliteParameter("@from", from.Full));
                insertCommand.Parameters.Add(new SqliteParameter("@to", to.Full));
                insertCommand.Parameters.Add(new SqliteParameter("@subject", subject));
                insertCommand.Parameters.Add(new SqliteParameter("@date", date));
                insertCommand.Parameters.Add(new SqliteParameter("@size", value: data.Length));
                insertCommand.Parameters.Add(new SqliteParameter("@data", data));

                insertCommand.ExecuteNonQuery();
            }
        });
    }
    public List<EmailMessage> GetDeliveredEmailsForUser(string toAddressStartsWith)
    {
        var deliveredEmails = new List<EmailMessage>();

        WithContext(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT * FROM {TABLE_EMAILS} WHERE delivery = 1 AND toAddress LIKE @toAddressPattern";

            selectCommand.Parameters.Add(new SqliteParameter("@toAddressPattern", toAddressStartsWith + "@%"));

            using var reader = selectCommand.ExecuteReader();

            while (reader.Read())
            {
                var emailMessage = new EmailMessage
                {
                    Id = reader.GetInt32(0),
                    Delivery = reader.GetInt32(1),
                    FromAddress = new EmailAddress(reader.GetString(2)),
                    ToAddress = new EmailAddress(reader.GetString(3)),
                    Subject = reader.GetString(4),
                    Date = reader.GetDateTime(5),
                    Size = reader.GetInt32(6),
                    Data = reader.GetString(7)
                };

                deliveredEmails.Add(emailMessage);
            }
        });

        return deliveredEmails;
    }

    public List<EmailMessage> GetUndeliveredEmails()
    {
        var undeliveredEmails = new List<EmailMessage>();

        WithContext(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT * FROM {TABLE_EMAILS} WHERE delivery = 0";

            using var reader = selectCommand.ExecuteReader();

            while (reader.Read())
            {
                var emailMessage = new EmailMessage
                {
                    Id = reader.GetInt32(0),
                    Delivery = reader.GetInt32(1),
                    FromAddress = new EmailAddress(reader.GetString(2)),
                    ToAddress = new EmailAddress(reader.GetString(3)),
                    Subject = reader.GetString(4),
                    Date = reader.GetDateTime(5),
                    Size = reader.GetInt32(6),
                    Data = reader.GetString(7)
                };

                undeliveredEmails.Add(emailMessage);
            }
        });

        return undeliveredEmails;
    }

    public void MarkEmailAsDelivered(int id)
    {
        WithContext(context =>
        {
            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = $"UPDATE {TABLE_EMAILS} SET delivery = 1 WHERE id = @id";

            updateCommand.Parameters.Add(new SqliteParameter("@id", id));

            updateCommand.ExecuteNonQuery();
        });
    }

    public void DeleteEmailById(int id)
    {
        WithContext(context =>
        {
            using var deleteCommand = context.CreateCommand();

            deleteCommand.CommandText = $"DELETE FROM {TABLE_EMAILS} WHERE id = @id";

            deleteCommand.Parameters.Add(new SqliteParameter("@id", id));

            deleteCommand.ExecuteNonQuery();
        });
    }

    #region Mailbox Methods

    public void CreateDefaultMailboxes(string username)
    {
        foreach (var name in DefaultMailboxNames)
        {
            CreateMailbox(username, name);
        }
    }

    public int CreateMailbox(string username, string name)
    {
        return WithContext<int>(context =>
        {
            using var cmd = context.CreateCommand();

            cmd.CommandText = $"INSERT OR IGNORE INTO {TABLE_MAILBOXES} (username, name, subscribed, uidvalidity) VALUES(@username, @name, 1, @uidvalidity)";

            cmd.Parameters.Add(new SqliteParameter("@username", username));
            cmd.Parameters.Add(new SqliteParameter("@name", name));
            cmd.Parameters.Add(new SqliteParameter("@uidvalidity", (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0x7FFFFFFF)));

            cmd.ExecuteNonQuery();

            using var idCmd = context.CreateCommand();

            idCmd.CommandText = $"SELECT id FROM {TABLE_MAILBOXES} WHERE username = @username AND name = @name";

            idCmd.Parameters.Add(new SqliteParameter("@username", username));
            idCmd.Parameters.Add(new SqliteParameter("@name", name));

            using var reader = idCmd.ExecuteReader();

            return reader.Read() ? reader.GetInt32(0) : -1;
        });
    }

    public bool DeleteMailbox(string username, string name)
    {
        if (name.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var getIdCmd = context.CreateCommand();

            getIdCmd.CommandText = $"SELECT id FROM {TABLE_MAILBOXES} WHERE username = @username AND name = @name";

            getIdCmd.Parameters.Add(new SqliteParameter("@username", username));
            getIdCmd.Parameters.Add(new SqliteParameter("@name", name));

            using var reader = getIdCmd.ExecuteReader();

            if (!reader.Read())
            {
                return false;
            }

            var mailboxId = reader.GetInt32(0);

            reader.Close();

            using var delMsgsCmd = context.CreateCommand();

            delMsgsCmd.CommandText = $"DELETE FROM {TABLE_MESSAGE_MAILBOX} WHERE mailbox_id = @mailboxId";

            delMsgsCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            delMsgsCmd.ExecuteNonQuery();

            using var delCmd = context.CreateCommand();

            delCmd.CommandText = $"DELETE FROM {TABLE_MAILBOXES} WHERE id = @id";

            delCmd.Parameters.Add(new SqliteParameter("@id", mailboxId));

            delCmd.ExecuteNonQuery();

            transaction.Commit();

            return true;
        });
    }

    public bool RenameMailbox(string username, string oldName, string newName)
    {
        if (oldName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return WithContext(context =>
        {
            using var cmd = context.CreateCommand();

            cmd.CommandText = $"UPDATE {TABLE_MAILBOXES} SET name = @newName WHERE username = @username AND name = @oldName";

            cmd.Parameters.Add(new SqliteParameter("@username", username));
            cmd.Parameters.Add(new SqliteParameter("@oldName", oldName));
            cmd.Parameters.Add(new SqliteParameter("@newName", newName));

            return cmd.ExecuteNonQuery() > 0;
        });
    }

    public List<(int Id, string Name, bool Subscribed, int UidValidity)> GetMailboxesForUser(string username)
    {
        return WithContext(context =>
        {
            var list = new List<(int, string, bool, int)>();

            using var cmd = context.CreateCommand();

            cmd.CommandText = $"SELECT id, name, subscribed, uidvalidity FROM {TABLE_MAILBOXES} WHERE username = @username ORDER BY name";

            cmd.Parameters.Add(new SqliteParameter("@username", username));

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2) != 0, reader.GetInt32(3)));
            }

            return list;
        });
    }

    public List<(int Id, string Name, bool Subscribed, int UidValidity)> GetSubscribedMailboxes(string username)
    {
        return WithContext(context =>
        {
            var list = new List<(int, string, bool, int)>();

            using var cmd = context.CreateCommand();

            cmd.CommandText = $"SELECT id, name, subscribed, uidvalidity FROM {TABLE_MAILBOXES} WHERE username = @username AND subscribed = 1 ORDER BY name";

            cmd.Parameters.Add(new SqliteParameter("@username", username));

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add((reader.GetInt32(0), reader.GetString(1), true, reader.GetInt32(3)));
            }

            return list;
        });
    }

    public bool SubscribeMailbox(string username, string name)
    {
        return WithContext(context =>
        {
            using var cmd = context.CreateCommand();

            cmd.CommandText = $"UPDATE {TABLE_MAILBOXES} SET subscribed = 1 WHERE username = @username AND name = @name";

            cmd.Parameters.Add(new SqliteParameter("@username", username));
            cmd.Parameters.Add(new SqliteParameter("@name", name));

            return cmd.ExecuteNonQuery() > 0;
        });
    }

    public bool UnsubscribeMailbox(string username, string name)
    {
        return WithContext(context =>
        {
            using var cmd = context.CreateCommand();

            cmd.CommandText = $"UPDATE {TABLE_MAILBOXES} SET subscribed = 0 WHERE username = @username AND name = @name";

            cmd.Parameters.Add(new SqliteParameter("@username", username));
            cmd.Parameters.Add(new SqliteParameter("@name", name));

            return cmd.ExecuteNonQuery() > 0;
        });
    }

    public (int Id, string Name, bool Subscribed, int UidValidity)? GetMailboxByName(string username, string name)
    {
        return WithContext(context =>
        {
            using var cmd = context.CreateCommand();

            cmd.CommandText = $"SELECT id, name, subscribed, uidvalidity FROM {TABLE_MAILBOXES} WHERE username = @username AND name = @name";

            cmd.Parameters.Add(new SqliteParameter("@username", username));
            cmd.Parameters.Add(new SqliteParameter("@name", name));

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
            {
                return ((int, string, bool, int)?)null;
            }

            return (reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2) != 0, reader.GetInt32(3));
        });
    }

    public (int MessageCount, int Recent, int Unseen, int UidNext, int UidValidity) GetMailboxStatus(int mailboxId)
    {
        return WithContext(context =>
        {
            using var countCmd = context.CreateCommand();

            countCmd.CommandText = $"SELECT COUNT(*) FROM {TABLE_MESSAGE_MAILBOX} WHERE mailbox_id = @mailboxId";

            countCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            var messageCount = Convert.ToInt32(countCmd.ExecuteScalar());

            using var recentCmd = context.CreateCommand();

            recentCmd.CommandText = $"SELECT COUNT(*) FROM {TABLE_MESSAGE_MAILBOX} WHERE mailbox_id = @mailboxId AND flags LIKE '%\\Recent%'";

            recentCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            var recent = Convert.ToInt32(recentCmd.ExecuteScalar());

            using var unseenCmd = context.CreateCommand();

            unseenCmd.CommandText = $"SELECT COUNT(*) FROM {TABLE_MESSAGE_MAILBOX} WHERE mailbox_id = @mailboxId AND flags NOT LIKE '%\\Seen%'";

            unseenCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            var unseen = Convert.ToInt32(unseenCmd.ExecuteScalar());

            using var uidNextCmd = context.CreateCommand();

            uidNextCmd.CommandText = $"SELECT COALESCE(MAX(uid), 0) + 1 FROM {TABLE_MESSAGE_MAILBOX} WHERE mailbox_id = @mailboxId";

            uidNextCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            var uidNext = Convert.ToInt32(uidNextCmd.ExecuteScalar());

            using var uidValidityCmd = context.CreateCommand();

            uidValidityCmd.CommandText = $"SELECT uidvalidity FROM {TABLE_MAILBOXES} WHERE id = @mailboxId";

            uidValidityCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            var uidValidity = Convert.ToInt32(uidValidityCmd.ExecuteScalar());

            return (messageCount, recent, unseen, uidNext, uidValidity);
        });
    }

    public List<EmailMessage> GetMessagesForMailbox(int mailboxId)
    {
        return WithContext(context =>
        {
            var list = new List<EmailMessage>();

            using var cmd = context.CreateCommand();

            cmd.CommandText = $"SELECT e.id, e.delivery, e.fromAddress, e.toAddress, e.subject, e.date, e.size, e.data, mm.uid, mm.flags, mm.mailbox_id FROM {TABLE_EMAILS} e INNER JOIN {TABLE_MESSAGE_MAILBOX} mm ON e.id = mm.email_id WHERE mm.mailbox_id = @mailboxId ORDER BY mm.uid ASC";

            cmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new EmailMessage
                {
                    Id = reader.GetInt32(0),
                    Delivery = reader.GetInt32(1),
                    FromAddress = new EmailAddress(reader.GetString(2)),
                    ToAddress = new EmailAddress(reader.GetString(3)),
                    Subject = reader.GetString(4),
                    Date = reader.GetDateTime(5),
                    Size = reader.GetInt32(6),
                    Data = reader.GetString(7),
                    Uid = reader.GetInt32(8),
                    Flags = reader.GetString(9),
                    MailboxId = reader.GetInt32(10)
                });
            }

            return list;
        });
    }

    public void SetMessageFlags(int emailId, int mailboxId, string flags)
    {
        WithContext(context =>
        {
            using var cmd = context.CreateCommand();

            cmd.CommandText = $"UPDATE {TABLE_MESSAGE_MAILBOX} SET flags = @flags WHERE email_id = @emailId AND mailbox_id = @mailboxId";

            cmd.Parameters.Add(new SqliteParameter("@flags", flags));
            cmd.Parameters.Add(new SqliteParameter("@emailId", emailId));
            cmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            cmd.ExecuteNonQuery();
        });
    }

    public string GetMessageFlags(int emailId, int mailboxId)
    {
        return WithContext<string>(context =>
        {
            using var cmd = context.CreateCommand();

            cmd.CommandText = $"SELECT flags FROM {TABLE_MESSAGE_MAILBOX} WHERE email_id = @emailId AND mailbox_id = @mailboxId";

            cmd.Parameters.Add(new SqliteParameter("@emailId", emailId));
            cmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            using var reader = cmd.ExecuteReader();

            return reader.Read() ? reader.GetString(0) : string.Empty;
        });
    }

    public void AddMessageFlags(int emailId, int mailboxId, string flagsToAdd)
    {
        var currentFlags = GetMessageFlags(emailId, mailboxId);
        var flagSet = new HashSet<string>(currentFlags.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        foreach (var flag in flagsToAdd.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            flagSet.Add(flag);
        }

        SetMessageFlags(emailId, mailboxId, string.Join(" ", flagSet));
    }

    public void RemoveMessageFlags(int emailId, int mailboxId, string flagsToRemove)
    {
        var currentFlags = GetMessageFlags(emailId, mailboxId);
        var flagSet = new HashSet<string>(currentFlags.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        foreach (var flag in flagsToRemove.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            flagSet.Remove(flag);
        }

        SetMessageFlags(emailId, mailboxId, string.Join(" ", flagSet));
    }

    public bool CopyMessageToMailbox(int emailId, int targetMailboxId)
    {
        return WithContext(context =>
        {
            var uid = GetNextUid(targetMailboxId);

            using var cmd = context.CreateCommand();

            cmd.CommandText = $"INSERT INTO {TABLE_MESSAGE_MAILBOX} (email_id, mailbox_id, uid, flags) VALUES(@emailId, @mailboxId, @uid, '')";

            cmd.Parameters.Add(new SqliteParameter("@emailId", emailId));
            cmd.Parameters.Add(new SqliteParameter("@mailboxId", targetMailboxId));
            cmd.Parameters.Add(new SqliteParameter("@uid", uid));

            return cmd.ExecuteNonQuery() > 0;
        });
    }

    public List<int> ExpungeDeleted(int mailboxId)
    {
        return WithContext(context =>
        {
            var expungedSequences = new List<int>();

            using var selectCmd = context.CreateCommand();

            selectCmd.CommandText = $"SELECT mm.id, mm.email_id FROM {TABLE_MESSAGE_MAILBOX} mm WHERE mm.mailbox_id = @mailboxId ORDER BY mm.uid ASC";

            selectCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            using var reader = selectCmd.ExecuteReader();

            var sequenceNum = 1;
            var toDelete = new List<int>();

            while (reader.Read())
            {
                var id = reader.GetInt32(0);

                var flags = string.Empty;

                using var flagCmd = context.CreateCommand();

                flagCmd.CommandText = $"SELECT flags FROM {TABLE_MESSAGE_MAILBOX} WHERE id = @id";

                flagCmd.Parameters.Add(new SqliteParameter("@id", id));

                using var flagReader = flagCmd.ExecuteReader();

                if (flagReader.Read())
                {
                    flags = flagReader.GetString(0);
                }

                if (flags.Contains("\\Deleted"))
                {
                    expungedSequences.Add(sequenceNum);
                    toDelete.Add(id);
                }

                sequenceNum++;
            }

            reader.Close();

            foreach (var id in toDelete)
            {
                using var delCmd = context.CreateCommand();

                delCmd.CommandText = $"DELETE FROM {TABLE_MESSAGE_MAILBOX} WHERE id = @id";

                delCmd.Parameters.Add(new SqliteParameter("@id", id));

                delCmd.ExecuteNonQuery();
            }

            return expungedSequences;
        });
    }

    public int AppendMessage(int mailboxId, string flags, DateTime date, string data, string fromAddress, string toAddress, string subject)
    {
        return WithContext<int>(context =>
        {
            using var transaction = context.BeginTransaction();

            using var insertEmailCmd = context.CreateCommand();

            insertEmailCmd.CommandText = $"INSERT INTO {TABLE_EMAILS} (delivery, fromAddress, toAddress, subject, date, size, data) VALUES(1, @from, @to, @subject, @date, @size, @data)";

            insertEmailCmd.Parameters.Add(new SqliteParameter("@from", fromAddress));
            insertEmailCmd.Parameters.Add(new SqliteParameter("@to", toAddress));
            insertEmailCmd.Parameters.Add(new SqliteParameter("@subject", subject));
            insertEmailCmd.Parameters.Add(new SqliteParameter("@date", date));
            insertEmailCmd.Parameters.Add(new SqliteParameter("@size", data.Length));
            insertEmailCmd.Parameters.Add(new SqliteParameter("@data", data));

            insertEmailCmd.ExecuteNonQuery();

            using var idCmd = context.CreateCommand();

            idCmd.CommandText = "SELECT last_insert_rowid()";

            var emailId = Convert.ToInt32(idCmd.ExecuteScalar());

            var uid = GetNextUid(mailboxId);

            using var insertMmCmd = context.CreateCommand();

            insertMmCmd.CommandText = $"INSERT INTO {TABLE_MESSAGE_MAILBOX} (email_id, mailbox_id, uid, flags) VALUES(@emailId, @mailboxId, @uid, @flags)";

            insertMmCmd.Parameters.Add(new SqliteParameter("@emailId", emailId));
            insertMmCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));
            insertMmCmd.Parameters.Add(new SqliteParameter("@uid", uid));
            insertMmCmd.Parameters.Add(new SqliteParameter("@flags", flags));

            insertMmCmd.ExecuteNonQuery();

            transaction.Commit();

            return emailId;
        });
    }

    public int GetNextUid(int mailboxId)
    {
        return WithContext<int>(context =>
        {
            using var cmd = context.CreateCommand();

            cmd.CommandText = $"SELECT COALESCE(MAX(uid), 0) + 1 FROM {TABLE_MESSAGE_MAILBOX} WHERE mailbox_id = @mailboxId";

            cmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            return Convert.ToInt32(cmd.ExecuteScalar());
        });
    }

    public void AssignMessageToMailbox(int emailId, int mailboxId)
    {
        WithContext(context =>
        {
            using var checkCmd = context.CreateCommand();

            checkCmd.CommandText = $"SELECT COUNT(*) FROM {TABLE_MESSAGE_MAILBOX} WHERE email_id = @emailId AND mailbox_id = @mailboxId";

            checkCmd.Parameters.Add(new SqliteParameter("@emailId", emailId));
            checkCmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));

            if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
            {
                return;
            }

            var uid = GetNextUid(mailboxId);

            using var cmd = context.CreateCommand();

            cmd.CommandText = $"INSERT INTO {TABLE_MESSAGE_MAILBOX} (email_id, mailbox_id, uid, flags) VALUES(@emailId, @mailboxId, @uid, '\\Recent')";

            cmd.Parameters.Add(new SqliteParameter("@emailId", emailId));
            cmd.Parameters.Add(new SqliteParameter("@mailboxId", mailboxId));
            cmd.Parameters.Add(new SqliteParameter("@uid", uid));

            cmd.ExecuteNonQuery();
        });
    }

    #endregion

    // Messages
    public void InsertUndeliverableEmail(string toAddress, string failedAddress)
    {
        var emailDataBuilder = new StringBuilder();
        var subject = $"UNDELIVERABLE: {failedAddress} is not a valid user! Rejected!";
        var date = DateTimeOffset.Now;
        var from = "postmaster@vintagehive";

        // Crafting the retro computer email humor message with appropriate headers
        emailDataBuilder.AppendLine("Date: " + date.ToRFC822String());
        emailDataBuilder.AppendLine("From: " + from);
        emailDataBuilder.AppendLine("To: " + toAddress);
        emailDataBuilder.AppendLine("Subject: " + subject);
        emailDataBuilder.AppendLine();
        emailDataBuilder.AppendLine("Your message reached the VintageHive Postmaster but...");
        emailDataBuilder.AppendLine($"ERROR: 404 - Recipient {failedAddress} not found in our Hive!");
        emailDataBuilder.AppendLine("Your bytes are floating aimlessly in the void.");
        emailDataBuilder.AppendLine();
        emailDataBuilder.AppendLine();
        emailDataBuilder.AppendLine("--");
        emailDataBuilder.AppendLine("VintageHive Postmaster");
        emailDataBuilder.AppendLine("Helping your data find its home since 1982.");

        var emailData = emailDataBuilder.ToString();

        WithContext(context =>
        {
            using var insertCommand = context.CreateCommand();

            insertCommand.CommandText = $"INSERT INTO {TABLE_EMAILS} (delivery, fromAddress, toAddress, subject, date, size, data) VALUES(@delivery, @from, @to, @subject, @date, @size, @data)";

            insertCommand.Parameters.Add(new SqliteParameter("@delivery", value: 1));
            insertCommand.Parameters.Add(new SqliteParameter("@from", from));
            insertCommand.Parameters.Add(new SqliteParameter("@to", toAddress));
            insertCommand.Parameters.Add(new SqliteParameter("@subject", subject));
            insertCommand.Parameters.Add(new SqliteParameter("@date", date));
            insertCommand.Parameters.Add(new SqliteParameter("@size", value: emailData.Length));
            insertCommand.Parameters.Add(new SqliteParameter("@data", emailData));

            insertCommand.ExecuteNonQuery();
        });
    }
}
