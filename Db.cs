using VintageHive.Data;

namespace VintageHive;

internal static class Db
{
    public static SessionDbContext Sessions { get; } = new();
}
