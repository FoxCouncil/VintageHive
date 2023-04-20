using System.Diagnostics;

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarIcbmService : IOscarService
{
    public const ushort FAMILY_ID = 0x04;

    public const ushort CLI_SRV_ERROR = 0x01;
    public const ushort CLI_SET_ICBM_PARAMS = 0x02;
    public const ushort CLI_ICBM_PARAM_REQ = 0x04;
    public const ushort SRV_ICBM_PARAMS = 0x05;
    public const ushort CLI_SEND_ICBM = 0x06;
    public const ushort SRV_CLIENT_ICBM = 0x07;
    public const ushort SRV_MSG_ACK = 0x0C;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarIcbmService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        switch(snac.SubType)
        {
            case CLI_SET_ICBM_PARAMS:
            {
                var channelId = OscarUtils.ToUInt16(snac.RawData[..2]);
                var messageFlags = OscarUtils.ToUInt32(snac.RawData[2..6]);
                
                var maxMessageSnacSize = OscarUtils.ToUInt16(snac.RawData[6..8]);
                var maxSenderWarningLevel = OscarUtils.ToUInt16(snac.RawData[8..10]);
                var maxRecieverWarningLevel = OscarUtils.ToUInt16(snac.RawData[10..12]);
                var minimumMessageIntervalSecs = OscarUtils.ToUInt16(snac.RawData[12..14]);
            }
            break;

            case CLI_ICBM_PARAM_REQ:
            {
                var icbmParamsReply = snac.NewReply(Family, SRV_ICBM_PARAMS);

                icbmParamsReply.WriteUInt16(0);
                icbmParamsReply.WriteUInt32(3);
                icbmParamsReply.WriteUInt16(512);
                icbmParamsReply.WriteUInt16(999);
                icbmParamsReply.WriteUInt16(999);
                icbmParamsReply.WriteUInt16(0);
                icbmParamsReply.WriteUInt16(1000);

                await session.SendSnac(icbmParamsReply);
            }
            break;

            case CLI_SEND_ICBM:
            {
                var msgIdCookie = OscarUtils.ToUint64(snac.RawData[..8]);
                var msgChannel = OscarUtils.ToUInt16(snac.RawData[8..10]);

                var screenNameLength = (ushort)snac.RawData[10];
                var screenName = Encoding.ASCII.GetString(snac.RawData[11..(11 + screenNameLength)]);

                var tlvs = OscarUtils.DecodeTlvs(snac.RawData[(11 + screenNameLength)..]);

                var userSession = OscarServer.Sessions.FirstOrDefault(x => x.ScreenName.ToLower() == screenName.ToLower());

                if (userSession == null)
                {
                    var notFoundSnac = snac.NewReply(Family, CLI_SRV_ERROR);

                    notFoundSnac.WriteUInt16(0x0004);

                    await session.SendSnac(notFoundSnac);
                }
                else
                {
                    var sendClientMessageSnac = new Snac(Family, SRV_CLIENT_ICBM);

                    sendClientMessageSnac.WriteUInt64(msgIdCookie);
                    sendClientMessageSnac.WriteUInt16(msgChannel);

                    sendClientMessageSnac.WriteUInt8((byte)session.ScreenName.Length);
                    sendClientMessageSnac.WriteString(session.ScreenName);

                    // TODO: Warning Level
                    sendClientMessageSnac.WriteUInt16(0);

                    var sendTlvs = new List<Tlv>
                    {
                        new Tlv(0x01, OscarUtils.GetBytes(0)),
                        new Tlv(0x06, OscarUtils.GetBytes((uint)session.Status)),
                        new Tlv(0x0F, OscarUtils.GetBytes((uint)0)),
                        new Tlv(0x03, OscarUtils.GetBytes((uint)420))
                    };

                    sendClientMessageSnac.WriteUInt16((ushort)sendTlvs.Count);

                    foreach (Tlv tlv in sendTlvs)
                    {
                        sendClientMessageSnac.Write(tlv.Encode());
                    }

                    sendClientMessageSnac.WriteTlv(new Tlv(0x02, tlvs.GetTlv(0x02).Value));

                    await userSession.SendSnac(sendClientMessageSnac);

                    var ackSentMessageSnac = new Snac(Family, SRV_MSG_ACK);

                    ackSentMessageSnac.WriteUInt64(msgIdCookie);
                    ackSentMessageSnac.WriteUInt16(msgChannel);

                    ackSentMessageSnac.WriteUInt8((byte)session.ScreenName.Length);
                    ackSentMessageSnac.WriteString(session.ScreenName);

                    await session.SendSnac(ackSentMessageSnac);
                }
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
