// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace VintageHive.Data.Contexts;

public class PostOfficeDbContext : DbContextBase
{
    private const string TABLE_EMAILS  = "emails";

    public PostOfficeDbContext() : base()
    {
        CreateTable(TABLE_EMAILS, "id INTEGER PRIMARY KEY AUTOINCREMENT, delivery INTEGER, fromAddress TEXT, toAddress TEXT, subject TEXT, date DATETIME, size INTEGER, data TEXT");
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
