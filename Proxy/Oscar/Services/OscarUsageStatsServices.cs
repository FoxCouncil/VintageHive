// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

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

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        await Task.Delay(0);

        switch (snac.SubType)
        {
            case CLI_STATS_REPORT:
            {

            }
            break;
        }
    }
}
