using Microsoft.Data.Sqlite;

namespace VintageHive.Data.Contexts;

internal class UserDbContext : DbContextBase
{
    public UserDbContext() : base()
    {
        WithContext(context =>
        {
            var walCommand = context.CreateCommand();

            walCommand.CommandText = @"PRAGMA journal_mode = 'wal'";

            walCommand.ExecuteNonQuery();

            var queries = new string[]
            {
                "CREATE TABLE IF NOT EXISTS user (username TEXT, password TEXT)"
            };

            // Create Tables
            foreach (var query in queries)
            {
                var command = context.CreateCommand();

                command.CommandText = query;

                command.ExecuteNonQuery();
            }
        });
    }

    public bool Create(string username, string password)
    {
        if (ExistsByUsername(username))
        {
            return false;
        }

        return WithContext(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "INSERT INTO user (username, password) VALUES(@username, @password)";

            command.Parameters.Add(new SqliteParameter("@username", username));
            command.Parameters.Add(new SqliteParameter("@password", password));

            command.ExecuteNonQuery();

            return true;
        });
    }

    public bool ExistsByUsername(string username)
    {
        return WithContext(context =>  
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT username FROM user WHERE username = @username";

            command.Parameters.Add(new SqliteParameter("@username", username));

            using var reader = command.ExecuteReader();

            return reader.Read();
        });
    }

    public HiveUser Fetch(string username)
    {
        return WithContext(context => 
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT * FROM user WHERE username = @username";

            command.Parameters.Add(new SqliteParameter("@username", username));

            using var reader = command.ExecuteReader();

            return HiveUser.ParseSQL(reader);
        });
    }

    public List<HiveUser> List()
    {
        return WithContext(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT * FROM user";

            using var reader = command.ExecuteReader();

            var users = new List<HiveUser>();

            while (reader.Read())
            {
                users.Add(HiveUser.ParseSQL(reader));
            }

            return users;
        });
    }
}
