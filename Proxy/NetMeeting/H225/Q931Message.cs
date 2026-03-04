// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// ITU-T Q.931 message parser and builder for H.225.0 call signaling.
/// Q.931 messages are carried inside TPKT frames over TCP port 1720.
///
/// Format:
///   Protocol Discriminator (0x08) | Call Ref Length (1 byte) |
///   Call Reference Value (2 bytes: flag + 15-bit CRV) |
///   Message Type (1 byte) | Information Elements (variable)
/// </summary>
internal class Q931Message
{
    // ──────────────────────────────────────────────────────────
    //  Q.931 constants
    // ──────────────────────────────────────────────────────────

    public const byte ProtocolDiscriminator = 0x08;

    // Message types
    public const byte MSG_ALERTING = 0x01;
    public const byte MSG_CALL_PROCEEDING = 0x02;
    public const byte MSG_SETUP = 0x05;
    public const byte MSG_CONNECT = 0x07;
    public const byte MSG_RELEASE_COMPLETE = 0x5A;
    public const byte MSG_FACILITY = 0x62;

    // Information Element tags
    public const byte IE_BEARER_CAPABILITY = 0x04;
    public const byte IE_CAUSE = 0x08;
    public const byte IE_DISPLAY = 0x28;
    public const byte IE_USER_USER = 0x7E;

    /// <summary>H.225.0 User-User IE protocol discriminator.</summary>
    public const byte UUIE_PROTOCOL_DISCRIMINATOR = 0x05;

    // ──────────────────────────────────────────────────────────
    //  Message properties
    // ──────────────────────────────────────────────────────────

    /// <summary>Call reference value (15-bit, 0-32767).</summary>
    public int CallReference { get; set; }

    /// <summary>Call reference flag: false=originator side, true=destination side.</summary>
    public bool CallReferenceFlag { get; set; }

    /// <summary>Q.931 message type.</summary>
    public byte MessageType { get; set; }

    /// <summary>Information elements, keyed by IE tag.</summary>
    public Dictionary<byte, byte[]> InformationElements { get; } = new();

    // ──────────────────────────────────────────────────────────
    //  Convenience accessors
    // ──────────────────────────────────────────────────────────

    /// <summary>Get the User-User IE payload (H.225.0 PER data after protocol discriminator).</summary>
    public byte[] GetUuieData()
    {
        if (!InformationElements.TryGetValue(IE_USER_USER, out var uuie))
        {
            return null;
        }

        if (uuie.Length < 1 || uuie[0] != UUIE_PROTOCOL_DISCRIMINATOR)
        {
            return null;
        }

        // Strip the protocol discriminator byte
        var data = new byte[uuie.Length - 1];
        Array.Copy(uuie, 1, data, 0, data.Length);
        return data;
    }

    /// <summary>Set the User-User IE with H.225.0 PER data.</summary>
    public void SetUuieData(byte[] perData)
    {
        var uuie = new byte[perData.Length + 1];
        uuie[0] = UUIE_PROTOCOL_DISCRIMINATOR;
        Array.Copy(perData, 0, uuie, 1, perData.Length);
        InformationElements[IE_USER_USER] = uuie;
    }

    /// <summary>Get display text from Display IE, or null.</summary>
    public string GetDisplay()
    {
        if (!InformationElements.TryGetValue(IE_DISPLAY, out var data))
        {
            return null;
        }

        return System.Text.Encoding.ASCII.GetString(data);
    }

    /// <summary>Set the Display IE.</summary>
    public void SetDisplay(string text)
    {
        InformationElements[IE_DISPLAY] = System.Text.Encoding.ASCII.GetBytes(text);
    }

    // ──────────────────────────────────────────────────────────
    //  Parse from raw Q.931 bytes (inside TPKT payload)
    // ──────────────────────────────────────────────────────────

    public static Q931Message Parse(byte[] data)
    {
        if (data.Length < 4)
        {
            throw new InvalidDataException("Q.931 message too short");
        }

        var offset = 0;

        // Protocol discriminator
        if (data[offset++] != ProtocolDiscriminator)
        {
            throw new InvalidDataException(
                $"Invalid Q.931 protocol discriminator: 0x{data[0]:X2}");
        }

        // Call reference
        var crLength = data[offset++];

        if (crLength < 1 || crLength > 2 || offset + crLength > data.Length)
        {
            throw new InvalidDataException($"Invalid call reference length: {crLength}");
        }

        var msg = new Q931Message();

        if (crLength == 1)
        {
            msg.CallReferenceFlag = (data[offset] & 0x80) != 0;
            msg.CallReference = data[offset] & 0x7F;
            offset++;
        }
        else // crLength == 2
        {
            msg.CallReferenceFlag = (data[offset] & 0x80) != 0;
            msg.CallReference = ((data[offset] & 0x7F) << 8) | data[offset + 1];
            offset += 2;
        }

        // Message type
        if (offset >= data.Length)
        {
            throw new InvalidDataException("Q.931 message truncated before message type");
        }

        msg.MessageType = data[offset++];

        // Information elements
        while (offset < data.Length)
        {
            var ieTag = data[offset++];

            // Single-octet IEs (tag bit 7 set, except for special cases)
            // In Q.931, tags 0x80-0xFF are single-octet. But for H.225.0,
            // we only handle variable-length IEs.
            if ((ieTag & 0x80) != 0 && ieTag != IE_USER_USER)
            {
                // Single-octet IE — skip (value is embedded in the tag byte)
                continue;
            }

            if (offset >= data.Length)
            {
                break;
            }

            int ieLength;

            if (ieTag == IE_USER_USER)
            {
                // User-User IE has 2-byte length
                if (offset + 1 >= data.Length)
                {
                    break;
                }

                ieLength = (data[offset] << 8) | data[offset + 1];
                offset += 2;
            }
            else
            {
                // Standard variable-length IE: 1-byte length
                ieLength = data[offset++];
            }

            if (offset + ieLength > data.Length)
            {
                break; // Truncated IE — best effort
            }

            var ieData = new byte[ieLength];
            Array.Copy(data, offset, ieData, 0, ieLength);
            msg.InformationElements[ieTag] = ieData;
            offset += ieLength;
        }

        return msg;
    }

    // ──────────────────────────────────────────────────────────
    //  Build raw Q.931 bytes (for TPKT payload)
    // ──────────────────────────────────────────────────────────

    public byte[] Build()
    {
        var ms = new MemoryStream();

        // Protocol discriminator
        ms.WriteByte(ProtocolDiscriminator);

        // Call reference (always 2 bytes for H.323)
        ms.WriteByte(0x02); // Length

        var crByte0 = (byte)((CallReference >> 8) & 0x7F);
        if (CallReferenceFlag)
        {
            crByte0 |= 0x80;
        }

        ms.WriteByte(crByte0);
        ms.WriteByte((byte)(CallReference & 0xFF));

        // Message type
        ms.WriteByte(MessageType);

        // Information elements — must be in ascending tag order
        var sortedIes = InformationElements.OrderBy(kv => kv.Key);

        foreach (var (tag, data) in sortedIes)
        {
            ms.WriteByte(tag);

            if (tag == IE_USER_USER)
            {
                // 2-byte length
                ms.WriteByte((byte)(data.Length >> 8));
                ms.WriteByte((byte)(data.Length & 0xFF));
            }
            else
            {
                // 1-byte length
                ms.WriteByte((byte)data.Length);
            }

            ms.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    // ──────────────────────────────────────────────────────────
    //  Factory methods
    // ──────────────────────────────────────────────────────────

    public static Q931Message CreateSetup(int callReference, byte[] uuiePerData)
    {
        var msg = new Q931Message
        {
            MessageType = MSG_SETUP,
            CallReference = callReference,
            CallReferenceFlag = false
        };

        msg.SetUuieData(uuiePerData);

        return msg;
    }

    public static Q931Message CreateCallProceeding(int callReference, byte[] uuiePerData)
    {
        var msg = new Q931Message
        {
            MessageType = MSG_CALL_PROCEEDING,
            CallReference = callReference,
            CallReferenceFlag = true
        };

        msg.SetUuieData(uuiePerData);

        return msg;
    }

    public static Q931Message CreateAlerting(int callReference, byte[] uuiePerData)
    {
        var msg = new Q931Message
        {
            MessageType = MSG_ALERTING,
            CallReference = callReference,
            CallReferenceFlag = true
        };

        msg.SetUuieData(uuiePerData);

        return msg;
    }

    public static Q931Message CreateConnect(int callReference, byte[] uuiePerData)
    {
        var msg = new Q931Message
        {
            MessageType = MSG_CONNECT,
            CallReference = callReference,
            CallReferenceFlag = true
        };

        msg.SetUuieData(uuiePerData);

        return msg;
    }

    public static Q931Message CreateReleaseComplete(int callReference, bool fromDest, byte[] uuiePerData)
    {
        var msg = new Q931Message
        {
            MessageType = MSG_RELEASE_COMPLETE,
            CallReference = callReference,
            CallReferenceFlag = fromDest
        };

        if (uuiePerData != null)
        {
            msg.SetUuieData(uuiePerData);
        }

        return msg;
    }

    public static Q931Message CreateFacility(int callReference, bool fromDest, byte[] uuiePerData)
    {
        var msg = new Q931Message
        {
            MessageType = MSG_FACILITY,
            CallReference = callReference,
            CallReferenceFlag = fromDest
        };

        if (uuiePerData != null)
        {
            msg.SetUuieData(uuiePerData);
        }

        return msg;
    }

    /// <summary>Human-readable message type name.</summary>
    public static string MessageTypeName(byte type)
    {
        return type switch
        {
            MSG_ALERTING => "Alerting",
            MSG_CALL_PROCEEDING => "CallProceeding",
            MSG_SETUP => "Setup",
            MSG_CONNECT => "Connect",
            MSG_RELEASE_COMPLETE => "ReleaseComplete",
            MSG_FACILITY => "Facility",
            _ => $"Unknown(0x{type:X2})"
        };
    }

    public override string ToString()
    {
        return $"Q931[{MessageTypeName(MessageType)} CRV={CallReference} flag={CallReferenceFlag} IEs={InformationElements.Count}]";
    }
}
