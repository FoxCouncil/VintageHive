// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;
using System.Security.Cryptography;

namespace VintageHive.Proxy.Oscar.Services;

public class OscarAuthorizationService : IOscarService
{
    public const ushort FAMILY_ID = 0x17;

    public const ushort CLI_MD5_LOGIN = 0x02;
    public const ushort SRV_LOGIN_REPLY = 0x03;
    public const ushort CLI_REGISTRATION_REQUEST = 0x04;
    public const ushort CLI_AUTH_REQUEST = 0x06;
    public const ushort SRV_AUTH_KEY_RESPONSE = 0x07;

    public ushort Family => FAMILY_ID;

    const string OSCAR_MD5_STRING = "AOL Instant Messenger (SM)";

    public OscarServer Server { get; }

    public OscarAuthorizationService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        switch (snac.SubType)
        {
            case CLI_MD5_LOGIN:
            {
                var tlvs = OscarUtils.DecodeTlvs(snac.RawData);

                session.ScreenName = tlvs.GetTlv(0x01).Value.ToASCII();

                if (!Mind.Db.UserExistsByUsername(session.ScreenName))
                {
                    await FailAuth(session, snac);

                    return;
                }

                var user = Mind.Db.UserFetch(session.ScreenName);

                session.UserAgent = tlvs.GetTlv(0x03).Value.ToASCII();

                var hashedPassword = tlvs.GetTlv(0x25).Value;

                var userPassword = MD5.HashData(Encoding.ASCII.GetBytes(session.ScreenName + user.Password + OSCAR_MD5_STRING));

                if (hashedPassword.SequenceEqual(userPassword))
                {
                    var remoteIpAddress = session.Client.RemoteIP;

                    var otherSession = Mind.Db.OscarGetSessionByScreenameAndIp(user.Username, remoteIpAddress);

                    if (otherSession != null)
                    {
                        session.Cookie = otherSession.Cookie;
                    }
                    else
                    {
                        session.Cookie = Guid.NewGuid().ToString().ToUpper();

                        Mind.Db.OscarInsertOrUpdateSession(session);
                    }

                    var serverIP = ((IPEndPoint)session.Client.RawSocket.LocalEndPoint).Address.MapToIPv4();

                    var authSuccessTlvs = new List<Tlv>
                    {
                        new Tlv(Tlv.Type_ScreenName, session.ScreenName),
                        new Tlv(0x0005, $"{serverIP}:5190"),
                        new Tlv(0x0006, session.Cookie),
                        new Tlv(0x0011, $"{session.ScreenName}@hive.com"),
                    };

                    var authSuccessSnac = snac.NewReply(Family, SRV_LOGIN_REPLY);

                    authSuccessSnac.WriteTlvs(authSuccessTlvs);

                    await session.SendSnac(authSuccessSnac);
                }
                else
                {
                    await FailAuth(session, snac);
                }
            }
            break;

            case CLI_REGISTRATION_REQUEST:
            {
                var tlvs = OscarUtils.DecodeTlvs(snac.RawData);

                var registrationData = tlvs.GetTlv(0x01).Value;

            }
            break;

            case CLI_AUTH_REQUEST:
            {
                var tlvs = OscarUtils.DecodeTlvs(snac.RawData);

                session.ScreenName = tlvs.GetTlv(0x01).Value.ToASCII();

                var authReplySnac = snac.NewReply(Family, SRV_AUTH_KEY_RESPONSE);

                authReplySnac.WriteUInt16((ushort)session.ScreenName.Length);
                authReplySnac.WriteString(session.ScreenName);

                await session.SendSnac(authReplySnac);
            }
            break;

            default:
            {
                await Task.Delay(0);

                Debugger.Break();
            }
            break;
        }
    }

    private async Task FailAuth(OscarSession session, Snac snac)
    {
        var authFailed = new List<Tlv>
        {
            new Tlv(Tlv.Type_ScreenName, session.ScreenName),
            new Tlv(0x0004, OscarServer.LoginHelpUrl),
            new Tlv(0x0008, (ushort)OscarAuthError.IncorrectScreenNameOrPassword)
        };

        var failedAuthSnac = snac.NewReply(Family, SRV_LOGIN_REPLY);

        failedAuthSnac.WriteTlvs(authFailed);

        await session.SendSnac(failedAuthSnac);
    }
}
