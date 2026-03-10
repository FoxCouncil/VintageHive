// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarPrivacyService : IOscarService
{
    public const ushort FAMILY_ID = 0x09;

    public const ushort CLI_PRIVACY_RIGHTS_REQ = 0x02;
    public const ushort SRV_PRIVACY_RIGHTS_REPLY = 0x03;
    public const ushort CLI_VISIBLE_ADD = 0x05;
    public const ushort CLI_VISIBLE_REMOVE = 0x06;
    public const ushort CLI_INVISIBLE_ADD = 0x07;
    public const ushort CLI_INVISIBLE_REMOVE = 0x08;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarPrivacyService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_PRIVACY_RIGHTS_REQ:
            {
                var replySnac = snac.NewReply(FAMILY_ID, SRV_PRIVACY_RIGHTS_REPLY);

                // Max visible list entries
                replySnac.WriteTlv(new Tlv(0x01, OscarUtils.GetBytes((ushort)200)));
                // Max invisible list entries
                replySnac.WriteTlv(new Tlv(0x02, OscarUtils.GetBytes((ushort)200)));

                await session.SendSnac(replySnac);
            }
            break;

            case CLI_VISIBLE_ADD:
            {
                var screenNames = ParseScreenNameList(snac.RawData);

                foreach (var name in screenNames)
                {
                    if (!session.PermitList.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        session.PermitList.Add(name);
                    }
                }

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarPrivacyService), $"Added {screenNames.Count} to visible list", traceId);
            }
            break;

            case CLI_VISIBLE_REMOVE:
            {
                var screenNames = ParseScreenNameList(snac.RawData);

                foreach (var name in screenNames)
                {
                    session.PermitList.RemoveAll(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
                }

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarPrivacyService), $"Removed {screenNames.Count} from visible list", traceId);
            }
            break;

            case CLI_INVISIBLE_ADD:
            {
                var screenNames = ParseScreenNameList(snac.RawData);

                foreach (var name in screenNames)
                {
                    if (!session.DenyList.Any(d => d.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        session.DenyList.Add(name);
                    }
                }

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarPrivacyService), $"Added {screenNames.Count} to invisible list", traceId);

                // Tell denied users we went "offline" (they can no longer see us)
                foreach (var name in screenNames)
                {
                    var targetSession = OscarServer.Sessions.GetByScreenName(name);

                    if (targetSession != null && targetSession.Buddies.Any(b => b.Equals(session.ScreenName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var offlineSnac = new Snac(OscarBuddyListService.FAMILY_ID, OscarBuddyListService.SRV_USER_OFFLINE);

                        offlineSnac.WriteUInt8((byte)session.ScreenName.Length);
                        offlineSnac.WriteString(session.ScreenName);
                        offlineSnac.WriteUInt16(session.WarningLevel);

                        offlineSnac.WriteUInt16(1);
                        offlineSnac.Write(new Tlv(0x01, OscarUtils.GetBytes(0)).Encode());

                        await targetSession.SendSnac(offlineSnac);
                    }
                }
            }
            break;

            case CLI_INVISIBLE_REMOVE:
            {
                var screenNames = ParseScreenNameList(snac.RawData);

                foreach (var name in screenNames)
                {
                    session.DenyList.RemoveAll(d => d.Equals(name, StringComparison.OrdinalIgnoreCase));
                }

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarPrivacyService), $"Removed {screenNames.Count} from invisible list", traceId);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarPrivacyService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }

    private static List<string> ParseScreenNameList(byte[] data)
    {
        var names = new List<string>();
        var readIdx = 0;

        while (readIdx < data.Length)
        {
            var nameLength = data[readIdx++];
            var name = Encoding.ASCII.GetString(data[readIdx..(readIdx + nameLength)]);
            names.Add(name);
            readIdx += nameLength;
        }

        return names;
    }
}
