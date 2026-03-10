// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Data;

namespace VintageHive.Proxy.Oscar;

public class OscarOfflineMessage
{
    public long Id { get; set; }

    public string FromScreenName { get; set; }

    public string ToScreenName { get; set; }

    public ushort Channel { get; set; }

    public byte[] MessageData { get; set; }

    public DateTime Timestamp { get; set; }

    public OscarOfflineMessage() { }

    public OscarOfflineMessage(IDataReader reader)
    {
        Id = reader.GetInt64(0);
        FromScreenName = reader.GetString(1);
        ToScreenName = reader.GetString(2);
        Channel = (ushort)reader.GetInt32(3);
        MessageData = (byte[])reader.GetValue(4);
        Timestamp = reader.GetDateTime(5);
    }
}
