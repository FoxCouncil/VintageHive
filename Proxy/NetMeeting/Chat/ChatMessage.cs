// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;

namespace VintageHive.Proxy.NetMeeting.Chat;

/// <summary>
/// NetMeeting Chat Protocol message codec.
///
/// Per MS-MNPR section 2.2.3, a chat packet is:
///   Offset 0x00 (1 byte)  — header length, MUST be 0x08
///   Offset 0x01 (7 bytes) — reserved, MUST be all zeros
///   Offset 0x08 (var)     — UTF-16LE null-terminated chat text
///
/// Chat does NOT use T.134 or the OMNET Object Manager workset model.
/// Messages ride directly on MCS SendDataRequest/SendDataIndication (T.125)
/// within the T.120 stack: TCP:1503 → TPKT → X.224 → MCS → ChatPacket.
///
/// All multi-byte values are little-endian per MS-MNPR.
/// </summary>
internal static class ChatMessage
{
    /// <summary>Fixed header length byte value.</summary>
    public const byte HEADER_LENGTH = 0x08;

    /// <summary>Total size of the fixed header (length byte + 7 reserved bytes).</summary>
    public const int HEADER_SIZE = 8;

    /// <summary>
    /// Encode a chat text message into the NetMeeting wire format.
    /// </summary>
    /// <param name="text">The chat message text. Must not be null.</param>
    /// <returns>Complete chat packet ready for MCS SendData.</returns>
    public static byte[] Encode(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        // UTF-16LE encoding of the text + null terminator (2 bytes)
        var textBytes = Encoding.Unicode.GetBytes(text);
        var packet = new byte[HEADER_SIZE + textBytes.Length + 2]; // +2 for null terminator

        // Header: 0x08 followed by 7 zero bytes
        packet[0] = HEADER_LENGTH;
        // Bytes 1-7 are already zero from array initialization

        // Chat text (UTF-16LE)
        Array.Copy(textBytes, 0, packet, HEADER_SIZE, textBytes.Length);

        // Null terminator (2 zero bytes for UTF-16LE) — already zero from array init

        return packet;
    }

    /// <summary>
    /// Decode a chat text message from the NetMeeting wire format.
    /// </summary>
    /// <param name="packet">Raw chat packet from MCS SendData userData.</param>
    /// <returns>The decoded chat text, or null if the packet is invalid.</returns>
    public static string Decode(byte[] packet)
    {
        if (packet == null || packet.Length < HEADER_SIZE)
        {
            return null;
        }

        // Validate header length byte
        if (packet[0] != HEADER_LENGTH)
        {
            return null;
        }

        // Validate reserved bytes are zero
        for (var i = 1; i < HEADER_SIZE; i++)
        {
            if (packet[i] != 0)
            {
                return null;
            }
        }

        // No text data after header
        if (packet.Length <= HEADER_SIZE)
        {
            return string.Empty;
        }

        // Decode UTF-16LE text, stripping the null terminator
        var textLength = packet.Length - HEADER_SIZE;

        // Remove trailing null terminator if present (2 zero bytes)
        if (textLength >= 2 &&
            packet[packet.Length - 2] == 0 &&
            packet[packet.Length - 1] == 0)
        {
            textLength -= 2;
        }

        if (textLength <= 0)
        {
            return string.Empty;
        }

        return Encoding.Unicode.GetString(packet, HEADER_SIZE, textLength);
    }

    /// <summary>
    /// Check if a byte array looks like a NetMeeting chat packet.
    /// Performs a quick heuristic check on the header.
    /// </summary>
    /// <param name="data">MCS SendData userData to check.</param>
    /// <returns>True if the data has a valid chat header.</returns>
    public static bool IsChatPacket(byte[] data)
    {
        if (data == null || data.Length < HEADER_SIZE)
        {
            return false;
        }

        if (data[0] != HEADER_LENGTH)
        {
            return false;
        }

        // Check reserved bytes
        for (var i = 1; i < HEADER_SIZE; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Get the byte length of a chat packet for a given text message,
    /// without actually encoding it.
    /// </summary>
    /// <param name="text">The chat message text.</param>
    /// <returns>Total packet length in bytes.</returns>
    public static int GetPacketLength(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        // Header (8) + UTF-16LE text (2 bytes per char) + null terminator (2)
        return HEADER_SIZE + (text.Length * 2) + 2;
    }
}
