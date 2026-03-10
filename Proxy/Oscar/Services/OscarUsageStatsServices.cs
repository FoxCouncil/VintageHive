// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarUsageStatsServices : IOscarService
{
    public const ushort FAMILY_ID = 0x0B;

    public const ushort CLI_STATS_REPORT = 0x03;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarUsageStatsServices(OscarServer server)
    {
        Server = server;
    }

    public Task ProcessSnac(OscarSession session, Snac snac)
    {
        switch (snac.SubType)
        {
            case CLI_STATS_REPORT:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarUsageStatsServices), "Stats report received", session.Client.TraceId.ToString());
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarUsageStatsServices), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", session.Client.TraceId.ToString());
            }
            break;
        }

        return Task.CompletedTask;
    }
}
