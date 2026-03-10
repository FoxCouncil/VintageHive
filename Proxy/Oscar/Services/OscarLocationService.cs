// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

public class OscarLocationService : IOscarService
{
    public const ushort FAMILY_ID = 0x02;

    public const ushort CLI_SRV_ERROR = 0x01;
    public const ushort CLI_LOCATION_RIGHTS_REQ = 0x02;
    public const ushort SRV_LOCATION_RIGHTS_REPLY = 0x03;
    public const ushort CLI_SET_LOCATION_INFO = 0x04;
    public const ushort CLI_LOCATION_INFO_REQ = 0x05;
    public const ushort SRV_USERxONLINExINFO = 0x06;
    public const ushort CLI_GET_DIR_INFO = 0x0B;
    public const ushort SRV_DIR_INFO_REPLY = 0x0C;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarLocationService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_LOCATION_RIGHTS_REQ:
            {
                var locationRightsReply = snac.NewReply(Family, SRV_LOCATION_RIGHTS_REPLY);

                locationRightsReply.WriteTlv(new Tlv(0x01, OscarUtils.GetBytes(1024)));

                await session.SendSnac(locationRightsReply);
            }
            break;

            case CLI_SET_LOCATION_INFO:
            {
                var tlvs = OscarUtils.DecodeTlvs(snac.RawData);

                var profileMessageTlv = tlvs.GetTlv(0x01);

                if (profileMessageTlv != null)
                {
                    session.ProfileMimeType = Encoding.ASCII.GetString(profileMessageTlv.Value);
                    session.Profile = Encoding.ASCII.GetString(tlvs.GetTlv(0x02).Value);
                }

                var awayMessageTlv = tlvs.GetTlv(0x03);

                if (awayMessageTlv != null)
                {
                    session.AwayMessageMimeType = Encoding.ASCII.GetString(awayMessageTlv.Value);
                    session.AwayMessage = Encoding.ASCII.GetString(tlvs.GetTlv(0x04).Value);
                }

                var previousStatus = session.Status;

                session.Status = session.AwayMessage == string.Empty ? OscarSessionOnlineStatus.Online : OscarSessionOnlineStatus.Away;

                var capsTlv = tlvs.GetTlv(0x05);

                if (capsTlv != null)
                {
                    var capsData = capsTlv.Value;

                    var readIdx = 0;

                    var capsList = new List<string>();

                    while (readIdx + 16 <= capsData.Length)
                    {
                        var bytes = capsData[readIdx..(readIdx + 16)];

                        var clsid = OscarUtils.ToCLSID(bytes);

                        capsList.Add(clsid);

                        readIdx += 16;
                    }

                    session.Capabilities = capsList;
                }

                session.Save();

                // Broadcast status change if it changed
                if (previousStatus != session.Status)
                {
                    await session.BroadcastStatusToWatchers();
                }
            }
            break;

            case CLI_LOCATION_INFO_REQ:
            {
                var data = snac.RawData;

                var type = OscarUtils.ToUInt16(data[0..2]);
                var screenNameLength = (ushort)data[2];
                var screenName = Encoding.ASCII.GetString(data[3..(3 + screenNameLength)]);

                var userSession = OscarServer.Sessions.GetByScreenName(screenName);

                if (userSession == null)
                {
                    var notFoundSnac = snac.NewReply(Family, CLI_SRV_ERROR);

                    notFoundSnac.WriteUInt16(0x0004);

                    await session.SendSnac(notFoundSnac);

                    return;
                }

                var userInfoReply = snac.NewReply(Family, SRV_USERxONLINExINFO);

                userInfoReply.WriteUInt8((byte)userSession.ScreenName.Length);
                userInfoReply.WriteString(userSession.ScreenName);

                userInfoReply.WriteUInt16(userSession.WarningLevel);

                var tlvs = new List<Tlv>
                {
                    new Tlv(0x01, OscarUtils.GetBytes(0)),
                    new Tlv(0x06, OscarUtils.GetBytes((uint)userSession.Status)),
                    new Tlv(0x0F, OscarUtils.GetBytes((uint)userSession.SignOnTime.ToUnixTimeSeconds())),
                    new Tlv(0x03, OscarUtils.GetBytes((uint)OscarServer.ServerTime.ToUnixTimeSeconds())),
                    new Tlv(0x05, OscarUtils.GetBytes((uint)userSession.SignOnTime.ToUnixTimeSeconds()))
                };

                if (userSession.GetCurrentIdleSeconds() > 0)
                {
                    tlvs.Add(new Tlv(0x04, OscarUtils.GetBytes((ushort)userSession.GetCurrentIdleSeconds())));
                }

                userInfoReply.WriteUInt16((ushort)tlvs.Count);

                foreach (Tlv tlv in tlvs)
                {
                    userInfoReply.Write(tlv.Encode());
                }

                switch (type)
                {
                    case 1:
                    {
                        if (userSession.ProfileMimeType == null || userSession.Profile == null)
                        {
                            break;
                        }

                        userInfoReply.WriteTlv(new Tlv(0x01, Encoding.ASCII.GetBytes(userSession.ProfileMimeType)));
                        userInfoReply.WriteTlv(new Tlv(0x02, Encoding.ASCII.GetBytes(userSession.Profile)));
                    }
                    break;

                    case 3:
                    {
                        if (userSession.AwayMessageMimeType == null || userSession.AwayMessage == null)
                        {
                            break;
                        }

                        userInfoReply.WriteTlv(new Tlv(0x01, Encoding.ASCII.GetBytes(userSession.AwayMessageMimeType)));
                        userInfoReply.WriteTlv(new Tlv(0x02, Encoding.ASCII.GetBytes(userSession.AwayMessage)));
                    }
                    break;
                }

                await session.SendSnac(userInfoReply);
            }
            break;

            case CLI_GET_DIR_INFO:
            {
                // Directory info — return profile data from DB
                var data = snac.RawData;
                var screenNameLength = (ushort)data[0];
                var screenName = Encoding.ASCII.GetString(data[1..(1 + screenNameLength)]);

                var profile = Mind.Db.OscarGetProfile(screenName);

                var dirReply = snac.NewReply(Family, SRV_DIR_INFO_REPLY);

                dirReply.WriteUInt16(0x0001); // Result: success

                if (profile != null)
                {
                    var dirTlvs = new List<Tlv>
                    {
                        new Tlv(0x01, screenName),                   // Screen name
                        new Tlv(0x02, profile.FirstName),            // First name
                        new Tlv(0x03, profile.LastName),             // Last name
                        new Tlv(0x04, profile.Email),                // Email
                        new Tlv(0x07, profile.HomeCity),             // City
                        new Tlv(0x08, profile.HomeState),            // State
                        new Tlv(0x0C, profile.Nickname),             // Nickname
                        new Tlv(0x0D, profile.HomeZip),              // Zip code
                    };

                    dirReply.WriteTlvs(dirTlvs);
                }

                await session.SendSnac(dirReply);

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarLocationService), $"Directory info requested for {screenName}", traceId);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarLocationService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }
}
