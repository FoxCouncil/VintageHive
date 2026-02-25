// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors.LocalServer.Streaming;

internal static class RadioMmshStreaming
{
    private const string LogSys = "MMSH";
    private const string LogSession = "STREAM";

    // ===================================================================
    // ASF GUIDs
    // ===================================================================

    private static readonly byte[] AsfFilePropertiesGuid =
    {
        0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11,
        0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
    };

    private static readonly byte[] AsfContentDescriptionGuid =
    {
        0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
        0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
    };

    private static readonly byte[] AsfExtendedContentDescriptionGuid =
    {
        0x40, 0xA4, 0xD0, 0xD2, 0x07, 0xE3, 0xD2, 0x11,
        0x97, 0xF0, 0x00, 0xA0, 0xC9, 0x5E, 0xA8, 0x50
    };

    // ASF Stream Properties Object GUID
    private static readonly byte[] AsfStreamPropertiesGuid =
    {
        0x91, 0x07, 0xDC, 0xB7, 0xB7, 0xA9, 0xCF, 0x11,
        0x8E, 0xE6, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
    };

    // Command Media (Script Command) stream type GUID
    private static readonly byte[] AsfCommandMediaGuid =
    {
        0xC0, 0xCF, 0xDA, 0x59, 0xE6, 0x59, 0xD0, 0x11,
        0xA3, 0xAC, 0x00, 0xA0, 0xC9, 0x03, 0x48, 0xF6
    };

    // No Error Correction GUID (used for non-audio streams)
    private static readonly byte[] AsfNoErrorCorrectionGuid =
    {
        0x00, 0x57, 0xFB, 0x20, 0x55, 0x5B, 0xCF, 0x11,
        0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B
    };

    // ===================================================================
    // ASF utilities
    // ===================================================================

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead));
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    private static int GetAsfPacketSize(byte[] headerData)
    {
        for (int i = 0; i <= headerData.Length - 100; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerData[i + j] != AsfFilePropertiesGuid[j]) { match = false; break; }
            }
            if (match)
            {
                // Min Data Packet Size is at offset 92 from start of File Properties Object
                return BitConverter.ToInt32(headerData, i + 92);
            }
        }
        throw new InvalidOperationException("ASF File Properties Object not found in header");
    }

    /// <summary>
    /// Build an MMSH framed packet per MS-WMSP sections 2.2.3.1.1 + 2.2.3.1.2.
    /// Layout: [Framing Header 4 bytes] [MMS Data Packet: 8 byte header + payload]
    /// </summary>
    private static byte[] BuildMmshChunk(byte chunkType, uint locationId, byte incarnation, byte afFlags, byte[] payload)
    {
        int mmsPacketSize = payload.Length + 8; // MMS header (8) + payload
        var chunk = new byte[4 + mmsPacketSize];

        // Framing Header (4 bytes) — B=1 means no Reason field (matches real WMS)
        chunk[0] = 0xA4;       // B-bit(1) | FrameHeader(0x24) = 0xA4
        chunk[1] = chunkType;  // 'H'=0x48, 'D'=0x44, 'M'=0x4D
        BitConverter.GetBytes((ushort)mmsPacketSize).CopyTo(chunk, 2); // PacketLength

        // MMS Data Packet header (8 bytes)
        BitConverter.GetBytes(locationId).CopyTo(chunk, 4);            // LocationId
        chunk[8] = incarnation;                                         // Incarnation
        chunk[9] = afFlags;                                             // AFFlags
        BitConverter.GetBytes((ushort)mmsPacketSize).CopyTo(chunk, 10); // PacketSize = total MMS packet size

        // Payload
        Buffer.BlockCopy(payload, 0, chunk, 12, payload.Length);
        return chunk;
    }

    /// <summary>
    /// Build an MMSH $M (Metadata) chunk with playlist/broadcast info.
    /// Matches Cougar/9.00.00.3372 format exactly:
    ///   header_line \0 name_len,name,type,value_len,value,... \r\n
    /// Entries are comma-separated with character-count prefixes on names.
    /// </summary>
    private static byte[] BuildMmshMetadataChunk(int playlistGenId, string title = null, string author = null)
    {
        var branding = $"VintageHive/{Mind.ApplicationVersion}";

        var header = $"playlist-gen-id={playlistGenId}, broadcast-id=1, features=\"broadcast\"";

        // Each entry: name_len,name,type,value_len,value
        // type 31 = string, type 3 = DWORD
        // Order and entries match real WMS Cougar/9.00.00.3372 capture exactly:
        //   language → branding → description → copyright → author → title → offset → duration → copied → url
        var entryList = new List<(string name, int type, string value)>
        {
            ("language", 31, ""),
            ("WMS_CONTENT_DESCRIPTION_SERVER_BRANDING_INFO", 31, branding),
            ("WMS_CONTENT_DESCRIPTION_DESCRIPTION", 31, ""),
            ("copyright", 31, ""),
        };

        // clientData attributes use short names per WMS spec — NOT WMS_CONTENT_DESCRIPTION_* prefix
        // Real WMS sends: 6,author,31,13,Static_Author then 5,title,31,12,Static_Title
        entryList.Add(("author", 31, author ?? ""));
        entryList.Add(("title", 31, title ?? ""));

        entryList.Add(("WMS_CONTENT_DESCRIPTION_PLAYLIST_ENTRY_START_OFFSET", 3, "3000"));
        entryList.Add(("WMS_CONTENT_DESCRIPTION_PLAYLIST_ENTRY_DURATION", 3, "0"));
        entryList.Add(("WMS_CONTENT_DESCRIPTION_COPIED_METADATA_FROM_PLAYLIST_FILE", 3, "1"));
        entryList.Add(("WMS_CONTENT_DESCRIPTION_PLAYLIST_ENTRY_URL", 31, "Push:*"));

        var entries = entryList.ToArray();

        var formatted = entries.Select(e => $"{e.name.Length},{e.name},{e.type},{e.value.Length},{e.value}");
        var metadataText = header + "\0" + string.Join(",", formatted) + "\r\n";
        var payload = Encoding.ASCII.GetBytes(metadataText);

        return BuildMmshChunk(0x4D, 0, 0, 0x0C, payload);
    }

    /// <summary>
    /// Write data as an HTTP chunked transfer encoding chunk.
    /// Format: {hex-length}\r\n{data}\r\n
    /// </summary>
    private static async Task WriteHttpChunkAsync(Stream socket, byte[] data)
    {
        var header = Encoding.ASCII.GetBytes($"{data.Length:X}\r\n");
        await socket.WriteAsync(header);
        await socket.WriteAsync(data);
        await socket.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
        await socket.FlushAsync();
    }

    /// <summary>
    /// Write the terminal HTTP chunk (0\r\n\r\n) to signal end of chunked transfer.
    /// </summary>
    private static async Task WriteHttpChunkEndAsync(Stream socket)
    {
        await socket.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"));
        await socket.FlushAsync();
    }

    /// <summary>
    /// Patch ASF File Properties Object for live streaming compatibility with WMP 6.4.
    /// Sets broadcast flag, clears seekable, and fills in sensible non-zero values
    /// for fields that WMP 6.4 needs to allocate decoder buffers.
    /// </summary>
    private static void PatchAsfHeaderForBroadcast(byte[] headerData, int packetSize)
    {
        // Find File Properties Object by its GUID
        for (int i = 0; i <= headerData.Length - 104; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerData[i + j] != AsfFilePropertiesGuid[j]) { match = false; break; }
            }
            if (!match) continue;

            // File Size: 2GB (large dummy value so WMP allocates buffers)
            BitConverter.GetBytes((long)0x7FFFFFFF).CopyTo(headerData, i + 40);

            // Data Packets Count: large number
            BitConverter.GetBytes((long)0xFFFF).CopyTo(headerData, i + 56);

            // Play Duration: ~24 hours in 100-nanosecond units
            BitConverter.GetBytes(24L * 3600 * 10_000_000).CopyTo(headerData, i + 64);

            // Send Duration: same
            BitConverter.GetBytes(24L * 3600 * 10_000_000).CopyTo(headerData, i + 72);

            // Preroll: 3000ms
            BitConverter.GetBytes((uint)3000).CopyTo(headerData, i + 80);

            // Flags: broadcast=1, seekable=0
            BitConverter.GetBytes((uint)0x01).CopyTo(headerData, i + 88);

            // Ensure packet sizes are set
            if (BitConverter.ToInt32(headerData, i + 92) == 0)
                BitConverter.GetBytes(packetSize).CopyTo(headerData, i + 92);
            if (BitConverter.ToInt32(headerData, i + 96) == 0)
                BitConverter.GetBytes(packetSize).CopyTo(headerData, i + 96);

            // Max Bitrate: ensure non-zero
            if (BitConverter.ToInt32(headerData, i + 100) == 0)
                BitConverter.GetBytes(128000).CopyTo(headerData, i + 100);

            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "Patched ASF File Properties: fileSize=2GB, packets=65535, preroll=3000ms, broadcast=1");
            return;
        }
    }

    /// <summary>
    /// Create a WMP 9-specific $H chunk by cloning and patching the shared one.
    /// Values match real Cougar/9.00.00.3372 capture:
    ///   File Size = header size (non-zero), Data Packets = 0xFFFFFFFF (unlimited),
    ///   Preroll = 5000ms, Flags = 0x09 (broadcast + bit 3).
    /// </summary>
    private static byte[] PatchHChunkForWmp9(byte[] hChunk)
    {
        var patched = (byte[])hChunk.Clone();

        for (int i = 12; i <= patched.Length - 104; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (patched[i + j] != AsfFilePropertiesGuid[j]) { match = false; break; }
            }
            if (!match) continue;

            // File Size: ASF payload size (exclude 12-byte MMSH framing + MMS header)
            // Real WMS uses the actual ASF content size, not the MMSH chunk size.
            BitConverter.GetBytes((long)(hChunk.Length - 12)).CopyTo(patched, i + 40);

            // Data Packets Count: 0xFFFFFFFF = unlimited/unknown (NOT zero!)
            // This is the critical value — zero tells WMP9 there's nothing to play.
            BitConverter.GetBytes((long)0xFFFFFFFF).CopyTo(patched, i + 56);

            // Play Duration: 0 (live broadcast)
            BitConverter.GetBytes((long)0).CopyTo(patched, i + 64);

            // Send Duration: 0 (live broadcast)
            BitConverter.GetBytes((long)0).CopyTo(patched, i + 72);

            // Preroll: 3000ms (matches $M WMS_CONTENT_DESCRIPTION_PLAYLIST_ENTRY_START_OFFSET and real WMS)
            BitConverter.GetBytes((long)3000).CopyTo(patched, i + 80);

            // Flags: 0x09 (broadcast=1 + bit 3, matching real WMS capture)
            BitConverter.GetBytes((uint)0x09).CopyTo(patched, i + 88);

            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP9: patched $H File Properties (fileSize={hChunk.Length}, packets=0xFFFFFFFF, preroll=3000, flags=0x09)");

            // Data Object: match real Cougar capture (Size=50, TotalDataPackets=0)
            long asfHeaderSize = BitConverter.ToInt64(patched, 12 + 16);
            int dataObjStart = 12 + (int)asfHeaderSize;
            if (dataObjStart + 50 <= patched.Length)
            {
                BitConverter.GetBytes((long)50).CopyTo(patched, dataObjStart + 16);   // Data Object Size = 50 bytes (header only)
                BitConverter.GetBytes((long)0).CopyTo(patched, dataObjStart + 40);    // TotalDataPackets = 0 (live broadcast)
                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "WMP9: Data Object Size=50, TotalDataPackets=0");
            }

            return patched;
        }

        return patched;
    }

    /// <summary>
    /// Build an ASF Content Description Object with the given metadata strings.
    /// </summary>
    private static byte[] BuildAsfContentDescription(
        string title, string author, string copyright, string description, string rating)
    {
        static byte[] EncodeField(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            var bytes = Encoding.Unicode.GetBytes(s);
            var result = new byte[bytes.Length + 2];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }

        var titleBytes = EncodeField(title);
        var authorBytes = EncodeField(author);
        var copyrightBytes = EncodeField(copyright);
        var descBytes = EncodeField(description);
        var ratingBytes = EncodeField(rating);

        long objectSize = 16 + 8 + 10 + titleBytes.Length + authorBytes.Length +
                          copyrightBytes.Length + descBytes.Length + ratingBytes.Length;

        var obj = new byte[objectSize];
        int pos = 0;

        Buffer.BlockCopy(AsfContentDescriptionGuid, 0, obj, pos, 16); pos += 16;
        BitConverter.GetBytes(objectSize).CopyTo(obj, pos); pos += 8;

        BitConverter.GetBytes((ushort)titleBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)authorBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)copyrightBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)descBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)ratingBytes.Length).CopyTo(obj, pos); pos += 2;

        Buffer.BlockCopy(titleBytes, 0, obj, pos, titleBytes.Length); pos += titleBytes.Length;
        Buffer.BlockCopy(authorBytes, 0, obj, pos, authorBytes.Length); pos += authorBytes.Length;
        Buffer.BlockCopy(copyrightBytes, 0, obj, pos, copyrightBytes.Length); pos += copyrightBytes.Length;
        Buffer.BlockCopy(descBytes, 0, obj, pos, descBytes.Length); pos += descBytes.Length;
        Buffer.BlockCopy(ratingBytes, 0, obj, pos, ratingBytes.Length);

        return obj;
    }

    /// <summary>
    /// Build an ASF Stream Properties Object declaring a Script Command stream (stream 2).
    /// WMP uses this to subscribe to TEXT/CAPTION payloads for live track updates.
    /// </summary>
    private static byte[] BuildAsfScriptStreamProperties()
    {
        // Real WMS sends Type-Specific Data Length = 0 (no command type names in header).
        // WMP9 discovers the command type ("TEXT") from the script data packets themselves.
        int objectSize = 24 + 16 + 16 + 8 + 4 + 4 + 2 + 4; // no type-specific data
        var obj = new byte[objectSize];
        int pos = 0;

        // Object GUID
        Buffer.BlockCopy(AsfStreamPropertiesGuid, 0, obj, pos, 16); pos += 16;
        // Object Size
        BitConverter.GetBytes((long)objectSize).CopyTo(obj, pos); pos += 8;
        // Stream Type = Command Media
        Buffer.BlockCopy(AsfCommandMediaGuid, 0, obj, pos, 16); pos += 16;
        // Error Correction Type = No Error Correction
        Buffer.BlockCopy(AsfNoErrorCorrectionGuid, 0, obj, pos, 16); pos += 16;
        // Time Offset = 0
        BitConverter.GetBytes((long)0).CopyTo(obj, pos); pos += 8;
        // Type-Specific Data Length = 0
        BitConverter.GetBytes(0).CopyTo(obj, pos); pos += 4;
        // Error Correction Data Length = 0
        BitConverter.GetBytes(0).CopyTo(obj, pos); pos += 4;
        // Flags: stream number 2, no encrypted content
        BitConverter.GetBytes((ushort)2).CopyTo(obj, pos); pos += 2;
        // Reserved
        BitConverter.GetBytes(0).CopyTo(obj, pos); pos += 4;

        return obj;
    }

    /// <summary>
    /// Build an ASF data packet containing a TEXT script command for stream 2.
    /// Format:
    ///   - Compressed payload (Rep Data Len = 1, Offset = Presentation Time)
    ///   - Sub-payload with 1-byte length prefix
    ///   - Body = null-terminated UTF-16LE strings: "TEXT\0" + "value\0"
    /// When targetPacketSize > 0, the packet is padded with zeros to match the
    /// declared ASF MaxPacketSize. MMSH requires this because WMP9 expects all
    /// $D payloads to be the fixed broadcast packet size. MMS/TCP callers pass 0
    /// for compact (unpadded) packets since MMS framing handles variable sizes.
    /// </summary>
    internal static byte[] BuildScriptCommandPacket(string trackTitle, uint sendTime, int targetPacketSize = 0, byte mediaObjectNumber = 0)
    {
        // Build script command body: "TEXT\0" + "value\0" (null-terminated UTF-16LE)
        var typeBytes = Encoding.Unicode.GetBytes("TEXT\0");
        var valueBytes = Encoding.Unicode.GetBytes((trackTitle ?? "") + "\0");
        int bodyLen = typeBytes.Length + valueBytes.Length;

        // Compressed payload header: stream(1) + MON(1) + presTime(4) + repDataLen=1(1) + delta(1)
        int payloadHeaderLen = 1 + 1 + 4 + 1 + 1;
        // Sub-payload: dataLen(1) + body
        int subPayloadLen = 1 + bodyLen;

        bool padded = targetPacketSize > 0;

        // Packet header: EC(3) + LTF(1) + PF(1) + pktLen(2) + [paddingLen(2)] + sendTime(4) + duration(2)
        int packetHeaderLen = padded ? 15 : 13; // +2 for WORD padding length field
        int contentSize = packetHeaderLen + payloadHeaderLen + subPayloadLen;
        int paddingLen = padded ? Math.Max(0, targetPacketSize - contentSize) : 0;
        int totalPacketSize = padded ? targetPacketSize : contentSize;

        var packet = new byte[totalPacketSize]; // zeros = implicit padding
        int pos = 0;

        // Error Correction: 0x82 + 2 opaque bytes
        packet[pos++] = 0x82;
        packet[pos++] = 0x00;
        packet[pos++] = 0x00;

        // LTF: bits 5-6 = Packet Length Type (10 = WORD)
        //       bits 3-4 = Padding Length Type (00 = none, 10 = WORD)
        packet[pos++] = padded ? (byte)0x50 : (byte)0x40;
        // Property Flags: 0x5D (BYTE stream, BYTE MON, DWORD offset, BYTE rep data len)
        packet[pos++] = 0x5D;
        // Packet Length (WORD)
        BitConverter.GetBytes((ushort)totalPacketSize).CopyTo(packet, pos); pos += 2;
        // Padding Length (WORD) — only present when padded
        if (padded)
        {
            BitConverter.GetBytes((ushort)paddingLen).CopyTo(packet, pos); pos += 2;
        }
        // Send Time (DWORD)
        BitConverter.GetBytes(sendTime).CopyTo(packet, pos); pos += 4;
        // Duration (WORD) = 0
        BitConverter.GetBytes((ushort)0).CopyTo(packet, pos); pos += 2;

        // Compressed payload header (Rep Data Len = 1 triggers compressed payload mode)
        packet[pos++] = 0x82; // stream 2 | key-frame bit (0x80)
        packet[pos++] = mediaObjectNumber;
        // Offset field = Presentation Time (reinterpreted in compressed payload mode)
        // Real WMS: PresentationTime = SendTime + Preroll(3000)
        BitConverter.GetBytes(sendTime + 3000u).CopyTo(packet, pos); pos += 4;
        packet[pos++] = 0x01; // replicated data length = 1 → compressed payload
        packet[pos++] = 0x00; // presentation time delta = 0

        // Sub-payload: length(1) + body
        packet[pos++] = (byte)bodyLen;
        Buffer.BlockCopy(typeBytes, 0, packet, pos, typeBytes.Length); pos += typeBytes.Length;
        Buffer.BlockCopy(valueBytes, 0, packet, pos, valueBytes.Length);
        // Remaining bytes are already zero (padding)

        return packet;
    }

    /// <summary>
    /// Rebuild the $H chunk with an updated Content Description title.
    /// </summary>
    internal static byte[] RebuildHChunkWithTitle(MmshLiveSession session, string newTitle)
    {
        var hChunk = session.HChunk;

        long asfHeaderSize = BitConverter.ToInt64(hChunk, 12 + 16);
        var headerObj = new byte[asfHeaderSize];
        Buffer.BlockCopy(hChunk, 12, headerObj, 0, (int)asfHeaderSize);

        // 1. Replace Content Description Object (Title field)
        int cdOffset = -1;
        for (int i = 0; i <= headerObj.Length - 24; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerObj[i + j] != AsfContentDescriptionGuid[j]) { match = false; break; }
            }
            if (match) { cdOffset = i; break; }
        }

        if (cdOffset < 0) return hChunk;

        long oldCdSize = BitConverter.ToInt64(headerObj, cdOffset + 16);

        var newCd = BuildAsfContentDescription(
            title: newTitle,
            author: "",
            copyright: "",
            description: "",
            rating: "");

        int beforeCd = cdOffset;
        int afterCdStart = cdOffset + (int)oldCdSize;
        int afterCdLen = (int)asfHeaderSize - afterCdStart;

        long newAsfHeaderSize = beforeCd + newCd.Length + afterCdLen;
        var newHeaderObj = new byte[newAsfHeaderSize];
        Buffer.BlockCopy(headerObj, 0, newHeaderObj, 0, beforeCd);
        Buffer.BlockCopy(newCd, 0, newHeaderObj, beforeCd, newCd.Length);
        Buffer.BlockCopy(headerObj, afterCdStart, newHeaderObj, beforeCd + newCd.Length, afterCdLen);

        BitConverter.GetBytes(newAsfHeaderSize).CopyTo(newHeaderObj, 16);

        // 2. Replace Extended Content Description's WM/Title + WM/Artist
        newHeaderObj = ReplaceExtendedContentDescriptor(newHeaderObj, "WM/Title", newTitle);
        newHeaderObj = ReplaceExtendedContentDescriptor(newHeaderObj, "WM/Artist", "");

        var dataObjHeader = new byte[50];
        Buffer.BlockCopy(hChunk, 12 + (int)asfHeaderSize, dataObjHeader, 0, 50);

        long finalHeaderSize = BitConverter.ToInt64(newHeaderObj, 16); // re-read in case ReplaceExtended changed it
        var hPayload = new byte[finalHeaderSize + 50];
        Buffer.BlockCopy(newHeaderObj, 0, hPayload, 0, (int)finalHeaderSize);
        Buffer.BlockCopy(dataObjHeader, 0, hPayload, (int)finalHeaderSize, 50);

        return BuildMmshChunk(0x48, 0, 0, 0x0C, hPayload);
    }

    /// <summary>
    /// Safely locate the Send Time (4 bytes) and Duration (2 bytes) fields within
    /// a raw ASF data packet starting at <paramref name="offset"/> in the buffer.
    /// </summary>
    internal static bool TryFindAsfSendTimeAndDurationOffsets(
        byte[] buffer, int offset, int length,
        out int sendTimeOffset,
        out int durationOffset)
    {
        sendTimeOffset = -1;
        durationOffset = -1;

        int pos = 0;
        if (length < 4) return false;

        byte ecFlags = buffer[offset + pos++];
        if ((ecFlags & 0x80) != 0)
        {
            int ecLen = ecFlags & 0x0F;
            if (length < pos + ecLen) return false;
            pos += ecLen;
        }

        if (length < pos + 2) return false;

        byte ltf = buffer[offset + pos++];
        pos++; // Property Flags

        static int TypeToSize(int t) => t switch { 1 => 1, 2 => 2, 3 => 4, _ => 0 };

        pos += TypeToSize((ltf >> 1) & 0x03); // Packet Length
        pos += TypeToSize((ltf >> 3) & 0x03); // Padding Length
        pos += TypeToSize((ltf >> 5) & 0x03); // Sequence

        if (length < pos + 6) return false;

        sendTimeOffset = pos;
        durationOffset = pos + 4;
        return true;
    }

    /// <summary>
    /// Build an ASF Extended Content Description Object with arbitrary string descriptors.
    /// Per ASF spec: Object ID (16) + Size (8) + Count (2) + per-descriptor entries.
    /// Each descriptor: Name Length (2) + Name (UTF-16LE null-term) + Value Type (2, 0=string) + Value Length (2) + Value (UTF-16LE null-term).
    /// </summary>
    private static byte[] BuildAsfExtendedContentDescription(params (string name, string value)[] descriptors)
    {
        // Calculate total size
        var encodedDescriptors = new List<(byte[] nameBytes, byte[] valueBytes)>();
        foreach (var (name, value) in descriptors)
        {
            var nameBytes = Encoding.Unicode.GetBytes(name + "\0");
            var valueBytes = Encoding.Unicode.GetBytes((value ?? "") + "\0");
            encodedDescriptors.Add((nameBytes, valueBytes));
        }

        // Object ID (16) + Object Size (8) + Descriptor Count (2)
        long objectSize = 16 + 8 + 2;
        foreach (var (nameBytes, valueBytes) in encodedDescriptors)
        {
            // Name Length (2) + Name + Value Type (2) + Value Length (2) + Value
            objectSize += 2 + nameBytes.Length + 2 + 2 + valueBytes.Length;
        }

        var obj = new byte[objectSize];
        int pos = 0;

        Buffer.BlockCopy(AsfExtendedContentDescriptionGuid, 0, obj, pos, 16); pos += 16;
        BitConverter.GetBytes(objectSize).CopyTo(obj, pos); pos += 8;
        BitConverter.GetBytes((ushort)encodedDescriptors.Count).CopyTo(obj, pos); pos += 2;

        foreach (var (nameBytes, valueBytes) in encodedDescriptors)
        {
            BitConverter.GetBytes((ushort)nameBytes.Length).CopyTo(obj, pos); pos += 2;
            Buffer.BlockCopy(nameBytes, 0, obj, pos, nameBytes.Length); pos += nameBytes.Length;
            BitConverter.GetBytes((ushort)0).CopyTo(obj, pos); pos += 2; // Value Type = 0 (string)
            BitConverter.GetBytes((ushort)valueBytes.Length).CopyTo(obj, pos); pos += 2;
            Buffer.BlockCopy(valueBytes, 0, obj, pos, valueBytes.Length); pos += valueBytes.Length;
        }

        return obj;
    }

    /// <summary>
    /// Replace a single descriptor's value in the Extended Content Description Object.
    /// Finds the descriptor by name and replaces its value in-place, adjusting sizes.
    /// Returns the (possibly resized) header array.
    /// </summary>
    private static byte[] ReplaceExtendedContentDescriptor(byte[] headerObj, string descriptorName, string newValue)
    {
        // Find Extended Content Description Object
        int ecdOffset = -1;
        for (int i = 0; i <= headerObj.Length - 26; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerObj[i + j] != AsfExtendedContentDescriptionGuid[j]) { match = false; break; }
            }
            if (match) { ecdOffset = i; break; }
        }

        if (ecdOffset < 0) return headerObj;

        long ecdSize = BitConverter.ToInt64(headerObj, ecdOffset + 16);
        ushort count = BitConverter.ToUInt16(headerObj, ecdOffset + 24);

        // Walk descriptors to find the target
        int pos = ecdOffset + 26; // after GUID(16) + Size(8) + Count(2)
        int ecdEnd = ecdOffset + (int)ecdSize;
        var targetNameBytes = Encoding.Unicode.GetBytes(descriptorName + "\0");

        for (int d = 0; d < count && pos + 6 <= ecdEnd; d++)
        {
            ushort nameLen = BitConverter.ToUInt16(headerObj, pos);
            int nameStart = pos + 2;

            if (nameStart + nameLen + 4 > ecdEnd) break;

            // Check if this is our target descriptor
            bool nameMatch = nameLen == targetNameBytes.Length;
            if (nameMatch)
            {
                for (int j = 0; j < nameLen; j++)
                {
                    if (headerObj[nameStart + j] != targetNameBytes[j]) { nameMatch = false; break; }
                }
            }

            ushort valueType = BitConverter.ToUInt16(headerObj, nameStart + nameLen);
            ushort oldValueLen = BitConverter.ToUInt16(headerObj, nameStart + nameLen + 2);
            int valueStart = nameStart + nameLen + 4;

            if (nameMatch && valueType == 0) // type 0 = Unicode string
            {
                var newValueBytes = Encoding.Unicode.GetBytes((newValue ?? "") + "\0");
                int sizeDiff = newValueBytes.Length - oldValueLen;

                var result = new byte[headerObj.Length + sizeDiff];
                Buffer.BlockCopy(headerObj, 0, result, 0, valueStart);
                Buffer.BlockCopy(newValueBytes, 0, result, valueStart, newValueBytes.Length);
                Buffer.BlockCopy(headerObj, valueStart + oldValueLen, result, valueStart + newValueBytes.Length,
                    headerObj.Length - valueStart - oldValueLen);

                // Update value length
                BitConverter.GetBytes((ushort)newValueBytes.Length).CopyTo(result, nameStart + nameLen + 2);
                // Update ECD object size
                BitConverter.GetBytes(ecdSize + sizeDiff).CopyTo(result, ecdOffset + 16);
                // Update ASF Header Object size
                long asfSize = BitConverter.ToInt64(result, 16);
                BitConverter.GetBytes(asfSize + sizeDiff).CopyTo(result, 16);

                return result;
            }

            pos = valueStart + oldValueLen;
        }

        return headerObj;
    }

    /// <summary>
    /// Find the existing Extended Content Description Object in the ASF header
    /// and merge additional descriptors into it. Returns a new header array
    /// (which may be larger if descriptors were added).
    /// If no existing object is found, returns the original array unchanged.
    /// </summary>
    private static byte[] MergeExtendedContentDescriptors(byte[] headerObj, List<(string name, string value)> newDescriptors)
    {
        // Find existing Extended Content Description Object
        int ecdOffset = -1;
        for (int i = 0; i <= headerObj.Length - 26; i++)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerObj[i + j] != AsfExtendedContentDescriptionGuid[j]) { match = false; break; }
            }
            if (match) { ecdOffset = i; break; }
        }

        if (ecdOffset < 0)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "MergeECD: no existing Extended Content Description found, skipping");
            return headerObj;
        }

        long oldEcdSize = BitConverter.ToInt64(headerObj, ecdOffset + 16);
        ushort oldCount = BitConverter.ToUInt16(headerObj, ecdOffset + 24);

        // Build additional descriptor bytes
        var additionalBytes = new List<byte>();
        foreach (var (name, value) in newDescriptors)
        {
            var nameBytes = Encoding.Unicode.GetBytes(name + "\0");
            var valueBytes = Encoding.Unicode.GetBytes((value ?? "") + "\0");

            additionalBytes.AddRange(BitConverter.GetBytes((ushort)nameBytes.Length));
            additionalBytes.AddRange(nameBytes);
            additionalBytes.AddRange(BitConverter.GetBytes((ushort)0)); // Value Type = string
            additionalBytes.AddRange(BitConverter.GetBytes((ushort)valueBytes.Length));
            additionalBytes.AddRange(valueBytes);
        }

        var extraBytes = additionalBytes.ToArray();
        long newEcdSize = oldEcdSize + extraBytes.Length;
        ushort newCount = (ushort)(oldCount + newDescriptors.Count);

        // Insert extra descriptor bytes at the end of the existing ECD object
        int insertPos = ecdOffset + (int)oldEcdSize;
        int afterInsert = headerObj.Length - insertPos;

        var newHeader = new byte[headerObj.Length + extraBytes.Length];
        Buffer.BlockCopy(headerObj, 0, newHeader, 0, insertPos);
        Buffer.BlockCopy(extraBytes, 0, newHeader, insertPos, extraBytes.Length);
        Buffer.BlockCopy(headerObj, insertPos, newHeader, insertPos + extraBytes.Length, afterInsert);

        // Update Extended Content Description size and count
        BitConverter.GetBytes(newEcdSize).CopyTo(newHeader, ecdOffset + 16);
        BitConverter.GetBytes(newCount).CopyTo(newHeader, ecdOffset + 24);

        // Update ASF Header Object size (at offset 16 in the ASF header)
        long oldAsfSize = BitConverter.ToInt64(newHeader, 16);
        BitConverter.GetBytes(oldAsfSize + extraBytes.Length).CopyTo(newHeader, 16);

        Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"MergeECD: added {newDescriptors.Count} descriptors (+{extraBytes.Length}B), total {newCount} descriptors");
        return newHeader;
    }

    private static string FormatStationDescription(RadioStationInfo info)
    {
        var parts = new List<string>();
        if (info.Bitrate > 0) parts.Add($"{info.Bitrate}kbps");
        if (!string.IsNullOrEmpty(info.Codec)) parts.Add(info.Codec);
        if (!string.IsNullOrEmpty(info.Tags)) parts.Add(info.Tags);
        return parts.Count > 0 ? string.Join(" | ", parts) : info.Name;
    }

    /// <summary>
    /// Remove an ASF sub-object by GUID from an ASF Header Object byte array.
    /// Returns the modified (possibly shorter) header with the object removed,
    /// or the original array unchanged if the object was not found.
    /// Updates the ASF Header Object's Size and NumObjects fields automatically.
    /// </summary>
    private static byte[] StripAsfObjectByGuid(byte[] headerObj, byte[] objectGuid)
    {
        // ASF Header Object layout: 16 GUID + 8 Size + 4 NumObjects + 1 Reserved1 + 1 Reserved2 = 30 bytes
        // Sub-objects start at offset 30
        int pos = 30;
        while (pos + 24 <= headerObj.Length)
        {
            bool match = true;
            for (int j = 0; j < 16; j++)
            {
                if (headerObj[pos + j] != objectGuid[j]) { match = false; break; }
            }

            long objSize = BitConverter.ToInt64(headerObj, pos + 16);

            if (match)
            {
                var newHeader = new byte[headerObj.Length - (int)objSize];
                Buffer.BlockCopy(headerObj, 0, newHeader, 0, pos);
                Buffer.BlockCopy(headerObj, pos + (int)objSize, newHeader, pos, headerObj.Length - pos - (int)objSize);

                // Update ASF Header Object Size (QWORD at offset 16)
                long oldSize = BitConverter.ToInt64(newHeader, 16);
                BitConverter.GetBytes(oldSize - objSize).CopyTo(newHeader, 16);

                // Decrement NumObjects (DWORD at offset 24)
                uint numObj = BitConverter.ToUInt32(newHeader, 24);
                BitConverter.GetBytes(numObj - 1u).CopyTo(newHeader, 24);

                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"StripAsfObject: removed {objSize}B object at offset {pos}");
                return newHeader;
            }

            pos += (int)objSize;
        }
        return headerObj;
    }

    // ===================================================================
    // FFmpeg process creation
    // ===================================================================

    private static Process CreateWmaFfmpegProcess()
    {
        var process = new Process();
        process.StartInfo.FileName = GetFfmpegExecutablePath();
        // No -re flag: let ffmpeg run as fast as possible to fill the ring buffer.
        // Consumer pacing is handled by WaitForDataAsync when caught up to live position.
        process.StartInfo.Arguments = "-probesize 32768 -analyzeduration 0 -i pipe:0 -fflags nobuffer -flush_packets 1 -map_metadata -1 -c:a adpcm_ms -ar 22050 -ac 1 -block_size 1024 -f asf pipe:1";
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        return process;
    }

    private static string GetFfmpegExecutablePath()
    {
        if (!Environment.Is64BitProcess)
        {
            throw new ApplicationException("Somehow, it's not x64? Everything VintageHive is 64bit. What?");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"libs\ffmpeg.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return @"libs\ffmpeg.osx.intel";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return @"libs\ffmpeg.amd64";
        }

        throw new Exception("Cannot determine operating system!");
    }

    // ===================================================================
    // Shared MMSH live sessions
    // ===================================================================

    private static readonly ConcurrentDictionary<string, MmshLiveSession> _liveSessions = new();
    private static readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    internal class MmshLiveSession : IDisposable
    {
        public byte[] HChunk { get; }
        public int AsfPacketSize { get; }
        public RadioStationInfo Station { get; }

        /// <summary>
        /// Raw ASF header bytes (strips 12-byte MMSH $H framing from HChunk).
        /// </summary>
        public byte[] RawAsfHeader => HChunk.Length > 12 ? HChunk[12..] : Array.Empty<byte>();
        private IcyMetadataStrippingStream _icyStream;

        public string CurrentTrack =>
            _icyStream?.CurrentTrack ?? Station?.CurrentTrack;

        // Track change notification
        private readonly object _trackLock = new();
        private TaskCompletionSource _trackChangeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _lastNotifiedTrack;
        private long _lastTrackChangeTimestamp;

        public Task TrackChangeTask { get { lock (_trackLock) return _trackChangeTcs.Task; } }

        public bool TryGetTrackUpdate(string lastKnown, out string newTrack)
        {
            newTrack = CurrentTrack;
            return newTrack != null && newTrack != lastKnown;
        }

        private const int RingCapacity = 300;
        private readonly byte[][] _ring = new byte[RingCapacity][];
        private long _headSeq;
        private readonly object _lock = new();
        private TaskCompletionSource _newDataTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly HttpClient _httpClient;
        private readonly HttpResponseMessage _upstreamResponse;
        private readonly Process _ffmpeg;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        private int _activeClients;
        private CancellationTokenSource _cleanupCts;

        public bool IsAlive => !_producerTask.IsCompleted && !_disposed;

        public MmshLiveSession(HttpClient httpClient, HttpResponseMessage upstreamResponse,
            Process ffmpeg, byte[] hChunk, int packetSize, Stream ffmpegOutput,
            RadioStationInfo stationInfo, IcyMetadataStrippingStream icyStream = null)
        {
            _httpClient = httpClient;
            _upstreamResponse = upstreamResponse;
            _ffmpeg = ffmpeg;
            HChunk = hChunk;
            AsfPacketSize = packetSize;
            Station = stationInfo;
            _icyStream = icyStream;
            _lastNotifiedTrack = stationInfo?.CurrentTrack;

            if (_icyStream != null)
            {
                _icyStream.TrackChanged += OnTrackChanged;
            }

            _producerTask = Task.Run(() => ProduceLoop(ffmpegOutput, packetSize));
        }

        private void OnTrackChanged(string newTrack)
        {
            var now = Stopwatch.GetTimestamp();
            lock (_trackLock)
            {
                // Debounce: skip if less than 5 seconds since last change
                var elapsedMs = (now - _lastTrackChangeTimestamp) * 1000.0 / Stopwatch.Frequency;
                if (_lastTrackChangeTimestamp != 0 && elapsedMs < 5000) return;

                _lastTrackChangeTimestamp = now;
                _lastNotifiedTrack = newTrack;

                var oldTcs = _trackChangeTcs;
                _trackChangeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                oldTcs.TrySetResult();
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Track changed: \"{newTrack}\"");
        }

        private async Task ProduceLoop(Stream ffmpegOut, int packetSize)
        {
            uint locationId = 0;
            byte afFlags = 0;
            var buf = new byte[packetSize];
            var lastPacketTime = Stopwatch.GetTimestamp();

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    int read = await ReadExactAsync(ffmpegOut, buf, 0, packetSize);
                    if (read == 0) break;
                    if (read < packetSize) Array.Clear(buf, read, packetSize - read);

                    var now = Stopwatch.GetTimestamp();
                    var gapMs = (now - lastPacketTime) * 1000.0 / Stopwatch.Frequency;
                    lastPacketTime = now;

                    // Incarnation=1 for $D packets (matches real Windows Media Server behavior)
                    var dChunk = BuildMmshChunk(0x44, locationId, 1, afFlags, buf);

                    TaskCompletionSource oldTcs;
                    lock (_lock)
                    {
                        _ring[_headSeq % RingCapacity] = dChunk;
                        _headSeq++;
                        oldTcs = _newDataTcs;
                        _newDataTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    oldTcs.TrySetResult();

                    locationId++;
                    afFlags = (byte)(afFlags + 1);

                    if (locationId <= 100 || gapMs > 500)
                    {
                        Log.WriteLine(Log.LEVEL_VERBOSE, LogSession, $"Producer: pkt={locationId} gap={gapMs:F0}ms");
                    }
                    else if (locationId % 500 == 0)
                    {
                        Log.WriteLine(Log.LEVEL_VERBOSE, LogSession, $"Producer: {locationId} packets");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_ERROR, LogSession, $"Producer error: {ex.Message}");
            }
            finally
            {
                lock (_lock) { _newDataTcs.TrySetResult(); }
            }

            Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Producer ended ({_headSeq} total packets)");
        }

        public long LivePosition { get { lock (_lock) return _headSeq; } }

        public (byte[] chunk, long seq) TryRead(long requestedSeq)
        {
            lock (_lock)
            {
                long oldest = Math.Max(0, _headSeq - RingCapacity);
                long seq = Math.Max(requestedSeq, oldest);
                if (seq >= _headSeq) return (null, seq);
                return (_ring[seq % RingCapacity], seq);
            }
        }

        public async Task WaitForDataAsync(long afterSeq)
        {
            Task waitTask;
            lock (_lock)
            {
                if (_headSeq > afterSeq) return;
                waitTask = _newDataTcs.Task;
            }
            await waitTask.WaitAsync(_cts.Token);
        }

        public void AddClient(string stationId)
        {
            var count = Interlocked.Increment(ref _activeClients);
            _cleanupCts?.Cancel();
            _cleanupCts = null;
            Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Station {stationId}: client connected ({count} active)");
        }

        public void RemoveClient(string stationId)
        {
            var count = Interlocked.Decrement(ref _activeClients);
            Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Station {stationId}: client disconnected ({count} active)");
            if (count <= 0)
            {
                Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Station {stationId}: last client left, cleanup in 30s");
                ScheduleCleanup(stationId);
            }
        }

        private void ScheduleCleanup(string stationId)
        {
            var cts = new CancellationTokenSource();
            _cleanupCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
                    if (_activeClients <= 0)
                    {
                        _liveSessions.TryRemove(stationId, out _);
                        Dispose();
                        Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Session for {stationId} cleaned up (no clients for 30s)");
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            try { _ffmpeg.Kill(); } catch { }
            try { _ffmpeg.Dispose(); } catch { }
            try { _upstreamResponse.Dispose(); } catch { }
            try { _httpClient.Dispose(); } catch { }
            _cts.Dispose();
        }
    }

    internal static async Task<MmshLiveSession> GetOrCreateSessionAsync(string stationId)
    {
        if (_liveSessions.TryGetValue(stationId, out var session) && session.IsAlive)
            return session;

        await _sessionCreateLock.WaitAsync();
        try
        {
            if (_liveSessions.TryGetValue(stationId, out session) && session.IsAlive)
                return session;

            var info = await RadioStationResolver.ResolveStation(stationId);
            Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Session creating: {info.Name} ({info.Codec})");

            var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                PlaintextStreamFilter = (filterContext, ct) =>
                    new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
            });
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            httpClient.DefaultRequestHeaders.Add("Icy-MetaData", "1");

            var upstreamResponse = await httpClient.GetAsync(info.StreamUrl, HttpCompletionOption.ResponseHeadersRead);
            var rawUpstreamStream = await upstreamResponse.Content.ReadAsStreamAsync();

            IcyMetadataStrippingStream icyStream = null;
            Stream upstreamStream = rawUpstreamStream;

            IEnumerable<string> metaintValues = null;
            if (!upstreamResponse.Headers.TryGetValues("icy-metaint", out metaintValues))
                upstreamResponse.Content.Headers.TryGetValues("icy-metaint", out metaintValues);

            if (metaintValues != null)
            {
                var metaintStr = metaintValues.FirstOrDefault();
                if (int.TryParse(metaintStr, out int metaInterval) && metaInterval > 0)
                {
                    icyStream = new IcyMetadataStrippingStream(rawUpstreamStream, metaInterval);
                    upstreamStream = icyStream;
                    Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Session: ICY metadata interval={metaInterval}");
                }
            }
            else
            {
                Log.WriteLine(Log.LEVEL_INFO, LogSession, "Session: upstream has no ICY metadata support");
            }

            var ffmpeg = CreateWmaFfmpegProcess();
            ffmpeg.Start();
            _ = Task.Run(async () =>
            {
                using var reader = ffmpeg.StandardError;
                while (await reader.ReadLineAsync() is { } line)
                {
                    Log.WriteLine(Log.LEVEL_VERBOSE, "FFMPEG", line);
                }
            });
            _ = Task.Run(async () =>
            {
                try { await upstreamStream.CopyToAsync(ffmpeg.StandardInput.BaseStream); }
                catch { }
                try { ffmpeg.StandardInput.Close(); } catch { }
            });

            var ffmpegOut = ffmpeg.StandardOutput.BaseStream;

            var headerPrefix = new byte[24];
            if (await ReadExactAsync(ffmpegOut, headerPrefix, 0, 24) < 24)
                throw new InvalidOperationException("Failed to read ASF Header Object prefix");

            long asfHeaderSize = BitConverter.ToInt64(headerPrefix, 16);
            var headerObj = new byte[asfHeaderSize];
            Buffer.BlockCopy(headerPrefix, 0, headerObj, 0, 24);
            if (await ReadExactAsync(ffmpegOut, headerObj, 24, (int)asfHeaderSize - 24) < (int)asfHeaderSize - 24)
                throw new InvalidOperationException("Incomplete ASF Header Object");

            var dataObjHeader = new byte[50];
            if (await ReadExactAsync(ffmpegOut, dataObjHeader, 0, 50) < 50)
                throw new InvalidOperationException("Failed to read ASF Data Object header");

            BitConverter.GetBytes((long)0).CopyTo(dataObjHeader, 16);
            if (BitConverter.ToInt64(dataObjHeader, 40) == 0)
                BitConverter.GetBytes((long)0xFFFF).CopyTo(dataObjHeader, 40);
            dataObjHeader[48] = 0x01; dataObjHeader[49] = 0x01;

            int packetSize = GetAsfPacketSize(headerObj);
            PatchAsfHeaderForBroadcast(headerObj, packetSize);

            // Strip any Content Description and Extended Content Description that ffmpeg wrote.
            // We'll add our own clean versions below — having two of either corrupts the header.
            var preStripSize = headerObj.Length;
            headerObj = StripAsfObjectByGuid(headerObj, AsfContentDescriptionGuid);
            headerObj = StripAsfObjectByGuid(headerObj, AsfExtendedContentDescriptionGuid);
            asfHeaderSize = BitConverter.ToInt64(headerObj, 16);
            Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Session: stripped ffmpeg header {preStripSize}B → {headerObj.Length}B (removed {preStripSize - headerObj.Length}B)");

            // Dump all sub-object GUIDs remaining in the ASF header for debugging
            {
                int objPos = 30;
                int objIdx = 0;
                while (objPos + 24 <= (int)asfHeaderSize)
                {
                    var guidStr = BitConverter.ToString(headerObj, objPos, 16);
                    long objSize = BitConverter.ToInt64(headerObj, objPos + 16);
                    Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Session: ASF sub-object[{objIdx}] at {objPos}: GUID={guidStr} size={objSize}B");
                    objPos += (int)objSize;
                    objIdx++;
                }
            }

            // By now, ffmpeg has consumed ~32KB of upstream audio through the IcyMetadataStrippingStream,
            // so the first ICY metadata block should have been parsed. Use it for the initial title.
            var initialTrack = icyStream?.CurrentTrack ?? info.CurrentTrack ?? "";
            Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Session: initialTrack=\"{initialTrack}\" (icy={icyStream?.CurrentTrack != null})");

            var contentDesc = BuildAsfContentDescription(
                title: initialTrack,
                author: "",
                copyright: "",
                description: "",
                rating: "");

            var extContentDesc = BuildAsfExtendedContentDescription(
                ("WM/Title", initialTrack));

            var scriptStream = BuildAsfScriptStreamProperties();

            var finalHeaderSize = asfHeaderSize + contentDesc.Length + extContentDesc.Length + scriptStream.Length;
            var finalHeaderObj = new byte[finalHeaderSize];
            int copyPos = 0;
            Buffer.BlockCopy(headerObj, 0, finalHeaderObj, copyPos, (int)asfHeaderSize); copyPos += (int)asfHeaderSize;
            Buffer.BlockCopy(contentDesc, 0, finalHeaderObj, copyPos, contentDesc.Length); copyPos += contentDesc.Length;
            Buffer.BlockCopy(extContentDesc, 0, finalHeaderObj, copyPos, extContentDesc.Length); copyPos += extContentDesc.Length;
            Buffer.BlockCopy(scriptStream, 0, finalHeaderObj, copyPos, scriptStream.Length);

            BitConverter.GetBytes(finalHeaderSize).CopyTo(finalHeaderObj, 16);

            uint numObjects = BitConverter.ToUInt32(finalHeaderObj, 24);
            BitConverter.GetBytes(numObjects + 3u).CopyTo(finalHeaderObj, 24); // +3: Content Desc + Ext Content Desc + Script Stream

            Log.WriteLine(Log.LEVEL_DEBUG, LogSession, $"Session: ASF header {finalHeaderSize}B (+CD:{contentDesc.Length}B +Script:{scriptStream.Length}B)");

            var hPayload = new byte[finalHeaderSize + 50];
            Buffer.BlockCopy(finalHeaderObj, 0, hPayload, 0, (int)finalHeaderSize);
            Buffer.BlockCopy(dataObjHeader, 0, hPayload, (int)finalHeaderSize, 50);
            var hChunk = BuildMmshChunk(0x48, 0, 0, 0x0C, hPayload);

            Log.WriteLine(Log.LEVEL_INFO, LogSession, $"Session ready: header={finalHeaderSize}B packetSize={packetSize} $H={hChunk.Length}B");

            var newSession = new MmshLiveSession(httpClient, upstreamResponse, ffmpeg, hChunk, packetSize, ffmpegOut, info, icyStream);

            if (_liveSessions.TryGetValue(stationId, out var old))
                old.Dispose();
            _liveSessions[stationId] = newSession;

            return newSession;
        }
        finally
        {
            _sessionCreateLock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // WMP 6.4 (NSPlayer/4.x) — MMSH handler
    // ═══════════════════════════════════════════════════════════════════

    public static async Task HandleWmp6Stream(HttpRequest request, HttpResponse response, string stationId)
    {
        var traceId = request.ListenerSocket.TraceId.ToString();

        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP6: {request.Method} /stream/wmp/{stationId}.asf", traceId);
        foreach (var h in request.Headers)
            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP6: header {h.Key}: {h.Value}", traceId);

        if (request.Method == "POST")
        {
            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "WMP6: POST log-line acknowledged", traceId);
            response.SetBodyString("", "text/plain");
            return;
        }

        try
        {
            var pragmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (request.Headers.TryGetValue("Pragma", out var pragmaRaw))
            {
                foreach (var item in pragmaRaw.Split(','))
                {
                    var trimmed = item.Trim();
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                        pragmas[trimmed[..eqIdx].Trim()] = trimmed[(eqIdx + 1)..].Trim();
                    else if (!string.IsNullOrEmpty(trimmed))
                        pragmas[trimmed] = "";
                }
            }

            bool isPlay = pragmas.ContainsKey("xPlayStrm") && pragmas["xPlayStrm"] == "1";
            var clientId = (uint)(Math.Abs(stationId.GetHashCode()) % 100000000);
            pragmas.TryGetValue("xClientGUID", out var clientGuid);

            int requestContext = 0;
            if (pragmas.TryGetValue("request-context", out var ctxStr))
                int.TryParse(ctxStr, out requestContext);

            Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP6: mode={(isPlay ? "PLAY" : "DESCRIBE")} ctx={requestContext}", traceId);

            var session = await GetOrCreateSessionAsync(stationId);
            var socket = request.ListenerSocket.Stream;
            var httpVer = request.Version;

            string BuildWmp6Response(int? contentLength = null) =>
                $"{httpVer} 200 OK\r\n" +
                "Server: Cougar/4.1\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                (contentLength.HasValue ? $"Content-Length: {contentLength.Value}\r\n" : "") +
                "Pragma: no-cache\r\n" +
                $"Pragma: client-id={clientId}\r\n" +
                "Pragma: features=\"broadcast\"\r\n" +
                "Pragma: timeout=60000\r\n" +
                "Cache-Control: no-cache\r\n" +
                "\r\n";

            if (!isPlay)
            {
                var resp = BuildWmp6Response(contentLength: session.HChunk.Length);

                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP6: DESCRIBE $H={session.HChunk.Length}B", traceId);

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.WriteAsync(session.HChunk);
                response.Handled = true;
            }
            else
            {
                request.ListenerSocket.IsKeepAlive = false;

                var resp = BuildWmp6Response();

                Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP6: PLAY ctx={requestContext}", traceId);

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));

                byte[] hChunk;
                if (requestContext > 2)
                {
                    var currentTrack = session.CurrentTrack ?? "";
                    hChunk = RebuildHChunkWithTitle(session, currentTrack);
                    Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP6: reconnect rebuilt $H title=\"{currentTrack}\"", traceId);
                }
                else
                {
                    hChunk = session.HChunk;
                }

                await socket.WriteAsync(hChunk);

                long readPos = Math.Max(0, session.LivePosition - 5);
                uint sent = 0;

                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP6: streaming from seq={readPos} live={session.LivePosition}", traceId);

                session.AddClient(stationId);
                try
                {
                    while (session.IsAlive)
                    {
                        var (chunk, seq) = session.TryRead(readPos);
                        if (chunk != null)
                        {
                            await socket.WriteAsync(chunk);
                            readPos = seq + 1;
                            sent++;

                            if (sent % 500 == 0)
                            {
                                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP6: sent {sent} (seq={seq})", traceId);
                            }
                        }
                        else
                        {
                            await session.WaitForDataAsync(readPos);
                        }
                    }
                }
                catch (IOException)
                {
                    Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP6: client disconnected after {sent} packets", traceId);
                }
                catch (OperationCanceledException) { }
                finally
                {
                    session.RemoveClient(stationId);
                }

                response.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Log.WriteException(LogSys, ex, traceId);
            response.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // WMP 9+ (NSPlayer/9.x+) — MMSH handler
    // Replicates Cougar/9.00.00.3372 (Windows Media Services 9.0)
    // Protocol flow captured via Wireshark from real WMS server.
    // ═══════════════════════════════════════════════════════════════════

    private static Dictionary<string, string> ParseMmshPragmas(HttpRequest request)
    {
        var pragmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Headers.TryGetValue("Pragma", out var pragmaRaw))
        {
            foreach (var item in pragmaRaw.Split(','))
            {
                var trimmed = item.Trim();
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                    pragmas[trimmed[..eqIdx].Trim()] = trimmed[(eqIdx + 1)..].Trim();
                else if (!string.IsNullOrEmpty(trimmed))
                    pragmas[trimmed] = "";
            }
        }
        return pragmas;
    }

    /// <summary>
    /// Build a Cougar/9.00.00.3372-style response for non-streaming requests (POST, DESCRIBE).
    /// </summary>
    private static string BuildCougarSimpleResponse(int statusCode, string statusPhrase, string clientId, string dateStr, bool includeContentLength = true)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {statusPhrase}\r\n");
        sb.Append("Server: Cougar/9.00.00.3372\r\n");
        if (statusCode == 204)
            sb.Append("Content-Length: 0\r\n");
        sb.Append($"Date: {dateStr}\r\n");
        sb.Append($"Pragma: no-cache, client-id={clientId}, features=\"broadcast\", timeout=60000\r\n");
        sb.Append("Cache-Control: no-cache\r\n");
        sb.Append("Supported: com.microsoft.wm.srvppair, com.microsoft.wm.sswitch, com.microsoft.wm.predstrm, com.microsoft.wm.fastcache\r\n");
        if (statusCode != 204)
            sb.Append("Content-Length: 0\r\n");
        sb.Append("\r\n");
        return sb.ToString();
    }

    public static async Task HandleWmp9Stream(HttpRequest request, HttpResponse response, string stationId)
    {
        var traceId = request.ListenerSocket.TraceId.ToString();

        Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP9: {request.Method} /stream/wmp/{stationId}.asf", traceId);
        foreach (var h in request.Headers)
            Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP9: header {h.Key}: {h.Value}", traceId);

        var pragmas = ParseMmshPragmas(request);
        var socket = request.ListenerSocket.Stream;
        var dateStr = DateTime.UtcNow.ToString("R");

        // Use client's own client-id if provided, otherwise generate one
        var clientId = pragmas.TryGetValue("client-id", out var cidStr) ? cidStr
            : ((uint)(Math.Abs(stationId.GetHashCode()) % 100000000)).ToString();

        // ═══ POST handling (matches Cougar/9.00.00.3372) ═══
        if (request.Method == "POST")
        {
            bool isStop = pragmas.ContainsKey("xStopStrm") && pragmas["xStopStrm"] == "1";
            bool isLogStats = request.Headers.TryGetValue("Content-Type", out var ct) &&
                              ct.Contains("x-wms-LogStats");

            if (isLogStats && !isStop)
            {
                // LogStats POST → 204 No Content (real Cougar sends this during active stream)
                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, "WMP9: POST LogStats -> 204 No Content", traceId);
                var resp = BuildCougarSimpleResponse(204, "No Content", clientId, dateStr);
                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.FlushAsync();
            }
            else
            {
                // xStopStrm or other POST → 200 OK Content-Length: 0
                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP9: POST {(isStop ? "xStopStrm" : "other")} -> 200 OK", traceId);
                var resp = BuildCougarSimpleResponse(200, "OK", clientId, dateStr);
                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.FlushAsync();
            }

            response.Handled = true;
            return;
        }

        // ═══ GET handling ═══
        try
        {
            bool isPipelineRequest = pragmas.ContainsKey("pipeline-request") && pragmas["pipeline-request"] == "1";
            bool isPlay = pragmas.ContainsKey("xPlayStrm") && pragmas["xPlayStrm"] == "1";
            bool isPlayNext = pragmas.ContainsKey("xPlayNextEntry") && pragmas["xPlayNextEntry"] == "1";

            // ═══ Pipeline Request — WMP9 sends this before PLAY for predstrm stream selection ═══
            // Real Cougar responds with a $T token in a chunked response.
            // Without this, WMP9's pipeline-request falls into DESCRIBE and gets
            // a full ASF header, which confuses the negotiation.
            if (isPipelineRequest && !isPlay && !isPlayNext)
            {
                var httpVer = request.Version ?? "HTTP/1.0";
                bool useChunked = httpVer.Contains("1.1");

                // Check if this is a predstrm request (has stream-switch-entry) → $T response
                // or a follow-up → pipeline-result response
                bool hasSwitchEntry = pragmas.ContainsKey("stream-switch-entry");

                if (hasSwitchEntry)
                {
                    // Predstrm: respond with $T token (4 bytes: 0x24 0x54 0x00 0x00)
                    var resp =
                        $"{httpVer} 200 OK\r\n" +
                        "Server: Cougar/9.00.00.3372\r\n" +
                        $"Date: {dateStr}\r\n" +
                        $"Pragma: no-cache, client-id={clientId}, features=\"broadcast\", timeout=60000\r\n" +
                        "Cache-Control: no-cache, max-age=0, x-wms-stream-type=\"broadcast\", user-public, must-revalidate, x-wms-proxy-split\r\n" +
                        $"Last-Modified: {DateTime.UtcNow.AddSeconds(-17).ToString("R")}\r\n" +
                        (useChunked ? "Transfer-Encoding: chunked\r\n" : "") +
                        "Supported: com.microsoft.wm.srvppair, com.microsoft.wm.sswitch, com.microsoft.wm.predstrm, com.microsoft.wm.fastcache\r\n" +
                        "\r\n";

                    await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));

                    // $T token: exactly 4 bytes
                    var tPacket = new byte[] { 0x24, 0x54, 0x00, 0x00 };
                    if (useChunked)
                    {
                        await WriteHttpChunkAsync(socket, tPacket);
                        await socket.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"));
                    }
                    else
                    {
                        await socket.WriteAsync(tPacket);
                    }
                    await socket.FlushAsync();

                    Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP9: pipeline-request (predstrm) → $T", traceId);
                }
                else
                {
                    // Follow-up pipeline request → pipeline-result response
                    var resp =
                        $"{httpVer} 200 OK\r\n" +
                        "Server: Cougar/9.00.00.3372\r\n" +
                        $"Date: {dateStr}\r\n" +
                        $"Pragma: no-cache, pipeline-result=1, client-id={clientId}, features=\"broadcast\", timeout=60000\r\n" +
                        "Cache-Control: no-cache, max-age=0, x-wms-stream-type=\"broadcast\", user-public, must-revalidate, x-wms-proxy-split\r\n" +
                        $"Last-Modified: {DateTime.UtcNow.AddSeconds(-17).ToString("R")}\r\n" +
                        "Supported: com.microsoft.wm.srvppair, com.microsoft.wm.sswitch, com.microsoft.wm.predstrm, com.microsoft.wm.fastcache\r\n" +
                        "Content-Length: 0\r\n" +
                        "\r\n";

                    await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                    await socket.FlushAsync();

                    Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP9: pipeline-request → pipeline-result=1", traceId);
                }

                response.Handled = true;
                return;
            }

            var session = await GetOrCreateSessionAsync(stationId);

            if (isPlay || isPlayNext)
            {
                // ═══════════════════════════════════════════════════════════
                // PLAY (xPlayStrm=1 or xPlayNextEntry=1)
                // Matches Cougar/9.00.00.3372 chunked streaming response.
                // Chunk sequence: $M → $H → $C → $M → $H → $D $D $D ...
                // ═══════════════════════════════════════════════════════════
                request.ListenerSocket.IsKeepAlive = false;

                const int InitialBufferPackets = 25;
                while (session.IsAlive && session.LivePosition < InitialBufferPackets)
                    await session.WaitForDataAsync(session.LivePosition);

                long readPos = Math.Max(0, session.LivePosition - InitialBufferPackets);
                var wmp9HChunk = PatchHChunkForWmp9(session.HChunk);

                int playlistGenId = 1;
                if (pragmas.TryGetValue("playlist-gen-id", out var genIdStr))
                    int.TryParse(genIdStr, out playlistGenId);

                var lastModified = DateTime.UtcNow.AddSeconds(-17).ToString("R");

                // HTTP/1.0 (through proxy) can't use chunked encoding — write MMSH
                // frames directly. HTTP/1.1 (direct) can use chunked encoding.
                var httpVer = request.Version ?? "HTTP/1.0";
                bool useChunked = httpVer.Contains("1.1");

                // Response headers — match Cougar/9.00.00.3372 capture
                var resp =
                    $"{httpVer} 200 OK\r\n" +
                    "Content-Type: application/x-mms-framed\r\n" +
                    "Server: Cougar/9.00.00.3372\r\n" +
                    $"Date: {dateStr}\r\n" +
                    $"Pragma: no-cache, client-id={clientId}, features=\"broadcast\", timeout=60000, AccelBW=3500000, AccelDuration=2000, Speed=1.000\r\n" +
                    "Cache-Control: no-cache, max-age=0, x-wms-stream-type=\"broadcast\", user-public, must-revalidate, x-wms-proxy-split\r\n" +
                    $"Last-Modified: {lastModified}\r\n" +
                    (useChunked ? "Transfer-Encoding: chunked\r\n" : "") +
                    "Supported: com.microsoft.wm.srvppair, com.microsoft.wm.sswitch, com.microsoft.wm.predstrm, com.microsoft.wm.fastcache\r\n" +
                    "\r\n";

                Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP9: PLAY {(isPlayNext ? "xPlayNextEntry" : "xPlayStrm")} genId={playlistGenId} {httpVer} chunked={useChunked}", traceId);

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));

                // Real WMS sends just ONE $M → ONE $H, then data.
                // No $C stream change on initial connection.
                var currentTrack = session.CurrentTrack ?? "";
                var mChunk = BuildMmshMetadataChunk(playlistGenId, currentTrack);

                if (useChunked) await WriteHttpChunkAsync(socket, mChunk);
                else await socket.WriteAsync(mChunk);

                if (useChunked) await WriteHttpChunkAsync(socket, wmp9HChunk);
                else await socket.WriteAsync(wmp9HChunk);

                await socket.FlushAsync();

                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP9: sent $M({mChunk.Length}B) $H({wmp9HChunk.Length}B) — streaming from seq={readPos} live={session.LivePosition}", traceId);

                // Stream $D data packets — pass through producer's LocationId/AFFlags/SendTime
                // unchanged so the MMS packet timeline stays continuous across reconnects.
                uint sent = 0;
                uint lastLocationIdSent = 0;
                byte lastAfFlagsSent = 0;
                uint lastSendTime = 0;
                var lastKnownTrack = session.CurrentTrack;
                string pendingScriptTitle = null;
                uint packetsSinceLastText = 0;
                const uint TextResendInterval = 50; // re-send TEXT every N audio packets
                byte scriptObjectNumber = 0; // media object number for TEXT stream — must increment per script command

                session.AddClient(stationId);
                try
                {
                    while (session.IsAlive)
                    {
                        var (chunk, seq) = session.TryRead(readPos);
                        if (chunk != null)
                        {
                            // Pass through producer's $D chunk unchanged — LocationId,
                            // AFFlags, and SendTime all stay as the producer wrote them.
                            if (useChunked) await WriteHttpChunkAsync(socket, chunk);
                            else await socket.WriteAsync(chunk);

                            // Track producer's header fields for script command injection
                            lastLocationIdSent = BitConverter.ToUInt32(chunk, 4);
                            lastAfFlagsSent = chunk[9];
                            if (TryFindAsfSendTimeAndDurationOffsets(
                                    chunk, 12, session.AsfPacketSize,
                                    out int stOffset, out _))
                            {
                                lastSendTime = BitConverter.ToUInt32(chunk, 12 + stOffset);
                            }

                            readPos = seq + 1;
                            sent++;
                            packetsSinceLastText++;

                            // Periodic TEXT re-send: keep Now Playing fresh even without track changes
                            if (pendingScriptTitle == null && packetsSinceLastText >= TextResendInterval)
                            {
                                var refreshTrack = session.CurrentTrack;
                                if (!string.IsNullOrEmpty(refreshTrack))
                                    pendingScriptTitle = refreshTrack;
                            }

                            // Inject TEXT $D inline after audio $D
                            if (pendingScriptTitle != null && lastSendTime > 0)
                            {
                                var scriptTitle = pendingScriptTitle;
                                pendingScriptTitle = null;
                                packetsSinceLastText = 0;

                                var scriptPacket = BuildScriptCommandPacket(scriptTitle, lastSendTime, mediaObjectNumber: scriptObjectNumber++);
                                var scriptChunk = BuildMmshChunk(0x44,
                                    lastLocationIdSent + 1, 1,
                                    (byte)(lastAfFlagsSent + 1), scriptPacket);

                                if (useChunked) await WriteHttpChunkAsync(socket, scriptChunk);
                                else await socket.WriteAsync(scriptChunk);

                                Log.WriteLine(Log.LEVEL_INFO, LogSys,
                                    $"MMSH: TEXT \"{scriptTitle}\" ({scriptPacket.Length}B, sendTime={lastSendTime})", traceId);
                            }

                            if (sent <= 50 || sent % 500 == 0)
                            {
                                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP9: $D pkt={sent} seq={seq} loc={lastLocationIdSent}", traceId);
                            }
                        }
                        else
                        {
                            var trackTask = session.TrackChangeTask;
                            var dataTask = session.WaitForDataAsync(readPos);
                            await Task.WhenAny(dataTask, trackTask);

                            if (trackTask.IsCompleted && session.TryGetTrackUpdate(lastKnownTrack, out var newTrack))
                            {
                                lastKnownTrack = newTrack;
                                Log.WriteLine(Log.LEVEL_INFO, LogSys, $"MMSH: track changed to \"{newTrack}\"", traceId);

                                // TEXT only — no $M/$C/$H, those cause decoder reinit and audio glitches
                                pendingScriptTitle = newTrack;
                            }
                        }
                    }
                }
                catch (IOException)
                {
                    Log.WriteLine(Log.LEVEL_INFO, LogSys, $"WMP9: client disconnected after {sent} packets", traceId);
                }
                catch (OperationCanceledException) { }
                finally
                {
                    session.RemoveClient(stationId);
                }

                response.Handled = true;
            }
            else
            {
                // ═══════════════════════════════════════════════════════════
                // DESCRIBE (no xPlayStrm/xPlayNextEntry — ASF header probe)
                // Must echo version11-enabled and experiment pragmas back
                // so WMP9 proceeds to the PLAY request.
                // ═══════════════════════════════════════════════════════════
                var wmp9HChunk = PatchHChunkForWmp9(session.HChunk);

                bool isVersion11 = pragmas.ContainsKey("version11-enabled");

                // Build extra Pragma lines that WMP9 expects echoed back
                var extraPragmas = new StringBuilder();
                if (isVersion11)
                    extraPragmas.Append("Pragma: version11-enabled=1\r\n");
                foreach (var key in new[] { "packet-pair-experiment", "pipeline-experiment" })
                {
                    if (pragmas.TryGetValue(key, out var val))
                        extraPragmas.Append($"Pragma: {key}={val}\r\n");
                }

                var descHttpVer = request.Version ?? "HTTP/1.0";
                var resp =
                    $"{descHttpVer} 200 OK\r\n" +
                    "Content-Type: application/vnd.ms.wms-hdr.asfv1\r\n" +
                    "Server: Cougar/9.00.00.3372\r\n" +
                    $"Date: {dateStr}\r\n" +
                    $"Content-Length: {wmp9HChunk.Length}\r\n" +
                    $"Pragma: no-cache, client-id={clientId}, features=\"broadcast\", timeout=60000\r\n" +
                    extraPragmas.ToString() +
                    "Cache-Control: no-cache\r\n" +
                    "Supported: com.microsoft.wm.srvppair, com.microsoft.wm.sswitch, com.microsoft.wm.predstrm, com.microsoft.wm.fastcache\r\n" +
                    "\r\n";

                Log.WriteLine(Log.LEVEL_DEBUG, LogSys, $"WMP9: DESCRIBE $H={wmp9HChunk.Length}B v11={isVersion11}", traceId);

                await socket.WriteAsync(Encoding.ASCII.GetBytes(resp));
                await socket.WriteAsync(wmp9HChunk);
                await socket.FlushAsync();
                response.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Log.WriteException(LogSys, ex, traceId);
            response.Handled = true;
        }
    }
}
