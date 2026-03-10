// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarGenericServiceControls : IOscarService
{
    public const ushort FAMILY_ID = 0x01;

    public const ushort CLI_READY = 0x02;
    public const ushort SRV_FAMILIES = 0x03;
    public const ushort CLI_SERVICExREQ = 0x04;
    public const ushort CLI_RATES_REQUEST = 0x06;
    public const ushort SRV_RATE_LIMIT_INFO = 0x07;
    public const ushort CLI_RATES_ACK = 0x08;
    public const ushort CLI_REQ_SELFINFO = 0x0E;
    public const ushort SRV_ONLINExINFO = 0x0F;
    public const ushort CLI_SETxIDLExTIME = 0x11;
    public const ushort CLI_FAMILIES_VERSIONS = 0x17;
    public const ushort SRV_FAMILIES_VERSIONS = 0x18;
    public const ushort CLI_SETxSTATUS = 0x1E;

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarGenericServiceControls(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (snac.SubType)
        {
            case CLI_READY:
            {
                session.IsReady = true;

                // Deliver offline messages now that client is ready
                await DeliverOfflineMessages(session);
            }
            break;

            // Special Case Used During Protocol Negotiations
            case SRV_FAMILIES:
            {
                var servicesSnac = new Snac(Family, SRV_FAMILIES);

                foreach (KeyValuePair<ushort, ushort> versions in OscarServer.ServiceVersions)
                {
                    var serviceBits = OscarUtils.GetBytes(versions.Key);

                    servicesSnac.Data.Write(serviceBits);
                }

                await session.SendSnac(servicesSnac);
            }
            break;

            case CLI_SERVICExREQ:
            {
                var familyReq = OscarUtils.ToUInt16(snac.RawData);

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarGenericServiceControls), $"Service request for family 0x{familyReq:X4} (not implemented)", traceId);
            }
            break;

            case CLI_RATES_REQUEST:
            {
                var rateInfoSnac = snac.NewReply(Family, SRV_RATE_LIMIT_INFO);

                // Only One Rate Class
                rateInfoSnac.WriteUInt16(1);

                // The Rate Class
                rateInfoSnac.WriteUInt16(1);    // ID
                rateInfoSnac.WriteUInt32(80);   // Window Size
                rateInfoSnac.WriteUInt32(2500); // Clear Level
                rateInfoSnac.WriteUInt32(2000); // Alert Level
                rateInfoSnac.WriteUInt32(1500); // Limit Level
                rateInfoSnac.WriteUInt32(800);  // Disconnect Level
                rateInfoSnac.WriteUInt32(3400); // Current Level
                rateInfoSnac.WriteUInt32(6000); // Max Level (^.^)
                rateInfoSnac.WriteUInt32(0);    // Last SNAC Time
                rateInfoSnac.WriteUInt8(0);     // State

                // Associate Rate Class To Services
                rateInfoSnac.WriteUInt16(1);

                rateInfoSnac.WriteUInt16((ushort)(OscarServer.ServiceVersions.Count * 0x21));

                foreach (ushort serviceFamily in OscarServer.ServiceVersions.Keys)
                {
                    for (ushort subType = 0; subType < 0x21; subType++)
                    {
                        rateInfoSnac.WriteUInt16(serviceFamily);
                        rateInfoSnac.WriteUInt16(subType);
                    }
                }

                await session.SendSnac(rateInfoSnac);
            }
            break;

            case CLI_RATES_ACK:
            {
                var value = OscarUtils.ToUInt16(snac.RawData);

                if (value != 1)
                {
                    throw new ApplicationException("CLI_RATES_ACK ID did not match!");
                }
            }
            break;

            case CLI_REQ_SELFINFO:
            {
                var infoReplySnac = snac.NewReply(Family, SRV_ONLINExINFO);

                infoReplySnac.WriteUInt8((byte)session.ScreenName.Length);
                infoReplySnac.WriteString(session.ScreenName);
                infoReplySnac.WriteUInt16(session.WarningLevel);

                session.Status = OscarSessionOnlineStatus.Online;

                var tlvs = new List<Tlv>
                {
                    new Tlv(0x01, OscarUtils.GetBytes(0)),
                    new Tlv(0x06, OscarUtils.GetBytes((uint)session.Status)),
                    new Tlv(0x0F, OscarUtils.GetBytes((uint)session.SignOnTime.ToUnixTimeSeconds())),
                    new Tlv(0x03, OscarUtils.GetBytes((uint)OscarServer.ServerTime.ToUnixTimeSeconds())),
                    new Tlv(0x1E, OscarUtils.GetBytes((uint)0)),
                    new Tlv(0x05, OscarUtils.GetBytes((uint)session.SignOnTime.ToUnixTimeSeconds()))
                };

                infoReplySnac.WriteUInt16((ushort)tlvs.Count);

                foreach (Tlv tlv in tlvs)
                {
                    infoReplySnac.Write(tlv.Encode());
                }

                await session.SendSnac(infoReplySnac);
            }
            break;

            case CLI_SETxIDLExTIME:
            {
                var idleTime = OscarUtils.ToUInt32(snac.RawData);

                session.SetIdle(idleTime);

                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarGenericServiceControls), $"Idle time set: {idleTime}s", traceId);

                // Broadcast status change to watchers
                await session.BroadcastStatusToWatchers();
            }
            break;

            case CLI_FAMILIES_VERSIONS:
            {
                var data = snac.RawData;

                Dictionary<ushort, ushort> clientRequestedServiceVersions = ProcessClientRequestedServiceVersion(data);

                var servicesSnac = snac.NewReply(Family, SRV_FAMILIES_VERSIONS);

                foreach (KeyValuePair<ushort, ushort> versions in OscarServer.ServiceVersions)
                {
                    if (!clientRequestedServiceVersions.ContainsKey(versions.Key))
                    {
                        continue;
                    }

                    servicesSnac.Data.Write(OscarUtils.GetBytes(versions.Key));  // Service Family
                    servicesSnac.Data.Write(OscarUtils.GetBytes(versions.Value));// Service Family Version
                }

                await session.SendSnac(servicesSnac);
            }
            break;

            case CLI_SETxSTATUS:
            {
                var tlvs = OscarUtils.DecodeTlvs(snac.RawData);

                var statusTlv = tlvs.GetTlv(0x06);

                if (statusTlv != null && statusTlv.Value.Length >= 4)
                {
                    var statusValue = OscarUtils.ToUInt32(statusTlv.Value);

                    var newStatus = (OscarSessionOnlineStatus)(statusValue & 0xFFFF);

                    session.Status = newStatus;
                    session.Save();

                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarGenericServiceControls), $"Status updated to {newStatus}", traceId);

                    // Broadcast status change to watchers
                    await session.BroadcastStatusToWatchers();
                }
                else
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarGenericServiceControls), $"Status update ({tlvs.Length} TLVs)", traceId);
                }
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarGenericServiceControls), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", traceId);
            }
            break;
        }
    }

    private async Task DeliverOfflineMessages(OscarSession session)
    {
        var messages = Mind.Db.OscarGetOfflineMessages(session.ScreenName);

        if (messages.Count == 0)
        {
            return;
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarGenericServiceControls), $"Delivering {messages.Count} offline message(s) to {session.ScreenName}", session.Client.TraceId.ToString());

        foreach (var msg in messages)
        {
            var deliverSnac = new Snac(OscarIcbmService.FAMILY_ID, OscarIcbmService.SRV_CLIENT_ICBM);

            // Generate a cookie for this delivery
            deliverSnac.WriteUInt64((ulong)Random.Shared.NextInt64());
            deliverSnac.WriteUInt16(msg.Channel);

            deliverSnac.WriteUInt8((byte)msg.FromScreenName.Length);
            deliverSnac.WriteString(msg.FromScreenName);

            deliverSnac.WriteUInt16(0); // Warning level

            var senderTlvs = new List<Tlv>
            {
                new Tlv(0x01, OscarUtils.GetBytes(0)),
                new Tlv(0x06, OscarUtils.GetBytes((uint)OscarSessionOnlineStatus.Online)),
                new Tlv(0x0F, OscarUtils.GetBytes((uint)0)),
                new Tlv(0x03, OscarUtils.GetBytes((uint)((DateTimeOffset)msg.Timestamp).ToUnixTimeSeconds()))
            };

            deliverSnac.WriteUInt16((ushort)senderTlvs.Count);

            foreach (Tlv tlv in senderTlvs)
            {
                deliverSnac.Write(tlv.Encode());
            }

            // Write the stored message data (TLVs from original message)
            deliverSnac.Write(msg.MessageData);

            await session.SendSnac(deliverSnac);
        }

        Mind.Db.OscarDeleteOfflineMessages(session.ScreenName);
    }

    private Dictionary<ushort, ushort> ProcessClientRequestedServiceVersion(byte[] data)
    {
        if (data.Length % 4 != 0)
        {
            throw new ApplicationException("ClientRequestServiceVerion packet was not divisible by 4!");
        }

        var requestedServiceVersions = new Dictionary<ushort, ushort>();

        for (int idx = 0; idx < data.Length; idx += 4)
        {
            requestedServiceVersions.Add(OscarUtils.ToUInt16(data[idx..(idx + 2)]), OscarUtils.ToUInt16(data[(idx + 2)..(idx + 4)]));
        }

        return requestedServiceVersions;
    }
}
