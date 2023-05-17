using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarUserLookupService : IOscarService
{
    public const ushort FAMILY_ID = 0x0A;

    public const ushort ERR_GENERIC = 0x01;
    public const ushort CLI_SEARCH_BY_EMAIL = 0x02;
    public const ushort SRV_SEARCH_RESPONSE = 0x03;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarUserLookupService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        await Task.Delay(0);

        switch (snac.SubType)
        {
            case CLI_SEARCH_BY_EMAIL:
            {
                var email = Encoding.ASCII.GetString(snac.RawData);

                var username = email.Split("@hive.com").FirstOrDefault();

                if (Mind.Db.UserExistsByUsername(username))
                {
                    var replySnac = snac.NewReply(FAMILY_ID, SRV_SEARCH_RESPONSE);

                    replySnac.WriteTlv(new Tlv(Tlv.Type_ScreenName, username));

                    await session.SendSnac(replySnac);
                }
                else
                {
                    var replySnac = snac.NewReply(FAMILY_ID, ERR_GENERIC);

                    replySnac.WriteTlv(new Tlv(Tlv.Type_ErrorSubCode, Tlv.Error_NoMatch));

                    await session.SendSnac(replySnac);
                }
            }
            break;
        }
    }
}
