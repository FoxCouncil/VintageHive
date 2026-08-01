// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain(HiveDomains.Api)]
internal class ApiController : Controller
{
    static readonly ConcurrentDictionary<string, Task<string>> _inflightFetches = new();
    static readonly SemaphoreSlim _fetchThrottle = new(8, 8);

    [Route("/image/fetch")]
    public async Task ImageFetch()
    {
        var url = Request.QueryParams.ContainsKey("url") && !string.IsNullOrWhiteSpace(Request.QueryParams["url"]) ? Request.QueryParams["url"] : string.Empty;
        var fallbackUrl = Request.QueryParams.ContainsKey("fburl") && !string.IsNullOrWhiteSpace(Request.QueryParams["fburl"]) ? Request.QueryParams["fburl"] : string.Empty;

        if (string.IsNullOrEmpty(url))
        {
            if (!string.IsNullOrEmpty(fallbackUrl))
            {
                Response.SetFound(fallbackUrl);
            }
            else
            {
                Response.SetNotFound();
            }

            return;
        }

        var imageDataBase64 = await _inflightFetches.GetOrAdd(url, _ => FetchAndCacheImage(url));

        _inflightFetches.TryRemove(url, out _);

        // Failed fetches are negatively cached for 365 days by Mind.Cache (empty string result), so no in-memory broken-URL set is needed
        if (string.IsNullOrEmpty(imageDataBase64))
        {
            if (!string.IsNullOrEmpty(fallbackUrl))
            {
                Response.SetFound(fallbackUrl);
            }
            else
            {
                Response.SetNotFound();
            }

            return;
        }

        Response.Headers.Add("Cache-Control", "public, max-age=15552000");

        Response.SetBodyData(Convert.FromBase64String(imageDataBase64), "image/jpeg");
    }

    /// <summary>
    /// Whether an image-fetch target is somewhere the proxy should reach on a client's behalf.
    /// </summary>
    /// <remarks>
    /// Blocks the destinations that are never a legitimate image host but ARE what makes a server-side fetcher
    /// useful as an SSRF pivot: loopback, link-local (including the cloud metadata endpoint at 169.254.169.254),
    /// and private ranges. Public hosts stay open, which is the whole purpose of the route. A DNS name that
    /// resolves to a blocked address still gets through - closing that needs resolve-then-pin, which is more
    /// machinery than a LAN retro proxy warrants; this shuts the direct-literal door.
    /// </remarks>
    internal static bool IsFetchableTarget(Uri uri)
    {
        if (uri == null)
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (!IPAddress.TryParse(uri.Host, out var address))
        {
            // A hostname; allow it. Resolution happens inside HttpClient.
            return true;
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.MapToIPv4().GetAddressBytes();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || address.IsIPv4MappedToIPv6)
        {
            // 10/8, 172.16/12, 192.168/16, 169.254/16 (link-local, incl. cloud metadata), 0/8
            if (bytes[0] == 10
                || bytes[0] == 0
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254))
            {
                return false;
            }
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return false;
        }

        return true;
    }

    private static async Task<string> FetchAndCacheImage(string url)
    {
        return await Mind.Cache.Do<string>($"API_IMG_FETCH:{url}", TimeSpan.FromDays(365), async () =>
        {
            await _fetchThrottle.WaitAsync();

            try
            {
                var fetchUri = new Uri(url);

                // This route fetches a URL the client supplies, and the only previous gate was "did the bytes
                // decode as an image", after the request had already been made. That let an intranet client
                // use the proxy to reach addresses it cannot route to itself, and exfiltrate anything that
                // happened to decode. Being an open web fetcher is the point of the route, so the block list
                // is deliberately narrow: only the schemes and destinations that are never a real image host.
                if (!IsFetchableTarget(fetchUri))
                {
                    return string.Empty;
                }

                using var httpClient = HttpClientUtils.GetHttpClient();

                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var imageData = await httpClient.GetByteArrayAsync(fetchUri);

                var image = Image.Load(imageData);

                if (image.Size.Width > 800)
                {
                    image.Mutate(x => x.Resize(800, 0));
                }

                var memoryStream = new MemoryStream();

                await image.SaveAsJpegAsync(memoryStream);

                byte[] processedImageData = memoryStream.ToArray();

                return Convert.ToBase64String(processedImageData);
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                _fetchThrottle.Release();
            }
        });
    }
}
