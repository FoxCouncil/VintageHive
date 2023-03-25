using Microsoft.Data.Sqlite;
using System.Data;
using System.Runtime.CompilerServices;

namespace VintageHive.Data;

internal class DbContextBase
{
    const string FilenameStringFormat = "{0}.db";
    const string ConnectionStringFormat = "Data Source={0};Cache=Shared";

    readonly string _filename = string.Empty;
    readonly string _connectionString = string.Empty;

    internal bool IsNewDb { get; private set; } = false;

    public DbContextBase()
    {
        var fullClassName = this.GetType().Name;

        var className = fullClassName.Replace("DbContext", string.Empty).ToLower();

        _filename = string.Format(FilenameStringFormat, className);

        IsNewDb = !File.Exists(_filename);

        _connectionString = string.Format(ConnectionStringFormat, _filename);

        WithContext(context =>
        {
            var walCommand = context.CreateCommand();

            walCommand.CommandText = @"PRAGMA journal_mode = 'wal'";

            walCommand.ExecuteNonQuery();
        });
    }

    protected T WithContext<T>(Func<IDbConnection, T> sqlTransaction)
    {
        using var connection = new SqliteConnection(_connectionString);

        connection.Open();

        return sqlTransaction(connection);
    }

    protected void WithContext(Action<IDbConnection> sqlTransaction)
    {
        using var connection = new SqliteConnection(_connectionString);

        connection.Open();

        sqlTransaction(connection);
    }

    protected async Task<T> WithContextAsync<T>(Func<IDbConnection, Task<T>> sqlTransaction)
    {
        using var connection = new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        return await sqlTransaction(connection);
    }

    protected async Task WithContextAsync<T>(Func<IDbConnection, Task> sqlTransaction)
    {
        using var connection = new SqliteConnection(_connectionString);

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