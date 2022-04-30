using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VintageHive.Data.Cache;

namespace VintageHive.Utilities;

public static class CacheUtils
{
    static CacheDbContext DbContext = Mind.Instance._cacheDb;

    public static void ClearCache()
    {
        DbContext.Clear();
    }

    public static Tuple<uint, uint> GetCounters()
    {
        return DbContext.GetCounters();
    }
}
