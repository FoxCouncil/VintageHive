// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Data.Sqlite;

namespace VintageHive.Data.Contexts;

public class RadioBrowserDbContext : DbContextBase
{
    private const string TABLE_COUNTRIES = "countries";

    private const string TABLE_TAGS = "tags";

    public RadioBrowserDbContext() : base()
    {
        CreateTable(TABLE_COUNTRIES, "iso TXT UNIQUE, name TEXT, count INTEGER");

        CreateTable(TABLE_TAGS, "name TEXT UNIQUE, count INTEGER");
    }

    public List<RadioBrowserTag> TagsGet(int limit = default)
    {
        return WithContext<List<RadioBrowserTag>>(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT * FROM {TABLE_TAGS} ORDER BY count DESC" + (limit != default ? " LIMIT @limit" : "");

            if (limit != default)
            {
                selectCommand.Parameters.Add(new SqliteParameter("@limit", limit));
            }

            using var reader = selectCommand.ExecuteReader();

            var list = new List<RadioBrowserTag>();

            while (reader.Read())
            {
                list.Add(new RadioBrowserTag
                {
                    Name = reader.GetString(0),
                    Stationcount = reader.GetInt32(1),
                });
            }

            return list;
        });
    }

    public void TagsLoad(List<RadioBrowserTag> tags)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            foreach (var tag in tags)
            {
                using var updateCommand = context.CreateCommand();

                updateCommand.CommandText = $"UPDATE {TABLE_TAGS} SET count = @count WHERE name = @name";

                updateCommand.Parameters.Add(new SqliteParameter("@count", tag.Stationcount));
                updateCommand.Parameters.Add(new SqliteParameter("@name", tag.Name));

                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    using var insertCommand = context.CreateCommand();

                    insertCommand.CommandText = $"INSERT INTO {TABLE_TAGS} (name, count) VALUES(@name, @count)";

                    insertCommand.Parameters.Add(new SqliteParameter("@name", tag.Name));
                    insertCommand.Parameters.Add(new SqliteParameter("@count", tag.Stationcount));

                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        });
    }

    public void CountriesLoad(List<RadioBrowserCountry> countries)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            foreach (var country in countries)
            {
                if (country.Iso31661 == "id")
                {
                    continue;
                }

                using var updateCommand = context.CreateCommand();

                updateCommand.CommandText = $"UPDATE {TABLE_COUNTRIES} SET count = @count WHERE iso = @iso";

                updateCommand.Parameters.Add(new SqliteParameter("@count", country.Stationcount));
                updateCommand.Parameters.Add(new SqliteParameter("@iso", country.Iso31661));

                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    using var insertCommand = context.CreateCommand();

                    insertCommand.CommandText = $"INSERT INTO {TABLE_COUNTRIES} (iso, name, count) VALUES(@iso, @name, @count)";

                    var countryName = Mind.Geonames.GetCountryNameByIso(country.Iso31661) ?? country.Iso31661;

                    insertCommand.Parameters.Add(new SqliteParameter("@iso", country.Iso31661));
                    insertCommand.Parameters.Add(new SqliteParameter("@name", countryName));
                    insertCommand.Parameters.Add(new SqliteParameter("@count", country.Stationcount));

                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        });
    }

    public List<RadioBrowserCountry> CountriesGet(int limit = default)
    {
        return WithContext<List<RadioBrowserCountry>>(context =>
        {

            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT * FROM {TABLE_COUNTRIES} ORDER BY count DESC" + (limit != default ? " LIMIT @limit" : "");

            if (limit != default)
            {
                selectCommand.Parameters.Add(new SqliteParameter("@limit", limit));
            }

            using var reader = selectCommand.ExecuteReader();

            var list = new List<RadioBrowserCountry>();

            while (reader.Read())
            {
                list.Add(new RadioBrowserCountry
                {
                    Iso31661 = reader.GetString(0),
                    Name = reader.GetString(1),
                    Stationcount = reader.GetInt32(2),
                });
            }

            return list;
        });
    }
}
