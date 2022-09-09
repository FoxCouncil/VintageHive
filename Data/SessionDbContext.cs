using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Dynamic;

namespace VintageHive.Data;

internal class SessionDbContext : DbContextBase
{
    public SessionDbContext() : base()
    {
        WithContext(context =>
        {
            var walCommand = context.CreateCommand();

            walCommand.CommandText = @"PRAGMA journal_mode = 'wal'";

            walCommand.ExecuteNonQuery();

            // Create Tables
            var command = context.CreateCommand();

            command.CommandText = "CREATE TABLE IF NOT EXISTS session (key TEXT UNIQUE, ttl TEXT, ip TEXT, ua TEXT, value BLOB)";

            command.ExecuteNonQuery();
        });
    }

    internal dynamic Get(Guid sessionId)
    {
        return WithContext<dynamic>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT value FROM session WHERE key = @key AND ip = @ip AND ua = @ua";

            command.Parameters.Add(new SqliteParameter("@key", sessionId.ToString()));
            command.Parameters.Add(new SqliteParameter("@ip", sessionId.ToString()));
            command.Parameters.Add(new SqliteParameter("@ua", sessionId.ToString()));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return new ExpandoObject();
            }

            var jsonString = reader.GetString(0);

            return JsonConvert.DeserializeObject<ExpandoObject>(jsonString);
        });
    }

    internal void Set(Guid sessionId, dynamic data)
    {
        WithContext(context =>
        {
            var jsonString = JsonConvert.SerializeObject(data);

            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = "UPDATE session SET value = @value WHERE key = @key AND ip = @ip AND ua = @ua";

            updateCommand.Parameters.Add(new SqliteParameter("@key", sessionId.ToString()));
            updateCommand.Parameters.Add(new SqliteParameter("@ip", sessionId.ToString()));
            updateCommand.Parameters.Add(new SqliteParameter("@ua", sessionId.ToString()));
            updateCommand.Parameters.Add(new SqliteParameter("@value", jsonString));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO session (key, ttl, ip, ua, value) VALUES(@key, datetime(), @ip, @ua, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@key", sessionId.ToString()));
                insertCommand.Parameters.Add(new SqliteParameter("@ip", sessionId.ToString()));
                insertCommand.Parameters.Add(new SqliteParameter("@ua", sessionId.ToString()));
                insertCommand.Parameters.Add(new SqliteParameter("@value", jsonString));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }
}
