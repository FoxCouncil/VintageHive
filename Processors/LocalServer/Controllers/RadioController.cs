// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Web;
using static VintageHive.Proxy.Http.HttpUtilities;
using static VintageHive.Utilities.SCUtils;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("radio.hive.com")]
internal class RadioController : Controller
{
    // ===================================================================
    // Station resolution — unified lookup for any station ID
    // UUID (contains '-') = radio-browser, numeric = shoutcast
    // ===================================================================

    private record StationInfo(
        string Name, string Codec, string StreamUrl,
        string Favicon = null, string Homepage = null,
        string Tags = null, string Country = null,
        string CurrentTrack = null, int Bitrate = 0);

    private static async Task<StationInfo> ResolveStation(string id)
    {
        if (id.Contains('-'))
        {
            var station = await Mind.RadioBrowser.StationGetAsync(id);
            return new StationInfo(
                station.Name, station.Codec.ToUpperInvariant(), station.UrlResolved,
                Favicon: station.Favicon, Homepage: station.Homepage,
                Tags: station.Tags, Country: station.Country,
                Bitrate: station.Bitrate);
        }
        else
        {
            var station = await GetStationById(id);
            var codec = GetFormatString(station.Item1.Mt);
            return new StationInfo(
                station.Item1.Name, codec, station.Item2.ToString(),
                Tags: station.Item1.Genre, CurrentTrack: station.Item1.Ct,
                Bitrate: station.Item1.Br);
        }
    }

    // ===================================================================
    // Ad banner helpers — pick random ad image from embedded resources
    // ===================================================================

    private static readonly string AdResourcePrefix = "controllers.ads.hive.com.img.";

    private static string GetRandomAdImageUrl()
    {
        var adKeys = Resources.Statics.Keys
            .Where(k => k.StartsWith(AdResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (adKeys.Count == 0) return null;

        var key = adKeys[Random.Shared.Next(adKeys.Count)];
        var filename = key[AdResourcePrefix.Length..];
        return $"http://ads.hive.com/img/{filename}";
    }

    // ===================================================================
    // Play path routing — /play/{id}/{player}.{ext}
    // Serves playlist/metafiles that point to /stream/{player}?id={id}
    //
    // Supported players:
    //   winamp  → .pls  → /stream/winamp
    //   wmp     → .asx  → /stream/wmp
    //   (future: itunes → .m3u, real → .ram, etc.)
    // ===================================================================

    private static bool TryParsePlayPath(string rawPath, out string id, out string player, out string ext)
    {
        id = null; player = null; ext = null;

        if (!rawPath.StartsWith("/play/")) return false;

        var rest = rawPath["/play/".Length..];
        var slashIdx = rest.LastIndexOf('/');
        if (slashIdx < 1) return false;

        id = rest[..slashIdx];
        var filename = rest[(slashIdx + 1)..];

        var dotIdx = filename.LastIndexOf('.');
        if (dotIdx < 1) return false;

        player = filename[..dotIdx];
        ext = filename[(dotIdx + 1)..];

        return !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(player);
    }

    public override async Task CallInitial(string rawPath)
    {
        Response.Context.SetValue("menu", new[] {
            "Browser",
            "Shoutcast",
        });

        if (TryParsePlayPath(rawPath, out var id, out var player, out var ext))
        {
            var info = await ResolveStation(id);

            switch (player)
            {
                case "winamp" when ext == "pls":
                {
                    var pls = new StringBuilder();
                    pls.AppendLine("[playlist]");
                    pls.AppendLine($"File1=http://radio.hive.com/stream/winamp?id={id}");
                    pls.AppendLine($"Title1={info.Name}");
                    pls.AppendLine("Length1=-1");
                    pls.AppendLine("NumberOfEntries=1");

                    SetPlaylistResponseHeaders();
                    Response.SetBodyString(pls.ToString(), "audio/x-scpls");
                    break;
                }
                case "wmp" when ext == "asx":
                {
                    var esc = (string s) => System.Security.SecurityElement.Escape(s ?? "");

                    var asx = new StringBuilder();
                    asx.AppendLine("<asx version=\"3.0\">");
                    asx.AppendLine($"  <title>{esc(info.Name)}</title>");
                    asx.AppendLine($"  <author>VintageHive/{Mind.ApplicationVersion}</author>");

                    if (!string.IsNullOrEmpty(info.Tags))
                        asx.AppendLine($"  <abstract>{esc(info.Tags)}</abstract>");

                    // Banner: random ad image (194x32 area in WMP 6.4)
                    var adUrl = GetRandomAdImageUrl();
                    if (adUrl != null)
                    {
                        asx.AppendLine($"  <banner href=\"{esc(adUrl)}\">");
                        asx.AppendLine($"    <abstract>{esc(info.Name)}</abstract>");
                        asx.AppendLine($"    <moreinfo href=\"http://radio.hive.com/browser.html?id={id}\" />");
                        asx.AppendLine("  </banner>");
                    }

                    asx.AppendLine("  <entry clientskip=\"no\">");
                    asx.AppendLine($"    <title>{esc(info.CurrentTrack ?? info.Name)}</title>");
                    asx.AppendLine($"    <author>VintageHive/{Mind.ApplicationVersion}</author>");

                    if (!string.IsNullOrEmpty(info.Country))
                        asx.AppendLine($"    <copyright>{esc(info.Country)}</copyright>");

                    // .asf extension triggers NSPlayer/WMSP pipeline for MMSH streaming
                    asx.AppendLine($"    <ref href=\"http://radio.hive.com/stream/wmp/{id}.asf\" />");
                    asx.AppendLine("  </entry>");
                    asx.AppendLine("</asx>");

                    SetPlaylistResponseHeaders();
                    Response.SetBodyString(asx.ToString(), "application/x-ms-asf");
                    break;
                }
            }
        }

        // MMSH stream: /stream/wmp/{id}.asf — WMSP/MMSH protocol for WMP
        if (!Response.Handled && rawPath.StartsWith("/stream/wmp/") && rawPath.EndsWith(".asf"))
        {
            var stationId = rawPath["/stream/wmp/".Length..^4]; // strip ".asf"
            if (!string.IsNullOrEmpty(stationId))
            {
                await HandleWmpMmshStream(stationId);
            }
        }

        // Plain HTTP MP3 stream: /stream/wmp/{id}.mp3 — fallback for non-WMSP clients
        if (!Response.Handled && rawPath.StartsWith("/stream/wmp/") && rawPath.EndsWith(".mp3"))
        {
            var stationId = rawPath["/stream/wmp/".Length..^4]; // strip ".mp3"
            if (!string.IsNullOrEmpty(stationId))
            {
                await HandleWmpStream(stationId);
            }
        }
    }

    // ===================================================================
    // Page routes
    // ===================================================================

    [Route("/index.html")]
    public async Task Index()
    {
        var top10countries = Mind.RadioBrowser.ListGetCountries(10);

        Response.Context.SetValue("top10countries", top10countries);

        var top10Tags = Mind.RadioBrowser.ListGetTags(10);

        Response.Context.SetValue("top10tags", top10Tags);

        var list = await Mind.RadioBrowser.StationsGetByClicksAsync(100);

        Response.Context.SetValue("stations", list);
    }

    [Route("/browser.html")]
    public async Task BrowserIndex()
    {
        Response.Context.SetValue("stats", await Mind.RadioBrowser.ServerStatsAsync());

        if (Request.QueryParams.ContainsKey("country"))
        {
            var countryCodeStripped = Request.QueryParams["country"][..2];

            var countryName = Mind.Geonames.GetCountryNameByIso(countryCodeStripped);

            if (string.IsNullOrWhiteSpace(countryName))
            {
                Response.Context.SetValue("error", $"This country code [{countryCodeStripped.ToUpper()}] is not a valid country, please check your input and try again.");
            }
            else
            {
                var stationsbycountry = await Mind.RadioBrowser.StationsByCountryCodePagedAsync(countryCodeStripped);

                Response.Context.SetValue("countrycode", countryCodeStripped);
                Response.Context.SetValue("countryname", countryName);
                Response.Context.SetValue("stationsbycountry", stationsbycountry);
            }
        }
        else if (Request.QueryParams.ContainsKey("tag"))
        {
            var tagName = Request.QueryParams["tag"];

            var stationsbytag = await Mind.RadioBrowser.StationsByTagPagedAsync(tagName);

            Response.Context.SetValue("tagname", tagName);
            Response.Context.SetValue("stationsbytag", stationsbytag);
        }
        else if (Request.QueryParams.ContainsKey("q"))
        {
            var searchTerm = Request.QueryParams["q"];

            var stationsbysearch = await Mind.RadioBrowser.StationsBySearchPagedAsync(searchTerm);

            Response.Context.SetValue("searchterm", searchTerm);
            Response.Context.SetValue("stationsbysearch", stationsbysearch);
        }
        else if (Request.QueryParams.ContainsKey("id"))
        {
            var station = await Mind.RadioBrowser.StationGetAsync(Request.QueryParams["id"]);

            Response.Context.SetValue("station", station);
        }
        else if (Request.QueryParams.ContainsKey("list") && Request.QueryParams["list"] == "countries")
        {
            var countryList = Mind.RadioBrowser.ListGetCountries();

            Response.Context.SetValue("countrylist", countryList);
        }
        else if (Request.QueryParams.ContainsKey("list") && Request.QueryParams["list"] == "tags")
        {
            var tagList = Mind.RadioBrowser.ListGetTags();

            Response.Context.SetValue("taglist", tagList.Take(2000));
        }
        else
        {
            var list = await Mind.RadioBrowser.StationsGetByClicksAsync();

            Response.Context.SetValue("stations", list);
        }
    }

    [Route("/shoutcast.html")]
    public async Task ShoutcastIndex()
    {
        List<ShoutcastStation> list;

        if (Request.QueryParams.ContainsKey("q"))
        {
            var query = HttpUtility.UrlEncode(Request.QueryParams["q"]);

            list = await StationSearch(query);

            Response.Context.SetValue("pagetitle", $"({Request.QueryParams["q"]}) Results");
        }
        else if (Request.QueryParams.ContainsKey("genre"))
        {
            var genreQuery = HttpUtility.UrlEncode(Request.QueryParams["genre"]);

            list = await StationSearchByGenre(genreQuery);

            Response.Context.SetValue("pagetitle", $"({Request.QueryParams["genre"]}) Genre");
        }
        else if (Request.QueryParams.ContainsKey("list") && Request.QueryParams["list"] == "genre")
        {
            var genreList = await GetGenres();

            Response.Context.SetValue("pagetitle", $"Genre List");
            Response.Context.SetValue("genres", genreList);

            return;
        }
        else
        {
            list = await GetTop500();

            Response.Context.SetValue("pagetitle", "Global Top500");
        }

        Response.Context.SetValue("stations", list);
    }

    // ===================================================================
    // Stream endpoints — one per player, takes any station ID
    // ===================================================================

    [Route("/stream/winamp")]
    public async Task StreamWinamp()
    {
        var info = await ResolveStation(Request.QueryParams["id"]);

        using var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
        });

        if (Request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, Request.Headers[HttpHeaderName.UserAgent]);
        }

        if (info.Codec == "MP3" && Request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        using var client = await httpClient.GetAsync(info.StreamUrl, HttpCompletionOption.ResponseHeadersRead);

        using var clientStream = await client.Content.ReadAsStreamAsync();

        if (info.Codec != "MP3")
        {
            using var process = CreateFfmpegProcess();

            Response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
            Response.Headers.Add("Icy-Name", info.Name + $" [Codec:{info.Codec}]");

            process.Start();
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            try
            {
                await Request.ListenerSocket.Stream.WriteAsync(Response.GetResponseEncodedData());

                Task.WaitAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(Request.ListenerSocket.Stream)
                );
            }
            catch (IOException) { }

            process.Kill();
            Response.Handled = true;
        }
        else
        {
            foreach (var header in client.Headers)
            {
                if (header.Key.ToLower().StartsWith("icy"))
                {
                    Response.Headers.Add(header.Key, header.Value.First());
                }
            }

            Response.Headers.Add(HttpHeaderName.ContentDisposition, "inline");
            Response.Headers.Add("Transfer-Encoding", "chunked");
            Response.Headers.Add("Connection", "keep-alive");
            Response.Headers.Add("Accept-Ranges", "bytes");

            try
            {
                Response.SetBodyStream(clientStream, "audio/x-mpeg");

                await Request.ListenerSocket.Stream.WriteAsync(Response.GetResponseEncodedData());

                await clientStream.CopyToAsync(Request.ListenerSocket.Stream);
            }
            catch (Exception) { }
        }
    }

    private async Task HandleWmpStream(string stationId)
    {
        Console.Error.WriteLine($"[WMP] HandleWmpStream entered, id={stationId}");
        Console.Error.Flush();

        try
        {
            var info = await ResolveStation(stationId);

            Console.Error.WriteLine($"[WMP] Station: {info.Name}, Codec: {info.Codec}, URL: {info.StreamUrl}");
            Console.Error.Flush();

            using var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
            });

            httpClient.Timeout = TimeSpan.FromSeconds(15);

            using var upstream = await httpClient.GetAsync(info.StreamUrl, HttpCompletionOption.ResponseHeadersRead);

            Console.Error.WriteLine($"[WMP] Upstream: {(int)upstream.StatusCode}");
            Console.Error.Flush();

            using var clientStream = await upstream.Content.ReadAsStreamAsync();

            // Plain HTTP/1.0 response — no WMSP, no chunked, no ICY
            // Large Content-Length tells WMP to start playback immediately
            // instead of buffering the entire "file" before playing.
            Response.Headers.Add(HttpHeaderName.ContentType, "audio/mpeg");
            Response.Headers.Add("Content-Length", "2147483647");
            Response.Headers.Add("Connection", "close");
            Response.Headers.Add("icy-name", info.Name);

            if (info.Codec != "MP3")
            {
                using var process = CreateFfmpegProcess();

                process.Start();
                _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

                var responseBytes = Response.GetResponseEncodedData();
                Console.Error.WriteLine($"[WMP] Headers (ffmpeg):\n{System.Text.Encoding.ASCII.GetString(responseBytes).TrimEnd()}");
                Console.Error.Flush();

                await Request.ListenerSocket.Stream.WriteAsync(responseBytes);

                Task.WaitAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(Request.ListenerSocket.Stream)
                );

                process.Kill();
            }
            else
            {
                var responseBytes = Response.GetResponseEncodedData();
                Console.Error.WriteLine($"[WMP] Headers (passthrough):\n{System.Text.Encoding.ASCII.GetString(responseBytes).TrimEnd()}");
                Console.Error.Flush();

                await Request.ListenerSocket.Stream.WriteAsync(responseBytes);

                Console.Error.WriteLine("[WMP] Streaming MP3...");
                Console.Error.Flush();

                await clientStream.CopyToAsync(Request.ListenerSocket.Stream);
            }

            Console.Error.WriteLine("[WMP] Stream ended normally");
            Console.Error.Flush();

            Response.Handled = true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WMP] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Console.Error.Flush();
            Response.Handled = true;
        }
    }

    // ===================================================================
    // MMSH (MMS over HTTP) — native Windows Media streaming protocol
    // NSPlayer sends Describe (get ASF header) then Play (stream data)
    // ===================================================================

    private static readonly byte[] AsfFilePropertiesGuid =
    {
        0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11,
        0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
    };

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

        // Framing Header (4 bytes)
        chunk[0] = 0x24;       // Frame = 0x24, B-bit = 0
        chunk[1] = chunkType;  // 'H'=0x48, 'D'=0x44
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

    private static Process CreateWmaFfmpegProcess()
    {
        var process = new Process();
        process.StartInfo.FileName = GetFfmpegExecutablePath();
        // Microsoft ADPCM (format tag 0x0002) inside ASF container
        // - Natively supported on ALL Windows versions (no codec install needed)
        // - ffmpeg encodes it correctly (unlike wmav1/wmav2 whose init data WMP 6.4 rejects)
        // - ~4:1 compression vs PCM → ~11 KB/s at 22050Hz mono
        // - block_size 1024 = reasonable ADPCM block alignment
        process.StartInfo.Arguments = "-probesize 32768 -analyzeduration 0 -i pipe:0 -fflags nobuffer -map_metadata -1 -c:a adpcm_ms -ar 22050 -ac 1 -block_size 1024 -f asf pipe:1";
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        return process;
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

            // File Properties Object layout (offsets from object start):
            //  0-15: GUID, 16-23: Object Size, 24-39: File ID
            // 40-47: File Size (uint64)       → set to large value
            // 48-55: Creation Date (uint64)   → leave as-is
            // 56-63: Data Packets Count (uint64) → set non-zero
            // 64-71: Play Duration (uint64, 100ns units) → set to ~24 hours
            // 72-79: Send Duration (uint64)   → same
            // 80-87: Preroll (uint64, ms)      → set to 3000ms
            // 88-91: Flags (uint32)           → set broadcast=1, seekable=0
            // 92-95: Min Data Packet Size (uint32)
            // 96-99: Max Data Packet Size (uint32)
            // 100-103: Max Bitrate (uint32)

            // File Size: 2GB (large dummy value so WMP allocates buffers)
            BitConverter.GetBytes((long)0x7FFFFFFF).CopyTo(headerData, i + 40);

            // Data Packets Count: large number (WMP may use this to allocate)
            BitConverter.GetBytes((long)0xFFFF).CopyTo(headerData, i + 56);

            // Play Duration: ~24 hours in 100-nanosecond units
            BitConverter.GetBytes(24L * 3600 * 10_000_000).CopyTo(headerData, i + 64);

            // Send Duration: same
            BitConverter.GetBytes(24L * 3600 * 10_000_000).CopyTo(headerData, i + 72);

            // Preroll: 3000ms (3 seconds buffer before playback)
            BitConverter.GetBytes((long)3000).CopyTo(headerData, i + 80);

            // Flags: broadcast=1 (bit 0), seekable=0 (bit 1 cleared)
            BitConverter.GetBytes((uint)0x01).CopyTo(headerData, i + 88);

            // Ensure packet sizes are set
            if (BitConverter.ToInt32(headerData, i + 92) == 0)
                BitConverter.GetBytes(packetSize).CopyTo(headerData, i + 92);
            if (BitConverter.ToInt32(headerData, i + 96) == 0)
                BitConverter.GetBytes(packetSize).CopyTo(headerData, i + 96);

            // Max Bitrate: ensure non-zero (e.g., 128kbps)
            if (BitConverter.ToInt32(headerData, i + 100) == 0)
                BitConverter.GetBytes(128000).CopyTo(headerData, i + 100);

            Console.Error.WriteLine($"[MMSH] Patched ASF File Properties: fileSize=2GB, packets=65535, preroll=3000ms, broadcast=1");
            Console.Error.Flush();
            return;
        }
    }

    // ── ASF Content Description Object ──
    // GUID: 33 26 B2 75 8E 66 CF 11 A6 D9 00 AA 00 62 CE 6C
    private static readonly byte[] AsfContentDescriptionGuid =
    {
        0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
        0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
    };

    /// <summary>
    /// Build an ASF Content Description Object with the given metadata strings.
    /// Empty/null strings produce a length of 0 with no payload bytes.
    /// </summary>
    private static byte[] BuildAsfContentDescription(
        string title, string author, string copyright, string description, string rating)
    {
        // Encode each string as UTF-16LE + null terminator (2 bytes)
        static byte[] EncodeField(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            var bytes = Encoding.Unicode.GetBytes(s);
            var result = new byte[bytes.Length + 2]; // +2 for null terminator
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            // last 2 bytes are already 0x00 0x00 from array init
            return result;
        }

        var titleBytes = EncodeField(title);
        var authorBytes = EncodeField(author);
        var copyrightBytes = EncodeField(copyright);
        var descBytes = EncodeField(description);
        var ratingBytes = EncodeField(rating);

        // Object layout: GUID(16) + ObjectSize(8) + 5×uint16 lengths + string data
        long objectSize = 16 + 8 + 10 + titleBytes.Length + authorBytes.Length +
                          copyrightBytes.Length + descBytes.Length + ratingBytes.Length;

        var obj = new byte[objectSize];
        int pos = 0;

        // GUID
        Buffer.BlockCopy(AsfContentDescriptionGuid, 0, obj, pos, 16); pos += 16;

        // Object Size
        BitConverter.GetBytes(objectSize).CopyTo(obj, pos); pos += 8;

        // String lengths (byte count including null terminator, or 0 if empty)
        BitConverter.GetBytes((ushort)titleBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)authorBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)copyrightBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)descBytes.Length).CopyTo(obj, pos); pos += 2;
        BitConverter.GetBytes((ushort)ratingBytes.Length).CopyTo(obj, pos); pos += 2;

        // String data
        Buffer.BlockCopy(titleBytes, 0, obj, pos, titleBytes.Length); pos += titleBytes.Length;
        Buffer.BlockCopy(authorBytes, 0, obj, pos, authorBytes.Length); pos += authorBytes.Length;
        Buffer.BlockCopy(copyrightBytes, 0, obj, pos, copyrightBytes.Length); pos += copyrightBytes.Length;
        Buffer.BlockCopy(descBytes, 0, obj, pos, descBytes.Length); pos += descBytes.Length;
        Buffer.BlockCopy(ratingBytes, 0, obj, pos, ratingBytes.Length);

        return obj;
    }

    // ── Shared MMSH live sessions: one ffmpeg per station, multiple consumers ──

    private static readonly ConcurrentDictionary<string, MmshLiveSession> _liveSessions = new();
    private static readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    /// <summary>
    /// A live MMSH streaming session for one station.
    /// Owns: upstream HTTP connection, ffmpeg transcode process, circular buffer of pre-framed $D chunks.
    /// Multiple PLAY connections read from the same buffer with continuous LocationId/AFFlags.
    /// </summary>
    private class MmshLiveSession : IDisposable
    {
        public byte[] HChunk { get; }
        public int AsfPacketSize { get; }
        public StationInfo Station { get; }
        private IcyMetadataStrippingStream _icyStream;

        /// <summary>Current track title from ICY metadata, or station info fallback.</summary>
        public string CurrentTrack =>
            _icyStream?.CurrentTrack ?? Station?.CurrentTrack;

        private const int RingCapacity = 300; // ~60s at ~5 pkt/s
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

        public bool IsAlive => !_producerTask.IsCompleted && !_disposed;

        public MmshLiveSession(HttpClient httpClient, HttpResponseMessage upstreamResponse,
            Process ffmpeg, byte[] hChunk, int packetSize, Stream ffmpegOutput,
            StationInfo stationInfo, IcyMetadataStrippingStream icyStream = null)
        {
            _httpClient = httpClient;
            _upstreamResponse = upstreamResponse;
            _ffmpeg = ffmpeg;
            HChunk = hChunk;
            AsfPacketSize = packetSize;
            Station = stationInfo;
            _icyStream = icyStream;
            _producerTask = Task.Run(() => ProduceLoop(ffmpegOutput, packetSize));
        }

        private async Task ProduceLoop(Stream ffmpegOut, int packetSize)
        {
            uint locationId = 0;
            byte afFlags = 0;
            var buf = new byte[packetSize];

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    int read = await ReadExactAsync(ffmpegOut, buf, 0, packetSize);
                    if (read == 0) break;
                    if (read < packetSize) Array.Clear(buf, read, packetSize - read);

                    var dChunk = BuildMmshChunk(0x44, locationId, 0, afFlags, buf);

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
                    afFlags = (byte)((afFlags + 1) % 255);

                    if (locationId % 500 == 0)
                    {
                        Console.Error.WriteLine($"[MMSH-Prod] {locationId} packets");
                        Console.Error.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MMSH-Prod] Error: {ex.Message}");
            }
            finally
            {
                // Wake any waiting consumers so they can exit
                lock (_lock) { _newDataTcs.TrySetResult(); }
            }

            Console.Error.WriteLine($"[MMSH-Prod] Ended ({_headSeq} total)");
            Console.Error.Flush();
        }

        public long LivePosition { get { lock (_lock) return _headSeq; } }

        /// <summary>
        /// Try to read a pre-framed $D chunk. Returns (null, seq) if not yet available.
        /// Automatically skips past stale positions that fell off the ring buffer.
        /// </summary>
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

        /// <summary>Wait until data is available at or after the given sequence.</summary>
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

    private static async Task<MmshLiveSession> GetOrCreateSessionAsync(string stationId)
    {
        if (_liveSessions.TryGetValue(stationId, out var session) && session.IsAlive)
            return session;

        await _sessionCreateLock.WaitAsync();
        try
        {
            // Double-check after lock
            if (_liveSessions.TryGetValue(stationId, out session) && session.IsAlive)
                return session;

            var info = await ResolveStation(stationId);
            Console.Error.WriteLine($"[MMSH-Session] Creating: {info.Name} ({info.Codec})");
            Console.Error.Flush();

            var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                PlaintextStreamFilter = (filterContext, ct) =>
                    new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
            });
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            // Request ICY metadata so we can capture now-playing info
            httpClient.DefaultRequestHeaders.Add("Icy-MetaData", "1");

            var upstreamResponse = await httpClient.GetAsync(info.StreamUrl, HttpCompletionOption.ResponseHeadersRead);
            var rawUpstreamStream = await upstreamResponse.Content.ReadAsStreamAsync();

            // Wrap with ICY metadata stripper if the server provides metadata
            IcyMetadataStrippingStream icyStream = null;
            Stream upstreamStream = rawUpstreamStream;

            if (upstreamResponse.Headers.TryGetValues("icy-metaint", out var metaintValues))
            {
                var metaintStr = metaintValues.FirstOrDefault();
                if (int.TryParse(metaintStr, out int metaInterval) && metaInterval > 0)
                {
                    icyStream = new IcyMetadataStrippingStream(rawUpstreamStream, metaInterval);
                    upstreamStream = icyStream;
                    Console.Error.WriteLine($"[MMSH-Session] ICY metadata: interval={metaInterval}");
                    Console.Error.Flush();
                }
            }

            var ffmpeg = CreateWmaFfmpegProcess();
            ffmpeg.Start();
            _ = ffmpeg.StandardError.BaseStream.CopyToAsync(Stream.Null);
            _ = Task.Run(async () =>
            {
                try { await upstreamStream.CopyToAsync(ffmpeg.StandardInput.BaseStream); }
                catch { }
                try { ffmpeg.StandardInput.Close(); } catch { }
            });

            var ffmpegOut = ffmpeg.StandardOutput.BaseStream;

            // Read ASF Header Object
            var headerPrefix = new byte[24];
            if (await ReadExactAsync(ffmpegOut, headerPrefix, 0, 24) < 24)
                throw new InvalidOperationException("Failed to read ASF Header Object prefix");

            long asfHeaderSize = BitConverter.ToInt64(headerPrefix, 16);
            var headerObj = new byte[asfHeaderSize];
            Buffer.BlockCopy(headerPrefix, 0, headerObj, 0, 24);
            if (await ReadExactAsync(ffmpegOut, headerObj, 24, (int)asfHeaderSize - 24) < (int)asfHeaderSize - 24)
                throw new InvalidOperationException("Incomplete ASF Header Object");

            // Read first 50 bytes of Data Object
            var dataObjHeader = new byte[50];
            if (await ReadExactAsync(ffmpegOut, dataObjHeader, 0, 50) < 50)
                throw new InvalidOperationException("Failed to read ASF Data Object header");

            // Patch Data Object for broadcast
            BitConverter.GetBytes((long)0).CopyTo(dataObjHeader, 16);
            if (BitConverter.ToInt64(dataObjHeader, 40) == 0)
                BitConverter.GetBytes((long)0xFFFF).CopyTo(dataObjHeader, 40);
            dataObjHeader[48] = 0x01; dataObjHeader[49] = 0x01;

            int packetSize = GetAsfPacketSize(headerObj);
            PatchAsfHeaderForBroadcast(headerObj, packetSize);

            // Inject ASF Content Description Object with station metadata
            var contentDesc = BuildAsfContentDescription(
                title: info.Name,
                author: $"VintageHive/{Mind.ApplicationVersion}",
                copyright: info.Country ?? "",
                description: info.Tags ?? "",
                rating: "");

            // Grow headerObj to include the Content Description Object
            var newHeaderSize = asfHeaderSize + contentDesc.Length;
            var newHeaderObj = new byte[newHeaderSize];
            Buffer.BlockCopy(headerObj, 0, newHeaderObj, 0, (int)asfHeaderSize);
            Buffer.BlockCopy(contentDesc, 0, newHeaderObj, (int)asfHeaderSize, contentDesc.Length);

            // Update ASF Header Object Size (bytes 16-23, uint64)
            BitConverter.GetBytes(newHeaderSize).CopyTo(newHeaderObj, 16);
            // Update NumObjects count (bytes 24-27, uint32) — increment by 1
            uint numObjects = BitConverter.ToUInt32(newHeaderObj, 24);
            BitConverter.GetBytes(numObjects + 1).CopyTo(newHeaderObj, 24);

            Console.Error.WriteLine($"[MMSH-Session] Injected Content Description: {contentDesc.Length}B, numObjects={numObjects}→{numObjects + 1}");
            Console.Error.Flush();

            var hPayload = new byte[newHeaderSize + 50];
            Buffer.BlockCopy(newHeaderObj, 0, hPayload, 0, (int)newHeaderSize);
            Buffer.BlockCopy(dataObjHeader, 0, hPayload, (int)newHeaderSize, 50);
            var hChunk = BuildMmshChunk(0x48, 0, 0, 0x0C, hPayload);

            Console.Error.WriteLine($"[MMSH-Session] ASF: header={newHeaderSize}B packetSize={packetSize} $H={hChunk.Length}B");
            Console.Error.Flush();

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

    /// <summary>
    /// Rebuild the $H chunk with an updated Content Description title.
    /// Finds and replaces the existing Content Description Object in the $H payload,
    /// preserving all other ASF objects.
    /// </summary>
    private static byte[] RebuildHChunkWithTitle(MmshLiveSession session, string newTitle)
    {
        var hChunk = session.HChunk;

        // $H payload starts at offset 12 (4-byte framing + 8-byte MMS header)
        // The payload is: ASF Header Object + Data Object header (50 bytes)
        // We need to find the Content Description Object within the ASF Header Object

        // Read the ASF Header Object size from bytes 16-23 of the payload (offset 28-35 in hChunk)
        long asfHeaderSize = BitConverter.ToInt64(hChunk, 12 + 16);
        var headerObj = new byte[asfHeaderSize];
        Buffer.BlockCopy(hChunk, 12, headerObj, 0, (int)asfHeaderSize);

        // Find existing Content Description Object by GUID
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

        if (cdOffset < 0) return hChunk; // No Content Description found, return original

        // Get size of existing Content Description Object
        long oldCdSize = BitConverter.ToInt64(headerObj, cdOffset + 16);

        // Build new Content Description with updated title
        var newCd = BuildAsfContentDescription(
            title: newTitle,
            author: $"VintageHive/{Mind.ApplicationVersion}",
            copyright: session.Station?.Country ?? "",
            description: session.Station?.Tags ?? "",
            rating: "");

        // Build new header: [before CD] + [new CD] + [after CD]
        int beforeCd = cdOffset;
        int afterCdStart = cdOffset + (int)oldCdSize;
        int afterCdLen = (int)asfHeaderSize - afterCdStart;

        long newAsfHeaderSize = beforeCd + newCd.Length + afterCdLen;
        var newHeaderObj = new byte[newAsfHeaderSize];
        Buffer.BlockCopy(headerObj, 0, newHeaderObj, 0, beforeCd);
        Buffer.BlockCopy(newCd, 0, newHeaderObj, beforeCd, newCd.Length);
        Buffer.BlockCopy(headerObj, afterCdStart, newHeaderObj, beforeCd + newCd.Length, afterCdLen);

        // Update ASF Header Object Size
        BitConverter.GetBytes(newAsfHeaderSize).CopyTo(newHeaderObj, 16);

        // Extract Data Object header (50 bytes at end of original $H payload)
        var dataObjHeader = new byte[50];
        Buffer.BlockCopy(hChunk, 12 + (int)asfHeaderSize, dataObjHeader, 0, 50);

        // Build new $H chunk
        var hPayload = new byte[newAsfHeaderSize + 50];
        Buffer.BlockCopy(newHeaderObj, 0, hPayload, 0, (int)newAsfHeaderSize);
        Buffer.BlockCopy(dataObjHeader, 0, hPayload, (int)newAsfHeaderSize, 50);

        return BuildMmshChunk(0x48, 0, 0, 0x0C, hPayload);
    }

    private async Task HandleWmpMmshStream(string stationId)
    {
        Console.Error.WriteLine($"[MMSH] === {Request.Method} /stream/wmp/{stationId}.asf ===");
        foreach (var h in Request.Headers)
            Console.Error.WriteLine($"[MMSH]   {h.Key}: {h.Value}");
        Console.Error.Flush();

        // Handle POST (log-line from NSPlayer) — just acknowledge
        if (Request.Method == "POST")
        {
            Console.Error.WriteLine("[MMSH] POST log-line acknowledged");
            Console.Error.Flush();
            Response.SetBodyString("", "text/plain");
            return;
        }

        try
        {
            // Parse Pragma values
            var pragmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Request.Headers.TryGetValue("Pragma", out var pragmaRaw))
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

            int requestContext = 0;
            if (pragmas.TryGetValue("request-context", out var ctxStr))
                int.TryParse(ctxStr, out requestContext);

            Console.Error.WriteLine($"[MMSH] Mode={(isPlay ? "PLAY" : "DESCRIBE")} ctx={requestContext}");
            Console.Error.Flush();

            // Get or reuse the shared session for this station
            var session = await GetOrCreateSessionAsync(stationId);
            var socket = Request.ListenerSocket.Stream;
            var serverHeader = "Cougar/4.1";

            if (!isPlay)
            {
                // ═══ DESCRIBE ═══  — $H only, with Content-Length
                var resp =
                    "HTTP/1.0 200 OK\r\n" +
                    $"Server: {serverHeader}\r\n" +
                    "Content-Type: application/octet-stream\r\n" +
                    $"Content-Length: {session.HChunk.Length}\r\n" +
                    "Pragma: no-cache\r\n" +
                    $"Pragma: client-id={clientId}\r\n" +
                    "Pragma: features=\"broadcast\"\r\n" +
                    "Pragma: timeout=60000\r\n" +
                    "Cache-Control: no-cache\r\n" +
                    "\r\n";

                Console.Error.WriteLine($"[MMSH] DESCRIBE: $H={session.HChunk.Length}B");
                Console.Error.Flush();

                await socket.WriteAsync(System.Text.Encoding.ASCII.GetBytes(resp));
                await socket.WriteAsync(session.HChunk);
                Response.Handled = true;
            }
            else
            {
                // ═══ PLAY ═══  — attach to live session, stream $D from current position
                // Always send $H so WMP gets metadata; on reconnects (ctx>2) update with current track
                var resp =
                    "HTTP/1.0 200 OK\r\n" +
                    $"Server: {serverHeader}\r\n" +
                    "Content-Type: application/octet-stream\r\n" +
                    "Pragma: no-cache\r\n" +
                    $"Pragma: client-id={clientId}\r\n" +
                    "Pragma: features=\"broadcast\"\r\n" +
                    "Pragma: timeout=60000\r\n" +
                    "Cache-Control: no-cache\r\n" +
                    "\r\n";

                await socket.WriteAsync(System.Text.Encoding.ASCII.GetBytes(resp));

                // On reconnect, rebuild $H with current track info if available
                byte[] hChunkToSend = session.HChunk;
                if (requestContext > 2)
                {
                    var track = session.CurrentTrack;
                    if (!string.IsNullOrEmpty(track))
                    {
                        var displayTitle = $"{session.Station?.Name} - {track}";
                        hChunkToSend = RebuildHChunkWithTitle(session, displayTitle);
                        Console.Error.WriteLine($"[MMSH] Reconnect: updated title to '{displayTitle}'");
                        Console.Error.Flush();
                    }
                }
                await socket.WriteAsync(hChunkToSend);

                // Start from current live position (back up a few for smoother join)
                long readPos = Math.Max(0, session.LivePosition - 5);
                Console.Error.WriteLine($"[MMSH] PLAY ctx={requestContext}: startSeq={readPos} live={session.LivePosition}");
                Console.Error.Flush();

                uint sent = 0;
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

                            if (sent == 1)
                            {
                                Console.Error.WriteLine($"[MMSH] First $D: seq={seq} hex={BitConverter.ToString(chunk, 0, Math.Min(12, chunk.Length))}");
                                Console.Error.Flush();
                            }
                            if (sent % 500 == 0)
                            {
                                Console.Error.WriteLine($"[MMSH] Sent {sent} (seq={seq})");
                                Console.Error.Flush();
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
                    Console.Error.WriteLine($"[MMSH] Client disconnected after {sent} packets (lastSeq={readPos - 1})");
                }
                catch (OperationCanceledException) { }

                Console.Error.Flush();
                Response.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MMSH] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Console.Error.Flush();
            Response.Handled = true;
        }
    }

    // ===================================================================
    // Legacy stream endpoints — kept for cached .pls files
    // ===================================================================

    [Route("/browser.mp3")]
    public async Task BrowserPlay()
    {
        var station = await Mind.RadioBrowser.StationGetAsync(Request.QueryParams["id"]);

        using var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
        });

        if (Request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, Request.Headers[HttpHeaderName.UserAgent]);
        }

        if (station.Codec.ToLower() == "mp3" && Request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        using var client = await httpClient.GetAsync(station.UrlResolved, HttpCompletionOption.ResponseHeadersRead);

        using var clientStream = await client.Content.ReadAsStreamAsync();

        if (!station.Codec.Equals("mp3", StringComparison.CurrentCultureIgnoreCase))
        {
            using var process = CreateFfmpegProcess();

            Response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
            Response.Headers.Add("Icy-Name", station.Name + $" [Codec:{station.Codec}]");

            process.Start();
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            try
            {
                await Request.ListenerSocket.Stream.WriteAsync(Response.GetResponseEncodedData());

                Task.WaitAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(Request.ListenerSocket.Stream)
                );
            }
            catch (IOException) { }

            process.Kill();
            Response.Handled = true;
        }
        else
        {
            foreach (var header in client.Headers)
            {
                if (header.Key.ToLower().StartsWith("icy"))
                {
                    Response.Headers.Add(header.Key, header.Value.First());
                }
            }

            Response.Headers.Add(HttpHeaderName.ContentDisposition, "inline");
            Response.Headers.Add("Transfer-Encoding", "chunked");
            Response.Headers.Add("Connection", "keep-alive");
            Response.Headers.Add("Accept-Ranges", "bytes");

            try
            {
                Response.SetBodyStream(clientStream, "audio/x-mpeg");

                await Request.ListenerSocket.Stream.WriteAsync(Response.GetResponseEncodedData());

                await clientStream.CopyToAsync(Request.ListenerSocket.Stream);
            }
            catch (Exception) { }
        }
    }

    [Route("/shoutcast.mp3")]
    public async Task SCPlay()
    {
        var station = await GetStationById(Request.QueryParams["id"]);

        var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
        });

        if (Request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, Request.Headers[HttpHeaderName.UserAgent]);
        }

        var details = station.Item1;

        var stationCodec = GetFormatString(details.Mt);

        if (stationCodec == "MP3" && Request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        var client = await httpClient.GetAsync(station.Item2, HttpCompletionOption.ResponseHeadersRead);

        var clientStream = await client.Content.ReadAsStreamAsync();

        if (stationCodec != "MP3")
        {
            using var process = CreateFfmpegProcess();

            Response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
            Response.Headers.Add("Icy-Name", details.Name + $" [Codec:{stationCodec}]");

            process.Start();
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            try
            {
                await Request.ListenerSocket.Stream.WriteAsync(Response.GetResponseEncodedData());

                Task.WaitAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(Request.ListenerSocket.Stream)
                );
            }
            catch (IOException) { }

            process.Kill();
            Response.Handled = true;
        }
        else
        {
            foreach (var header in client.Headers)
            {
                if (header.Key.ToLower().StartsWith("icy"))
                {
                    Response.Headers.Add(header.Key, header.Value.First());
                }
            }

            Response.SetBodyStream(clientStream, HttpContentTypeMimeType.Audio.Mpeg);
        }
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private static Process CreateFfmpegProcess()
    {
        var cmdPath = GetFfmpegExecutablePath();
        var argsff = "-probesize 32768 -analyzeduration 0 -i pipe:0 -fflags nobuffer -c:a libmp3lame -b:a 128k -ar 44100 -f mp3 pipe:1";

        var process = new Process();

        process.StartInfo.FileName = cmdPath;
        process.StartInfo.Arguments = argsff;
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

    private void SetPlaylistResponseHeaders()
    {
        Response.Headers.Add("Pragma", "public");
        Response.Headers.Add("Cache-Control", "must-revalidate, post-check=0, pre-check=0");
        Response.Headers.Add("Expires", "0");
    }
}
