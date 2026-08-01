// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;

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

    private static string GetFfmpegExecutablePath() => FfmpegUtils.GetExecutablePath();

    // ===================================================================
    // Winamp streaming - /stream/winamp?id={id}
    // ===================================================================

    // ===================================================================
    // Shared upstream -> client pipeline
    // ===================================================================

    /// <summary>
    /// Fetches an upstream radio stream and writes it to the client, transcoding to MP3 when needed.
    /// </summary>
    /// <remarks>
    /// This body used to exist three times over - once each for the Winamp, browser and Shoutcast entry points -
    /// differing only in how the station was looked up and, by drift, in which headers they sent and how they
    /// compared the codec string. That drift was load-bearing: two of the three advertised
    /// "Transfer-Encoding: chunked" over an unframed byte stream and the third did not, so the same audio was
    /// broken for spec-compliant clients on two paths and fine on the other. The entry points now only resolve
    /// a station and hand the answer here, so there is one place for a protocol fix to land.
    /// </remarks>
    private static async Task StreamStation(HttpRequest request, HttpResponse response, string stationName, string codec, string streamUrl, string logContext)
    {
        var isMp3 = string.Equals(codec, "MP3", StringComparison.OrdinalIgnoreCase);

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

        if (isMp3 && request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        using var client = await httpClient.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead);

        using var clientStream = await client.Content.ReadAsStreamAsync();

        if (!isMp3)
        {
            using var process = CreateFfmpegProcess();

            response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
            response.Headers.Add("Icy-Name", stationName + $" [Codec:{codec}]");

            process.Start();
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            try
            {
                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                // await (not Task.WaitAny) so we don't block a thread-pool thread for the whole stream.
                await Task.WhenAny(
                    clientStream.CopyToAsync(process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(request.ListenerSocket.Stream)
                );
            }
            catch (IOException) { }
            finally
            {
                // Always tear down the ffmpeg process tree - a non-IOException escaping the try used to orphan it.
                try { process.Kill(true); } catch { }
            }

            response.Handled = true;

            return;
        }

        foreach (var header in client.Headers)
        {
            if (header.Key.ToLower().StartsWith("icy"))
            {
                response.Headers.Add(header.Key, header.Value.First());
            }
        }

        response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
        response.Headers.Add(HttpHeaderName.ContentDisposition, "inline");
        // Deliberately NOT chunked: the audio below is written raw with no chunk-size framing anywhere, so
        // advertising chunked makes a spec-compliant HTTP/1.1 client read MP3 bytes as hex chunk-size lines
        // and abort immediately.
        response.Headers.Add("Connection", "keep-alive");
        response.Headers.Add("Accept-Ranges", "bytes");

        try
        {
            // Own the copy here rather than handing the stream to HttpProxy via SetBodyStream: that deferred
            // the copy until after this method - and its using-scoped httpClient and response - had been
            // disposed, closing the stream out from under the copy and leaking the HttpClient. Setting
            // Handled also stops HttpProxy re-sending the headers into the middle of the audio.
            response.Handled = true;

            await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

            await clientStream.CopyToAsync(request.ListenerSocket.Stream);
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(RadioMp3Streaming), $"{logContext} stream write failed: {ex.Message}", "");
        }
    }

    public static async Task HandleWinampStream(HttpRequest request, HttpResponse response)
    {
        var info = await RadioStationResolver.ResolveStation(request.QueryParams["id"]);

        await StreamStation(request, response, info.Name, info.Codec, info.StreamUrl, "Winamp");
    }


    // ===================================================================
    // WMP MP3 fallback - /stream/wmp/{id}.mp3
    // ===================================================================

    public static async Task HandleWmpMp3Stream(HttpRequest request, HttpResponse response, string stationId)
    {
        var headersSent = false;

        try
        {
            var info = await RadioStationResolver.ResolveStation(stationId);

            using var httpClient = HttpClientUtils.GetHttpClientWithSocketHandler(null, new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                PlaintextStreamFilter = (filterContext, ct) => new ValueTask<Stream>(new HttpFixerDelegatingStream(filterContext.PlaintextStream))
            });

            httpClient.Timeout = TimeSpan.FromSeconds(15);

            using var upstream = await httpClient.GetAsync(info.StreamUrl, HttpCompletionOption.ResponseHeadersRead);

            using var clientStream = await upstream.Content.ReadAsStreamAsync();

            // Plain HTTP/1.0 response - no WMSP, no chunked, no ICY. A large Content-Length tells WMP to start
            // playback immediately instead of buffering the entire "file" before playing.
            response.Headers.Add(HttpHeaderName.ContentType, "audio/mpeg");
            response.Headers.Add("Content-Length", "2147483647");
            response.Headers.Add("Connection", "close");
            response.Headers.Add("icy-name", info.Name);

            if (info.Codec != "MP3")
            {
                using var process = CreateFfmpegProcess();

                process.Start();
                _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

                headersSent = true;
                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                try
                {
                    await Task.WhenAny(
                        clientStream.CopyToAsync(process.StandardInput.BaseStream),
                        process.StandardOutput.BaseStream.CopyToAsync(request.ListenerSocket.Stream)
                    );
                }
                catch (IOException) { }
                finally
                {
                    try { process.Kill(true); } catch { }
                }
            }
            else
            {
                headersSent = true;
                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                await clientStream.CopyToAsync(request.ListenerSocket.Stream);
            }

            response.Handled = true;
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(RadioMp3Streaming), ex, "");

            // If nothing has been written yet (station lookup or upstream connect failed) return a real error so the
            // client sees a failure instead of an empty 200. Once bytes are on the wire we can only log and stop.
            if (!headersSent)
            {
                response.SetStatusCode(VintageHive.Proxy.Http.HttpStatusCode.BadGateway).SetBodyString($"Unable to reach radio station: {ex.Message}");
            }
            else
            {
                response.Handled = true;
            }
        }
    }

    // ===================================================================
    // Legacy browser MP3 - /browser.mp3?id={id}
    // ===================================================================

    public static async Task HandleBrowserPlay(HttpRequest request, HttpResponse response)
    {
        var station = await Mind.RadioBrowser.StationGetAsync(request.QueryParams["id"]);

        await StreamStation(request, response, station.Name, station.Codec, station.UrlResolved.ToString(), "ICY");
    }

    // ===================================================================
    // Shoutcast directory play - /play/shoutcast
    // ===================================================================

    public static async Task HandleShoutcastPlay(HttpRequest request, HttpResponse response)
    {
        var station = await GetStationById(request.QueryParams["id"]);

        var details = station.Item1;

        await StreamStation(request, response, details.Name, GetFormatString(details.Mt), station.Item2.ToString(), "Shoutcast");
    }

}
