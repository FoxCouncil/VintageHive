// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.Rtp;

/// <summary>
/// Constants for RTP (RFC 3550) and RTCP protocols.
/// </summary>
internal static class RtpConstants
{
    // ──────────────────────────────────────────────────────────
    //  RTP header
    // ──────────────────────────────────────────────────────────

    /// <summary>RTP version 2 (RFC 3550).</summary>
    public const int RTP_VERSION = 2;

    /// <summary>Minimum RTP header size in bytes (no CSRC, no extension).</summary>
    public const int RTP_HEADER_MIN = 12;

    // ──────────────────────────────────────────────────────────
    //  RTCP packet types
    // ──────────────────────────────────────────────────────────

    /// <summary>Sender Report.</summary>
    public const int RTCP_SR = 200;

    /// <summary>Receiver Report.</summary>
    public const int RTCP_RR = 201;

    /// <summary>Source Description.</summary>
    public const int RTCP_SDES = 202;

    /// <summary>Goodbye.</summary>
    public const int RTCP_BYE = 203;

    /// <summary>Application-Defined.</summary>
    public const int RTCP_APP = 204;

    // ──────────────────────────────────────────────────────────
    //  Audio payload types (RFC 3551)
    // ──────────────────────────────────────────────────────────

    /// <summary>G.711 µ-law (PCMU), 64 kbps, 8 kHz.</summary>
    public const int PT_PCMU = 0;

    /// <summary>G.723.1, 5.3/6.3 kbps, 8 kHz.</summary>
    public const int PT_G723 = 4;

    /// <summary>G.711 A-law (PCMA), 64 kbps, 8 kHz.</summary>
    public const int PT_PCMA = 8;

    /// <summary>G.729, 8 kbps, 8 kHz.</summary>
    public const int PT_G729 = 18;

    // ──────────────────────────────────────────────────────────
    //  Video payload types (RFC 3551)
    // ──────────────────────────────────────────────────────────

    /// <summary>H.261 video, 90 kHz clock.</summary>
    public const int PT_H261 = 31;

    /// <summary>H.263 video, 90 kHz clock.</summary>
    public const int PT_H263 = 34;

    // ──────────────────────────────────────────────────────────
    //  Port allocation
    // ──────────────────────────────────────────────────────────

    /// <summary>Start of the dynamic/ephemeral port range for RTP relay.</summary>
    public const int RELAY_PORT_RANGE_START = 49152;

    /// <summary>End of the dynamic/ephemeral port range for RTP relay.</summary>
    public const int RELAY_PORT_RANGE_END = 65534;

    /// <summary>Maximum UDP datagram size for RTP (typical MTU minus IP+UDP headers).</summary>
    public const int MAX_RTP_PACKET_SIZE = 1500;

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>Return a friendly name for an RTP payload type.</summary>
    public static string PayloadTypeName(int pt)
    {
        return pt switch
        {
            PT_PCMU => "G.711µ",
            PT_G723 => "G.723.1",
            PT_PCMA => "G.711A",
            PT_G729 => "G.729",
            PT_H261 => "H.261",
            PT_H263 => "H.263",
            _ => $"PT{pt}"
        };
    }

    /// <summary>Return a friendly name for an RTCP packet type.</summary>
    public static string RtcpTypeName(int pt)
    {
        return pt switch
        {
            RTCP_SR => "SR",
            RTCP_RR => "RR",
            RTCP_SDES => "SDES",
            RTCP_BYE => "BYE",
            RTCP_APP => "APP",
            _ => $"RTCP({pt})"
        };
    }
}
