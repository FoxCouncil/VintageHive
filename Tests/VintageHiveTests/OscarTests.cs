// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.Oscar;

namespace Oscar;

[TestClass]
public class OscarUtilsTests
{
    #region RoastPassword

    [TestMethod]
    public void RoastPassword_XorsWithRoastKey()
    {
        // Arrange
        var password = "password";

        // Act
        var roasted = OscarUtils.RoastPassword(password);

        // Assert — each byte should be password[i] XOR ROAST_KEY[i]
        Assert.AreEqual(password.Length, roasted.Length);

        // The ROAST_KEY starts with 0xF3, 0x26, 0x81, 0xC4, 0x39, 0x86, 0xDB, 0x92
        // 'p' (0x70) XOR 0xF3 = 0x83
        Assert.AreEqual(0x83, roasted[0]);

        // 'a' (0x61) XOR 0x26 = 0x47
        Assert.AreEqual(0x47, roasted[1]);
    }

    [TestMethod]
    public void RoastPassword_EmptyPassword_ReturnsEmpty()
    {
        var roasted = OscarUtils.RoastPassword("");

        Assert.AreEqual(0, roasted.Length);
    }

    [TestMethod]
    public void RoastPassword_LongerThan16_WrapsKey()
    {
        // The key is 16 bytes, so position 16 wraps back to key[0]
        var password = "abcdefghijklmnopq"; // 17 chars

        var roasted = OscarUtils.RoastPassword(password);

        Assert.AreEqual(17, roasted.Length);

        // char 0 ('a' = 0x61) XOR ROAST_KEY[0] (0xF3) = 0x92
        Assert.AreEqual(0x92, roasted[0]);

        // char 16 ('q' = 0x71) XOR ROAST_KEY[0] (0xF3) = 0x82
        Assert.AreEqual(0x82, roasted[16]);
    }

    #endregion

    #region Network Byte Order Conversions

    [TestMethod]
    public void GetBytes_UInt16_NetworkByteOrder()
    {
        var bytes = OscarUtils.GetBytes((ushort)0x0102);

        // Network byte order (big-endian): 0x01, 0x02
        Assert.AreEqual(2, bytes.Length);
        Assert.AreEqual(0x01, bytes[0]);
        Assert.AreEqual(0x02, bytes[1]);
    }

    [TestMethod]
    public void GetBytes_UInt32_NetworkByteOrder()
    {
        var bytes = OscarUtils.GetBytes((uint)0x01020304);

        Assert.AreEqual(4, bytes.Length);
        Assert.AreEqual(0x01, bytes[0]);
        Assert.AreEqual(0x02, bytes[1]);
        Assert.AreEqual(0x03, bytes[2]);
        Assert.AreEqual(0x04, bytes[3]);
    }

    [TestMethod]
    public void GetBytes_UInt64_NetworkByteOrder()
    {
        var bytes = OscarUtils.GetBytes((ulong)0x0102030405060708);

        Assert.AreEqual(8, bytes.Length);
        Assert.AreEqual(0x01, bytes[0]);
        Assert.AreEqual(0x02, bytes[1]);
        Assert.AreEqual(0x03, bytes[2]);
        Assert.AreEqual(0x04, bytes[3]);
        Assert.AreEqual(0x05, bytes[4]);
        Assert.AreEqual(0x06, bytes[5]);
        Assert.AreEqual(0x07, bytes[6]);
        Assert.AreEqual(0x08, bytes[7]);
    }

    [TestMethod]
    public void ToUInt16_RoundTrips()
    {
        var original = (ushort)12345;
        var bytes = OscarUtils.GetBytes(original);
        var result = OscarUtils.ToUInt16(bytes);

        Assert.AreEqual(original, result);
    }

    [TestMethod]
    public void ToUInt32_RoundTrips()
    {
        var original = (uint)1234567890;
        var bytes = OscarUtils.GetBytes(original);
        var result = OscarUtils.ToUInt32(bytes);

        Assert.AreEqual(original, result);
    }

    [TestMethod]
    public void ToUInt64_RoundTrips()
    {
        var original = (ulong)9876543210123456789;
        var bytes = OscarUtils.GetBytes(original);
        var result = OscarUtils.ToUInt64(bytes);

        Assert.AreEqual(original, result);
    }

    #endregion

    #region ToCLSID

    [TestMethod]
    public void ToCLSID_ValidData_FormatsCorrectly()
    {
        var data = new byte[]
        {
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06,
            0x07, 0x08,
            0x09, 0x0A,
            0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10
        };

        var result = OscarUtils.ToCLSID(data);

        Assert.AreEqual("01020304-0506-0708-090A-0B0C0D0E0F10", result);
    }

    [TestMethod]
    public void ToCLSID_WrongLength_Throws()
    {
        Assert.ThrowsExactly<ApplicationException>(() => OscarUtils.ToCLSID(new byte[] { 0x01, 0x02, 0x03 }));
    }

    #endregion

    #region DecodeFlaps

    [TestMethod]
    public void DecodeFlaps_SingleFlap_DecodesCorrectly()
    {
        // Build a FLAP: * (0x2A), type, seq (2 bytes), length (2 bytes), data
        var data = new byte[]
        {
            0x2A,       // '*' — FLAP header marker
            0x02,       // Type: Data
            0x00, 0x01, // Sequence: 1
            0x00, 0x03, // Length: 3
            0x41, 0x42, 0x43  // Data: "ABC"
        };

        var flaps = OscarUtils.DecodeFlaps(data);

        Assert.AreEqual(1, flaps.Length);
        Assert.AreEqual(FlapFrameType.Data, flaps[0].Type);
        Assert.AreEqual((ushort)1, flaps[0].Sequence);
        Assert.AreEqual(3, flaps[0].Data.Length);
        Assert.AreEqual((byte)'A', flaps[0].Data[0]);
        Assert.AreEqual((byte)'B', flaps[0].Data[1]);
        Assert.AreEqual((byte)'C', flaps[0].Data[2]);
    }

    [TestMethod]
    public void DecodeFlaps_MultipleFlaps_DecodesAll()
    {
        var data = new byte[]
        {
            // FLAP 1: SignOn, seq=1, 0 bytes data
            0x2A, 0x01, 0x00, 0x01, 0x00, 0x00,
            // FLAP 2: Data, seq=2, 2 bytes data (0xAB, 0xCD)
            0x2A, 0x02, 0x00, 0x02, 0x00, 0x02, 0xAB, 0xCD
        };

        var flaps = OscarUtils.DecodeFlaps(data);

        Assert.AreEqual(2, flaps.Length);

        Assert.AreEqual(FlapFrameType.SignOn, flaps[0].Type);
        Assert.AreEqual((ushort)1, flaps[0].Sequence);
        Assert.AreEqual(0, flaps[0].Data.Length);

        Assert.AreEqual(FlapFrameType.Data, flaps[1].Type);
        Assert.AreEqual((ushort)2, flaps[1].Sequence);
        Assert.AreEqual(2, flaps[1].Data.Length);
    }

    [TestMethod]
    public void DecodeFlaps_TooShort_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => OscarUtils.DecodeFlaps(new byte[] { 0x2A, 0x01, 0x00 }));
    }

    [TestMethod]
    public void DecodeFlaps_InvalidHeader_Throws()
    {
        // Missing the '*' (0x2A) header marker
        var data = new byte[] { 0xFF, 0x02, 0x00, 0x01, 0x00, 0x00 };

        Assert.ThrowsExactly<ArgumentException>(() => OscarUtils.DecodeFlaps(data));
    }

    #endregion

    #region DecodeTlvs

    [TestMethod]
    public void DecodeTlvs_SingleTlv_DecodesCorrectly()
    {
        // TLV: type (2 bytes), length (2 bytes), value
        var data = new byte[]
        {
            0x00, 0x01, // Type: 1 (ScreenName)
            0x00, 0x03, // Length: 3
            0x46, 0x6F, 0x78  // Value: "Fox"
        };

        var tlvs = OscarUtils.DecodeTlvs(data);

        Assert.AreEqual(1, tlvs.Length);
        Assert.AreEqual((ushort)1, tlvs[0].Type);
        Assert.AreEqual("Fox", Encoding.ASCII.GetString(tlvs[0].Value));
    }

    [TestMethod]
    public void DecodeTlvs_MultipleTlvs_DecodesAll()
    {
        var data = new byte[]
        {
            // TLV 1: type=1, length=3, value="Fox"
            0x00, 0x01, 0x00, 0x03, 0x46, 0x6F, 0x78,
            // TLV 2: type=8, length=2, value=0x00 0x14
            0x00, 0x08, 0x00, 0x02, 0x00, 0x14
        };

        var tlvs = OscarUtils.DecodeTlvs(data);

        Assert.AreEqual(2, tlvs.Length);
        Assert.AreEqual((ushort)1, tlvs[0].Type);
        Assert.AreEqual((ushort)8, tlvs[1].Type);
    }

    [TestMethod]
    public void DecodeTlvs_ZeroLengthValue()
    {
        var data = new byte[]
        {
            0x00, 0x05, // Type: 5
            0x00, 0x00  // Length: 0
        };

        var tlvs = OscarUtils.DecodeTlvs(data);

        Assert.AreEqual(1, tlvs.Length);
        Assert.AreEqual(0, tlvs[0].Value.Length);
    }

    [TestMethod]
    public void DecodeTlvs_TooShort_Throws()
    {
        Assert.ThrowsExactly<ApplicationException>(() => OscarUtils.DecodeTlvs(new byte[] { 0x00, 0x01 }));
    }

    #endregion

    #region GetTlv Extension

    [TestMethod]
    public void GetTlv_List_FindsByType()
    {
        var tlvs = new List<Tlv>
        {
            new Tlv(1, "screen"),
            new Tlv(8, new byte[] { 0x00, 0x14 })
        };

        var result = tlvs.GetTlv(8);

        Assert.IsNotNull(result);
        Assert.AreEqual((ushort)8, result.Type);
    }

    [TestMethod]
    public void GetTlv_List_NotFound_ReturnsNull()
    {
        var tlvs = new List<Tlv> { new Tlv(1, "test") };

        Assert.IsNull(tlvs.GetTlv(99));
    }

    [TestMethod]
    public void GetTlv_Array_FindsByType()
    {
        var tlvs = new Tlv[]
        {
            new Tlv(1, "hello"),
            new Tlv(2, "world")
        };

        var result = tlvs.GetTlv(2);

        Assert.IsNotNull(result);
        Assert.AreEqual("world", Encoding.ASCII.GetString(result.Value));
    }

    #endregion

    #region EncodeTlvs Extension

    [TestMethod]
    public void EncodeTlvs_List_EncodesAll()
    {
        var tlvs = new List<Tlv>
        {
            new Tlv(1, "AB"),
            new Tlv(2, (ushort)42)
        };

        var encoded = tlvs.EncodeTlvs();

        // TLV 1: 2 (type) + 2 (length) + 2 (value "AB") = 6
        // TLV 2: 2 (type) + 2 (length) + 2 (value ushort) = 6
        Assert.AreEqual(12, encoded.Length);
    }

    #endregion
}

[TestClass]
public class TlvTests
{
    #region Constructor

    [TestMethod]
    public void Constructor_String_EncodesAsAscii()
    {
        var tlv = new Tlv(1, "Hello");

        Assert.AreEqual((ushort)1, tlv.Type);
        Assert.AreEqual(5, tlv.Value.Length);
        Assert.AreEqual("Hello", Encoding.ASCII.GetString(tlv.Value));
    }

    [TestMethod]
    public void Constructor_UInt16_EncodesNetworkOrder()
    {
        var tlv = new Tlv(1, (ushort)0x0102);

        Assert.AreEqual(2, tlv.Value.Length);
        Assert.AreEqual(0x01, tlv.Value[0]);
        Assert.AreEqual(0x02, tlv.Value[1]);
    }

    [TestMethod]
    public void Constructor_ByteArray_StoresDirectly()
    {
        var data = new byte[] { 0xAA, 0xBB, 0xCC };
        var tlv = new Tlv(42, data);

        Assert.AreEqual((ushort)42, tlv.Type);
        CollectionAssert.AreEqual(data, tlv.Value);
    }

    #endregion

    #region Constants

    [TestMethod]
    public void Constants_ScreenNameType()
    {
        Assert.AreEqual((ushort)0x0001, Tlv.Type_ScreenName);
    }

    [TestMethod]
    public void Constants_ErrorSubCodeType()
    {
        Assert.AreEqual((ushort)0x0008, Tlv.Type_ErrorSubCode);
    }

    [TestMethod]
    public void Constants_ErrorNoMatch()
    {
        Assert.AreEqual((ushort)0x0014, Tlv.Error_NoMatch);
    }

    #endregion

    #region Encode

    [TestMethod]
    public void Encode_ProducesCorrectBinaryFormat()
    {
        // TLV with type=0x0001, value="Fox"
        var tlv = new Tlv(0x0001, "Fox");

        var encoded = tlv.Encode();

        // Type: 0x00 0x01
        Assert.AreEqual(0x00, encoded[0]);
        Assert.AreEqual(0x01, encoded[1]);

        // Length: 0x00 0x03 (3 bytes)
        Assert.AreEqual(0x00, encoded[2]);
        Assert.AreEqual(0x03, encoded[3]);

        // Value: "Fox" = 0x46 0x6F 0x78
        Assert.AreEqual(0x46, encoded[4]);
        Assert.AreEqual(0x6F, encoded[5]);
        Assert.AreEqual(0x78, encoded[6]);
    }

    [TestMethod]
    public void Encode_EmptyValue_EncodesTypeAndZeroLength()
    {
        var tlv = new Tlv(5, Array.Empty<byte>());
        var encoded = tlv.Encode();

        Assert.AreEqual(4, encoded.Length); // type (2) + length (2) + value (0)
        Assert.AreEqual(0x00, encoded[2]);
        Assert.AreEqual(0x00, encoded[3]);
    }

    [TestMethod]
    public void Encode_Decode_RoundTrip()
    {
        var original = new Tlv(0x0001, "TestScreenName");
        var encoded = original.Encode();
        var decoded = OscarUtils.DecodeTlvs(encoded);

        Assert.AreEqual(1, decoded.Length);
        Assert.AreEqual(original.Type, decoded[0].Type);
        CollectionAssert.AreEqual(original.Value, decoded[0].Value);
    }

    #endregion
}

[TestClass]
public class SnacTests
{
    #region Constructor

    [TestMethod]
    public void Constructor_SetsProperties()
    {
        var snac = new Snac(0x0001, 0x0007, 0x0000, 12345);

        Assert.AreEqual((ushort)0x0001, snac.Family);
        Assert.AreEqual((ushort)0x0007, snac.SubType);
        Assert.AreEqual((ushort)0x0000, snac.Flags);
        Assert.AreEqual((uint)12345, snac.RequestID);
    }

    [TestMethod]
    public void Constructor_DefaultsToZeroFlagsAndRequestID()
    {
        var snac = new Snac(1, 2);

        Assert.AreEqual((ushort)0, snac.Flags);
        Assert.AreEqual((uint)0, snac.RequestID);
    }

    #endregion

    #region Write Methods

    [TestMethod]
    public void WriteString_AppendsAsciiData()
    {
        var snac = new Snac(1, 1);

        snac.WriteString("Hello");

        var data = snac.RawData;
        Assert.AreEqual("Hello", Encoding.ASCII.GetString(data));
    }

    [TestMethod]
    public void WriteUInt8_AppendsByte()
    {
        var snac = new Snac(1, 1);

        snac.WriteUInt8(0x42);

        Assert.AreEqual(1, snac.RawData.Length);
        Assert.AreEqual(0x42, snac.RawData[0]);
    }

    [TestMethod]
    public void WriteUInt16_AppendsNetworkOrder()
    {
        var snac = new Snac(1, 1);

        snac.WriteUInt16(0x0102);

        var data = snac.RawData;
        Assert.AreEqual(2, data.Length);
        Assert.AreEqual(0x01, data[0]);
        Assert.AreEqual(0x02, data[1]);
    }

    [TestMethod]
    public void WriteUInt32_AppendsNetworkOrder()
    {
        var snac = new Snac(1, 1);

        snac.WriteUInt32(0x01020304);

        var data = snac.RawData;
        Assert.AreEqual(4, data.Length);
        Assert.AreEqual(0x01, data[0]);
        Assert.AreEqual(0x04, data[3]);
    }

    [TestMethod]
    public void WriteUInt64_AppendsNetworkOrder()
    {
        var snac = new Snac(1, 1);

        snac.WriteUInt64(0x0102030405060708);

        var data = snac.RawData;
        Assert.AreEqual(8, data.Length);
        Assert.AreEqual(0x01, data[0]);
        Assert.AreEqual(0x08, data[7]);
    }

    [TestMethod]
    public void MultipleWrites_Accumulate()
    {
        var snac = new Snac(1, 1);

        snac.WriteUInt8(0xAA);
        snac.WriteUInt16(0xBBCC);
        snac.WriteString("X");

        var data = snac.RawData;
        Assert.AreEqual(4, data.Length);
        Assert.AreEqual(0xAA, data[0]);
        Assert.AreEqual(0xBB, data[1]);
        Assert.AreEqual(0xCC, data[2]);
        Assert.AreEqual((byte)'X', data[3]);
    }

    #endregion

    #region NewReply

    [TestMethod]
    public void NewReply_InheritsRequestIDAndFamily()
    {
        var original = new Snac(0x0001, 0x0007, 0x0000, 999);

        var reply = original.NewReply();

        Assert.AreEqual(original.Family, reply.Family);
        Assert.AreEqual(original.SubType, reply.SubType);
        Assert.AreEqual(original.Flags, reply.Flags);
        Assert.AreEqual(original.RequestID, reply.RequestID);
    }

    [TestMethod]
    public void NewReply_OverridesFamily()
    {
        var original = new Snac(0x0001, 0x0007, 0x0000, 999);

        var reply = original.NewReply(family: 0x0002);

        Assert.AreEqual((ushort)0x0002, reply.Family);
        Assert.AreEqual(original.SubType, reply.SubType);
        Assert.AreEqual(original.RequestID, reply.RequestID);
    }

    [TestMethod]
    public void NewReply_OverridesSubType()
    {
        var original = new Snac(0x0001, 0x0007, 0x0000, 999);

        var reply = original.NewReply(subType: 0x0008);

        Assert.AreEqual(original.Family, reply.Family);
        Assert.AreEqual((ushort)0x0008, reply.SubType);
    }

    [TestMethod]
    public void NewReply_HasEmptyData()
    {
        var original = new Snac(1, 1);
        original.WriteString("some data");

        var reply = original.NewReply();

        Assert.AreEqual(0, reply.RawData.Length);
    }

    #endregion

    #region Encode

    [TestMethod]
    public void Encode_ProducesCorrectBinaryFormat()
    {
        var snac = new Snac(0x0001, 0x0017, 0x0000, 1);
        snac.WriteUInt8(0xFF);

        var encoded = snac.Encode();

        // SNAC header: family(2) + subtype(2) + flags(2) + requestID(4) = 10 bytes + 1 byte data
        Assert.AreEqual(11, encoded.Length);

        // Family: 0x00 0x01
        Assert.AreEqual(0x00, encoded[0]);
        Assert.AreEqual(0x01, encoded[1]);

        // SubType: 0x00 0x17
        Assert.AreEqual(0x00, encoded[2]);
        Assert.AreEqual(0x17, encoded[3]);

        // Flags: 0x00 0x00
        Assert.AreEqual(0x00, encoded[4]);
        Assert.AreEqual(0x00, encoded[5]);

        // RequestID: 0x00 0x00 0x00 0x01
        Assert.AreEqual(0x00, encoded[6]);
        Assert.AreEqual(0x01, encoded[9]);

        // Data: 0xFF
        Assert.AreEqual(0xFF, encoded[10]);
    }

    #endregion

    #region ToString

    [TestMethod]
    public void ToString_FormatsCorrectly()
    {
        var snac = new Snac(0x0001, 0x0017);

        Assert.AreEqual("SNAC(01, 17)", snac.ToString());
    }

    #endregion
}

[TestClass]
public class FlapTests
{
    #region Constructor

    [TestMethod]
    public void Constructor_Default_TypeIsData()
    {
        var flap = new Flap();

        Assert.AreEqual(FlapFrameType.Data, flap.Type);
    }

    [TestMethod]
    public void Constructor_WithType_SetsType()
    {
        var flap = new Flap(FlapFrameType.SignOn);

        Assert.AreEqual(FlapFrameType.SignOn, flap.Type);
    }

    [TestMethod]
    public void DefaultData_IsEmpty()
    {
        var flap = new Flap();

        Assert.IsNotNull(flap.Data);
        Assert.AreEqual(0, flap.Data.Length);
    }

    #endregion

    #region Encode

    [TestMethod]
    public void Encode_ProducesCorrectBinaryFormat()
    {
        var flap = new Flap(FlapFrameType.Data)
        {
            Sequence = 1,
            Data = new byte[] { 0x41, 0x42 }
        };

        var encoded = flap.Encode();

        // '*' + type + seq(2) + length(2) + data(2) = 8
        Assert.AreEqual(8, encoded.Length);

        Assert.AreEqual((byte)'*', encoded[0]);
        Assert.AreEqual((byte)FlapFrameType.Data, encoded[1]);

        // Sequence: 0x00 0x01
        Assert.AreEqual(0x00, encoded[2]);
        Assert.AreEqual(0x01, encoded[3]);

        // Length: 0x00 0x02
        Assert.AreEqual(0x00, encoded[4]);
        Assert.AreEqual(0x02, encoded[5]);

        // Data
        Assert.AreEqual(0x41, encoded[6]);
        Assert.AreEqual(0x42, encoded[7]);
    }

    [TestMethod]
    public void Encode_EmptyData_MinimalFrame()
    {
        var flap = new Flap(FlapFrameType.KeepAlive)
        {
            Sequence = 0,
            Data = Array.Empty<byte>()
        };

        var encoded = flap.Encode();

        Assert.AreEqual(6, encoded.Length); // header only, no data
        Assert.AreEqual(0x00, encoded[4]); // length high
        Assert.AreEqual(0x00, encoded[5]); // length low
    }

    [TestMethod]
    public void Encode_Decode_RoundTrip()
    {
        var original = new Flap(FlapFrameType.Data)
        {
            Sequence = 42,
            Data = Encoding.ASCII.GetBytes("TestPayload")
        };

        var encoded = original.Encode();
        var decoded = OscarUtils.DecodeFlaps(encoded);

        Assert.AreEqual(1, decoded.Length);
        Assert.AreEqual(original.Type, decoded[0].Type);
        Assert.AreEqual(original.Sequence, decoded[0].Sequence);
        CollectionAssert.AreEqual(original.Data, decoded[0].Data);
    }

    #endregion

    #region GetSnac

    [TestMethod]
    public void GetSnac_ParsesSnacFromFlapData()
    {
        // Build SNAC data: family(2) + subtype(2) + flags(2) + requestID(4) + payload
        var snacBytes = new byte[]
        {
            0x00, 0x01, // Family: 1
            0x00, 0x17, // SubType: 23
            0x00, 0x00, // Flags: 0
            0x00, 0x00, 0x00, 0x05, // RequestID: 5
            0x41, 0x42, 0x43 // Payload: "ABC"
        };

        var flap = new Flap(FlapFrameType.Data) { Data = snacBytes };

        var snac = flap.GetSnac();

        Assert.AreEqual((ushort)1, snac.Family);
        Assert.AreEqual((ushort)23, snac.SubType);
        Assert.AreEqual((ushort)0, snac.Flags);
        Assert.AreEqual((uint)5, snac.RequestID);
        Assert.AreEqual(3, snac.RawData.Length);
        Assert.AreEqual((byte)'A', snac.RawData[0]);
    }

    [TestMethod]
    public void GetSnac_DataTooShort_Throws()
    {
        var flap = new Flap { Data = new byte[] { 0x00, 0x01, 0x00 } };

        Assert.ThrowsExactly<ApplicationException>(() => flap.GetSnac());
    }

    #endregion
}

[TestClass]
public class FlapFrameTypeTests
{
    [TestMethod]
    public void SignOn_Is0x01()
    {
        Assert.AreEqual((byte)0x01, (byte)FlapFrameType.SignOn);
    }

    [TestMethod]
    public void Data_Is0x02()
    {
        Assert.AreEqual((byte)0x02, (byte)FlapFrameType.Data);
    }

    [TestMethod]
    public void Error_Is0x03()
    {
        Assert.AreEqual((byte)0x03, (byte)FlapFrameType.Error);
    }

    [TestMethod]
    public void SignOff_Is0x04()
    {
        Assert.AreEqual((byte)0x04, (byte)FlapFrameType.SignOff);
    }

    [TestMethod]
    public void KeepAlive_Is0x05()
    {
        Assert.AreEqual((byte)0x05, (byte)FlapFrameType.KeepAlive);
    }
}
