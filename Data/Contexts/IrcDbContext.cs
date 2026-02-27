// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Data.Sqlite;

namespace VintageHive.Data.Contexts;

public class IrcDbContext : DbContextBase
{
    private const string TABLE_CHANNELS = "channels";
    private const string TABLE_CHANNEL_BANS = "channel_bans";

    public IrcDbContext() : base()
    {
        CreateTable(TABLE_CHANNELS, "name TEXT UNIQUE COLLATE NOCASE, topic TEXT, topicsetby TEXT, topicsetat TEXT, modes TEXT, key TEXT, userlimit INTEGER");
        CreateTable(TABLE_CHANNEL_BANS, "channel TEXT COLLATE NOCASE, mask TEXT, setby TEXT, setat TEXT");
    }

    public void SaveChannel(string name, string topic, string topicSetBy, DateTime topicSetAt, string modes, string key, int userLimit)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"INSERT OR REPLACE INTO {TABLE_CHANNELS} (name, topic, topicsetby, topicsetat, modes, key, userlimit) VALUES(@name, @topic, @topicsetby, @topicsetat, @modes, @key, @userlimit)";

            command.Parameters.Add(new SqliteParameter("@name", name));
            command.Parameters.Add(new SqliteParameter("@topic", topic));
            command.Parameters.Add(new SqliteParameter("@topicsetby", topicSetBy));
            command.Parameters.Add(new SqliteParameter("@topicsetat", topicSetAt.ToString("o")));
            command.Parameters.Add(new SqliteParameter("@modes", modes));
            command.Parameters.Add(new SqliteParameter("@key", key));
            command.Parameters.Add(new SqliteParameter("@userlimit", userLimit));

            command.ExecuteNonQuery();
        });
    }

    public void DeleteChannel(string name)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"DELETE FROM {TABLE_CHANNELS} WHERE name = @name";

            command.Parameters.Add(new SqliteParameter("@name", name));

            command.ExecuteNonQuery();

            using var banCommand = context.CreateCommand();

            banCommand.CommandText = $"DELETE FROM {TABLE_CHANNEL_BANS} WHERE channel = @channel";

            banCommand.Parameters.Add(new SqliteParameter("@channel", name));

            banCommand.ExecuteNonQuery();
        });
    }

    public List<IrcChannelRecord> GetAllChannels()
    {
        return WithContext(context =>
        {
            var channels = new List<IrcChannelRecord>();

            using var command = context.CreateCommand();

            command.CommandText = $"SELECT name, topic, topicsetby, topicsetat, modes, key, userlimit FROM {TABLE_CHANNELS}";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                channels.Add(new IrcChannelRecord
                {
                    Name = reader.GetString(0),
                    Topic = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    TopicSetBy = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    TopicSetAt = reader.IsDBNull(3) ? DateTime.MinValue : DateTime.TryParse(reader.GetString(3), out var dt) ? dt : DateTime.MinValue,
                    Modes = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Key = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    UserLimit = reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
                });
            }

            return channels;
        });
    }

    public void AddBan(string channel, string mask, string setBy)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"INSERT INTO {TABLE_CHANNEL_BANS} (channel, mask, setby, setat) VALUES(@channel, @mask, @setby, @setat)";

            command.Parameters.Add(new SqliteParameter("@channel", channel));
            command.Parameters.Add(new SqliteParameter("@mask", mask));
            command.Parameters.Add(new SqliteParameter("@setby", setBy));
            command.Parameters.Add(new SqliteParameter("@setat", DateTime.UtcNow.ToString("o")));

            command.ExecuteNonQuery();
        });
    }

    public void RemoveBan(string channel, string mask)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"DELETE FROM {TABLE_CHANNEL_BANS} WHERE channel = @channel AND mask = @mask";

            command.Parameters.Add(new SqliteParameter("@channel", channel));
            command.Parameters.Add(new SqliteParameter("@mask", mask));

            command.ExecuteNonQuery();
        });
    }

    public List<IrcBanRecord> GetBans(string channel)
    {
        return WithContext(context =>
        {
            var bans = new List<IrcBanRecord>();

            using var command = context.CreateCommand();

            command.CommandText = $"SELECT channel, mask, setby, setat FROM {TABLE_CHANNEL_BANS} WHERE channel = @channel";

            command.Parameters.Add(new SqliteParameter("@channel", channel));

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                bans.Add(new IrcBanRecord
                {
                    Channel = reader.GetString(0),
                    Mask = reader.GetString(1),
                    SetBy = reader.GetString(2),
                    SetAt = DateTime.TryParse(reader.GetString(3), out var dt) ? dt : DateTime.MinValue
                });
            }

            return bans;
        });
    }
}

public class IrcChannelRecord
{
    public string Name { get; set; }
    public string Topic { get; set; }
    public string TopicSetBy { get; set; }
    public DateTime TopicSetAt { get; set; }
    public string Modes { get; set; }
    public string Key { get; set; }
    public int UserLimit { get; set; }
}

public class IrcBanRecord
{
    public string Channel { get; set; }
    public string Mask { get; set; }
    public string SetBy { get; set; }
    public DateTime SetAt { get; set; }
}
