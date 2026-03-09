// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.NetMeeting.FileTransfer;
using VintageHive.Proxy.NetMeeting.T120;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  FileTransfer Constants tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileTransferConstantsTests
{
    [TestMethod]
    public void T127Oid_HasExpectedValues()
    {
        CollectionAssert.AreEqual(new[] { 0, 0, 20, 127 }, FileTransferConstants.T127_OID);
    }

    [TestMethod]
    public void MbftRootCount_Is16()
    {
        Assert.AreEqual(16, FileTransferConstants.MBFT_ROOT_COUNT);
    }

    [TestMethod]
    public void PduName_AllKnownTypes()
    {
        Assert.AreEqual("File-Offer", FileTransferConstants.PduName(FileTransferConstants.PDU_FILE_OFFER));
        Assert.AreEqual("File-Accept", FileTransferConstants.PduName(FileTransferConstants.PDU_FILE_ACCEPT));
        Assert.AreEqual("File-Reject", FileTransferConstants.PduName(FileTransferConstants.PDU_FILE_REJECT));
        Assert.AreEqual("File-Start", FileTransferConstants.PduName(FileTransferConstants.PDU_FILE_START));
        Assert.AreEqual("File-Data", FileTransferConstants.PduName(FileTransferConstants.PDU_FILE_DATA));
        Assert.AreEqual("File-Abort", FileTransferConstants.PduName(FileTransferConstants.PDU_FILE_ABORT));
        Assert.AreEqual("File-Error", FileTransferConstants.PduName(FileTransferConstants.PDU_FILE_ERROR));
        Assert.AreEqual("NonStandard", FileTransferConstants.PduName(FileTransferConstants.PDU_NON_STANDARD));
    }

    [TestMethod]
    public void PduName_Unknown_ReturnsGeneric()
    {
        Assert.IsTrue(FileTransferConstants.PduName(99).Contains("99"));
    }

    [TestMethod]
    public void RejectReasonName_KnownValues()
    {
        Assert.AreEqual("unspecified", FileTransferConstants.RejectReasonName(FileTransferConstants.REJECT_UNSPECIFIED));
        Assert.AreEqual("fileExists", FileTransferConstants.RejectReasonName(FileTransferConstants.REJECT_FILE_EXISTS));
        Assert.AreEqual("insufficientResources",
            FileTransferConstants.RejectReasonName(FileTransferConstants.REJECT_INSUFFICIENT_RESOURCES));
    }

    [TestMethod]
    public void NonStandardKeys_HaveCorrectValues()
    {
        Assert.AreEqual("NetMeeting 1 MBFT", FileTransferConstants.NS_KEY_MBFT);
        Assert.AreEqual("NetMeeting 1 FileEnd", FileTransferConstants.NS_KEY_FILE_END);
        Assert.AreEqual("NetMeeting 1 ChannelLeave", FileTransferConstants.NS_KEY_CHANNEL_LEAVE);
    }

    [TestMethod]
    public void MaxChunkSize_Is65535()
    {
        Assert.AreEqual(65535, FileTransferConstants.MAX_CHUNK_SIZE);
    }
}

// ──────────────────────────────────────────────────────────
//  File-OfferPDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileOfferTests
{
    [TestMethod]
    public void FileOffer_RoundTrip()
    {
        var pdu = new FileOfferPdu
        {
            FileHeader = new FileHeader
            {
                Filename = "readme.txt",
                Filesize = 1024,
                DateCreation = "19970716120000Z",
                DateModification = "19970717150000Z"
            },
            DataChannelId = 2000,
            FileHandle = 1,
            AckFlag = true
        };

        var data = MbftCodec.EncodeFileOffer(pdu);
        Assert.IsTrue(data.Length > 0);

        var msg = MbftCodec.Decode(data);
        Assert.AreEqual(FileTransferConstants.PDU_FILE_OFFER, msg.Type);
        Assert.IsNotNull(msg.FileOffer);

        var fo = msg.FileOffer;
        Assert.AreEqual("readme.txt", fo.FileHeader.Filename);
        Assert.AreEqual(1024L, fo.FileHeader.Filesize);
        Assert.AreEqual("19970716120000Z", fo.FileHeader.DateCreation);
        Assert.AreEqual("19970717150000Z", fo.FileHeader.DateModification);
        Assert.AreEqual(2000, fo.DataChannelId);
        Assert.AreEqual(1, fo.FileHandle);
        Assert.IsTrue(fo.AckFlag);
    }

    [TestMethod]
    public void FileOffer_WithRosterInstance()
    {
        var pdu = new FileOfferPdu
        {
            FileHeader = new FileHeader { Filename = "test.bin", Filesize = 100 },
            DataChannelId = 3000,
            FileHandle = 42,
            RosterInstance = 5,
            AckFlag = false
        };

        var data = MbftCodec.EncodeFileOffer(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(5, msg.FileOffer.RosterInstance);
        Assert.IsFalse(msg.FileOffer.AckFlag);
    }

    [TestMethod]
    public void FileOffer_MinimalHeader()
    {
        var pdu = new FileOfferPdu
        {
            FileHeader = new FileHeader { Filename = "a.dat" },
            DataChannelId = 1001,
            FileHandle = 0,
            AckFlag = true
        };

        var data = MbftCodec.EncodeFileOffer(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual("a.dat", msg.FileOffer.FileHeader.Filename);
        Assert.IsNull(msg.FileOffer.FileHeader.Filesize);
        Assert.IsNull(msg.FileOffer.FileHeader.DateCreation);
    }

    [TestMethod]
    public void FileOffer_LargeFile()
    {
        var pdu = new FileOfferPdu
        {
            FileHeader = new FileHeader
            {
                Filename = "large_file.zip",
                Filesize = 4_294_967_295L // ~4GB
            },
            DataChannelId = 5000,
            FileHandle = 100,
            AckFlag = true
        };

        var data = MbftCodec.EncodeFileOffer(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(4_294_967_295L, msg.FileOffer.FileHeader.Filesize);
    }
}

// ──────────────────────────────────────────────────────────
//  File-AcceptPDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileAcceptTests
{
    [TestMethod]
    public void FileAccept_RoundTrip()
    {
        var pdu = new FileAcceptPdu { FileHandle = 42 };
        var data = MbftCodec.EncodeFileAccept(pdu);

        var msg = MbftCodec.Decode(data);
        Assert.AreEqual(FileTransferConstants.PDU_FILE_ACCEPT, msg.Type);
        Assert.AreEqual(42, msg.FileAccept.FileHandle);
    }

    [TestMethod]
    public void FileAccept_ZeroHandle()
    {
        var pdu = new FileAcceptPdu { FileHandle = 0 };
        var data = MbftCodec.EncodeFileAccept(pdu);

        var msg = MbftCodec.Decode(data);
        Assert.AreEqual(0, msg.FileAccept.FileHandle);
    }

    [TestMethod]
    public void FileAccept_MaxHandle()
    {
        var pdu = new FileAcceptPdu { FileHandle = FileTransferConstants.MAX_HANDLE };
        var data = MbftCodec.EncodeFileAccept(pdu);

        var msg = MbftCodec.Decode(data);
        Assert.AreEqual(FileTransferConstants.MAX_HANDLE, msg.FileAccept.FileHandle);
    }
}

// ──────────────────────────────────────────────────────────
//  File-RejectPDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileRejectTests
{
    [TestMethod]
    public void FileReject_RoundTrip()
    {
        var pdu = new FileRejectPdu
        {
            FileHandle = 1,
            Reason = FileTransferConstants.REJECT_FILE_EXISTS
        };

        var data = MbftCodec.EncodeFileReject(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(FileTransferConstants.PDU_FILE_REJECT, msg.Type);
        Assert.AreEqual(1, msg.FileReject.FileHandle);
        Assert.AreEqual(FileTransferConstants.REJECT_FILE_EXISTS, msg.FileReject.Reason);
    }

    [TestMethod]
    public void FileReject_AllReasons_RoundTrip()
    {
        for (var reason = 0; reason < FileTransferConstants.REJECT_ROOT_COUNT; reason++)
        {
            var pdu = new FileRejectPdu { FileHandle = 1, Reason = reason };
            var data = MbftCodec.EncodeFileReject(pdu);
            var msg = MbftCodec.Decode(data);
            Assert.AreEqual(reason, msg.FileReject.Reason, $"Reason {reason} failed round-trip");
        }
    }
}

// ──────────────────────────────────────────────────────────
//  File-StartPDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileStartTests
{
    [TestMethod]
    public void FileStart_RoundTrip()
    {
        var chunk = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"

        var pdu = new FileStartPdu
        {
            FileHeader = new FileHeader
            {
                Filename = "hello.txt",
                Filesize = 5
            },
            FileHandle = 1,
            EofFlag = true,
            CrcFlag = false,
            DataOffset = 0,
            Data = chunk
        };

        var data = MbftCodec.EncodeFileStart(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(FileTransferConstants.PDU_FILE_START, msg.Type);
        var fs = msg.FileStart;
        Assert.AreEqual("hello.txt", fs.FileHeader.Filename);
        Assert.AreEqual(5L, fs.FileHeader.Filesize);
        Assert.AreEqual(1, fs.FileHandle);
        Assert.IsTrue(fs.EofFlag);
        Assert.IsFalse(fs.CrcFlag);
        Assert.AreEqual(0L, fs.DataOffset);
        CollectionAssert.AreEqual(chunk, fs.Data);
        Assert.IsNull(fs.CrcCheck);
    }

    [TestMethod]
    public void FileStart_WithCrc()
    {
        var pdu = new FileStartPdu
        {
            FileHeader = new FileHeader { Filename = "data.bin" },
            FileHandle = 2,
            EofFlag = true,
            CrcFlag = true,
            DataOffset = 0,
            Data = new byte[] { 0x01, 0x02, 0x03 },
            CrcCheck = 0xDEADBEEF
        };

        var data = MbftCodec.EncodeFileStart(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.IsTrue(msg.FileStart.CrcFlag);
        Assert.AreEqual(0xDEADBEEFu, msg.FileStart.CrcCheck);
    }

    [TestMethod]
    public void FileStart_EmptyData()
    {
        var pdu = new FileStartPdu
        {
            FileHeader = new FileHeader { Filename = "empty.txt", Filesize = 0 },
            FileHandle = 1,
            EofFlag = true,
            CrcFlag = false,
            DataOffset = 0,
            Data = Array.Empty<byte>()
        };

        var data = MbftCodec.EncodeFileStart(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(0, msg.FileStart.Data.Length);
        Assert.IsTrue(msg.FileStart.EofFlag);
    }
}

// ──────────────────────────────────────────────────────────
//  File-DataPDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileDataTests
{
    [TestMethod]
    public void FileData_RoundTrip()
    {
        var chunk = new byte[256];
        new Random(42).NextBytes(chunk);

        var pdu = new FileDataPdu
        {
            FileHandle = 1,
            EofFlag = false,
            AbortFlag = false,
            Data = chunk
        };

        var data = MbftCodec.EncodeFileData(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(FileTransferConstants.PDU_FILE_DATA, msg.Type);
        Assert.AreEqual(1, msg.FileData.FileHandle);
        Assert.IsFalse(msg.FileData.EofFlag);
        Assert.IsFalse(msg.FileData.AbortFlag);
        CollectionAssert.AreEqual(chunk, msg.FileData.Data);
    }

    [TestMethod]
    public void FileData_EofWithCrc()
    {
        var pdu = new FileDataPdu
        {
            FileHandle = 1,
            EofFlag = true,
            AbortFlag = false,
            Data = new byte[] { 0xFF },
            CrcCheck = 0x12345678
        };

        var data = MbftCodec.EncodeFileData(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.IsTrue(msg.FileData.EofFlag);
        Assert.AreEqual(0x12345678u, msg.FileData.CrcCheck);
    }

    [TestMethod]
    public void FileData_Abort()
    {
        var pdu = new FileDataPdu
        {
            FileHandle = 1,
            EofFlag = false,
            AbortFlag = true,
            Data = Array.Empty<byte>()
        };

        var data = MbftCodec.EncodeFileData(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.IsTrue(msg.FileData.AbortFlag);
    }

    [TestMethod]
    public void FileData_MaxChunk()
    {
        var chunk = new byte[1024]; // Smaller than MAX_CHUNK for test speed
        new Random(99).NextBytes(chunk);

        var pdu = new FileDataPdu
        {
            FileHandle = 42,
            EofFlag = false,
            AbortFlag = false,
            Data = chunk
        };

        var data = MbftCodec.EncodeFileData(pdu);
        var msg = MbftCodec.Decode(data);

        CollectionAssert.AreEqual(chunk, msg.FileData.Data);
    }
}

// ──────────────────────────────────────────────────────────
//  File-AbortPDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileAbortTests
{
    [TestMethod]
    public void FileAbort_Minimal()
    {
        var pdu = new FileAbortPdu { Reason = FileTransferConstants.ABORT_UNSPECIFIED };
        var data = MbftCodec.EncodeFileAbort(pdu);

        var msg = MbftCodec.Decode(data);
        Assert.AreEqual(FileTransferConstants.PDU_FILE_ABORT, msg.Type);
        Assert.AreEqual(FileTransferConstants.ABORT_UNSPECIFIED, msg.FileAbort.Reason);
        Assert.IsNull(msg.FileAbort.DataChannelId);
        Assert.IsNull(msg.FileAbort.FileHandle);
    }

    [TestMethod]
    public void FileAbort_WithChannelAndHandle()
    {
        var pdu = new FileAbortPdu
        {
            Reason = FileTransferConstants.ABORT_BANDWIDTH_REQUIRED,
            DataChannelId = 3000,
            FileHandle = 5
        };

        var data = MbftCodec.EncodeFileAbort(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(FileTransferConstants.ABORT_BANDWIDTH_REQUIRED, msg.FileAbort.Reason);
        Assert.AreEqual(3000, msg.FileAbort.DataChannelId);
        Assert.AreEqual(5, msg.FileAbort.FileHandle);
    }

    [TestMethod]
    public void FileAbort_AllReasons()
    {
        for (var reason = 0; reason < FileTransferConstants.ABORT_ROOT_COUNT; reason++)
        {
            var pdu = new FileAbortPdu { Reason = reason };
            var data = MbftCodec.EncodeFileAbort(pdu);
            var msg = MbftCodec.Decode(data);
            Assert.AreEqual(reason, msg.FileAbort.Reason);
        }
    }
}

// ──────────────────────────────────────────────────────────
//  File-ErrorPDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileErrorTests
{
    [TestMethod]
    public void FileError_RoundTrip()
    {
        var pdu = new FileErrorPdu
        {
            FileHandle = 1,
            ErrorType = FileTransferConstants.ERROR_TYPE_PERMANENT,
            ErrorId = 3000, // filename-not-found
            ErrorText = "File not found"
        };

        var data = MbftCodec.EncodeFileError(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(FileTransferConstants.PDU_FILE_ERROR, msg.Type);
        Assert.AreEqual(1, msg.FileError.FileHandle);
        Assert.AreEqual(FileTransferConstants.ERROR_TYPE_PERMANENT, msg.FileError.ErrorType);
        Assert.AreEqual(3000, msg.FileError.ErrorId);
        Assert.AreEqual("File not found", msg.FileError.ErrorText);
    }

    [TestMethod]
    public void FileError_Minimal()
    {
        var pdu = new FileErrorPdu
        {
            ErrorType = FileTransferConstants.ERROR_TYPE_INFORMATIVE,
            ErrorId = 0
        };

        var data = MbftCodec.EncodeFileError(pdu);
        var msg = MbftCodec.Decode(data);

        Assert.IsNull(msg.FileError.FileHandle);
        Assert.IsNull(msg.FileError.ErrorText);
    }
}

// ──────────────────────────────────────────────────────────
//  MBFT-NonStandardPDU tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class MbftNonStandardTests
{
    [TestMethod]
    public void NonStandard_FileEnd_RoundTrip()
    {
        var data = MbftCodec.EncodeNonStandard(FileTransferConstants.NS_KEY_FILE_END);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(FileTransferConstants.PDU_NON_STANDARD, msg.Type);
        Assert.AreEqual(FileTransferConstants.NS_KEY_FILE_END, msg.NonStandardKey);
    }

    [TestMethod]
    public void NonStandard_ChannelLeave_RoundTrip()
    {
        var data = MbftCodec.EncodeNonStandard(FileTransferConstants.NS_KEY_CHANNEL_LEAVE);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(FileTransferConstants.NS_KEY_CHANNEL_LEAVE, msg.NonStandardKey);
    }

    [TestMethod]
    public void NonStandard_WithData_RoundTrip()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var data = MbftCodec.EncodeNonStandard(FileTransferConstants.NS_KEY_MBFT, payload);
        var msg = MbftCodec.Decode(data);

        Assert.AreEqual(FileTransferConstants.NS_KEY_MBFT, msg.NonStandardKey);
        CollectionAssert.AreEqual(payload, msg.NonStandardData);
    }
}

// ──────────────────────────────────────────────────────────
//  PDU type detection tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class MbftDetectionTests
{
    [TestMethod]
    public void PeekPduType_FileOffer()
    {
        var pdu = new FileOfferPdu
        {
            FileHeader = new FileHeader { Filename = "test.txt" },
            DataChannelId = 2000,
            FileHandle = 1,
            AckFlag = true
        };

        var data = MbftCodec.EncodeFileOffer(pdu);
        Assert.AreEqual(FileTransferConstants.PDU_FILE_OFFER, MbftCodec.PeekPduType(data));
    }

    [TestMethod]
    public void PeekPduType_FileData()
    {
        var pdu = new FileDataPdu
        {
            FileHandle = 1,
            EofFlag = false,
            AbortFlag = false,
            Data = new byte[] { 0x00 }
        };

        var data = MbftCodec.EncodeFileData(pdu);
        Assert.AreEqual(FileTransferConstants.PDU_FILE_DATA, MbftCodec.PeekPduType(data));
    }

    [TestMethod]
    public void PeekPduType_Null_ReturnsNegative1()
    {
        Assert.AreEqual(-1, MbftCodec.PeekPduType(null));
        Assert.AreEqual(-1, MbftCodec.PeekPduType(Array.Empty<byte>()));
    }

    [TestMethod]
    public void Decode_UnknownType_ReturnsRawData()
    {
        // Encode a DirectoryRequest (index 9) — not fully decoded
        var enc = new VintageHive.Proxy.NetMeeting.Asn1.PerEncoder();
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_DIRECTORY_REQUEST, FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);
        enc.WriteConstrainedWholeNumber(0, 0, 65535);
        var data = enc.ToArray();

        var msg = MbftCodec.Decode(data);
        Assert.AreEqual(FileTransferConstants.PDU_DIRECTORY_REQUEST, msg.Type);
        Assert.IsNotNull(msg.RawData);
    }
}

// ──────────────────────────────────────────────────────────
//  Full file transfer sequence simulation
// ──────────────────────────────────────────────────────────

[TestClass]
public class FileTransferSequenceTests
{
    [TestMethod]
    public void FullTransfer_SmallFile_OverMcs()
    {
        var fileContent = System.Text.Encoding.UTF8.GetBytes("Hello, NetMeeting file transfer!");

        // 1. Sender: File-OfferPDU on control channel
        var offer = MbftCodec.EncodeFileOffer(new FileOfferPdu
        {
            FileHeader = new FileHeader
            {
                Filename = "greeting.txt",
                Filesize = fileContent.Length
            },
            DataChannelId = 3000,
            FileHandle = 1,
            AckFlag = true
        });

        var offerMcs = McsCodec.EncodeSendDataRequest(1001, 2000,
            McsConstants.PRIORITY_HIGH, offer);

        // Verify MCS round-trip
        var offerPdu = McsCodec.DecodeDomainPdu(offerMcs);
        var offerMsg = MbftCodec.Decode(offerPdu.UserData);
        Assert.AreEqual("greeting.txt", offerMsg.FileOffer.FileHeader.Filename);

        // 2. Receiver: File-AcceptPDU on control channel
        var accept = MbftCodec.EncodeFileAccept(new FileAcceptPdu { FileHandle = 1 });
        var acceptMsg = MbftCodec.Decode(accept);
        Assert.AreEqual(1, acceptMsg.FileAccept.FileHandle);

        // 3. Sender: File-StartPDU on data channel (small file fits in one PDU)
        var start = MbftCodec.EncodeFileStart(new FileStartPdu
        {
            FileHeader = new FileHeader
            {
                Filename = "greeting.txt",
                Filesize = fileContent.Length
            },
            FileHandle = 1,
            EofFlag = true,
            CrcFlag = false,
            DataOffset = 0,
            Data = fileContent
        });

        var startMcs = McsCodec.EncodeSendDataRequest(1001, 3000,
            McsConstants.PRIORITY_HIGH, start);
        var startPdu = McsCodec.DecodeDomainPdu(startMcs);
        var startMsg = MbftCodec.Decode(startPdu.UserData);

        Assert.IsTrue(startMsg.FileStart.EofFlag);
        Assert.AreEqual(fileContent.Length, startMsg.FileStart.Data.Length);
        CollectionAssert.AreEqual(fileContent, startMsg.FileStart.Data);

        // 4. Sender: NonStandard FileEnd notification
        var fileEnd = MbftCodec.EncodeNonStandard(FileTransferConstants.NS_KEY_FILE_END);
        var fileEndMsg = MbftCodec.Decode(fileEnd);
        Assert.AreEqual(FileTransferConstants.NS_KEY_FILE_END, fileEndMsg.NonStandardKey);
    }

    [TestMethod]
    public void MultiChunkTransfer_Simulation()
    {
        // Simulate a file split across multiple data chunks
        var totalSize = 1500;
        var chunkSize = 500;
        var fileData = new byte[totalSize];
        new Random(42).NextBytes(fileData);

        // File-StartPDU with first chunk
        var firstChunk = new byte[chunkSize];
        Array.Copy(fileData, 0, firstChunk, 0, chunkSize);

        var start = MbftCodec.EncodeFileStart(new FileStartPdu
        {
            FileHeader = new FileHeader { Filename = "multi.bin", Filesize = totalSize },
            FileHandle = 1,
            EofFlag = false,
            CrcFlag = true,
            DataOffset = 0,
            Data = firstChunk
        });

        var startMsg = MbftCodec.Decode(start);
        Assert.IsFalse(startMsg.FileStart.EofFlag);
        Assert.IsTrue(startMsg.FileStart.CrcFlag);
        Assert.AreEqual(chunkSize, startMsg.FileStart.Data.Length);

        // File-DataPDU for middle chunk
        var midChunk = new byte[chunkSize];
        Array.Copy(fileData, chunkSize, midChunk, 0, chunkSize);

        var mid = MbftCodec.EncodeFileData(new FileDataPdu
        {
            FileHandle = 1,
            EofFlag = false,
            AbortFlag = false,
            Data = midChunk
        });

        var midMsg = MbftCodec.Decode(mid);
        Assert.IsFalse(midMsg.FileData.EofFlag);
        CollectionAssert.AreEqual(midChunk, midMsg.FileData.Data);

        // File-DataPDU for final chunk with CRC
        var lastChunk = new byte[chunkSize];
        Array.Copy(fileData, chunkSize * 2, lastChunk, 0, chunkSize);

        var last = MbftCodec.EncodeFileData(new FileDataPdu
        {
            FileHandle = 1,
            EofFlag = true,
            AbortFlag = false,
            Data = lastChunk,
            CrcCheck = 0xCAFEBABE
        });

        var lastMsg = MbftCodec.Decode(last);
        Assert.IsTrue(lastMsg.FileData.EofFlag);
        Assert.AreEqual(0xCAFEBABEu, lastMsg.FileData.CrcCheck);

        // Reassemble and verify
        var reassembled = new byte[totalSize];
        Array.Copy(startMsg.FileStart.Data, 0, reassembled, 0, chunkSize);
        Array.Copy(midMsg.FileData.Data, 0, reassembled, chunkSize, chunkSize);
        Array.Copy(lastMsg.FileData.Data, 0, reassembled, chunkSize * 2, chunkSize);
        CollectionAssert.AreEqual(fileData, reassembled);
    }

    [TestMethod]
    public void RejectedTransfer_Simulation()
    {
        // Offer → Reject → no data sent
        var offer = MbftCodec.EncodeFileOffer(new FileOfferPdu
        {
            FileHeader = new FileHeader { Filename = "unwanted.exe", Filesize = 1000000 },
            DataChannelId = 4000,
            FileHandle = 7,
            AckFlag = true
        });

        var offerMsg = MbftCodec.Decode(offer);
        Assert.AreEqual("unwanted.exe", offerMsg.FileOffer.FileHeader.Filename);

        var reject = MbftCodec.EncodeFileReject(new FileRejectPdu
        {
            FileHandle = 7,
            Reason = FileTransferConstants.REJECT_FILE_NOT_REQUIRED
        });

        var rejectMsg = MbftCodec.Decode(reject);
        Assert.AreEqual(7, rejectMsg.FileReject.FileHandle);
        Assert.AreEqual(FileTransferConstants.REJECT_FILE_NOT_REQUIRED, rejectMsg.FileReject.Reason);
    }

    [TestMethod]
    public void AbortedTransfer_Simulation()
    {
        // Mid-transfer abort
        var abort = MbftCodec.EncodeFileAbort(new FileAbortPdu
        {
            Reason = FileTransferConstants.ABORT_BANDWIDTH_REQUIRED,
            DataChannelId = 3000,
            FileHandle = 1
        });

        var abortMsg = MbftCodec.Decode(abort);
        Assert.AreEqual(FileTransferConstants.ABORT_BANDWIDTH_REQUIRED, abortMsg.FileAbort.Reason);
        Assert.AreEqual(3000, abortMsg.FileAbort.DataChannelId);

        // Also test abort via File-DataPDU abort flag
        var abortData = MbftCodec.EncodeFileData(new FileDataPdu
        {
            FileHandle = 1,
            EofFlag = false,
            AbortFlag = true,
            Data = Array.Empty<byte>()
        });

        var abortDataMsg = MbftCodec.Decode(abortData);
        Assert.IsTrue(abortDataMsg.FileData.AbortFlag);
    }
}
