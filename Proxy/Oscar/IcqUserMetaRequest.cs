// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

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

    private byte[] extraData;

    Snac icqMetaRequestSanc;

    public IcqUserMetaRequest(Snac snac)
    {
        icqMetaRequestSanc = snac;

        var tlv = OscarUtils.DecodeTlvs(snac.RawData).GetTlv(0x01);

        // A meta request must carry TLV 0x01 with at least the 2-byte chunk-size prefix. An absent TLV
        // (GetTlv returns null) or an empty value is a malformed request from a hostile/broken client,
        // not a reason to dereference null or slice past the buffer. The IsValid flag exists for this.
        if (tlv?.Value == null || tlv.Value.Length < 2)
        {
            IsValid = false;

            return;
        }

        var value = tlv.Value;

        var dataChunkSize = BitConverter.ToUInt16(value[..2]);

        // The declared chunk size must match what is present, and the block must be long enough for the
        // fixed UIN + request-type + sequence header (10 bytes including the 2-byte prefix).
        if (value.Length - 2 != dataChunkSize || value.Length < 10)
        {
            IsValid = false;

            return;
        }

        ClientUin = BitConverter.ToUInt32(value[2..6]);

        RequestType = BitConverter.ToUInt16(value[6..8]);

        Sequence = BitConverter.ToUInt16(value[8..10]);

        if (RequestType == OscarIcqService.CLI_META_INFO_REQ)
        {
            // The meta sub-type is the next 2 bytes.
            if (value.Length < 12)
            {
                IsValid = false;

                return;
            }

            RequestSubType = BitConverter.ToUInt16(value[10..12]);

            if (RequestSubType == OscarIcqService.CLI_FULLINFO_REQUEST2 || RequestSubType == OscarIcqService.CLI_FIND_BY_UIN)
            {
                if (value.Length < 16)
                {
                    IsValid = false;

                    return;
                }

                SearchUin = BitConverter.ToUInt32(value[12..16]);
            }
            else if (RequestSubType == OscarIcqService.CLI_REQ_XML_INFO)
            {
                if (value.Length < 14)
                {
                    IsValid = false;

                    return;
                }

                // The wire length counts a trailing NUL, so the key is (length - 1) bytes. A declared
                // length that runs past the payload is malformed, not an out-of-range read.
                var stringLength = BitConverter.ToUInt16(value[12..14]);
                var charCount = stringLength > 0 ? stringLength - 1 : 0;
                var end = 14 + charCount;

                if (end > value.Length)
                {
                    IsValid = false;

                    return;
                }

                XmlKey = Encoding.ASCII.GetString(value[14..end]);
            }
            else if (RequestSubType == OscarIcqService.META_SET_PERMS_USERINFO)
            {
                if (value.Length > 15)
                {
                    var auth = value[12];
                    var web = value[13];
                    var dcPerm = value[14];
                    var flags = value[15];
                }
            }
            else
            {
                // For set-info subtypes, store extra data after the subtype
                if (value.Length > 12)
                {
                    extraData = value[12..];
                }
            }
        }
    }

    public byte[] GetExtraData()
    {
        return extraData;
    }
}
