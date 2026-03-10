// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarChatService : IOscarService
{
    public const ushort FAMILY_ID = 0x0E;

    public const ushort CLI_SRV_ERROR = 0x01;
    public const ushort CLI_CHAT_RIGHTS_REQ = 0x02;
    public const ushort SRV_CHAT_ROOM_INFO = 0x02;
    public const ushort SRV_USER_JOINED = 0x03;
    public const ushort SRV_USER_LEFT = 0x04;
    public const ushort CLI_CHAT_SEND = 0x05;
    public const ushort SRV_CHAT_MSG = 0x06;
    public const ushort SRV_EVIL_REQUEST = 0x07;
    public const ushort SRV_EVIL_REPLY = 0x08;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarChatService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_CHAT_SEND:
            {
                await HandleChatMessage(session, snac);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarChatService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }

    public async Task JoinRoom(OscarSession session, OscarChatRoom room)
    {
        var traceId = session.Client.TraceId.ToString();

        // Add to room members
        lock (room.Members)
        {
            if (!room.Members.Contains(session))
            {
                room.Members.Add(session);
            }
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarChatService), $"{session.ScreenName} joined chat room \"{room.Name}\"", traceId);

        // Send room info to the joining user
        var roomInfoSnac = new Snac(FAMILY_ID, SRV_CHAT_ROOM_INFO);

        roomInfoSnac.Write(room.EncodeChatRoomInfo());

        // Detail level
        roomInfoSnac.WriteUInt8(0x02);

        // Number of TLVs for the room
        var roomTlvData = room.EncodeRoomInfoTlvs();
        roomInfoSnac.WriteUInt16(0x0006);
        roomInfoSnac.Write(roomTlvData);

        await session.SendSnac(roomInfoSnac);

        // Send existing members to the joining user
        List<OscarSession> currentMembers;

        lock (room.Members)
        {
            currentMembers = room.Members.ToList();
        }

        foreach (var member in currentMembers)
        {
            if (member == session)
            {
                continue;
            }

            var existingMemberSnac = new Snac(FAMILY_ID, SRV_USER_JOINED);

            existingMemberSnac.WriteUInt8((byte)member.ScreenName.Length);
            existingMemberSnac.WriteString(member.ScreenName);
            existingMemberSnac.WriteUInt16(member.WarningLevel);

            var memberTlvs = new List<Tlv>
            {
                new Tlv(0x01, OscarUtils.GetBytes(0)),
                new Tlv(0x06, OscarUtils.GetBytes((uint)member.Status)),
                new Tlv(0x0F, OscarUtils.GetBytes((uint)member.SignOnTime.ToUnixTimeSeconds()))
            };

            existingMemberSnac.WriteUInt16((ushort)memberTlvs.Count);

            foreach (var tlv in memberTlvs)
            {
                existingMemberSnac.Write(tlv.Encode());
            }

            await session.SendSnac(existingMemberSnac);
        }

        // Notify all room members that this user joined
        var joinSnac = new Snac(FAMILY_ID, SRV_USER_JOINED);

        joinSnac.WriteUInt8((byte)session.ScreenName.Length);
        joinSnac.WriteString(session.ScreenName);
        joinSnac.WriteUInt16(session.WarningLevel);

        var joinTlvs = new List<Tlv>
        {
            new Tlv(0x01, OscarUtils.GetBytes(0)),
            new Tlv(0x06, OscarUtils.GetBytes((uint)session.Status)),
            new Tlv(0x0F, OscarUtils.GetBytes((uint)session.SignOnTime.ToUnixTimeSeconds()))
        };

        joinSnac.WriteUInt16((ushort)joinTlvs.Count);

        foreach (var tlv in joinTlvs)
        {
            joinSnac.Write(tlv.Encode());
        }

        foreach (var member in currentMembers)
        {
            if (member == session)
            {
                continue;
            }

            try
            {
                await member.SendSnac(joinSnac);
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(OscarChatService), ex, member.Client.TraceId.ToString());
            }
        }
    }

    public async Task LeaveRoom(OscarSession session, OscarChatRoom room)
    {
        var traceId = session.Client.TraceId.ToString();

        lock (room.Members)
        {
            room.Members.Remove(session);
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarChatService), $"{session.ScreenName} left chat room \"{room.Name}\"", traceId);

        // Notify remaining members
        var leaveSnac = new Snac(FAMILY_ID, SRV_USER_LEFT);

        leaveSnac.WriteUInt8((byte)session.ScreenName.Length);
        leaveSnac.WriteString(session.ScreenName);
        leaveSnac.WriteUInt16(session.WarningLevel);

        var leaveTlvs = new List<Tlv>
        {
            new Tlv(0x01, OscarUtils.GetBytes(0))
        };

        leaveSnac.WriteUInt16((ushort)leaveTlvs.Count);

        foreach (var tlv in leaveTlvs)
        {
            leaveSnac.Write(tlv.Encode());
        }

        List<OscarSession> remainingMembers;

        lock (room.Members)
        {
            remainingMembers = room.Members.ToList();
        }

        foreach (var member in remainingMembers)
        {
            try
            {
                await member.SendSnac(leaveSnac);
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(OscarChatService), ex, member.Client.TraceId.ToString());
            }
        }

        // Clean up empty rooms
        lock (room.Members)
        {
            if (room.Members.Count == 0)
            {
                OscarServer.RemoveChatRoom(room);
            }
        }
    }

    private async Task HandleChatMessage(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        // Find what room this session is in
        var room = OscarServer.GetChatRoomForSession(session);

        if (room == null)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarChatService), "Chat message but session is not in a room", traceId);
            return;
        }

        // Parse message TLVs
        var data = snac.RawData;

        // The message data contains cookies and TLVs
        // Skip cookie (8 bytes) and channel (2 bytes) if present, or just read TLVs
        var msgTlvs = new List<Tlv>();

        if (data.Length >= 4)
        {
            try
            {
                var parsedTlvs = OscarUtils.DecodeTlvs(data);
                msgTlvs.AddRange(parsedTlvs);
            }
            catch
            {
                // If TLV parsing fails, treat entire data as message
            }
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarChatService), $"{session.ScreenName} sent message in \"{room.Name}\"", traceId);

        // Broadcast to all room members (including sender for echo)
        var chatMsgSnac = new Snac(FAMILY_ID, SRV_CHAT_MSG);

        // Cookie
        chatMsgSnac.WriteUInt64((ulong)Random.Shared.NextInt64());

        // Channel
        chatMsgSnac.WriteUInt16(0x0003); // Chat channel

        // Sender TLVs
        var senderTlv = new Tlv(0x03, Encoding.ASCII.GetBytes(session.ScreenName));
        chatMsgSnac.WriteTlv(senderTlv);

        // Forward all message TLVs
        foreach (var tlv in msgTlvs)
        {
            chatMsgSnac.WriteTlv(tlv);
        }

        List<OscarSession> members;

        lock (room.Members)
        {
            members = room.Members.ToList();
        }

        foreach (var member in members)
        {
            try
            {
                await member.SendSnac(chatMsgSnac);
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(OscarChatService), ex, member.Client.TraceId.ToString());
            }
        }
    }
}
