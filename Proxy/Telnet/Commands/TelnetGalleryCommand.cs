// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
    private List<string> _asciiArticleLinks;
    private bool _shouldRemoveNextCommand;
    private readonly System.Timers.Timer _startupDelay = new();
    private readonly List<string> asciiArticleImages = new();
    private int currentImage = 1;
    private int totalImages = 0;
    private bool _loading = false;

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        // Parse out args as list of strings which are image links.
        _asciiArticleLinks = args as List<string>;

        _loading = true;
        UpdateImage();

        _startupDelay.AutoReset = false;
        _startupDelay.Elapsed += Startup_Timer;
        _startupDelay.Interval = 1;
        _startupDelay.Enabled = true;
        _startupDelay.Start();
    }

    private async void Startup_Timer(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Never run the timer again.
        _startupDelay.Stop();
        _startupDelay.Enabled = false;

        // Create a cancellation token source.
        var timeout = 5000;
        var cancellationToken = new CancellationToken();

        // Start the blocking task
        Task task = DownloadImages(_asciiArticleLinks, cancellationToken);

        if (await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)) == task)
        {
            // Task completed within timeout.
            // Consider that the task may have faulted or been canceled.
            // We re-await the task so that any exceptions/cancellation is rethrown.
            await task;

            _loading = false;
            UpdateImage();
        }
        else
        {
            // timeout/cancellation logic
            _loading = false;
            _text = "Failed to load images, type [q]uit to return to previous window.\r\n";
            Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetGalleryCommand), $"Client {_session.Client.RemoteIP} timeout occurred while downloading images. Task cancelled.", _session.Client.TraceId.ToString());
        }

        // Force a tick to update screen!
        await _session.TickWindows();
    }

    private async Task DownloadImages(List<string> asciiArticleLinks, CancellationToken cancelToken)
    {
        // Download and convert these images into ascii art.
        foreach (var imageLink in asciiArticleLinks)
        {
            Image<Rgba32> image = null;

            try
            {
                var imageBytes = await DownloadSingleImage(imageLink, cancelToken);
                Stream stream = new MemoryStream(imageBytes);
                image = await Image.LoadAsync<Rgba32>(stream, cancelToken);
            }
            catch (Exception err)
            {
                image = null;
                Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetGalleryCommand), $"Client {_session.Client.RemoteIP} had an error occur while loading image {err.Message}", _session.Client.TraceId.ToString());
            }

            // Skip invalid images and just continue on like nothing happened.
            if (image == null)
            {
                continue;
            }

            var asciiArt = AsciiUtils.ConvertToAsciiArt(image, _session.TermWidth, _session.TermHeight);
            asciiArticleImages.Add(asciiArt);
        }

        // Total number of images only counts the ones that succeded
        currentImage = 1;
        totalImages = asciiArticleImages.Count;
    }

    private async Task<byte[]> DownloadSingleImage(string imageUrl, CancellationToken cancelToken)
    {
        using HttpClient httpClient = new();

        try
        {
            byte[] imageData = await httpClient.GetByteArrayAsync(imageUrl, cancelToken);
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
        // Skip if still loading images and waiting for timeout or completion of that task.
        if (_loading)
        {
            _text = "Loading images, please wait...\r\n";
            return;
        }

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

    public void Destroy()
    {
        _startupDelay.Dispose();
    }

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
