// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Data.Sqlite;
using System.Data;

namespace VintageHive.Data;

public class DbContextBase
{
    const string FilenameStringFormat = "{0}.db";
    const string ConnectionStringFormat = "Data Source={0};Cache=Shared";

    readonly string connectionString = string.Empty;

    internal bool IsNewDb { get; private set; } = false;

    public DbContextBase(string dbName = "")
    {
        var dbPath = "";

        if (string.IsNullOrEmpty(dbName))
        {
            var fullClassName = this.GetType().Name;

            var className = fullClassName.Replace("DbContext", string.Empty).ToLower();

            var basePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, VFS.BasePath, VFS.DataPath));

            dbPath = Path.Combine(basePath, string.Format(FilenameStringFormat, className));
        }
        else
        {
            // External Readonly DB's
            dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbName);
        }

        IsNewDb = !File.Exists(dbPath);

        connectionString = string.Format(ConnectionStringFormat, dbPath);

        WithContext(context =>
        {
            var walCommand = context.CreateCommand();

            walCommand.CommandText = @"PRAGMA journal_mode = 'wal'";

            walCommand.ExecuteNonQuery();

            var busyCommand = context.CreateCommand();

            busyCommand.CommandText = @"PRAGMA busy_timeout = 5000";

            busyCommand.ExecuteNonQuery();
        });
    }

    protected T WithContext<T>(Func<IDbConnection, T> sqlTransaction)
    {
        using var connection = new SqliteConnection(connectionString);

        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000";
        cmd.ExecuteNonQuery();

        return sqlTransaction(connection);
    }

    protected void WithContext(Action<IDbConnection> sqlTransaction)
    {
        using var connection = new SqliteConnection(connectionString);

        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000";
        cmd.ExecuteNonQuery();

        sqlTransaction(connection);
    }

    protected async Task<T> WithContextAsync<T>(Func<IDbConnection, Task<T>> sqlTransaction)
    {
        using var connection = new SqliteConnection(connectionString);

        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000";
        cmd.ExecuteNonQuery();

        return await sqlTransaction(connection);
    }

    protected async Task WithContextAsync<T>(Func<IDbConnection, Task> sqlTransaction)
    {
        using var connection = new SqliteConnection(connectionString);

        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000";
        cmd.ExecuteNonQuery();

        await sqlTransaction(connection);
    }

    protected void CreateTable(string tableName, string tableFields)
    {
        WithContext(context =>
        {
            var createTableCommand = context.CreateCommand();

            createTableCommand.CommandText = $"CREATE TABLE IF NOT EXISTS {tableName} ({tableFields})";

            createTableCommand.ExecuteNonQuery();
        });
    }

    protected string GetCachedValue(string table, string keyColumn, string keyValue)
    {
        return WithContext<string>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT value, ttl FROM {table} WHERE {keyColumn} = @key";

            command.Parameters.Add(new SqliteParameter("@key", keyValue));

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

    protected void SetCachedValue(string table, string keyColumn, string keyValue, TimeSpan ttl, string value)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            var futureTimestamp = DateTime.UtcNow + ttl;

            updateCommand.CommandText = $"UPDATE {table} SET value = @value, ttl = @ttl WHERE {keyColumn} = @key";

            updateCommand.Parameters.Add(new SqliteParameter("@key", keyValue));
            updateCommand.Parameters.Add(new SqliteParameter("@value", value));
            updateCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = $"INSERT INTO {table} ({keyColumn}, ttl, value) VALUES(@key, @ttl, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@key", keyValue));
                insertCommand.Parameters.Add(new SqliteParameter("@value", value));
                insertCommand.Parameters.Add(new SqliteParameter("@ttl", futureTimestamp));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }
}