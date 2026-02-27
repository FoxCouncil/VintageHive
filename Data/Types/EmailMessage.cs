// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

public class EmailMessage
{
    public int Id { get; set; }

    public int Delivery { get; set; }

    public EmailAddress FromAddress { get; set; }

    public EmailAddress ToAddress { get; set; }

    public string Subject { get; set; }

    public DateTime Date { get; set; }

    public int Size { get; set; }

    public string Data { get; set; }

    public int Uid { get; set; }

    public string Flags { get; set; } = string.Empty;

    public int MailboxId { get; set; }
}

