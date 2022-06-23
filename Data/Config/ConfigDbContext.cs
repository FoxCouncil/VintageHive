using Microsoft.Data.Sqlite;
using System.Net;

namespace VintageHive.Data.Config;

internal class ConfigDbContext : DbContextBase, IConfigDb
{
    static readonly Dictionary<string, object> kDefaultSettings = new()
    {
        { ConfigNames.IpAddress, IPAddress.Any.ToString() },
        { ConfigNames.PortHttp, 1990 },
        { ConfigNames.PortFtp, 1971 },
        { ConfigNames.Intranet, true },
        { ConfigNames.ProtoWeb, true },
        { ConfigNames.InternetArchive, true },
        { ConfigNames.InternetArchiveYear, 1999 }
    };

    public ConfigDbContext(string connectionString) : base(connectionString)
    {
        WithContext(context =>
        {
            var walCommand = context.CreateCommand();

            walCommand.CommandText = @"PRAGMA journal_mode = 'wal'";

            walCommand.ExecuteNonQuery();

            // Create Tables
            var command = context.CreateCommand();

            command.CommandText = "CREATE TABLE IF NOT EXISTS config (key TEXT UNIQUE, value BLOB)";

            command.ExecuteNonQuery();
        });
    }

    public T SettingGet<T>(string key)
    {
        return WithContext<T>(context =>
        {
            key = key.ToLowerInvariant();

            var command = context.CreateCommand();

            command.CommandText = "SELECT value FROM config WHERE key = @key";

            command.Parameters.Add(new SqliteParameter("@key", key));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                if (kDefaultSettings.ContainsKey(key))
                {
                    // Todo: Make better err handling
                    var val = (T)kDefaultSettings[key];

                    SettingSet(key, val);

                    return val;
                }

                return default;
            }

            if (typeof(T) == typeof(int))
            {
                return (T)(object)reader.GetInt32(0);
            }

            if (typeof(T) == typeof(bool))
            {
                return (T)(object)reader.GetBoolean(0);
            }

            return (T)reader.GetValue(0);
        });
    }

    public void SettingSet<T>(string key, T value)
    {
        WithContext(context =>
        {
            key = key.ToLowerInvariant();

            using var transaction = context.BeginTransaction();

            if (value == null)
            {
                using var deleteCommand = context.CreateCommand();

                deleteCommand.CommandText = "DELETE FROM config WHERE key = @key";

                deleteCommand.Parameters.Add(new SqliteParameter("@key", key));

                deleteCommand.ExecuteNonQuery();
            }
            else 
            {
                using var updateCommand = context.CreateCommand();

                updateCommand.CommandText = "UPDATE config SET value = @value WHERE key = @key";

                updateCommand.Parameters.Add(new SqliteParameter("@key", key));
                updateCommand.Parameters.Add(new SqliteParameter("@value", value));

                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    using var insertCommand = context.CreateCommand();

                    insertCommand.CommandText = "INSERT INTO config (key, value) VALUES(@key, @value)";

                    insertCommand.Parameters.Add(new SqliteParameter("@key", key));
                    insertCommand.Parameters.Add(new SqliteParameter("@value", value));

                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        });
    }
}
