using Microsoft.Data.Sqlite;
using VintageHive.Data.Types;

namespace VintageHive.Data.Contexts;

internal class GeonamesDbContext : DbContextBase
{
    const string dbPath = "libs/geonames.db";

    public GeonamesDbContext() : base(dbPath) 
    {
        if (IsNewDb)
        {
            throw new ApplicationException("GeonamesDB should never be created! It's in the /libs folder!");
        }
    }

    internal GeoIp GetLocationBySearch(string search)
    {
        search = "%" + search + "%";

        return WithContext<GeoIp>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = "SELECT * FROM geoname_fulltext WHERE longname LIKE @search ORDER BY population DESC";

            command.Parameters.Add(new SqliteParameter("@search", search));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            var returnObj = new GeoIp()
            {
                query = "Geonames",
                city = reader.GetString(2),
                region = reader.GetString(3),
                regionName = reader.GetString(3),
                country = reader.GetString(4),
                lat = reader.GetFloat(6),
                lon = reader.GetFloat(7),
                timezone = reader.GetString(8)
            };

            return returnObj;
        });
    }
}
