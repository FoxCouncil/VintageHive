using Microsoft.Data.Sqlite;
using System.Data;

namespace VintageHive.Data;

internal class DbContextBase
{
    readonly string _connectionString = string.Empty;

    public DbContextBase(string connectionString)
    {
        _connectionString = connectionString;
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

    protected async Task<T> WithConnectionAsync<T>(Func<IDbConnection, Task<T>> sqlTransaction)
    {
        using var connection = new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        return await sqlTransaction(connection);
    }

    protected async Task WithConnectionAsync<T>(Func<IDbConnection, Task> sqlTransaction)
    {
        using var connection = new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        await sqlTransaction(connection);
    }
}