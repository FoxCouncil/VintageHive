// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarPrivacyService : IOscarService
{
    public const ushort FAMILY_ID = 0x09;

    public const ushort CLI_PRIVACY_RIGHTS_REQ = 0x02;
    public const ushort SRV_PRIVACY_RIGHTS_REPLY = 0x03;
    public const ushort CLI_VISIBLE_ADD = 0x05;
    public const ushort CLI_INVISIBLE_ADD = 0x07;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarPrivacyService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        switch (snac.SubType)
        {
            case CLI_PRIVACY_RIGHTS_REQ:
            {
                var replySnac = snac.NewReply(FAMILY_ID, SRV_PRIVACY_RIGHTS_REPLY);

                replySnac.WriteTlv(new Tlv(0x01, new byte[] { 0x04, 0x00 }));
                replySnac.WriteTlv(new Tlv(0x02, new byte[] { 0x04, 0x00 }));

                await session.SendSnac(replySnac);
            }
            break;

            case CLI_VISIBLE_ADD:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarPrivacyService), "Visible list add (not implemented)", session.Client.TraceId.ToString());
            }
            break;

            case CLI_INVISIBLE_ADD:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarPrivacyService), "Invisible list add (not implemented)", session.Client.TraceId.ToString());
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarPrivacyService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", session.Client.TraceId.ToString());
            }
            break;
        }
    }
}
