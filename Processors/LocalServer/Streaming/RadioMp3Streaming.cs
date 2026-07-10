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
                // Stream straight to the socket and mark the response handled. Previously this ALSO called
                // SetBodyStream without setting Handled, so HttpProxy re-sent the headers and re-copied the
                // already-consumed (and disposed) stream, injecting stray header bytes into the audio.
                response.Handled = true;

                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                await clientStream.CopyToAsync(request.ListenerSocket.Stream);
            }
            catch (Exception ex) { Log.WriteLine(Log.LEVEL_DEBUG, nameof(RadioMp3Streaming), $"ICY stream write failed: {ex.Message}", ""); }
        }
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
                // Stream straight to the socket and mark the response handled (see the ICY branch above - calling
                // SetBodyStream here as well made HttpProxy re-emit headers and re-copy the disposed stream).
                response.Handled = true;

                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                await clientStream.CopyToAsync(request.ListenerSocket.Stream);
            }
            catch (Exception ex) { Log.WriteLine(Log.LEVEL_DEBUG, nameof(RadioMp3Streaming), $"Browser stream write failed: {ex.Message}", ""); }
        }
    }

    // ===================================================================
    // Legacy shoutcast MP3 - /shoutcast.mp3?id={id}
    // ===================================================================

    public static async Task HandleShoutcastPlay(HttpRequest request, HttpResponse response)
    {
        var station = await GetStationById(request.QueryParams["id"]);

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

        var details = station.Item1;

        var stationCodec = GetFormatString(details.Mt);

        if (stationCodec == "MP3" && request.Headers.ContainsKey(HttpHeaderName.IcyMetadata))
        {
            httpClient.DefaultRequestHeaders.Add(HttpHeaderName.IcyMetadata, "1");
        }

        using var client = await httpClient.GetAsync(station.Item2, HttpCompletionOption.ResponseHeadersRead);

        using var clientStream = await client.Content.ReadAsStreamAsync();

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

            response.Headers.Add(HttpHeaderName.ContentType, HttpContentTypeMimeType.Audio.Mpeg);
            response.Headers.Add(HttpHeaderName.ContentDisposition, "inline");
            response.Headers.Add("Connection", "keep-alive");
            response.Headers.Add("Accept-Ranges", "bytes");

            try
            {
                // Stream directly (was SetBodyStream, which deferred the copy to HttpProxy AFTER this handler - and
                // its httpClient/response - was disposed, closing the stream out from under the copy and leaking the
                // HttpClient). Owning the copy here lets the using-scoped httpClient/client/clientStream dispose safely.
                response.Handled = true;

                await request.ListenerSocket.Stream.WriteAsync(response.GetResponseEncodedData());

                await clientStream.CopyToAsync(request.ListenerSocket.Stream);
            }
            catch (Exception ex) { Log.WriteLine(Log.LEVEL_DEBUG, nameof(RadioMp3Streaming), $"Shoutcast stream write failed: {ex.Message}", ""); }
        }
    }
}
