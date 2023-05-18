// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarInvitationService : IOscarService
{
    public const ushort FAMILY_ID = 0x06;

    public const ushort SRV_INVITATION_REPLY = 0x03;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarInvitationService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var reply = snac.NewReply(FAMILY_ID, SRV_INVITATION_REPLY);

        await session.SendSnac(reply);
    }
}
