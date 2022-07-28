using System.Diagnostics;

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

    public static readonly Dictionary<ushort, ushort> ServiceVersions = new()
    {
        { 0x01, 0x03 }, // Generic Service Controls
        { 0x02, 0x01 }, // Location Services
        { 0x03, 0x01 }, // Buddy List Management Service
        { 0x04, 0x01 }, // ICBM (messages) Service
        { 0x17, 0x01 }  // Authorization/Registration Service
    };

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarGenericServiceControls(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        switch (snac.SubType)
        {
            case CLI_READY:
            {
                session.IsReady = true;
            }
            break;

            // Special Case Used During Protocol Negotiations
            case SRV_FAMILIES:
            {
                var servicesSnac = new Snac(Family, SRV_FAMILIES);

                foreach (KeyValuePair<ushort, ushort> versions in ServiceVersions)
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

                // TODO: Be more specific about which types to rate limit
                rateInfoSnac.WriteUInt16((ushort)(ServiceVersions.Count * 0x21));

                foreach (ushort serviceFamily in ServiceVersions.Keys)
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
                infoReplySnac.WriteUInt16(0); // Warning Level

                session.Status = OscarSessionOnlineStatus.Online;

                var tlvs = new List<Tlv>
                {
                    new Tlv(0x01, OscarUtils.GetBytes(0)),
                    new Tlv(0x06, OscarUtils.GetBytes((uint)session.Status)),
                    new Tlv(0x0F, OscarUtils.GetBytes((uint)0)),
                    new Tlv(0x03, OscarUtils.GetBytes((uint)420)),
                    new Tlv(0x1E, OscarUtils.GetBytes((uint)0)),
                    new Tlv(0x05, OscarUtils.GetBytes((uint)420))
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
            }
            break;

            case CLI_FAMILIES_VERSIONS:
            {
                var data = snac.RawData;

                Dictionary<ushort, ushort> clientRequestedServiceVersions = ProcessClientRequestedServiceVersion(data);

                var servicesSnac = snac.NewReply(Family, SRV_FAMILIES_VERSIONS);

                foreach (KeyValuePair<ushort, ushort> versions in ServiceVersions)
                {
                    if (!clientRequestedServiceVersions.ContainsKey(versions.Key))
                    {
                        // throw new ApplicationException("Client is requesting SNAC families we don't have!");
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
            }
            break;

            default:
            {
                Debugger.Break();
            }
            break;
        }
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
