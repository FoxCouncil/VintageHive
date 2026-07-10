// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using VintageHive.Network;
using VintageHive.Proxy.Oscar.Services;

namespace VintageHive.Proxy.Oscar;

public class OscarServer : Listener
{
    public const string LoginHelpUrl = "http://" + HiveDomains.Intranet + "/help.html#aim_login";

    public static readonly ConcurrentDictionary<ulong, OscarSession> Sessions = new();

    public static readonly ConcurrentDictionary<string, OscarChatRoom> ChatRooms = new();

    // Keyed by a UNIQUE one-shot join cookie (not the shared room cookie) so concurrent joiners to the same room
    // don't race over a single entry that the first redemption deletes. Each entry carries an expiry so a cookie
    // that is issued but never redeemed cannot linger forever.
    public static readonly ConcurrentDictionary<string, PendingChatCookie> PendingChatCookies = new();

    private static ushort nextChatInstance = 1;

    /// <summary>A pending chat-room join grant: the target room plus the instant its one-shot cookie expires.</summary>
    public readonly record struct PendingChatCookie(OscarChatRoom Room, DateTimeOffset ExpiresAt);

    /// <summary>How long an issued-but-unredeemed chat join cookie stays valid.</summary>
    private static readonly TimeSpan ChatCookieLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Drop any pending chat cookies whose grant window has elapsed. Cheap; called on each sign-on.</summary>
    private static void PurgeExpiredChatCookies()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var kvp in PendingChatCookies)
        {
            if (kvp.Value.ExpiresAt < now)
            {
                PendingChatCookies.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>Issue a fresh one-shot join cookie for a room and register it with an expiry. Returns the cookie.</summary>
    public static string IssueChatCookie(OscarChatRoom room)
    {
        PurgeExpiredChatCookies();

        var cookie = $"CHAT:{room.Cookie}:{Guid.NewGuid():N}";

        PendingChatCookies[cookie] = new PendingChatCookie(room, DateTimeOffset.UtcNow.Add(ChatCookieLifetime));

        return cookie;
    }

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

        OscarChatRoom chatRoom = null;

        try
        {
            Sessions[session.ID] = session;

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Client connected from {connection.RemoteAddress}", traceId);

            await session.SendFlap(HelloFlap);

            session.SentHello = true;

            while (session.Client.IsConnected)
            {
                var flaps = await session.ReceiveFlaps();

                if (flaps == null)
                {
                    // null now means the client disconnected (or a framing desync) - stop, don't hot-spin
                    break;
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
                                var cookieTlv = tlvs.GetTlv(0x06);

                                if (cookieTlv?.Value == null)
                                {
                                    // A single-TLV sign-on that isn't the auth cookie (0x06) used to NRE here and drop
                                    // the socket with no reply. Answer with an auth-failed FLAP instead.
                                    Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), "Sign-on rejected: single TLV carried no auth cookie", traceId);

                                    await AuthFailedError(session);

                                    break;
                                }

                                var cookieValue = Encoding.ASCII.GetString(cookieTlv.Value);

                                PurgeExpiredChatCookies();

                                // Chat-room join cookie? Each joiner holds a unique one-shot cookie, so concurrent
                                // joiners to the same room no longer race over a single shared entry.
                                if (PendingChatCookies.TryRemove(cookieValue, out var pending))
                                {
                                    chatRoom = pending.Room;

                                    // A chat connection authenticates purely by the cookie; give it a placeholder name
                                    // when one wasn't carried over.
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
                            // Client is alive - nothing to do
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
        finally
        {
            // Deterministic teardown: broadcast the departure, then drop the session from the registry.
            // Previously an early abort (hello throw) skipped Remove entirely, leaking a ghost "online" session.
            try
            {
                if (chatRoom != null)
                {
                    var chatService = (OscarChatService)services.FirstOrDefault(x => x.Family == OscarChatService.FAMILY_ID);

                    await chatService.LeaveRoom(session, chatRoom);
                }
                else if (session.Buddies.Count > 0)
                {
                    var blmService = (OscarBuddyListService)services.FirstOrDefault(x => x.Family == OscarBuddyListService.FAMILY_ID);

                    await blmService.ProcessOfflineNotifications(session);
                }
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(OscarServer), ex, traceId);
            }

            Sessions.TryRemove(session.ID, out _);

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Client disconnected: {session.ScreenName ?? "unknown"}", traceId);
        }

        return null;
    }

    private async Task ProcessCookieAuth(OscarSession session, Tlv[] tlvs)
    {
        var traceId = session.Client.TraceId.ToString();

        var cookie = Encoding.ASCII.GetString(tlvs.GetTlv(0x06).Value);

        var storedSession = Mind.Db.OscarGetSessionByCookie(cookie);

        // An unknown/garbage cookie (neither a pending chat cookie nor a stored sign-on cookie) used to NRE on the
        // next line and drop the socket silently. Reply with an auth-failed FLAP instead.
        if (storedSession == null)
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), "Cookie auth failed: unknown session cookie", traceId);

            await AuthFailedError(session);

            return;
        }

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

        var screenNameTlv = tlvs.GetTlv(0x01);
        var passwordTlv = tlvs.GetTlv(0x02);

        if (screenNameTlv?.Value != null)
        {
            session.ScreenName = Encoding.ASCII.GetString(screenNameTlv.Value);
        }

        // A malformed sign-on that omits the screen-name or password TLV used to NRE on GetTlv(...).Value and
        // drop the socket with no reply. Answer with an auth-failed FLAP so the client shows an error instead.
        if (screenNameTlv?.Value == null || passwordTlv?.Value == null)
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Auth failed (malformed sign-on: missing screen-name or password TLV) for '{session.ScreenName}'", traceId);

            await AuthFailedError(session);

            return;
        }

        if (!Mind.Db.UserExistsByUsername(session.ScreenName))
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarServer), $"Auth failed (unknown user): {session.ScreenName}", traceId);

            await AuthFailedError(session);

            return;
        }

        var user = Mind.Db.UserFetch(session.ScreenName);

        if (OscarUtils.RoastPassword(user.Password).SequenceEqual(passwordTlv.Value))
        {
            // The user-agent TLV 0x03 is optional; a client that omits it used to NRE here and drop sign-on.
            session.UserAgent = tlvs.GetTlv(0x03)?.Value is { } uaBytes ? Encoding.ASCII.GetString(uaBytes) : "unknown";

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
            new Tlv(Tlv.Type_ScreenName, session.ScreenName ?? string.Empty),
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
