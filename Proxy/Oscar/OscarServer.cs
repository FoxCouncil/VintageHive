// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using VintageHive.Network;
using VintageHive.Proxy.Oscar.Services;

namespace VintageHive.Proxy.Oscar;

public class OscarServer : Listener
{
    public const string LoginHelpUrl = "http://hive.com/help.html#aim_login";

    public static readonly List<OscarSession> Sessions = new();

    public static readonly ConcurrentDictionary<string, OscarChatRoom> ChatRooms = new();

    public static readonly ConcurrentDictionary<string, OscarChatRoom> PendingChatCookies = new();

    private static ushort nextChatInstance = 1;

    public static DateTimeOffset ServerTime => DateTime.Now;

    public static readonly Dictionary<ushort, ushort> ServiceVersions = new()
    {
        { OscarGenericServiceControls.FAMILY_ID, 0x03 }, // Generic Service Controls
        { OscarLocationService.FAMILY_ID, 0x01 },        // Location Services
        { OscarBuddyListService.FAMILY_ID, 0x01 },       // Buddy List Management Service
        { OscarIcbmService.FAMILY_ID, 0x01 },             // ICBM (messages) Service
        { OscarInvitationService.FAMILY_ID, 0x01 },       // Invitation Service
        { OscarPrivacyService.FAMILY_ID, 0x01 },          // Permit/Deny settings
        { OscarUserLookupService.FAMILY_ID, 0x01 },       // User Lookup
        { OscarUsageStatsServices.FAMILY_ID, 0x01 },      // Usage Stats
        { OscarChatNavService.FAMILY_ID, 0x01 },          // Chat Navigation
        { OscarChatService.FAMILY_ID, 0x01 },             // Chat Rooms
        { OscarBartService.FAMILY_ID, 0x01 },             // BART (Buddy Art/Icons)
        { OscarSsiService.FAMILY_ID, 0x03 },              // SSI (Server-Side Information)
        { OscarIcqService.FAMILY_ID, 0x01 },              // ICQ specific services
        { OscarAuthorizationService.FAMILY_ID, 0x01 }     // Authorization/Registration
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
            new OscarChatNavService(this),
            new OscarChatService(this),
            new OscarBartService(this),
            new OscarSsiService(this),
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

        OscarChatRoom chatRoom = null;

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
                                var cookieValue = Encoding.ASCII.GetString(tlvs.GetTlv(0x06).Value);

                                // Check if this is a chat room cookie
                                if (PendingChatCookies.TryRemove(cookieValue, out var pendingRoom))
                                {
                                    chatRoom = pendingRoom;

                                    // Look up the original session for this user
                                    var originalSession = Sessions.FirstOrDefault(s => s != session && s.ScreenName != null && PendingChatCookies.Values.Any(r => r == pendingRoom));

                                    // Try to find the user who requested this room
                                    // The cookie format is "CHAT:{roomCookie}", extract room cookie
                                    if (cookieValue.StartsWith("CHAT:"))
                                    {
                                        var roomCookieId = cookieValue[5..];
                                        var room = ChatRooms.Values.FirstOrDefault(r => r.Cookie == roomCookieId);

                                        if (room != null)
                                        {
                                            chatRoom = room;
                                        }
                                    }

                                    // Set a screen name from the pending chat context (best effort)
                                    if (session.ScreenName == null)
                                    {
                                        session.ScreenName = "ChatUser" + session.ID;
                                    }

                                    Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Chat connection for room \"{chatRoom.Name}\" from {session.ScreenName}", traceId);

                                    // Send chat-specific families
                                    var chatFamiliesSnac = new Snac(OscarGenericServiceControls.FAMILY_ID, OscarGenericServiceControls.SRV_FAMILIES);

                                    chatFamiliesSnac.Data.Write(OscarUtils.GetBytes(OscarGenericServiceControls.FAMILY_ID));
                                    chatFamiliesSnac.Data.Write(OscarUtils.GetBytes(OscarChatService.FAMILY_ID));

                                    await session.SendSnac(chatFamiliesSnac);

                                    // Join the room
                                    var chatService = (OscarChatService)services.FirstOrDefault(x => x.Family == OscarChatService.FAMILY_ID);

                                    await chatService.JoinRoom(session, chatRoom);
                                }
                                else
                                {
                                    await ProcessCookieAuth(session, tlvs);
                                }
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

                        case FlapFrameType.KeepAlive:
                        {
                            // Client is alive — nothing to do
                            Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarServer), "KeepAlive received", traceId);
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

        // Leave chat room if in one
        if (chatRoom != null)
        {
            var chatService = (OscarChatService)services.FirstOrDefault(x => x.Family == OscarChatService.FAMILY_ID);

            await chatService.LeaveRoom(session, chatRoom);
        }

        // Send offline notifications for normal sessions
        if (chatRoom == null && session.Buddies.Count > 0)
        {
            try
            {
                var blmService = (OscarBuddyListService)services.FirstOrDefault(x => x.Family == OscarBuddyListService.FAMILY_ID);

                await blmService.ProcessOfflineNotifications(session);
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(OscarServer), ex, traceId);
            }
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
        session.SignOnTime = DateTimeOffset.UtcNow;

        // Ensure user profile exists in the database
        Mind.Db.OscarEnsureProfileExists(session.ScreenName);

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

            // Ensure user profile exists in the database
            Mind.Db.OscarEnsureProfileExists(session.ScreenName);

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

    #region Chat Room Management

    public static OscarChatRoom GetOrCreateChatRoom(string name, ushort exchange, string createdBy)
    {
        var key = $"{exchange}:{name}".ToLowerInvariant();

        return ChatRooms.GetOrAdd(key, _ => new OscarChatRoom
        {
            Name = name,
            Exchange = exchange,
            Instance = nextChatInstance++,
            Cookie = Guid.NewGuid().ToString("N").ToUpper(),
            CreatedBy = createdBy
        });
    }

    public static OscarChatRoom GetChatRoomForSession(OscarSession session)
    {
        foreach (var room in ChatRooms.Values)
        {
            lock (room.Members)
            {
                if (room.Members.Contains(session))
                {
                    return room;
                }
            }
        }

        return null;
    }

    public static void RemoveChatRoom(OscarChatRoom room)
    {
        var key = $"{room.Exchange}:{room.Name}".ToLowerInvariant();

        ChatRooms.TryRemove(key, out _);

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Chat room \"{room.Name}\" removed (empty)", "");
    }

    #endregion
}
