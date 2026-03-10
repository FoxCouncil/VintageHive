// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarBartService : IOscarService
{
    public const ushort FAMILY_ID = 0x10;

    public const ushort CLI_BART_UPLOAD = 0x02;
    public const ushort SRV_BART_UPLOAD_REPLY = 0x03;
    public const ushort CLI_BART_QUERY = 0x04;
    public const ushort SRV_BART_RESPONSE = 0x05;
    public const ushort CLI_BART_QUERY2 = 0x06;
    public const ushort SRV_BART_RESPONSE2 = 0x07;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarBartService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_BART_UPLOAD:
            {
                // Client wants to upload an icon — acknowledge but we don't store it yet
                var reply = snac.NewReply(FAMILY_ID, SRV_BART_UPLOAD_REPLY);

                reply.WriteUInt16(0x0004); // Status: success
                reply.WriteUInt16(0x0001); // BART type: buddy icon
                reply.WriteUInt8(0x00);    // Hash length = 0 (no icon stored)

                await session.SendSnac(reply);

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarBartService), $"Icon upload from {session.ScreenName} (acknowledged, not stored)", traceId);
            }
            break;

            case CLI_BART_QUERY:
            case CLI_BART_QUERY2:
            {
                // Client wants a buddy icon — we don't have any, send empty response
                var replySubtype = snac.SubType == CLI_BART_QUERY ? SRV_BART_RESPONSE : SRV_BART_RESPONSE2;
                var reply = snac.NewReply(FAMILY_ID, replySubtype);

                // Parse the query to get screen name
                if (snac.RawData.Length > 0)
                {
                    var screenNameLength = snac.RawData[0];

                    if (screenNameLength > 0 && 1 + screenNameLength <= snac.RawData.Length)
                    {
                        var screenName = Encoding.ASCII.GetString(snac.RawData[1..(1 + screenNameLength)]);

                        reply.WriteUInt8(screenNameLength);
                        reply.WriteString(screenName);
                    }
                    else
                    {
                        reply.WriteUInt8(0);
                    }
                }

                reply.WriteUInt16(0x0001); // BART type: buddy icon
                reply.WriteUInt8(0x04);    // Flags: no icon available
                reply.WriteUInt8(0x00);    // Hash length
                reply.WriteUInt16(0x0000); // Data length

                await session.SendSnac(reply);

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarBartService), "Icon query (no icons available)", traceId);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarBartService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }
}
