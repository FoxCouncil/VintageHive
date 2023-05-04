// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using AngleSharp.Dom;
using Microsoft.Data.Sqlite;

namespace VintageHive.Data.Contexts;

internal class CacheDbContext : DbContextBase
{
    private IReadOnlyList<string> tables;

    public CacheDbContext() : base()
    {
        CreateTable("proxyhttp", "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable("proxyftp", "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable("wayback", "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable("waybackavailability", "url TEXT, year TEXT, value TEXT, results TEXT");

        CreateTable("protoweb", "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable("protowebsitelist", "protocol TEXT, url TEXT");

        CreateTable("weather", "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable("radiobrowser", "key TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable("data", "key TEXT UNIQUE, ttl TEXT, value TEXT");
    }

    internal string GetHttpProxy(string url)
    {
        return WithContext<string>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT value, ttl FROM proxyhttp WHERE url = @url";

            command.Parameters.Add(new SqliteParameter("@url", url));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            if (reader.GetDateTime(1) <= DateTime.UtcNow)
            {
                return default;
            }

            return reader.GetString(0);
        });
    }

    internal void SetHttpProxy(string url, TimeSpan ttl, string value)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            var futureTimestamp = DateTime.UtcNow + ttl;

            updateCommand.CommandText = "UPDATE proxyhttp SET value = @value, ttl = @ttl WHERE url = @url";

            updateCommand.Parameters.Add(new SqliteParameter("@url", url));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO proxyhttp (url, ttl, value) VALUES(@url, @ttl, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@url", url));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    internal string GetFtpProxy(string url)
    {
        return WithContext<string>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT value, ttl FROM proxyftp WHERE url = @url";

            command.Parameters.Add(new SqliteParameter("@url", url));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            if (reader.GetDateTime(1) <= DateTime.UtcNow)
            {
                return default;
            }

            return reader.GetString(0);
        });
    }

    internal void SetFtpProxy(string url, TimeSpan ttl, string value)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            var futureTimestamp = DateTime.UtcNow + ttl;

            updateCommand.CommandText = "UPDATE proxyftp SET value = @value, ttl = @ttl WHERE url = @url";

            updateCommand.Parameters.Add(new SqliteParameter("@url", url));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO proxyftp (url, ttl, value) VALUES(@url, @ttl, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@url", url));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    internal string GetWayback(Uri url)
    {
        return WithContext<string>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT value, ttl FROM wayback WHERE url = @url";

            command.Parameters.Add(new SqliteParameter("@url", url.OriginalString));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            if (reader.GetDateTime(1) <= DateTime.UtcNow)
            {
                return default;
            }

            return reader.GetString(0);
        });
    }

    internal void SetWayback(Uri url, TimeSpan ttl, string value)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            var futureTimestamp = DateTime.UtcNow + ttl;

            updateCommand.CommandText = "UPDATE wayback SET value = @value, ttl = @ttl WHERE url = @url";

            updateCommand.Parameters.Add(new SqliteParameter("@url", url.OriginalString));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO wayback (url, ttl, value) VALUES(@url, @ttl, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@url", url.OriginalString));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    internal string GetWaybackAvailability(string url, int year)
    {
        return WithContext<string>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT value FROM waybackavailability WHERE url = @url AND year = @year";

            command.Parameters.Add(new SqliteParameter("@url", url));
            command.Parameters.Add(new SqliteParameter("@year", year));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            return reader.GetString(0);
        });
    }

    internal void SetWaybackAvailability(string url, int year, string value, string results)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = "UPDATE waybackavailability SET value = @value, results = @results WHERE url = @url AND year = @year";

            updateCommand.Parameters.Add(new SqliteParameter("@url", url));
            updateCommand.Parameters.Add(new SqliteParameter("@year", year));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@results", results));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO waybackavailability (url, year, value, results) VALUES(@url, @year, @value, @results)";

                insertCommand.Parameters.Add(new SqliteParameter("@url", url));
                insertCommand.Parameters.Add(new SqliteParameter("@year", year));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@results", results));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    internal string GetProtoweb(Uri url)
    {
        return WithContext<string>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT value, ttl FROM protoweb WHERE url = @url";

            command.Parameters.Add(new SqliteParameter("@url", url.OriginalString));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            if (reader.GetDateTime(1) <= DateTime.UtcNow)
            {
                return default;
            }

            return reader.GetString(0);
        });
    }

    internal void SetProtoweb(Uri url, TimeSpan ttl, string value)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            var futureTimestamp = DateTime.UtcNow + ttl;

            updateCommand.CommandText = "UPDATE protoweb SET value = @value, ttl = @ttl WHERE url = @url";

            updateCommand.Parameters.Add(new SqliteParameter("@url", url.OriginalString));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO protoweb (url, ttl, value) VALUES(@url, @ttl, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@url", url.OriginalString));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    internal string GetWeather(string url)
    {
        return WithContext<string>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT value, ttl FROM weather WHERE url = @url";

            command.Parameters.Add(new SqliteParameter("@url", url));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            if (reader.GetDateTime(1) <= DateTime.UtcNow)
            {
                return default;
            }

            return reader.GetString(0);
        });
    }

    internal void SetWeather(string url, TimeSpan ttl, string value)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            var futureTimestamp = DateTime.UtcNow + ttl;

            updateCommand.CommandText = "UPDATE weather SET value = @value, ttl = @ttl WHERE url = @url";

            updateCommand.Parameters.Add(new SqliteParameter("@url", url));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO weather (url, ttl, value) VALUES(@url, @ttl, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@url", url));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    internal string GetData(string key)
    {
        return WithContext<string>(context =>
        {
            key = key.ToLowerInvariant();

            var command = context.CreateCommand();

            command.CommandText = "SELECT value, ttl FROM data WHERE key = @key";

            command.Parameters.Add(new SqliteParameter("@key", key));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            if (reader.GetDateTime(1) <= DateTime.UtcNow)
            {
                return default;
            }

            return reader.GetString(0);
        });
    }

    internal void SetData(string key, TimeSpan ttl, string value)
    {
        WithContext(context =>
        {
            key = key.ToLowerInvariant();

            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            var futureTimestamp = DateTime.UtcNow + ttl;

            updateCommand.CommandText = "UPDATE data SET value = @value, ttl = @ttl WHERE key = @key";

            updateCommand.Parameters.Add(new SqliteParameter("@key", key));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO data (key, ttl, value) VALUES(@key, @ttl, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@key", key));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    internal async Task<T> Do<T>(string key, TimeSpan ttl, Func<Task<T>> func)
    {
        return await WithContextAsync<T>(async context =>
        {
            key = key.ToLowerInvariant();

            var command = context.CreateCommand();

            command.CommandText = "SELECT value, ttl FROM data WHERE key = @key";

            command.Parameters.Add(new SqliteParameter("@key", key));

            using var reader = command.ExecuteReader();

            if (!reader.Read() || reader.GetDateTime(1) <= DateTime.UtcNow)
            {
                var data = await func();

                string stringData;

                if (data is string)
                {
                    stringData = data as string;
                }
                else
                {
                    stringData = JsonSerializer.Serialize(data);
                }

                using var transaction = context.BeginTransaction();

                using var updateCommand = context.CreateCommand();

                var futureTtl = DateTime.UtcNow + ttl;

                updateCommand.CommandText = "UPDATE data SET value = @value, ttl = @ttl WHERE key = @key";

                updateCommand.Parameters.Add(new SqliteParameter("@key", key));
                updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTtl));
                updateCommand.Parameters.Add(new SqliteParameter("@value", stringData));

                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    using var insertCommand = context.CreateCommand();

                    insertCommand.CommandText = "INSERT INTO data (key, ttl, value) VALUES(@key, @ttl, @value)";

                    insertCommand.Parameters.Add(new SqliteParameter("@key", key));
                    insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTtl));
                    insertCommand.Parameters.Add(new SqliteParameter("@value", stringData));

                    insertCommand.ExecuteNonQuery();
                }

                transaction.Commit();

                return data;
            }

            if (typeof(T) == string.Empty.GetType())
            {
                return (T)reader.GetValue(0);
            }
            else
            {
                return JsonSerializer.Deserialize<T>(reader.GetString(0));
            }
        });
    }

    internal Dictionary<string, uint> GetCounters()
    {
        return WithContext<Dictionary<string, uint>>(context =>
        {
            var tableList = GetTables();

            var output = new Dictionary<string, uint>();

            foreach (var tableName in tableList)
            {
                var countCommand = context.CreateCommand();

                countCommand.CommandText = "SELECT COUNT(*) FROM " + tableName;

                using var countReader = countCommand.ExecuteReader();

                countReader.Read();

                var tableCount = (uint)countReader.GetInt64(0);

                output.Add(tableName, tableCount);
            }

            return output;
        });
    }

    internal void Clear(string tableName = null)
    {
        var tables = GetTables();

        List<string> tablesToClear;

        if (tableName != null && !tables.Contains(tableName))
        {
            return;
        }
        else if (tableName != null && tables.Contains(tableName))
        {
            tablesToClear = new List<string> { tableName };
        }
        else if (tableName == null)
        {
            tablesToClear = tables.ToList();
        }
        else
        {
            throw new Exception("This should never happen");
        }

        WithContext(context =>
        {
            foreach (var table in tablesToClear)
            {
                var command = context.CreateCommand();

                command.CommandText = "DELETE FROM " + table;

                command.ExecuteNonQuery();
            }
        });
    }

    private IReadOnlyList<string> GetTables()
    {
        if (tables == null)
        {
            tables = WithContext<List<string>>(context =>
            {
                var tableListCommand = context.CreateCommand();

                tableListCommand.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";

                using var tablesListReader = tableListCommand.ExecuteReader();

                var output = new List<string>();

                while (tablesListReader.Read())
                {
                    var tableName = tablesListReader.GetString(0);

                    output.Add(tableName);
                }

                return output;
            });
        }

        return tables;
    }
}
