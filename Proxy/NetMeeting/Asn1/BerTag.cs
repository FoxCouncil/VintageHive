// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.Asn1;

/// <summary>
/// ASN.1 BER tag: class, constructed/primitive flag, and tag number.
/// Supports both single-byte (tag numbers 0-30) and multi-byte (tag numbers >= 31,
/// X.690 Section 8.1.2.4) encoding.
/// </summary>
internal readonly struct BerTag
{
    public const int CLASS_UNIVERSAL = 0;
    public const int CLASS_APPLICATION = 1;
    public const int CLASS_CONTEXT = 2;
    public const int CLASS_PRIVATE = 3;

    // Universal primitive tags
    public const byte BOOLEAN = 0x01;
    public const byte INTEGER = 0x02;
    public const byte BIT_STRING = 0x03;
    public const byte OCTET_STRING = 0x04;
    public const byte NULL = 0x05;
    public const byte OBJECT_IDENTIFIER = 0x06;
    public const byte ENUMERATED = 0x0A;

    // Universal constructed tags
    public const byte SEQUENCE = 0x30;
    public const byte SET = 0x31;

    public int Class { get; }
    public bool Constructed { get; }
    public int Number { get; }
    public byte RawByte { get; }

    public BerTag(byte rawByte)
    {
        RawByte = rawByte;
        Class = (rawByte >> 6) & 0x03;
        Constructed = (rawByte & 0x20) != 0;
        Number = rawByte & 0x1F;
    }

    public BerTag(int tagClass, bool constructed, int number)
    {
        Class = tagClass;
        Constructed = constructed;
        Number = number;
        RawByte = (byte)((tagClass << 6) | (constructed ? 0x20 : 0) | (number < 31 ? number & 0x1F : 0x1F));
    }

    /// <summary>
    /// Constructor for multi-byte tags (tag number >= 31).
    /// The firstByte has the low 5 bits all set (0x1F); the actual tag number
    /// was decoded from subsequent base-128 continuation bytes.
    /// </summary>
    internal BerTag(byte firstByte, int fullNumber)
    {
        RawByte = firstByte;
        Class = (firstByte >> 6) & 0x03;
        Constructed = (firstByte & 0x20) != 0;
        Number = fullNumber;
    }

    public bool IsUniversal => Class == CLASS_UNIVERSAL;
    public bool IsApplication => Class == CLASS_APPLICATION;
    public bool IsContext => Class == CLASS_CONTEXT;

    public bool Is(byte expected) => RawByte == expected;

    public static byte Application(int number, bool constructed = true)
    {
        return (byte)((CLASS_APPLICATION << 6) | (constructed ? 0x20 : 0) | (number & 0x1F));
    }

    public static byte Context(int number, bool constructed = false)
    {
        return (byte)((CLASS_CONTEXT << 6) | (constructed ? 0x20 : 0) | (number & 0x1F));
    }

    public override string ToString()
    {
        var className = Class switch
        {
            CLASS_UNIVERSAL => "Universal",
            CLASS_APPLICATION => "Application",
            CLASS_CONTEXT => "Context",
            CLASS_PRIVATE => "Private",
            _ => $"Unknown({Class})"
        };
        return $"[{className} {(Constructed ? "Constructed" : "Primitive")} {Number}] (0x{RawByte:X2})";
    }
}
