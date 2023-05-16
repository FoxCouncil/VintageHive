using VintageHive.Proxy.Telnet.Commands.News;

namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetGalleryCommand : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => _shouldRemoveNextCommand;

    public string Title => "image_gallery";

    public string Description => "View gallery of images";

    public string Text => _text;

    public bool HiddenCommand => true;

    public bool AcceptsCommands => true;

    private string _text;

    private TelnetSession _session;
    private bool _shouldRemoveNextCommand;
    private readonly List<string> asciiArticleImages = new();
    private int currentImage = 1;
    private int totalImages = 0;

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        // Parse out args as list of strings which are image links.
        var asciiArticleLinks = args as List<string>;
        
        // Download and convert these images into ascii art.
        foreach (var imageLink in asciiArticleLinks)
        {
            var imageBytes = DownloadImage(imageLink).Result;
            Image<Rgba32> image = Image.Load<Rgba32>(imageBytes);
            var asciiArt = AsciiUtils.ConvertToAsciiArt(image, _session.TermWidth, _session.TermHeight);
            asciiArticleImages.Add(asciiArt);
        }

        currentImage = 1;
        totalImages = asciiArticleImages.Count;

        UpdateImage();
    }

    private async Task<byte[]> DownloadImage(string imageUrl)
    {
        using HttpClient httpClient = new();

        try
        {
            byte[] imageData = await httpClient.GetByteArrayAsync(imageUrl);
            return imageData;
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetGalleryCommand), $"Client {_session.Client.RemoteIP} had an error occur while downloading image {ex.Message}", _session.Client.TraceId.ToString());
            return null;
        }
    }

    private void UpdateImage()
    {
        // Complain if nothing to view!
        if (asciiArticleImages != null && asciiArticleImages.Count <= 0)
        {
            _text = "No images to view, type [q]uit to return to previous window...\r\n";
            return;
        }

        var result = new StringBuilder();
        result.Append($"Image Viewer - Image {currentImage}/{totalImages}\r\n");
        result.Append("[N]ext image  [P]revious image  [Q]uit\r\n\r\n");

        result.Append(asciiArticleImages[currentImage - 1]);

        _text = result.ToString();
    }

    public void Destroy() { }

    public void Refresh() { }

    public void ProcessCommand(string command)
    {
        switch (command)
        {
            case "n":
                // Next image
                if (currentImage < totalImages)
                {
                    currentImage++;
                }
                break;
            case "p":
                // Previous image
                if (currentImage > 1)
                {
                    currentImage--;
                }
                break;
            case "q":
                // Close window
                _shouldRemoveNextCommand = true;
                _session.ForceCloseWindow(this);
                break;
        }

        UpdateImage();
    }
}
