// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;
using VintageHive.Proxy.Oscar.Services;

namespace VintageHive.Proxy.Oscar;

public class OscarServer : Listener
{
    public const string LoginHelpUrl = "http://hive.com/help.html#aim_login";

    public static readonly List<OscarSession> Sessions = new();

    public static DateTimeOffset ServerTime => DateTime.Now;

    public static readonly Dictionary<ushort, ushort> ServiceVersions = new()
    {
        { OscarGenericServiceControls.FAMILY_ID, 0x03 }, // Generic Service Controls
        { OscarLocationService.FAMILY_ID, 0x01 }, // Location Services
        { OscarBuddyListService.FAMILY_ID, 0x01 }, // Buddy List Management Service
        { OscarIcbmService.FAMILY_ID, 0x01 }, // ICBM (messages) Service
        { 0x05, 0x01 },
        { OscarInvitationService.FAMILY_ID, 0x01 },
        { 0x08, 0x01 },
        { OscarPrivacyService.FAMILY_ID, 0x01 }, // (PD) Permit/Deny settings for the user.
        { OscarUserLookupService.FAMILY_ID, 0x01 },
        { OscarUsageStatsServices.FAMILY_ID, 0x01 },
        { 0x0C, 0x01 },
        { 0x10, 0x01 },
        { 0x13, 0x01 },
        { OscarIcqService.FAMILY_ID, 0x01 }, // ICQ specific services.
        { OscarAuthorizationService.FAMILY_ID, 0x01 }  // Authorization/Registration Service
    };

    public readonly Flap HelloFlap = new(FlapFrameType.SignOn)
    {
        Data = new byte[] { 0x00, 0x00, 0x00, 0x01 }
    };

    readonly List<IOscarService> services;

    public OscarServer(IPAddress listenAddress) : base(listenAddress, 5190, SocketType.Stream, ProtocolType.Tcp, false)
    {
        services = new()
        {
            new OscarGenericServiceControls(this),
            new OscarLocationService(this),
            new OscarBuddyListService(this),
            new OscarIcbmService(this),
            new OscarInvitationService(this),
            new OscarPrivacyService(this),
            new OscarUserLookupService(this),
            new OscarUsageStatsServices(this),
            new OscarIcqService(this),
            new OscarAuthorizationService(this)
        };
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var traceId = connection.TraceId.ToString();

        var session = new OscarSession(connection);

        Sessions.Add(session);

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Client connected from {connection.RemoteAddress}", traceId);

        await session.SendFlap(HelloFlap);

        session.SentHello = true;

        try
        {
            while (session.Client.IsConnected)
            {
                var flaps = await session.ReceiveFlaps();

                if (flaps == null)
                {
                    Thread.Sleep(1);

                    continue;
                }

                foreach (Flap flap in flaps)
                {
                    switch (flap.Type)
                    {
                        case FlapFrameType.SignOn:
                        {
                            if (flap.Data.Length == 4)
                            {
                                // MD5 Style Login

                                continue;
                            }

                            var tlvs = OscarUtils.DecodeTlvs(flap.Data[4..]);

                            if (tlvs.Length != 1)
                            {
                                await ProcessChannelOneAuth(session, tlvs);
                            }
                            else
                            {
                                await ProcessCookieAuth(session, tlvs);
                            }
                        }
                        break;

                        case FlapFrameType.Data:
                        {
                            var snacPacket = flap.GetSnac();

                            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"-> {snacPacket}", traceId);

                            var familyProcessor = services.FirstOrDefault(x => x.Family == snacPacket.Family);

                            if (familyProcessor != null)
                            {
                                try
                                {
                                    await familyProcessor.ProcessSnac(session, snacPacket);
                                }
                                catch (Exception ex)
                                {
                                    Log.WriteException(nameof(OscarServer), ex, traceId);
                                }
                            }
                            else
                            {
                                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarServer), $"No handler for SNAC family 0x{snacPacket.Family:X4}", traceId);
                            }
                        }
                        break;

                        case FlapFrameType.SignOff:
                        {
                            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Client signing off: {session.ScreenName ?? "unknown"}", traceId);

                            var blmService = (OscarBuddyListService)services.FirstOrDefault(x => x.Family == OscarBuddyListService.FAMILY_ID);

                            await blmService.ProcessOfflineNotifications(session);

                            session.Client.RawSocket.Close();
                        }
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(OscarServer), ex, traceId);
        }

        Sessions.Remove(session);

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Client disconnected: {session.ScreenName ?? "unknown"}", traceId);

        return null;
    }

    private async Task ProcessCookieAuth(OscarSession session, Tlv[] tlvs)
    {
        var traceId = session.Client.TraceId.ToString();

        var cookie = Encoding.ASCII.GetString(tlvs.GetTlv(0x06).Value);

        var storedSession = Mind.Db.OscarGetSessionByCookie(cookie);

        session.Cookie = storedSession.Cookie;
        session.ScreenName = storedSession.ScreenName;
        session.UserAgent = storedSession.UserAgent;

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Cookie auth successful: {session.ScreenName} ({session.UserAgent})", traceId);

        var genericServiceControls = services.FirstOrDefault(x => x.Family == OscarGenericServiceControls.FAMILY_ID);

        await genericServiceControls.ProcessSnac(session, new Snac(genericServiceControls.Family, OscarGenericServiceControls.SRV_FAMILIES));
    }

    private static async Task ProcessChannelOneAuth(OscarSession session, Tlv[] tlvs)
    {
        var traceId = session.Client.TraceId.ToString();

        session.ScreenName = Encoding.ASCII.GetString(tlvs.GetTlv(0x01).Value);

        if (!Mind.Db.UserExistsByUsername(session.ScreenName))
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Auth failed (unknown user): {session.ScreenName}", traceId);

            await AuthFailedError(session);

            return;
        }

        var user = Mind.Db.UserFetch(session.ScreenName);

        if (OscarUtils.RoastPassword(user.Password).SequenceEqual(tlvs.GetTlv(0x02).Value))
        {
            session.UserAgent = Encoding.ASCII.GetString(tlvs.GetTlv(0x03).Value);

            session.Load(session.ScreenName);

            var serverIP = ((IPEndPoint)session.Client.RawSocket.LocalEndPoint).Address.MapToIPv4();

            var srvCookie = new List<Tlv>
            {
                new Tlv(Tlv.Type_ScreenName, session.ScreenName),
                new Tlv(0x0005, $"{serverIP}:5190"),
                new Tlv(0x0006, session.Cookie)
            };

            var authSuccessFlap = new Flap(FlapFrameType.SignOff)
            {
                Data = srvCookie.EncodeTlvs()
            };

            await session.SendFlap(authSuccessFlap);

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Channel-1 auth successful: {session.ScreenName} ({session.UserAgent})", traceId);

            await session.Client.Stream.FlushAsync();

            session.Client.RawSocket.Close();
        }
        else
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Auth failed (bad password): {session.ScreenName}", traceId);

            await AuthFailedError(session);
        }
    }

    private static async Task AuthFailedError(OscarSession session)
    {
        var authFailed = new List<Tlv>
        {
            new Tlv(Tlv.Type_ScreenName, session.ScreenName),
            new Tlv(0x0004, LoginHelpUrl),
            new Tlv(0x0008, (ushort)OscarAuthError.IncorrectScreenNameOrPassword),
            new Tlv(0x000C, 0x0001)
        };

        var authFailedFlap = new Flap(FlapFrameType.SignOff)
        {
            Data = authFailed.EncodeTlvs()
        };

        await session.SendFlap(authFailedFlap);
    }
}
