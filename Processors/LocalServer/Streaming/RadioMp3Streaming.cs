// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;
using System.Runtime.InteropServices;
using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;
using static VintageHive.Utilities.SCUtils;

namespace VintageHive.Processors.LocalServer.Streaming;

internal static class RadioMp3Streaming
{
    // ===================================================================
    // FFmpeg process creation (duplicated for independence)
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

    // ===================================================================
    // Winamp streaming — /stream/winamp?id={id}
    // ===================================================================

    public static async Task HandleWinampStream(HttpRequest request, HttpResponse response)
    {
        var info = await RadioStationResolver.ResolveStation(request.QueryParams["id"]);

        using var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
        });

        if (request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, request.Headers[HttpHeaderName.UserAgent]);
        }

        if (info.Codec == "MP3" && request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        using var client = await httpClient.GetAsync(info.StreamUrl, HttpCompletionOption.ResponseHeadersRead);

        using var clientStream = await client.Content.ReadAsStreamAsync();

        if (info.Codec != "MP3")
        {
            using var process = CreateFfmpegProcess();

            response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
            response.Headers.Add("Icy-Name", info.Name + $" [Codec:{info.Codec}]");

            process.Start();
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            try
            {
                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                Task.WaitAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(request.ListenerSocket.Stream)
                );
            }
            catch (IOException) { }

            process.Kill();
            response.Handled = true;
        }
        else
        {
            foreach (var header in client.Headers)
            {
                if (header.Key.ToLower().StartsWith("icy"))
                {
                    response.Headers.Add(header.Key, header.Value.First());
                }
            }

            response.Headers.Add(HttpHeaderName.ContentDisposition, "inline");
            response.Headers.Add("Transfer-Encoding", "chunked");
            response.Headers.Add("Connection", "keep-alive");
            response.Headers.Add("Accept-Ranges", "bytes");

            try
            {
                response.SetBodyStream(clientStream, "audio/x-mpeg");

                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                await clientStream.CopyToAsync(request.ListenerSocket.Stream);
            }
            catch (Exception) { }
        }
    }

    // ===================================================================
    // WMP MP3 fallback — /stream/wmp/{id}.mp3
    // ===================================================================

    public static async Task HandleWmpMp3Stream(HttpRequest request, HttpResponse response, string stationId)
    {
        Console.Error.WriteLine($"[WMP] HandleWmpStream entered, id={stationId}");
        Console.Error.Flush();

        try
        {
            var info = await RadioStationResolver.ResolveStation(stationId);

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
            response.Headers.Add(HttpHeaderName.ContentType, "audio/mpeg");
            response.Headers.Add("Content-Length", "2147483647");
            response.Headers.Add("Connection", "close");
            response.Headers.Add("icy-name", info.Name);

            if (info.Codec != "MP3")
            {
                using var process = CreateFfmpegProcess();

                process.Start();
                _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

                var responseBytes = response.GetResponseEncodedData();
                Console.Error.WriteLine($"[WMP] Headers (ffmpeg):\n{System.Text.Encoding.ASCII.GetString(responseBytes).TrimEnd()}");
                Console.Error.Flush();

                await request.ListenerSocket.Stream.WriteAsync(responseBytes);

                Task.WaitAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(request.ListenerSocket.Stream)
                );

                process.Kill();
            }
            else
            {
                var responseBytes = response.GetResponseEncodedData();
                Console.Error.WriteLine($"[WMP] Headers (passthrough):\n{System.Text.Encoding.ASCII.GetString(responseBytes).TrimEnd()}");
                Console.Error.Flush();

                await request.ListenerSocket.Stream.WriteAsync(responseBytes);

                Console.Error.WriteLine("[WMP] Streaming MP3...");
                Console.Error.Flush();

                await clientStream.CopyToAsync(request.ListenerSocket.Stream);
            }

            Console.Error.WriteLine("[WMP] Stream ended normally");
            Console.Error.Flush();

            response.Handled = true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WMP] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Console.Error.Flush();
            response.Handled = true;
        }
    }

    // ===================================================================
    // Legacy browser MP3 — /browser.mp3?id={id}
    // ===================================================================

    public static async Task HandleBrowserPlay(HttpRequest request, HttpResponse response)
    {
        var station = await Mind.RadioBrowser.StationGetAsync(request.QueryParams["id"]);

        using var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
        });

        if (request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, request.Headers[HttpHeaderName.UserAgent]);
        }

        if (station.Codec.ToLower() == "mp3" && request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        using var client = await httpClient.GetAsync(station.UrlResolved, HttpCompletionOption.ResponseHeadersRead);

        using var clientStream = await client.Content.ReadAsStreamAsync();

        if (!station.Codec.Equals("mp3", StringComparison.CurrentCultureIgnoreCase))
        {
            using var process = CreateFfmpegProcess();

            response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
            response.Headers.Add("Icy-Name", station.Name + $" [Codec:{station.Codec}]");

            process.Start();
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            try
            {
                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                Task.WaitAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(request.ListenerSocket.Stream)
                );
            }
            catch (IOException) { }

            process.Kill();
            response.Handled = true;
        }
        else
        {
            foreach (var header in client.Headers)
            {
                if (header.Key.ToLower().StartsWith("icy"))
                {
                    response.Headers.Add(header.Key, header.Value.First());
                }
            }

            response.Headers.Add(HttpHeaderName.ContentDisposition, "inline");
            response.Headers.Add("Transfer-Encoding", "chunked");
            response.Headers.Add("Connection", "keep-alive");
            response.Headers.Add("Accept-Ranges", "bytes");

            try
            {
                response.SetBodyStream(clientStream, "audio/x-mpeg");

                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                await clientStream.CopyToAsync(request.ListenerSocket.Stream);
            }
            catch (Exception) { }
        }
    }

    // ===================================================================
    // Legacy shoutcast MP3 — /shoutcast.mp3?id={id}
    // ===================================================================

    public static async Task HandleShoutcastPlay(HttpRequest request, HttpResponse response)
    {
        var station = await GetStationById(request.QueryParams["id"]);

        var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
        });

        if (request.Headers.ContainsKey(HttpHeaderName.UserAgent))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, request.Headers[HttpHeaderName.UserAgent]);
        }

        var details = station.Item1;

        var stationCodec = GetFormatString(details.Mt);

        if (stationCodec == "MP3" && request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        var client = await httpClient.GetAsync(station.Item2, HttpCompletionOption.ResponseHeadersRead);

        var clientStream = await client.Content.ReadAsStreamAsync();

        if (stationCodec != "MP3")
        {
            using var process = CreateFfmpegProcess();

            response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
            response.Headers.Add("Icy-Name", details.Name + $" [Codec:{stationCodec}]");

            process.Start();
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            try
            {
                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                Task.WaitAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(request.ListenerSocket.Stream)
                );
            }
            catch (IOException) { }

            process.Kill();
            response.Handled = true;
        }
        else
        {
            foreach (var header in client.Headers)
            {
                if (header.Key.ToLower().StartsWith("icy"))
                {
                    response.Headers.Add(header.Key, header.Value.First());
                }
            }

            response.SetBodyStream(clientStream, HttpContentTypeMimeType.Audio.Mpeg);
        }
    }
}
