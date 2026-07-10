// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Data;

namespace VintageHive.Proxy.Yahoo;

public class YahooOfflineMessage
{
    public long Id { get; set; }

    public string FromUsername { get; set; }

    public string ToUsername { get; set; }

    public string Message { get; set; }

    // Unix seconds at store time; delivered verbatim in YMSG field 15 so the client can date the message.
    public long Timestamp { get; set; }

    public YahooOfflineMessage() { }

    public YahooOfflineMessage(IDataReader reader)
    {
        Id = reader.GetInt64(0);
        FromUsername = reader.GetString(1);
        ToUsername = reader.GetString(2);
        Message = reader.GetString(3);
        Timestamp = reader.GetInt64(4);
    }
}
