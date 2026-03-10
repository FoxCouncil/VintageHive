// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Processors.LocalServer.Streaming;

/// <summary>
/// Codec type for RealAudio streaming. Determines the encoding pipeline:
/// Cook uses our CookEncoder; Ra144 uses FFmpeg's built-in ra_144 encoder.
/// </summary>
internal enum RealCodecType
{
    /// <summary>Cook codec — our CookEncoder produces raw cook frames, we build RM container.</summary>
    Cook,

    /// <summary>RealAudio 14.4 (lpcJ) — FFmpeg encodes and produces complete RM output.</summary>
    Ra144,
}

/// <summary>Defines a codec configuration for RealAudio streaming, selected based on client bandwidth reported in the PNA handshake (CHUNK_BANDWIDTH 0x0005).</summary>
internal class RealCodecProfile
{
    public RealCodecType CodecType { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public int Bitrate { get; }
    public string Key { get; }

    private RealCodecProfile(RealCodecType codecType, int sampleRate, int channels, int bitrate)
    {
        CodecType = codecType;
        SampleRate = sampleRate;
        Channels = channels;
        Bitrate = bitrate;
        Key = codecType == RealCodecType.Ra144 ? "ra144" : $"cook-{sampleRate}-{channels}-{bitrate}";
    }

    // Predefined profiles ordered by bitrate (ascending)
    public static readonly RealCodecProfile Ra144 = new(RealCodecType.Ra144, 8000, 1, 14400);
    public static readonly RealCodecProfile CookMono8k = new(RealCodecType.Cook, 8000, 1, 6000);
    public static readonly RealCodecProfile CookStereo11k = new(RealCodecType.Cook, 11025, 2, 19000);
    public static readonly RealCodecProfile CookStereo22k = new(RealCodecType.Cook, 22050, 2, 32000);
    public static readonly RealCodecProfile CookStereo44k = new(RealCodecType.Cook, 44100, 2, 64000);

    /// <summary>
    /// Select the best codec profile for the client's reported bandwidth.
    /// Bandwidth is in bytes/sec from PNA CHUNK_BANDWIDTH (0x0005).
    /// Usable audio bitrate is approximately bandwidth * 8 * 0.85
    /// (85% of raw bandwidth, leaving headroom for RM headers and protocol overhead).
    /// </summary>
    public static RealCodecProfile SelectForBandwidth(uint bandwidthBytesPerSec)
    {
        // 0 = unknown/not reported — default to our proven 11025/2/19kbps
        if (bandwidthBytesPerSec == 0)
        {
            return CookStereo11k;
        }

        // Usable audio kbps (with protocol overhead margin)
        double usableKbps = bandwidthBytesPerSec * 8.0 * 0.85 / 1000.0;

        // 14.4k modem: ~1800 bytes/sec → ~12 kbps usable
        if (usableKbps < 15)
        {
            return Ra144;
        }

        // 28.8k modem: ~3600 bytes/sec → ~24 kbps usable
        if (usableKbps < 25)
        {
            return CookStereo11k;
        }

        // 56k modem / ISDN: ~7000 bytes/sec → ~48 kbps usable
        if (usableKbps < 50)
        {
            return CookStereo22k;
        }

        // Broadband: > 50 kbps usable
        return CookStereo44k;
    }

    public override string ToString() => $"{CodecType} {SampleRate}Hz {Channels}ch {Bitrate / 1000}kbps";
}
