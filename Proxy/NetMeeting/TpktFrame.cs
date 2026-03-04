// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting;

/// <summary>
/// TPKT framing (RFC 1006) — 4-byte header over TCP.
/// Used by H.225.0 call signaling (port 1720) and T.120 (port 1503).
///
/// Format: | Version=0x03 | Reserved=0x00 | Length (2 bytes, big-endian) |
/// Length includes the 4-byte header itself.
/// </summary>
internal static class TpktFrame
{
    public const byte Version = 0x03;
    public const int HeaderSize = 4;
    public const int MaxPayloadSize = 65531; // 65535 - 4

    /// <summary>
    /// Read a complete TPKT frame from the stream.
    /// Returns the payload (without the 4-byte header), or null on disconnect.
    /// </summary>
    public static async Task<byte[]> ReadAsync(NetworkStream stream)
    {
        var header = await ReadExactAsync(stream, HeaderSize);
        if (header == null)
        {
            return null;
        }

        if (header[0] != Version)
        {
            throw new InvalidDataException($"Invalid TPKT version: 0x{header[0]:X2} (expected 0x03)");
        }

        var totalLength = (header[2] << 8) | header[3];

        if (totalLength < HeaderSize)
        {
            throw new InvalidDataException($"Invalid TPKT length: {totalLength} (minimum is {HeaderSize})");
        }

        var payloadLength = totalLength - HeaderSize;

        if (payloadLength == 0)
        {
            return Array.Empty<byte>();
        }

        var payload = await ReadExactAsync(stream, payloadLength);
        if (payload == null)
        {
            return null;
        }

        return payload;
    }

    /// <summary>
    /// Write a TPKT frame to the stream.
    /// </summary>
    public static async Task WriteAsync(NetworkStream stream, byte[] payload)
    {
        var totalLength = HeaderSize + payload.Length;

        if (totalLength > 65535)
        {
            throw new ArgumentException($"TPKT payload too large: {payload.Length} bytes");
        }

        var frame = new byte[totalLength];
        frame[0] = Version;
        frame[1] = 0x00; // Reserved
        frame[2] = (byte)(totalLength >> 8);
        frame[3] = (byte)(totalLength & 0xFF);

        Array.Copy(payload, 0, frame, HeaderSize, payload.Length);

        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }

    /// <summary>Build a TPKT frame as a byte array (for testing or buffered writes).</summary>
    public static byte[] Build(byte[] payload)
    {
        var totalLength = HeaderSize + payload.Length;
        var frame = new byte[totalLength];
        frame[0] = Version;
        frame[1] = 0x00;
        frame[2] = (byte)(totalLength >> 8);
        frame[3] = (byte)(totalLength & 0xFF);

        Array.Copy(payload, 0, frame, HeaderSize, payload.Length);

        return frame;
    }

    /// <summary>Parse payload from a complete TPKT frame byte array.</summary>
    public static byte[] ParsePayload(byte[] frame)
    {
        if (frame.Length < HeaderSize)
        {
            throw new ArgumentException("Frame too short for TPKT header");
        }

        if (frame[0] != Version)
        {
            throw new InvalidDataException($"Invalid TPKT version: 0x{frame[0]:X2}");
        }

        var totalLength = (frame[2] << 8) | frame[3];
        var payloadLength = totalLength - HeaderSize;

        var payload = new byte[payloadLength];
        Array.Copy(frame, HeaderSize, payload, 0, payloadLength);

        return payload;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var totalRead = 0;

        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, totalRead, count - totalRead);
            if (read == 0)
            {
                return null;
            }

            totalRead += read;
        }

        return buffer;
    }
}
