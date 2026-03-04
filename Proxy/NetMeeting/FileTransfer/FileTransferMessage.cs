// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.FileTransfer;

/// <summary>
/// T.127 FileHeader — file metadata carried in File-OfferPDU and File-StartPDU.
///
/// Uses IMPLICIT TAGS with context-specific tag numbers.
/// For our codec we encode the commonly used subset:
///   [0] filename, [4] creation date, [5] modification date, [13] filesize.
/// </summary>
internal class FileHeader
{
    /// <summary>File name (from tag [0] Filename-Attribute).</summary>
    public string Filename { get; init; }

    /// <summary>File size in bytes (from tag [13]).</summary>
    public long? Filesize { get; init; }

    /// <summary>Creation date as GeneralizedTime string (from tag [4]).</summary>
    public string DateCreation { get; init; }

    /// <summary>Last modification date as GeneralizedTime string (from tag [5]).</summary>
    public string DateModification { get; init; }

    public override string ToString() =>
        $"{Filename} ({Filesize?.ToString() ?? "?"} bytes)";
}

/// <summary>
/// File-OfferPDU (MBFTPDU CHOICE index 0) — offers a file to recipient(s).
/// </summary>
internal class FileOfferPdu
{
    public FileHeader FileHeader { get; init; }
    public int DataChannelId { get; init; }
    public int FileHandle { get; init; }
    public int? RosterInstance { get; init; }
    public bool AckFlag { get; init; }
}

/// <summary>
/// File-AcceptPDU (MBFTPDU CHOICE index 1) — accepts a file offer.
/// </summary>
internal class FileAcceptPdu
{
    public int FileHandle { get; init; }
}

/// <summary>
/// File-RejectPDU (MBFTPDU CHOICE index 2) — rejects a file offer.
/// </summary>
internal class FileRejectPdu
{
    public int FileHandle { get; init; }

    /// <summary>Reject reason from FileTransferConstants.REJECT_*.</summary>
    public int Reason { get; init; }
}

/// <summary>
/// File-StartPDU (MBFTPDU CHOICE index 7) — begins data transfer with first chunk.
/// </summary>
internal class FileStartPdu
{
    public FileHeader FileHeader { get; init; }
    public int FileHandle { get; init; }
    public bool EofFlag { get; init; }
    public bool CrcFlag { get; init; }
    public long DataOffset { get; init; }
    public byte[] Data { get; init; }
    public uint? CrcCheck { get; init; }
}

/// <summary>
/// File-DataPDU (MBFTPDU CHOICE index 8) — file data chunk.
/// </summary>
internal class FileDataPdu
{
    public int FileHandle { get; init; }
    public bool EofFlag { get; init; }
    public bool AbortFlag { get; init; }
    public byte[] Data { get; init; }
    public uint? CrcCheck { get; init; }
}

/// <summary>
/// File-AbortPDU (MBFTPDU CHOICE index 6) — aborts a transfer.
/// </summary>
internal class FileAbortPdu
{
    public int Reason { get; init; }
    public int? DataChannelId { get; init; }
    public int? FileHandle { get; init; }
}

/// <summary>
/// File-ErrorPDU (MBFTPDU CHOICE index 5) — reports an error.
/// </summary>
internal class FileErrorPdu
{
    public int? FileHandle { get; init; }
    public int ErrorType { get; init; }
    public int ErrorId { get; init; }
    public string ErrorText { get; init; }
}

/// <summary>
/// Decoded MBFTPDU envelope.
/// </summary>
internal class MbftMessage
{
    /// <summary>MBFTPDU CHOICE index.</summary>
    public int Type { get; init; }

    public FileOfferPdu FileOffer { get; init; }
    public FileAcceptPdu FileAccept { get; init; }
    public FileRejectPdu FileReject { get; init; }
    public FileStartPdu FileStart { get; init; }
    public FileDataPdu FileData { get; init; }
    public FileAbortPdu FileAbort { get; init; }
    public FileErrorPdu FileError { get; init; }

    /// <summary>NonStandard key string (for PDU_NON_STANDARD).</summary>
    public string NonStandardKey { get; init; }

    /// <summary>NonStandard data (for PDU_NON_STANDARD).</summary>
    public byte[] NonStandardData { get; init; }

    /// <summary>Raw bytes for types not fully decoded.</summary>
    public byte[] RawData { get; init; }

    public override string ToString() => FileTransferConstants.PduName(Type);
}

/// <summary>
/// T.127 MBFT PER codec — encodes and decodes file transfer PDUs.
///
/// Uses ASN.1 BASIC ALIGNED PER (ITU-T X.691) via PerEncoder/PerDecoder.
/// </summary>
internal static class MbftCodec
{
    // ──────────────────────────────────────────────────────────
    //  Detection
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Peek at the MBFTPDU CHOICE index from PER-encoded data.
    /// Returns -1 if the data is too short or invalid.
    /// </summary>
    public static int PeekPduType(byte[] data)
    {
        if (data == null || data.Length < 1)
        {
            return -1;
        }

        try
        {
            var dec = new PerDecoder(data);
            var hasExtensions = dec.ReadExtensionBit();

            if (!hasExtensions)
            {
                return (int)dec.ReadConstrainedWholeNumber(0,
                    FileTransferConstants.MBFT_ROOT_COUNT - 1);
            }

            return -1;
        }
        catch
        {
            return -1;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Encode
    // ──────────────────────────────────────────────────────────

    /// <summary>Encode a File-OfferPDU.</summary>
    public static byte[] EncodeFileOffer(FileOfferPdu pdu)
    {
        var enc = new PerEncoder();

        // MBFTPDU CHOICE (extensible, 16 root)
        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_FILE_OFFER,
            FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);

        // File-OfferPDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // Optional bitmap: rosterInstance, fileTransmitToken, fileRequestToken,
        //   fileRequestHandle, mbftID, compressionSpecifier, compressedFilesize
        var hasRoster = pdu.RosterInstance.HasValue;
        enc.WriteOptionalBitmap(hasRoster, false, false, false, false, false, false);

        // file-header
        EncodeFileHeader(enc, pdu.FileHeader);

        // data-channel-id: ChannelID (INTEGER 0..65535)
        enc.WriteConstrainedWholeNumber(pdu.DataChannelId, 0, FileTransferConstants.MAX_CHANNEL_ID);

        // file-handle: Handle (INTEGER 0..65535)
        enc.WriteConstrainedWholeNumber(pdu.FileHandle, 0, FileTransferConstants.MAX_HANDLE);

        // roster-instance OPTIONAL
        if (hasRoster)
        {
            enc.WriteConstrainedWholeNumber(pdu.RosterInstance.Value, 0, 65535);
        }

        // ack-flag: BOOLEAN
        enc.WriteBoolean(pdu.AckFlag);

        return enc.ToArray();
    }

    /// <summary>Encode a File-AcceptPDU.</summary>
    public static byte[] EncodeFileAccept(FileAcceptPdu pdu)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_FILE_ACCEPT,
            FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);

        // File-AcceptPDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // No optional fields in root

        // file-handle
        enc.WriteConstrainedWholeNumber(pdu.FileHandle, 0, FileTransferConstants.MAX_HANDLE);

        return enc.ToArray();
    }

    /// <summary>Encode a File-RejectPDU.</summary>
    public static byte[] EncodeFileReject(FileRejectPdu pdu)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_FILE_REJECT,
            FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);

        // File-RejectPDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // file-handle
        enc.WriteConstrainedWholeNumber(pdu.FileHandle, 0, FileTransferConstants.MAX_HANDLE);

        // reason: ENUMERATED (8 root, extensible)
        enc.WriteEnumerated(pdu.Reason,
            FileTransferConstants.REJECT_ROOT_COUNT, extensible: true, isExtension: false);

        return enc.ToArray();
    }

    /// <summary>Encode a File-StartPDU.</summary>
    public static byte[] EncodeFileStart(FileStartPdu pdu)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_FILE_START,
            FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);

        // File-StartPDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // Optional bitmap: compressionSpecifier, compFilesize, crcCheck
        var hasCrc = pdu.CrcCheck.HasValue;
        enc.WriteOptionalBitmap(false, false, hasCrc);

        // file-header
        EncodeFileHeader(enc, pdu.FileHeader);

        // file-handle
        enc.WriteConstrainedWholeNumber(pdu.FileHandle, 0, FileTransferConstants.MAX_HANDLE);

        // eof-flag: BOOLEAN
        enc.WriteBoolean(pdu.EofFlag);

        // crc-flag: BOOLEAN
        enc.WriteBoolean(pdu.CrcFlag);

        // data-offset: INTEGER (unconstrained)
        enc.WriteUnconstrainedWholeNumber(pdu.DataOffset);

        // data: OCTET STRING (SIZE 0..65535)
        enc.WriteOctetString(pdu.Data ?? Array.Empty<byte>(), 0, FileTransferConstants.MAX_CHUNK_SIZE);

        // crc-check OPTIONAL
        if (hasCrc)
        {
            enc.WriteConstrainedWholeNumber(pdu.CrcCheck.Value, 0, uint.MaxValue);
        }

        return enc.ToArray();
    }

    /// <summary>Encode a File-DataPDU.</summary>
    public static byte[] EncodeFileData(FileDataPdu pdu)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_FILE_DATA,
            FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);

        // File-DataPDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // Optional bitmap: crcCheck
        var hasCrc = pdu.CrcCheck.HasValue;
        enc.WriteOptionalBitmap(hasCrc);

        // file-handle
        enc.WriteConstrainedWholeNumber(pdu.FileHandle, 0, FileTransferConstants.MAX_HANDLE);

        // eof-flag: BOOLEAN
        enc.WriteBoolean(pdu.EofFlag);

        // abort-flag: BOOLEAN
        enc.WriteBoolean(pdu.AbortFlag);

        // data: OCTET STRING (SIZE 0..65535)
        enc.WriteOctetString(pdu.Data ?? Array.Empty<byte>(), 0, FileTransferConstants.MAX_CHUNK_SIZE);

        // crc-check OPTIONAL
        if (hasCrc)
        {
            enc.WriteConstrainedWholeNumber(pdu.CrcCheck.Value, 0, uint.MaxValue);
        }

        return enc.ToArray();
    }

    /// <summary>Encode a File-AbortPDU.</summary>
    public static byte[] EncodeFileAbort(FileAbortPdu pdu)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_FILE_ABORT,
            FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);

        // File-AbortPDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // Optional bitmap: dataChannelId, transmitterUserId, fileHandle
        var hasChannel = pdu.DataChannelId.HasValue;
        var hasHandle = pdu.FileHandle.HasValue;
        enc.WriteOptionalBitmap(hasChannel, false, hasHandle);

        // reason: ENUMERATED (5 root, extensible)
        enc.WriteEnumerated(pdu.Reason,
            FileTransferConstants.ABORT_ROOT_COUNT, extensible: true, isExtension: false);

        // data-channel-id OPTIONAL
        if (hasChannel)
        {
            enc.WriteConstrainedWholeNumber(pdu.DataChannelId.Value, 0, FileTransferConstants.MAX_CHANNEL_ID);
        }

        // file-handle OPTIONAL
        if (hasHandle)
        {
            enc.WriteConstrainedWholeNumber(pdu.FileHandle.Value, 0, FileTransferConstants.MAX_HANDLE);
        }

        return enc.ToArray();
    }

    /// <summary>Encode a File-ErrorPDU.</summary>
    public static byte[] EncodeFileError(FileErrorPdu pdu)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_FILE_ERROR,
            FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);

        // File-ErrorPDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // Optional bitmap: fileHandle, errorText
        var hasHandle = pdu.FileHandle.HasValue;
        var hasText = !string.IsNullOrEmpty(pdu.ErrorText);
        enc.WriteOptionalBitmap(hasHandle, hasText);

        // file-handle OPTIONAL
        if (hasHandle)
        {
            enc.WriteConstrainedWholeNumber(pdu.FileHandle.Value, 0, FileTransferConstants.MAX_HANDLE);
        }

        // error-type: ENUMERATED (3 root, extensible)
        enc.WriteEnumerated(pdu.ErrorType,
            FileTransferConstants.ERROR_TYPE_ROOT_COUNT, extensible: true, isExtension: false);

        // error-id: INTEGER (unconstrained)
        enc.WriteUnconstrainedWholeNumber(pdu.ErrorId);

        // error-text OPTIONAL: TextString = BMPString(SIZE 0..255)
        if (hasText)
        {
            enc.WriteBMPString(pdu.ErrorText, 0, 255);
        }

        return enc.ToArray();
    }

    /// <summary>Encode an MBFT-NonStandardPDU with an H.221 key string.</summary>
    public static byte[] EncodeNonStandard(string key, byte[] data = null)
    {
        var enc = new PerEncoder();

        enc.WriteExtensionBit(false);
        enc.WriteChoiceIndex(FileTransferConstants.PDU_NON_STANDARD,
            FileTransferConstants.MBFT_ROOT_COUNT, extensible: false, isExtension: false);

        // MBFT-NonStandardPDU SEQUENCE (extensible)
        enc.WriteExtensionBit(false);

        // data: NonStandardParameter SEQUENCE { key Key, data OCTET STRING }
        // Key CHOICE: 0=object, 1=h221NonStandard
        enc.WriteChoiceIndex(1, 2, extensible: false, isExtension: false); // h221NonStandard

        // H221NonStandardIdentifier: OCTET STRING (SIZE 4..255)
        var keyBytes = Encoding.ASCII.GetBytes(key);
        enc.WriteOctetString(keyBytes, 4, 255);

        // data: OCTET STRING (unconstrained)
        enc.WriteOctetString(data ?? Array.Empty<byte>(), null, null);

        return enc.ToArray();
    }

    // ──────────────────────────────────────────────────────────
    //  Decode
    // ──────────────────────────────────────────────────────────

    /// <summary>Decode an MBFTPDU from PER-encoded bytes.</summary>
    public static MbftMessage Decode(byte[] data)
    {
        if (data == null || data.Length < 1)
        {
            throw new ArgumentException("MBFT PDU too short");
        }

        var dec = new PerDecoder(data);

        // MBFTPDU CHOICE (extensible, 16 root)
        var hasExtensions = dec.ReadExtensionBit();
        if (hasExtensions)
        {
            return new MbftMessage { Type = -1, RawData = data };
        }

        var pduType = (int)dec.ReadConstrainedWholeNumber(0,
            FileTransferConstants.MBFT_ROOT_COUNT - 1);

        return pduType switch
        {
            FileTransferConstants.PDU_FILE_OFFER => DecodeFileOffer(dec, data),
            FileTransferConstants.PDU_FILE_ACCEPT => DecodeFileAccept(dec, data),
            FileTransferConstants.PDU_FILE_REJECT => DecodeFileReject(dec, data),
            FileTransferConstants.PDU_FILE_START => DecodeFileStart(dec, data),
            FileTransferConstants.PDU_FILE_DATA => DecodeFileData(dec, data),
            FileTransferConstants.PDU_FILE_ABORT => DecodeFileAbort(dec, data),
            FileTransferConstants.PDU_FILE_ERROR => DecodeFileError(dec, data),
            FileTransferConstants.PDU_NON_STANDARD => DecodeNonStandard(dec, data),
            _ => new MbftMessage { Type = pduType, RawData = data }
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Internal decode helpers
    // ──────────────────────────────────────────────────────────

    private static MbftMessage DecodeFileOffer(PerDecoder dec, byte[] rawData)
    {
        // File-OfferPDU SEQUENCE (extensible)
        dec.ReadExtensionBit();

        // Optional bitmap: rosterInstance, fileTransmitToken, fileRequestToken,
        //   fileRequestHandle, mbftID, compressionSpecifier, compressedFilesize
        var opts = dec.ReadOptionalBitmap(7);

        var header = DecodeFileHeader(dec);
        var dataChannelId = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_CHANNEL_ID);
        var fileHandle = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_HANDLE);

        int? rosterInstance = null;
        if (opts[0])
        {
            rosterInstance = (int)dec.ReadConstrainedWholeNumber(0, 65535);
        }

        // Skip fileTransmitToken (opts[1]), fileRequestToken (opts[2]),
        //   fileRequestHandle (opts[3]), mbftID (opts[4]),
        //   compressionSpecifier (opts[5]), compressedFilesize (opts[6])

        var ackFlag = dec.ReadBoolean();

        return new MbftMessage
        {
            Type = FileTransferConstants.PDU_FILE_OFFER,
            FileOffer = new FileOfferPdu
            {
                FileHeader = header,
                DataChannelId = dataChannelId,
                FileHandle = fileHandle,
                RosterInstance = rosterInstance,
                AckFlag = ackFlag
            },
            RawData = rawData
        };
    }

    private static MbftMessage DecodeFileAccept(PerDecoder dec, byte[] rawData)
    {
        dec.ReadExtensionBit();

        var fileHandle = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_HANDLE);

        return new MbftMessage
        {
            Type = FileTransferConstants.PDU_FILE_ACCEPT,
            FileAccept = new FileAcceptPdu { FileHandle = fileHandle },
            RawData = rawData
        };
    }

    private static MbftMessage DecodeFileReject(PerDecoder dec, byte[] rawData)
    {
        dec.ReadExtensionBit();

        var fileHandle = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_HANDLE);
        var reason = dec.ReadEnumerated(FileTransferConstants.REJECT_ROOT_COUNT, extensible: true);

        return new MbftMessage
        {
            Type = FileTransferConstants.PDU_FILE_REJECT,
            FileReject = new FileRejectPdu
            {
                FileHandle = fileHandle,
                Reason = reason
            },
            RawData = rawData
        };
    }

    private static MbftMessage DecodeFileStart(PerDecoder dec, byte[] rawData)
    {
        dec.ReadExtensionBit();

        // Optional bitmap: compressionSpecifier, compFilesize, crcCheck
        var opts = dec.ReadOptionalBitmap(3);

        var header = DecodeFileHeader(dec);
        var fileHandle = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_HANDLE);
        var eofFlag = dec.ReadBoolean();
        var crcFlag = dec.ReadBoolean();

        // Skip compressionSpecifier (opts[0]), compFilesize (opts[1])

        var dataOffset = dec.ReadUnconstrainedWholeNumber();
        var data = dec.ReadOctetString(0, FileTransferConstants.MAX_CHUNK_SIZE);

        uint? crcCheck = null;
        if (opts[2])
        {
            crcCheck = (uint)dec.ReadConstrainedWholeNumber(0, uint.MaxValue);
        }

        return new MbftMessage
        {
            Type = FileTransferConstants.PDU_FILE_START,
            FileStart = new FileStartPdu
            {
                FileHeader = header,
                FileHandle = fileHandle,
                EofFlag = eofFlag,
                CrcFlag = crcFlag,
                DataOffset = dataOffset,
                Data = data,
                CrcCheck = crcCheck
            },
            RawData = rawData
        };
    }

    private static MbftMessage DecodeFileData(PerDecoder dec, byte[] rawData)
    {
        dec.ReadExtensionBit();

        // Optional bitmap: crcCheck
        var opts = dec.ReadOptionalBitmap(1);

        var fileHandle = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_HANDLE);
        var eofFlag = dec.ReadBoolean();
        var abortFlag = dec.ReadBoolean();
        var data = dec.ReadOctetString(0, FileTransferConstants.MAX_CHUNK_SIZE);

        uint? crcCheck = null;
        if (opts[0])
        {
            crcCheck = (uint)dec.ReadConstrainedWholeNumber(0, uint.MaxValue);
        }

        return new MbftMessage
        {
            Type = FileTransferConstants.PDU_FILE_DATA,
            FileData = new FileDataPdu
            {
                FileHandle = fileHandle,
                EofFlag = eofFlag,
                AbortFlag = abortFlag,
                Data = data,
                CrcCheck = crcCheck
            },
            RawData = rawData
        };
    }

    private static MbftMessage DecodeFileAbort(PerDecoder dec, byte[] rawData)
    {
        dec.ReadExtensionBit();

        // Optional bitmap: dataChannelId, transmitterUserId, fileHandle
        var opts = dec.ReadOptionalBitmap(3);

        var reason = dec.ReadEnumerated(FileTransferConstants.ABORT_ROOT_COUNT, extensible: true);

        int? dataChannelId = null;
        if (opts[0])
        {
            dataChannelId = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_CHANNEL_ID);
        }

        // Skip transmitterUserId (opts[1])

        int? fileHandle = null;
        if (opts[2])
        {
            fileHandle = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_HANDLE);
        }

        return new MbftMessage
        {
            Type = FileTransferConstants.PDU_FILE_ABORT,
            FileAbort = new FileAbortPdu
            {
                Reason = reason,
                DataChannelId = dataChannelId,
                FileHandle = fileHandle
            },
            RawData = rawData
        };
    }

    private static MbftMessage DecodeFileError(PerDecoder dec, byte[] rawData)
    {
        dec.ReadExtensionBit();

        // Optional bitmap: fileHandle, errorText
        var opts = dec.ReadOptionalBitmap(2);

        int? fileHandle = null;
        if (opts[0])
        {
            fileHandle = (int)dec.ReadConstrainedWholeNumber(0, FileTransferConstants.MAX_HANDLE);
        }

        var errorType = dec.ReadEnumerated(FileTransferConstants.ERROR_TYPE_ROOT_COUNT, extensible: true);
        var errorId = (int)dec.ReadUnconstrainedWholeNumber();

        string errorText = null;
        if (opts[1])
        {
            errorText = dec.ReadBMPString(0, 255);
        }

        return new MbftMessage
        {
            Type = FileTransferConstants.PDU_FILE_ERROR,
            FileError = new FileErrorPdu
            {
                FileHandle = fileHandle,
                ErrorType = errorType,
                ErrorId = errorId,
                ErrorText = errorText
            },
            RawData = rawData
        };
    }

    private static MbftMessage DecodeNonStandard(PerDecoder dec, byte[] rawData)
    {
        dec.ReadExtensionBit();

        // NonStandardParameter SEQUENCE { key Key, data OCTET STRING }
        // Key CHOICE (2 root)
        var keyChoice = (int)dec.ReadConstrainedWholeNumber(0, 1);

        string key = null;
        if (keyChoice == 1) // h221NonStandard
        {
            var keyBytes = dec.ReadOctetString(4, 255);
            key = Encoding.ASCII.GetString(keyBytes);
        }

        var nsData = dec.ReadOctetString(null, null);

        return new MbftMessage
        {
            Type = FileTransferConstants.PDU_NON_STANDARD,
            NonStandardKey = key,
            NonStandardData = nsData,
            RawData = rawData
        };
    }

    // ──────────────────────────────────────────────────────────
    //  FileHeader encode/decode
    // ──────────────────────────────────────────────────────────

    private static void EncodeFileHeader(PerEncoder enc, FileHeader header)
    {
        // FileHeader SEQUENCE (extensible, 24 optional fields)
        enc.WriteExtensionBit(false);

        // Build optional bitmap for the 24 fields
        // We support: [0] filename, [4] dateCreation, [5] dateModification, [13] filesize
        var optBits = new bool[FileTransferConstants.FH_OPTIONAL_COUNT];

        var hasFilename = !string.IsNullOrEmpty(header.Filename);
        var hasDateCreation = !string.IsNullOrEmpty(header.DateCreation);
        var hasDateMod = !string.IsNullOrEmpty(header.DateModification);
        var hasFilesize = header.Filesize.HasValue;

        // Map to bitmap positions:
        // The FileHeader has tags [0]-[29] with gaps. The optional bitmap
        // covers the OPTIONAL fields in declaration order.
        // Simplified: filename=0, permittedActions=1, contentsType=2, storageAccount=3,
        //   dateCreation=4, dateModification=5, dateReadAccess=6,
        //   identityCreator=7, identityModifier=8, identityReader=9,
        //   filesize=10, futureFilesize=11, accessControl=12, legalQualifications=13,
        //   privateUse=14, structure=15, applicationReference=16, machine=17,
        //   operatingSystem=18, recipient=19, characterSet=20, compression=21,
        //   environment=22, pathname=23
        optBits[0] = hasFilename;        // filename
        optBits[4] = hasDateCreation;     // date-and-time-of-creation
        optBits[5] = hasDateMod;          // date-and-time-of-last-modification
        optBits[10] = hasFilesize;        // filesize

        enc.WriteOptionalBitmap(optBits);

        // protocol-version DEFAULT — present bit = false means default
        // We don't write it (the decoder will use the default)

        // filename [0] OPTIONAL: Filename-Attribute = SEQUENCE OF GraphicString
        if (hasFilename)
        {
            // SEQUENCE OF with 1 element
            enc.WriteLengthDeterminant(1);
            // GraphicString — encode as IA5String (subset of GraphicString)
            enc.WriteIA5String(header.Filename, null, null);
        }

        // dateCreation [4] OPTIONAL: GeneralizedTime
        if (hasDateCreation)
        {
            var gtBytes = Encoding.ASCII.GetBytes(header.DateCreation);
            enc.WriteOctetString(gtBytes, null, null);
        }

        // dateModification [5] OPTIONAL: GeneralizedTime
        if (hasDateMod)
        {
            var gtBytes = Encoding.ASCII.GetBytes(header.DateModification);
            enc.WriteOctetString(gtBytes, null, null);
        }

        // filesize [13] OPTIONAL: INTEGER (unconstrained)
        if (hasFilesize)
        {
            enc.WriteUnconstrainedWholeNumber(header.Filesize.Value);
        }
    }

    private static FileHeader DecodeFileHeader(PerDecoder dec)
    {
        // FileHeader SEQUENCE (extensible)
        dec.ReadExtensionBit();

        // Optional bitmap (24 fields)
        var opts = dec.ReadOptionalBitmap(FileTransferConstants.FH_OPTIONAL_COUNT);

        string filename = null;
        string dateCreation = null;
        string dateModification = null;
        long? filesize = null;

        // filename [0]
        if (opts[0])
        {
            var count = (int)dec.ReadLengthDeterminant();
            if (count > 0)
            {
                filename = dec.ReadIA5String(null, null);
                // Skip additional elements
                for (var i = 1; i < count; i++)
                {
                    dec.ReadIA5String(null, null);
                }
            }
        }

        // permittedActions [1]
        if (opts[1])
        {
            dec.ReadOctetString(null, null); // Skip
        }

        // contentsType [2]
        if (opts[2])
        {
            dec.ReadOctetString(null, null); // Skip
        }

        // storageAccount [3]
        if (opts[3])
        {
            dec.ReadIA5String(null, null); // Skip
        }

        // dateCreation [4]
        if (opts[4])
        {
            var gtBytes = dec.ReadOctetString(null, null);
            dateCreation = Encoding.ASCII.GetString(gtBytes);
        }

        // dateModification [5]
        if (opts[5])
        {
            var gtBytes = dec.ReadOctetString(null, null);
            dateModification = Encoding.ASCII.GetString(gtBytes);
        }

        // dateReadAccess [6]
        if (opts[6])
        {
            dec.ReadOctetString(null, null); // Skip
        }

        // identityCreator [7]
        if (opts[7])
        {
            dec.ReadIA5String(null, null); // Skip
        }

        // identityModifier [8]
        if (opts[8])
        {
            dec.ReadIA5String(null, null); // Skip
        }

        // identityReader [9]
        if (opts[9])
        {
            dec.ReadIA5String(null, null); // Skip
        }

        // filesize [10]
        if (opts[10])
        {
            filesize = dec.ReadUnconstrainedWholeNumber();
        }

        // futureFilesize [11]
        if (opts[11])
        {
            dec.ReadUnconstrainedWholeNumber(); // Skip
        }

        // Skip remaining optional fields [12]-[23]
        for (var i = 12; i < FileTransferConstants.FH_OPTIONAL_COUNT; i++)
        {
            if (opts[i])
            {
                dec.ReadOctetString(null, null); // Skip generically
            }
        }

        return new FileHeader
        {
            Filename = filename,
            Filesize = filesize,
            DateCreation = dateCreation,
            DateModification = dateModification
        };
    }
}
