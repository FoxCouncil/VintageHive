// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarInvitationService : IOscarService
{
    public const ushort FAMILY_ID = 0x06;

    public const ushort CLI_INVITE_REQUEST = 0x02;
    public const ushort SRV_INVITATION_REPLY = 0x03;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarInvitationService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_INVITE_REQUEST:
            {
                // Parse invitation and forward to target user
                if (snac.RawData.Length > 0)
                {
                    var data = snac.RawData;
                    var readIdx = 0;

                    // Cookie (8 bytes)
                    if (readIdx + 8 <= data.Length)
                    {
                        var cookie = OscarUtils.ToUInt64(data[readIdx..(readIdx + 8)]);
                        readIdx += 8;
                    }

                    // Channel (2 bytes)
                    if (readIdx + 2 <= data.Length)
                    {
                        var channel = OscarUtils.ToUInt16(data[readIdx..(readIdx + 2)]);
                        readIdx += 2;
                    }

                    // Screen name
                    if (readIdx < data.Length)
                    {
                        var screenNameLength = data[readIdx++];

                        if (readIdx + screenNameLength <= data.Length)
                        {
                            var screenName = Encoding.ASCII.GetString(data[readIdx..(readIdx + screenNameLength)]);

                            Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarInvitationService), $"Invitation from {session.ScreenName} to {screenName}", traceId);
                        }
                    }
                }

                var reply = snac.NewReply(FAMILY_ID, SRV_INVITATION_REPLY);

                await session.SendSnac(reply);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarInvitationService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }
}
