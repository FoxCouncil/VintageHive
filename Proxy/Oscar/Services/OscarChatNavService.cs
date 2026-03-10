// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarChatNavService : IOscarService
{
    public const ushort FAMILY_ID = 0x0D;

    public const ushort CLI_SRV_ERROR = 0x01;
    public const ushort CLI_CHATNAV_RIGHTS_REQ = 0x02;
    public const ushort SRV_CHATNAV_INFO = 0x09;
    public const ushort CLI_CREATE_ROOM = 0x08;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarChatNavService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_CHATNAV_RIGHTS_REQ:
            {
                var reply = snac.NewReply(FAMILY_ID, SRV_CHATNAV_INFO);

                // Max concurrent rooms
                reply.WriteTlv(new Tlv(0x02, OscarUtils.GetBytes((ushort)10)));

                // Exchange info (exchange 4 = public rooms)
                var exchangeInfo = new MemoryStream();
                exchangeInfo.Write(OscarUtils.GetBytes((ushort)4));     // Exchange number
                exchangeInfo.Write(OscarUtils.GetBytes((ushort)0x03));  // Class: public
                exchangeInfo.Write(OscarUtils.GetBytes((ushort)0x04));  // Max rooms per exchange

                reply.WriteTlv(new Tlv(0x03, exchangeInfo.ToArray()));

                await session.SendSnac(reply);

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarChatNavService), "Chat nav rights sent", traceId);
            }
            break;

            case CLI_CREATE_ROOM:
            {
                await HandleCreateRoom(session, snac);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarChatNavService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }

    private async Task HandleCreateRoom(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();
        var data = snac.RawData;
        var readIdx = 0;

        // Exchange (2 bytes)
        var exchange = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
        readIdx += 2;

        // Cookie (length-prefixed)
        var cookieLength = data[readIdx++];
        var roomCookie = Encoding.ASCII.GetString(data[readIdx..(readIdx + cookieLength)]);
        readIdx += cookieLength;

        // Instance (2 bytes)
        var instance = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
        readIdx += 2;

        // Detail level (1 byte)
        if (readIdx < data.Length)
        {
            readIdx++;
        }

        // Number of TLVs (2 bytes)
        var roomName = roomCookie;

        if (readIdx + 2 <= data.Length)
        {
            var numTlvs = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
            readIdx += 2;

            if (readIdx < data.Length)
            {
                var tlvs = OscarUtils.DecodeTlvs(data[readIdx..]);
                var nameTlv = tlvs.GetTlv(0x00D3);

                if (nameTlv != null)
                {
                    roomName = Encoding.ASCII.GetString(nameTlv.Value);
                }
            }
        }

        // Find or create the room
        var room = OscarServer.GetOrCreateChatRoom(roomName, exchange, session.ScreenName);

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarChatNavService), $"{session.ScreenName} created/joining chat room \"{roomName}\"", traceId);

        // Generate a chat cookie for this session
        var chatCookie = $"CHAT:{room.Cookie}";

        OscarServer.PendingChatCookies[chatCookie] = room;

        // Send room info back
        var reply = snac.NewReply(FAMILY_ID, SRV_CHATNAV_INFO);

        // Room info TLV block
        var roomInfoData = new MemoryStream();

        // Exchange
        roomInfoData.Write(OscarUtils.GetBytes(exchange));

        // Cookie
        var nameBytes = Encoding.ASCII.GetBytes(roomName);
        roomInfoData.WriteByte((byte)nameBytes.Length);
        roomInfoData.Write(nameBytes);

        // Instance
        roomInfoData.Write(OscarUtils.GetBytes(room.Instance));

        // Detail level
        roomInfoData.WriteByte(0x02);

        // Room TLVs
        var roomTlvData = room.EncodeRoomInfoTlvs();
        roomInfoData.Write(OscarUtils.GetBytes((ushort)6)); // TLV count (approximate)
        roomInfoData.Write(roomTlvData);

        reply.WriteTlv(new Tlv(0x04, roomInfoData.ToArray()));

        await session.SendSnac(reply);

        // Now send a service redirect to the "chat server" (ourselves)
        var serverIP = ((IPEndPoint)session.Client.RawSocket.LocalEndPoint).Address.MapToIPv4();

        var redirectSnac = new Snac(0x01, 0x05); // Generic Service Controls: Redirect

        var redirectTlvs = new List<Tlv>
        {
            new Tlv(0x0D, OscarUtils.GetBytes((ushort)0x0E)),  // Service family (Chat)
            new Tlv(0x05, $"{serverIP}:5190"),                   // Server address
            new Tlv(0x06, chatCookie),                           // Auth cookie
        };

        // Include chat room info in redirect
        redirectSnac.WriteTlvs(redirectTlvs);

        await session.SendSnac(redirectSnac);
    }
}
