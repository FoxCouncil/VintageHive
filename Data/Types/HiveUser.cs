using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
