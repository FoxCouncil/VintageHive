using LibFoxyProxy.Http;
using Microsoft.Data.Sqlite;

namespace VintageHive.Data.Cache;

internal class CacheDbContext : DbContextBase, ICacheDb
{
    public CacheDbContext(string connectionString) : base(connectionString)
    {
        WithContext(context =>
        {
            var walCommand = context.CreateCommand();

            walCommand.CommandText = @"PRAGMA journal_mode = 'wal'";

            walCommand.ExecuteNonQuery();

            // Create Tables
            var command = context.CreateCommand();

            command.CommandText = "CREATE TABLE IF NOT EXISTS cache (key TEXT UNIQUE, ttl TEXT, value BLOB)";

            command.ExecuteNonQuery();
        });
    }

    public T Get<T>(string key)
    {
        return WithContext<T>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT value, ttl FROM cache WHERE key = @key";

            command.Parameters.Add(new SqliteParameter("@key", key));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default(T);
            }

            if (reader.GetDateTime(1) <= DateTime.UtcNow)
            {
                return default;
            }

            return (T)reader.GetValue(0);
        });
    }

    public void Set<T>(string key, TimeSpan ttl, T value)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            var futureTimestamp = DateTime.UtcNow + ttl;

            updateCommand.CommandText = "UPDATE cache SET value = @value, ttl = @ttl WHERE key = @key";

            updateCommand.Parameters.Add(new SqliteParameter("@key", key));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO cache (key, ttl, value) VALUES(@key, @ttl, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@key", key));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }
}
