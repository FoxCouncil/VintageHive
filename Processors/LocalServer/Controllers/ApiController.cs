// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("api.hive.com")]
internal class ApiController : Controller
{
    static readonly ConcurrentDictionary<string, string> _brokenUrls = new();
    static readonly ConcurrentDictionary<string, Task<string>> _inflightFetches = new();
    static readonly SemaphoreSlim _fetchThrottle = new(8, 8);

    [Route("/image/fetch")]
    public async Task ImageFetch()
    {
        var url = Request.QueryParams.ContainsKey("url") && !string.IsNullOrWhiteSpace(Request.QueryParams["url"]) ? Request.QueryParams["url"] : string.Empty;
        var fallbackUrl = Request.QueryParams.ContainsKey("fburl") && !string.IsNullOrWhiteSpace(Request.QueryParams["fburl"]) ? Request.QueryParams["fburl"] : string.Empty;

        if (string.IsNullOrEmpty(url) || _brokenUrls.ContainsKey(url))
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

        if (string.IsNullOrEmpty(imageDataBase64))
        {
            _brokenUrls.TryAdd(url, string.Empty);

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

    private static async Task<string> FetchAndCacheImage(string url)
    {
        return await Mind.Cache.Do<string>($"API_IMG_FETCH:{url}", TimeSpan.FromDays(365), async () =>
        {
            await _fetchThrottle.WaitAsync();

            try
            {
                var fetchUri = new Uri(url);

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
