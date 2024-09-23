// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("api.hive.com")]
internal class ApiController : Controller
{
    readonly List<string> brokenList = new();

    [Route("/image/fetch")]
    public async Task ImageFetch()
    {
        var url = Request.QueryParams.ContainsKey("url") && !string.IsNullOrWhiteSpace(Request.QueryParams["url"]) ? Request.QueryParams["url"] : string.Empty;
        var fallbackUrl = Request.QueryParams.ContainsKey("fburl") && !string.IsNullOrWhiteSpace(Request.QueryParams["fburl"]) ? Request.QueryParams["fburl"] : string.Empty;

        if (string.IsNullOrEmpty(url) || brokenList.Contains(url))
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

        var imageDataBase64 = await Mind.Cache.Do<string>($"API_IMG_FETCH:{url}", TimeSpan.FromDays(365), async () =>
        {
            Image image;

            try
            {
                var fetchUri = new Uri(url);

                byte[] imageData;

                using var httpClient = HttpClientUtils.GetHttpClient(Request);

                imageData = await httpClient.GetByteArrayAsync(fetchUri);

                image = Image.Load(imageData);
            }
            catch
            {
                return string.Empty;
            }

            if (image.Size.Width > 800)
            {
                image.Mutate(x => x.Resize(800, 0));
            }

            var memoryStream = new MemoryStream();

            await image.SaveAsJpegAsync(memoryStream);

            byte[] processedImageData = memoryStream.ToArray();

            return Convert.ToBase64String(processedImageData);
        });

        if (string.IsNullOrEmpty(imageDataBase64))
        {
            brokenList.Add(url);

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
}
