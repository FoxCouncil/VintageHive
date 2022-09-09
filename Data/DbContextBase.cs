using Microsoft.Data.Sqlite;
using System.Data;
using System.Runtime.CompilerServices;

namespace VintageHive.Data;

internal class DbContextBase
{
    const string ConnectionStringFormat = "Data Source={0}.db;Cache=Shared";

    readonly string _connectionString = string.Empty;

    public DbContextBase()
    {
        var fullClassName = this.GetType().Name;

        var className = fullClassName.Replace("DbContext", string.Empty).ToLower();

        _connectionString = string.Format(ConnectionStringFormat, className);
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
}