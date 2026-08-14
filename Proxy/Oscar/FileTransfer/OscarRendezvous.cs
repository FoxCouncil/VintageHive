// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Buffers.Binary;
using System.Net.Sockets;

namespace VintageHive.Proxy.Oscar.FileTransfer;

/// <summary>
/// A parsed OSCAR channel-2 ICBM rendezvous fragment (the TLV 0x05 block). Layout, from the ICBM channel-2
/// documentation cross-checked against mk6i's wire/snacs.go: a 2-byte message type, an 8-byte rendezvous
/// cookie (equal to the ICBM cookie), a 16-byte capability CLSID, then the rendezvous TLVs (0x02 RdvIP,
/// 0x03 RequesterIP, 0x04 VerifiedIP, 0x05 Port, 0x10 UseARS, 0x16 IPXOR, 0x17 PortXOR, 0x2711 SvcData...).
/// <para>
/// Only ever acted on for the SendFile capability; every other rendezvous (direct-IM, other services) is left
/// exactly as it arrived. The rewrite points the connectivity TLVs at the relay and sets the UseARS proxy flag
/// so both parties dial the rendezvous proxy, and preserves the cookie so the two proxy connections pair up.
/// </para>
/// </summary>
public sealed class OscarRendezvous
{
    /// <summary>The SendFile capability CLSID, in the dashed uppercase form <see cref="OscarUtils.ToCLSID"/> emits.</summary>
    public const string SendFileCapability = "09461343-4C7F-11D1-8222-444553540000";

    public const ushort MessageTypePropose = 0x0000;

    public const ushort MessageTypeCancel = 0x0001;

    public const ushort MessageTypeAccept = 0x0002;

    // rendezvous TLV tags used by the rewrite (see the class doc for the full table).
    const ushort TlvRdvIP = 0x0002;

    const ushort TlvRequesterIP = 0x0003;

    const ushort TlvVerifiedIP = 0x0004;

    const ushort TlvPort = 0x0005;

    const ushort TlvUseARS = 0x0010;

    const ushort TlvIPXOR = 0x0016;

    const ushort TlvPortXOR = 0x0017;

    const int HeaderLength = 2 + 8 + 16;

    List<Tlv> _tlvs;

    OscarRendezvous() { }

    public ushort MessageType { get; private set; }

    public byte[] Cookie { get; private set; }

    public byte[] Capability { get; private set; }

    public string CapabilityClsid => OscarUtils.ToCLSID(Capability);

    public bool IsSendFile => string.Equals(CapabilityClsid, SendFileCapability, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(byte[] value, out OscarRendezvous rendezvous)
    {
        rendezvous = null;

        if (value == null || value.Length < HeaderLength)
        {
            return false;
        }

        List<Tlv> tlvs;

        var tlvBytes = value[HeaderLength..];

        try
        {
            tlvs = tlvBytes.Length >= 4 ? OscarUtils.DecodeTlvs(tlvBytes).ToList() : new List<Tlv>();
        }
        catch (Exception)
        {
            // A fragment whose trailing block does not cleanly decode is left alone rather than mangled.
            return false;
        }

        rendezvous = new OscarRendezvous
        {
            MessageType = BinaryPrimitives.ReadUInt16BigEndian(value.AsSpan(0, 2)),
            Cookie = value[2..10],
            Capability = value[10..26],
            _tlvs = tlvs,
        };

        return true;
    }

    /// <summary>
    /// Rewrites the connectivity to the relay endpoint and sets the UseARS proxy flag, so both the proposer and
    /// the peer dial the relay and speak the rendezvous-proxy handshake instead of attempting a direct (and,
    /// behind NAT, impossible) peer connection. The XOR-guarded copies (0x16/0x17) are rewritten in lockstep so
    /// a client that validates them against the plain values does not read the rewrite as NAT tampering.
    /// </summary>
    public void PointAtRelay(IPAddress ip, ushort port)
    {
        var ipBytes = ip.MapToIPv4().GetAddressBytes();

        var portBytes = new byte[2];

        BinaryPrimitives.WriteUInt16BigEndian(portBytes, port);

        SetTlv(TlvRdvIP, ipBytes);
        SetTlv(TlvRequesterIP, ipBytes);
        SetTlv(TlvVerifiedIP, ipBytes);
        SetTlv(TlvPort, portBytes);

        // Only rewrite the XOR copies if the client sent them; adding them unbidden would be a shape the
        // proposer never emitted.
        if (HasTlv(TlvIPXOR))
        {
            var xor = new byte[4];

            BinaryPrimitives.WriteUInt32BigEndian(xor, BinaryPrimitives.ReadUInt32BigEndian(ipBytes) ^ 0xFFFFFFFF);

            SetTlv(TlvIPXOR, xor);
        }

        if (HasTlv(TlvPortXOR))
        {
            var xor = new byte[2];

            BinaryPrimitives.WriteUInt16BigEndian(xor, (ushort)(port ^ 0xFFFF));

            SetTlv(TlvPortXOR, xor);
        }

        SetTlv(TlvUseARS, Array.Empty<byte>());
    }

    public byte[] Serialize()
    {
        using var stream = new MemoryStream();

        var header = new byte[2];

        BinaryPrimitives.WriteUInt16BigEndian(header, MessageType);

        stream.Write(header);
        stream.Write(Cookie);
        stream.Write(Capability);

        foreach (var tlv in _tlvs)
        {
            stream.Write(tlv.Encode());
        }

        return stream.ToArray();
    }

    bool HasTlv(ushort type)
    {
        return _tlvs.Exists(t => t.Type == type);
    }

    void SetTlv(ushort type, byte[] value)
    {
        for (var i = 0; i < _tlvs.Count; i++)
        {
            if (_tlvs[i].Type == type)
            {
                _tlvs[i] = new Tlv(type, value);

                return;
            }
        }

        _tlvs.Add(new Tlv(type, value));
    }
}
