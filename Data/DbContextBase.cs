﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

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
        });
    }

    protected T WithContext<T>(Func<IDbConnection, T> sqlTransaction)
    {
        using var connection = new SqliteConnection(connectionString);

        connection.Open();

        return sqlTransaction(connection);
    }

    protected void WithContext(Action<IDbConnection> sqlTransaction)
    {
        using var connection = new SqliteConnection(connectionString);

        connection.Open();

        sqlTransaction(connection);
    }

    protected async Task<T> WithContextAsync<T>(Func<IDbConnection, Task<T>> sqlTransaction)
    {
        using var connection = new SqliteConnection(connectionString);

        await connection.OpenAsync();

        return await sqlTransaction(connection);
    }

    protected async Task WithContextAsync<T>(Func<IDbConnection, Task> sqlTransaction)
    {
        using var connection = new SqliteConnection(connectionString);

        await connection.OpenAsync();

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
}