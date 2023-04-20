// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Data;

namespace VintageHive.Data.Types;

internal class HiveUser
{
    public string Username { get; set; }

    public string Password { get; set; }


    public static HiveUser ParseSQL(IDataReader reader)
    {
        var user = new HiveUser
        {
            Username = reader.GetString(0),

            Password = reader.GetString(1)
        };

        return user;
    }
}
