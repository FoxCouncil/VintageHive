using System.Diagnostics;

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

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarLocationService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        switch (snac.SubType)
        {
            case CLI_LOCATION_RIGHTS_REQ:
            {
                var locationRightsReply = snac.NewReply(Family, SRV_LOCATION_RIGHTS_REPLY);

                locationRightsReply.WriteTlv(new Tlv(0x01, OscarUtils.GetBytes(256)));

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

                session.Status = session.AwayMessage == string.Empty ? OscarSessionOnlineStatus.Online : OscarSessionOnlineStatus.Away;

                var capsTlv = tlvs.GetTlv(0x05);

                if (capsTlv != null)
                {
                    var capsData = capsTlv.Value;

                    var readIdx = 0;

                    var capsList = new List<string>();

                    while (readIdx < capsData.Length)
                    {
                        var bytes = capsData[readIdx..(readIdx + 16)];

                        var clsid = OscarUtils.ToCLSID(bytes);

                        capsList.Add(clsid);

                        readIdx += 16;
                    }

                    session.Capabilities = capsList;
                }
            }
            break;

            case CLI_LOCATION_INFO_REQ:
            {
                var data = snac.RawData;

                var type = OscarUtils.ToUInt16(data[0..2]);
                var screenNameLength = (ushort)data[2];
                var screenName = Encoding.ASCII.GetString(data[3..(3 + screenNameLength)]);

                var userSession = OscarServer.Sessions.FirstOrDefault(x => x.ScreenName.ToLower() == screenName.ToLower());

                if (userSession == null)
                {
                    var notFoundSnac = snac.NewReply(Family, CLI_SRV_ERROR);

                    notFoundSnac.WriteUInt16(0x0004);

                    await session.SendSnac(notFoundSnac);
                }
                else
                {
                    var userInfoReply = snac.NewReply(Family, SRV_USERxONLINExINFO);

                    userInfoReply.WriteUInt8((byte)userSession.ScreenName.Length);
                    userInfoReply.WriteString(userSession.ScreenName);

                    // TODO: Warning Levels
                    userInfoReply.WriteUInt16(0);

                    var tlvs = new List<Tlv>
                    {
                        new Tlv(0x01, OscarUtils.GetBytes(0)),
                        new Tlv(0x06, OscarUtils.GetBytes((uint)userSession.Status)),
                        new Tlv(0x0F, OscarUtils.GetBytes((uint)0)),
                        new Tlv(0x03, OscarUtils.GetBytes((uint)420)),
                        new Tlv(0x05, OscarUtils.GetBytes((uint)420))
                    };

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
            }
            break;

            case CLI_GET_DIR_INFO:
            {
                var huh = Encoding.ASCII.GetString(snac.RawData[1..]);

                // NO-OP FOR NOW
            }
            break;

            default:
            {
                Debugger.Break();
            }
            break;
        }
    }
}
