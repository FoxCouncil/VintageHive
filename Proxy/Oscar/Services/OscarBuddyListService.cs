// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

public class OscarBuddyListService : IOscarService
{
    public const ushort FAMILY_ID = 0x03;

    public const ushort CLI_BUDDYLIST_RIGHTS_REQ = 0x02;
    public const ushort SRV_BUDDYLIST_RIGHTS_REPLY = 0x03;
    public const ushort CLI_BUDDYLIST_ADD = 0x04;
    public const ushort CLI_BUDDYLIST_REMOVE = 0x05;
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
        var traceId = session.Client.TraceId.ToString();

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
                var buddies = ParseBuddyList(snac.RawData);

                session.Buddies = buddies;

                session.Save();

                await ProcessOnlineNotifications(session, buddies);
            }
            break;

            case CLI_BUDDYLIST_REMOVE:
            {
                var removedBuddies = ParseBuddyList(snac.RawData);

                foreach (var buddy in removedBuddies)
                {
                    session.Buddies.RemoveAll(b => b.Equals(buddy, StringComparison.OrdinalIgnoreCase));
                }

                session.Save();

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarBuddyListService), $"Removed {removedBuddies.Count} buddies from list", traceId);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarBuddyListService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
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
                await SendUserOffline(buddySession, session.ScreenName, session.WarningLevel);
            }
        }
    }

    private async Task ProcessOnlineNotifications(OscarSession session, List<string> buddies)
    {
        foreach (var buddy in buddies)
        {
            var buddySession = OscarServer.Sessions.GetByScreenName(buddy);

            if (buddySession != null)
            {
                // Tell session that buddy is online
                await SendUserOnline(session, buddySession);

                // Tell buddy that session is online
                await SendUserOnline(buddySession, session);
            }
        }
    }

    public static async Task SendUserOnline(OscarSession recipient, OscarSession onlineUser)
    {
        var isOnlineSnac = new Snac(FAMILY_ID, SRV_USER_ONLINE);

        isOnlineSnac.WriteUInt8((byte)onlineUser.ScreenName.Length);
        isOnlineSnac.WriteString(onlineUser.ScreenName);
        isOnlineSnac.WriteUInt16(onlineUser.WarningLevel);

        var tlvs = new List<Tlv>
        {
            new Tlv(0x01, OscarUtils.GetBytes(0)),
            new Tlv(0x06, OscarUtils.GetBytes((uint)onlineUser.Status)),
            new Tlv(0x0F, OscarUtils.GetBytes((uint)onlineUser.SignOnTime.ToUnixTimeSeconds())),
            new Tlv(0x03, OscarUtils.GetBytes((uint)OscarServer.ServerTime.ToUnixTimeSeconds())),
            new Tlv(0x05, OscarUtils.GetBytes((uint)onlineUser.SignOnTime.ToUnixTimeSeconds()))
        };

        if (onlineUser.GetCurrentIdleSeconds() > 0)
        {
            tlvs.Add(new Tlv(0x04, OscarUtils.GetBytes((ushort)onlineUser.GetCurrentIdleSeconds())));
        }

        isOnlineSnac.WriteUInt16((ushort)tlvs.Count);

        foreach (Tlv tlv in tlvs)
        {
            isOnlineSnac.Write(tlv.Encode());
        }

        await recipient.SendSnac(isOnlineSnac);
    }

    private static async Task SendUserOffline(OscarSession recipient, string offlineScreenName, ushort warningLevel)
    {
        var isOffline = new Snac(FAMILY_ID, SRV_USER_OFFLINE);

        isOffline.WriteUInt8((byte)offlineScreenName.Length);
        isOffline.WriteString(offlineScreenName);
        isOffline.WriteUInt16(warningLevel);

        var tlvs = new List<Tlv>
        {
            new Tlv(0x01, OscarUtils.GetBytes(0))
        };

        isOffline.WriteUInt16((ushort)tlvs.Count);

        foreach (Tlv tlv in tlvs)
        {
            isOffline.Write(tlv.Encode());
        }

        await recipient.SendSnac(isOffline);
    }

    private static List<string> ParseBuddyList(byte[] data)
    {
        var buddies = new List<string>();
        var readIdx = 0;

        while (readIdx < data.Length)
        {
            var buddyLength = (ushort)data[readIdx];

            var buddy = Encoding.ASCII.GetString(data[(readIdx + 1)..(readIdx + 1 + buddyLength)]);

            buddies.Add(buddy);

            readIdx += 1 + buddyLength;
        }

        return buddies;
    }
}
