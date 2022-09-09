using System.Text;
using VintageHive.Proxy.Oscar.Services;

namespace VintageHive.Proxy.Oscar;

internal class IcqUserMetaRequest
{
    public bool IsValid { get; } = true;

    public uint ClientUin { get; }

    public ushort RequestType { get; }
    
    public ushort RequestSubType { get; }

    public ushort Sequence { get; }

    public uint SearchUin { get; }

    public string XmlKey { get; }

    Snac icqMetaRequestSanc;

    public IcqUserMetaRequest(Snac snac)
    {
        icqMetaRequestSanc = snac;

        var tlv = OscarUtils.DecodeTlvs(snac.RawData).GetTlv(0x01);

        var tlvLength = tlv.Value.Length;

        var dataChunkSize = BitConverter.ToUInt16(tlv.Value[..2]);

        if (tlvLength - 2 != dataChunkSize)
        {
            IsValid = false;

            return;
        }

        ClientUin = BitConverter.ToUInt32(tlv.Value[2..6]);

        RequestType = BitConverter.ToUInt16(tlv.Value[6..8]);

        Sequence = BitConverter.ToUInt16(tlv.Value[8..10]);

        if (RequestType == OscarIcqService.CLI_META_INFO_REQ)
        {
            RequestSubType = BitConverter.ToUInt16(tlv.Value[10..12]);

            if (RequestSubType == OscarIcqService.CLI_FULLINFO_REQUEST2 || RequestSubType == OscarIcqService.CLI_FIND_BY_UIN)
            {
                SearchUin = BitConverter.ToUInt32(tlv.Value[12..]);
            }
            else if (RequestSubType == OscarIcqService.CLI_REQ_XML_INFO)
            {
                var stringLength = BitConverter.ToUInt16(tlv.Value[12..14]);

                XmlKey = Encoding.ASCII.GetString(tlv.Value[14..(14 + stringLength - 1)]);
            }
            else if (RequestType == OscarIcqService.META_SET_PERMS_USERINFO)
            {
                var auth = tlv.Value[12];
                var web = tlv.Value[13];
                var dcPerm = tlv.Value[14];
                var huh = tlv.Value[15];
            }
        }
    }
}
