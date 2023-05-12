using SmartReader;

namespace VintageHive.Proxy.Telnet.Commands.News;

public class TelnetNewsArticleView : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => _shouldRemoveNextCommand;

    public string Title => "news_article_view";

    public string Text => _text;

    public string Description => "Views individual news aritcles";

    public bool HiddenCommand => true;

    public bool AcceptsCommands => true;

    private string _text;
    private TelnetSession _session;
    private Headlines _headline;
    private bool _shouldRemoveNextCommand;

    private const int LinesPerPage = 80;
    private int totalPages = 1;
    private int currentPage = 1;

    private string articleText;
    private string[] words = null;
    private System.Timers.Timer _startupDelay = new System.Timers.Timer();

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        // Object containing title, id, and other data passed to us from headline viewer.
        _headline = args as Headlines;

        _text = "Loading article...\r\n";

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

        // Create a cancellation token source with a timeout of 5 seconds
        var timeout = 1000;
        var cancellationToken = new CancellationToken();

        // Start the blocking task
        Task<Article> task = DownloadArticle(_headline.Id);

        if (await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)) == task)
        {
            // Task completed within timeout.
            // Consider that the task may have faulted or been canceled.
            // We re-await the task so that any exceptions/cancellation is rethrown.
            await task;

            var article = task.Result;
            var rawArticleText = article.TextContent.ReplaceLineEndings("\r\n");
            articleText = article.Title.ReplaceLineEndings(string.Empty) + "\r\n";
            articleText += rawArticleText;

            // Figure out how many pages this text will take to display.
            words = articleText.Split(' ');
            totalPages = (int)Math.Ceiling((double)words.Length / LinesPerPage);
            currentPage = 1;

            UpdateArticleText();
        }
        else
        {
            // timeout/cancellation logic
            _text = "Failed to load article, type [q]uit to return to headline view.\r\n";
            Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetNewsArticleView), $"Client {_session.Client.RemoteIP} timeout occurred getting news article {_headline.Id}. Task cancelled.", _session.Client.TraceId.ToString());
        }

        // Since this happens on another thread and at a random time we have to force a tick to update screen!
        await _session.TickWindows();
    }

    private static async Task<Article> DownloadArticle(string id)
    {
        return await NewsUtils.GetGoogleNewsArticle(id);
    }

    private void UpdateArticleText()
    {
        // Skip if no work to do!
        if (string.IsNullOrEmpty(articleText))
        {
            return;
        }

        int startIndex = (currentPage - 1) * LinesPerPage;
        int endIndex = Math.Min(startIndex + LinesPerPage, words.Length);

        // Build up article to display, keeping pagenation in mind.
        var result = new StringBuilder();
        result.Append($"News Article Viewer - Page {currentPage}/{totalPages}\r\n");
        result.Append("[N]ext page  [P]revious page  [Q]uit\r\n\r\n");

        for (int i = startIndex; i < endIndex; i++)
        {
            result.Append($"{words[i]} ");
        }

        _text = result.ToString().WordWrapText(_session.TermWidth, _session.TermHeight);
    }

    public void Destroy() { }

    public void Refresh()
    {
        UpdateArticleText();
    }

    public void ProcessCommand(string command)
    {
        switch (command)
        {
            case "n":
                // Next page
                if (currentPage < totalPages)
                {
                    currentPage++;
                }
                break;
            case "p":
                // Previous page
                if (currentPage > 1)
                {
                    currentPage--;
                }
                break;
            case "q":
                // Close window
                _shouldRemoveNextCommand = true;
                _session.ForceCloseWindow(this);
                break;
        }

        UpdateArticleText();
    }
}
