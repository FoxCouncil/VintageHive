using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive;

public static class Extensions
{
    public static bool HostContains(this Uri uri, string searchPattern)
    {
        if (uri == null)
        {
            return false;
        }

        if (searchPattern == null)
        {
            throw new ArgumentNullException(nameof(searchPattern));
        }

        if (uri.Host == null)
        {
            return false;
        }

        return uri.Host.Contains(searchPattern);
    }
}
