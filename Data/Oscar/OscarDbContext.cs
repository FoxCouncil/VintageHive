using Microsoft.Data.Sqlite;
using VintageHive.Proxy.Oscar;

namespace VintageHive.Data.Oscar
{
    internal class OscarDbContext : DbContextBase, IOscarDb
    {
        public OscarDbContext() : base()
        {
            WithContext(context =>
            {
                var walCommand = context.CreateCommand();

                walCommand.CommandText = @"PRAGMA journal_mode = 'wal'";

                walCommand.ExecuteNonQuery();

                // Create Tables
                var command = context.CreateCommand();

                command.CommandText = "CREATE TABLE IF NOT EXISTS oscar_users (screenname TEXT UNIQUE, password TEXT)";

                command.ExecuteNonQuery();

                command = context.CreateCommand();

                command.CommandText = "CREATE TABLE IF NOT EXISTS oscar_session (cookie TEXT UNIQUE, screenname TEXT, useragent TEXT, clientip TEXT, timestamp TEXT)";

                command.ExecuteNonQuery();
            });
        }

        public OscarSession GetSessionByCookie(string cookie)
        {
            return WithContext<OscarSession>(context =>
            {
                var command = context.CreateCommand();

                command.CommandText = "SELECT * FROM oscar_session WHERE cookie = @cookie";

                command.Parameters.Add(new SqliteParameter("@cookie", cookie));

                using var reader = command.ExecuteReader();

                if (!reader.Read())
                {
                    return default;
                }

                return new OscarSession() { 
                    Cookie = reader.GetString(0),
                    ScreenName = reader.GetString(1),
                    UserAgent = reader.GetString(2)
                };
            });
        }

        public void SetSession(OscarSession session)
        {
            WithContext(context =>
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = "INSERT INTO oscar_session (cookie, screenname, useragent, timestamp) VALUES(@cookie, @screenname, @useragent, datetime('now'))";

                insertCommand.Parameters.Add(new SqliteParameter("@cookie", session.Cookie));
                insertCommand.Parameters.Add(new SqliteParameter("@screenname", session.ScreenName));
                insertCommand.Parameters.Add(new SqliteParameter("@useragent", session.UserAgent));

                insertCommand.ExecuteNonQuery();
            });
        }
    }
}
