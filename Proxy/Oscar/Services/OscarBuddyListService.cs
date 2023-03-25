using System.Diagnostics;
using System.Text;

namespace VintageHive.Proxy.Oscar.Services;

public class OscarBuddyListService : IOscarService
{
    public const ushort FAMILY_ID = 0x03;

    public const ushort CLI_BUDDYLIST_RIGHTS_REQ = 0x02;
    public const ushort SRV_BUDDYLIST_RIGHTS_REPLY = 0x03;
    public const ushort CLI_BUDDYLIST_ADD = 0x04;
    public const ushort SRV_USER_ONLINE = 0x0B;
    public const ushort SRV_USER_OFFLINE = 0x0C;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarBuddyListService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        switch (snac.SubType)
        {
            case CLI_BUDDYLIST_RIGHTS_REQ:
            {
                var buddyRightsReply = snac.NewReply(Family, SRV_BUDDYLIST_RIGHTS_REPLY);

                buddyRightsReply.WriteTlv(new Tlv(0x01, OscarUtils.GetBytes(500)));
                buddyRightsReply.WriteTlv(new Tlv(0x02, OscarUtils.GetBytes(750)));
                buddyRightsReply.WriteTlv(new Tlv(0x03, OscarUtils.GetBytes(512)));

                await session.SendSnac(buddyRightsReply);
            }
            break;

            case CLI_BUDDYLIST_ADD:
            {
                var buddies = new List<string>();

                var readIdx = 0;

                while (readIdx < snac.RawData.Length)
                {
                    var buddyLength = (ushort)snac.RawData[readIdx];

                    var buddy = Encoding.ASCII.GetString(snac.RawData[(readIdx + 1)..(readIdx + 1 + buddyLength)]);

                    buddies.Add(buddy);

                    readIdx += 1 + buddyLength;
                }

                session.Buddies = buddies;

                await ProcessOnlineNotifications(session, buddies);

                // Debugger.Break();
            }
            break;

            default:
            {
                Debugger.Break();
            }
            break;
        }
    }

    public async Task ProcessOfflineNotifications(OscarSession session)
    {
        foreach (var buddy in session.Buddies)
        {
            var buddySession = OscarServer.Sessions.GetByScreenName(buddy);

            if (buddySession != null)
            {
                var isOffline = new Snac(Family, SRV_USER_OFFLINE);

                isOffline.WriteUInt8((byte)session.ScreenName.Length);
                isOffline.WriteString(session.ScreenName);
                isOffline.WriteUInt16(0); // TODO: Warnings!

                var tlvs = new List<Tlv>
                {
                    new Tlv(0x01, OscarUtils.GetBytes(0))
                };

                isOffline.WriteUInt16((ushort)tlvs.Count);

                foreach (Tlv tlv in tlvs)
                {
                    isOffline.Write(tlv.Encode());
                }

                await buddySession.SendSnac(isOffline);
            }
        }
    }

    private async Task ProcessOnlineNotifications(OscarSession session, List<string> buddies)
    {
        foreach(var buddy in buddies)
        {
            var buddySession = OscarServer.Sessions.GetByScreenName(buddy);

            if (buddySession != null)
            {
                var isOnlineSnac = new Snac(Family, SRV_USER_ONLINE);

                isOnlineSnac.WriteUInt8((byte)buddy.Length);
                isOnlineSnac.WriteString(buddy);
                isOnlineSnac.WriteUInt16(0); // TODO: Warnings!

                var tlvs = new List<Tlv>
                {
                    new Tlv(0x01, OscarUtils.GetBytes(0)),
                    new Tlv(0x06, OscarUtils.GetBytes((uint)buddySession.Status)),
                    new Tlv(0x0F, OscarUtils.GetBytes((uint)0)),
                    new Tlv(0x03, OscarUtils.GetBytes((uint)OscarServer.ServerTime.ToUnixTimeSeconds())),
                    new Tlv(0x05, OscarUtils.GetBytes((uint)420))
                };

                isOnlineSnac.WriteUInt16((ushort)tlvs.Count);

                foreach (Tlv tlv in tlvs)
                {
                    isOnlineSnac.Write(tlv.Encode());
                }

                await session.SendSnac(isOnlineSnac);

                await SendOnlineNotifications(buddySession, session.ScreenName);
            }
        }
    }

    private async Task SendOnlineNotifications(OscarSession session, string screenName)
    {
        var buddySession = OscarServer.Sessions.GetByScreenName(screenName);

        if (buddySession != null)
        {
            var isOnlineSnac = new Snac(Family, SRV_USER_ONLINE);

            isOnlineSnac.WriteUInt8((byte)screenName.Length);
            isOnlineSnac.WriteString(screenName);
            isOnlineSnac.WriteUInt16(0); // TODO: Warnings!

            var tlvs = new List<Tlv>
                {
                    new Tlv(0x01, OscarUtils.GetBytes(0)),
                    new Tlv(0x06, OscarUtils.GetBytes((uint)buddySession.Status)),
                    new Tlv(0x0F, OscarUtils.GetBytes((uint)0)),
                    new Tlv(0x03, OscarUtils.GetBytes((uint)OscarServer.ServerTime.ToUnixTimeSeconds())),
                    new Tlv(0x05, OscarUtils.GetBytes((uint)420))
                };

            isOnlineSnac.WriteUInt16((ushort)tlvs.Count);

            foreach (Tlv tlv in tlvs)
            {
                isOnlineSnac.Write(tlv.Encode());
            }

            await session.SendSnac(isOnlineSnac);
        }
        }
}
