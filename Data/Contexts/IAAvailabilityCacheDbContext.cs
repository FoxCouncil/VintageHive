namespace VintageHive.Data.Contexts;

internal class IAAvailabilityCacheDbContext : DbContextBase
{
    public IAAvailabilityCacheDbContext() : base()
    {
        WithContext(context =>
        {
            var walCommand = context.CreateCommand();

            walCommand.CommandText = @"PRAGMA journal_mode = 'wal'";

            walCommand.ExecuteNonQuery();

            // Create Tables
            var command = context.CreateCommand();

            command.CommandText = "CREATE TABLE IF NOT EXISTS cache (key TEXT UNIQUE, year TEXT, ttl TEXT, value text)";

            command.ExecuteNonQuery();
        });
    }

    
}
