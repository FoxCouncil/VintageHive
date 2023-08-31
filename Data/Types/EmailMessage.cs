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
}

