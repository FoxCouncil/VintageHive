using System.Diagnostics;

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
                // Debugger.Break();
            }
            break;

            case CLI_INVISIBLE_ADD:
            {

            }
            break;

            default:
            {
                // Debugger.Break();

                await Task.Delay(0);
            }
            break;
        }
    }
}
