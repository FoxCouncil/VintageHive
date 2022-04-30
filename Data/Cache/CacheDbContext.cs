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

    internal void Clear()
    {
        WithContext(context =>
        {
            // Create Tables
            var command = context.CreateCommand();

            command.CommandText = "DELETE FROM cache";

            command.ExecuteNonQuery();
        });
    }

    internal Tuple<uint, uint> GetCounters()
    {
        return WithContext<Tuple<uint, uint>>(context =>
        {
            var proxyCacheCountCommand = context.CreateCommand();

            proxyCacheCountCommand.CommandText = "SELECT COUNT(*) FROM cache WHERE key LIKE 'PC-%'";

            using var proxyCacheCountReader = proxyCacheCountCommand.ExecuteReader();

            proxyCacheCountReader.Read();

            var proxyCacheCount = (uint)proxyCacheCountReader.GetInt64(0);

            var archiveAvailabilityCacheCountCommand = context.CreateCommand();

            archiveAvailabilityCacheCountCommand.CommandText = "SELECT COUNT(*) FROM cache WHERE key LIKE 'AREQ-%'";

            using var archiveAvailabilityCacheCountReader = archiveAvailabilityCacheCountCommand.ExecuteReader();

            archiveAvailabilityCacheCountReader.Read();

            var archiveAvailabilityCacheCount = (uint)archiveAvailabilityCacheCountReader.GetInt64(0);

            return new Tuple<uint, uint>(proxyCacheCount, archiveAvailabilityCacheCount);
        });
    }
}
