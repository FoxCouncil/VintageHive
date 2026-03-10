// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Data.Sqlite;

namespace VintageHive.Data.Contexts;

public class CacheDbContext : DbContextBase
{
    private IReadOnlyList<string> tables;

    public CacheDbContext() : base()
    {
        CreateTable(CacheTableNames.ProxyHttp, "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable(CacheTableNames.ProxyFtp, "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable(CacheTableNames.Wayback, "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable(CacheTableNames.WaybackAvailability, "url TEXT, year TEXT, value TEXT, results TEXT");

        CreateTable(CacheTableNames.Protoweb, "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable(CacheTableNames.ProtowebSiteList, "protocol TEXT, url TEXT");

        CreateTable(CacheTableNames.Weather, "url TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable(CacheTableNames.RadioBrowser, "key TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable(CacheTableNames.Data, "key TEXT UNIQUE, ttl TEXT, value TEXT");

        CreateTable(CacheTableNames.Usenet, "key TEXT UNIQUE, ttl TEXT, value TEXT");
    }

    internal string GetHttpProxy(string url) => GetCachedValue(CacheTableNames.ProxyHttp, "url", url);

    internal void SetHttpProxy(string url, TimeSpan ttl, string value) => SetCachedValue(CacheTableNames.ProxyHttp, "url", url, ttl, value);

    internal string GetFtpProxy(string url) => GetCachedValue(CacheTableNames.ProxyFtp, "url", url);

    internal void SetFtpProxy(string url, TimeSpan ttl, string value) => SetCachedValue(CacheTableNames.ProxyFtp, "url", url, ttl, value);

    internal string GetWayback(Uri url) => GetCachedValue(CacheTableNames.Wayback, "url", url.OriginalString);

    internal void SetWayback(Uri url, TimeSpan ttl, string value) => SetCachedValue(CacheTableNames.Wayback, "url", url.OriginalString, ttl, value);

    internal string GetProtoweb(Uri url) => GetCachedValue(CacheTableNames.Protoweb, "url", url.OriginalString);

    internal void SetProtoweb(Uri url, TimeSpan ttl, string value) => SetCachedValue(CacheTableNames.Protoweb, "url", url.OriginalString, ttl, value);

    internal string GetWeather(string url) => GetCachedValue(CacheTableNames.Weather, "url", url);

    internal void SetWeather(string url, TimeSpan ttl, string value) => SetCachedValue(CacheTableNames.Weather, "url", url, ttl, value);

    internal string GetUsenet(string key) => GetCachedValue(CacheTableNames.Usenet, "key", key.ToLowerInvariant());

    internal void SetUsenet(string key, TimeSpan ttl, string value) => SetCachedValue(CacheTableNames.Usenet, "key", key.ToLowerInvariant(), ttl, value);

    internal string GetData(string key) => GetCachedValue(CacheTableNames.Data, "key", key.ToLowerInvariant());

    internal void SetData(string key, TimeSpan ttl, string value) => SetCachedValue(CacheTableNames.Data, "key", key.ToLowerInvariant(), ttl, value);

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

    internal async Task<T> Do<T>(string key, TimeSpan ttl, Func<Task<T>> func)
    {
        key = key.ToLowerInvariant();

        // Phase 1: Quick read — check cache with a short-lived connection
        var cached = GetData(key);

        if (cached != null)
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)cached;
            }
            else
            {
                return JsonSerializer.Deserialize<T>(cached);
            }
        }

        // Phase 2: Cache miss — run the async work with NO connection held
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

        // Phase 3: Quick write — store result with a short-lived connection
        SetData(key, ttl, stringData);

        return data;
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
