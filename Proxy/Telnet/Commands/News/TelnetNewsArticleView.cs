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
    private bool _shouldRemoveNextCommand;

    private const int PageSize = 500;
    private int totalPages = 1;
    private int currentPage = 1;

    private string wrappedArticleText;

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        // Convert our headline ID into a full article.
        var headline = args as Headlines;
        var article = NewsUtils.GetGoogleNewsArticle(headline.Id).Result;
        var rawArticleText = article.TextContent.ReplaceLineEndings("\r\n");
        wrappedArticleText = article.Title.WordWrapText(_session.TermWidth, _session.TermHeight) + "\r\n";
        wrappedArticleText += rawArticleText.WordWrapText(_session.TermWidth, _session.TermHeight);

        // Figure out how many pages this text will take to display.
        totalPages = (int)Math.Ceiling((double)wrappedArticleText.Length / PageSize);
        currentPage = 1;

        UpdateArticleText();
    }

    private void UpdateArticleText()
    {
        int startIndex = (currentPage - 1) * PageSize;
        int endIndex = Math.Min(startIndex + PageSize, wrappedArticleText.Length);
        string pageText = wrappedArticleText.Substring(startIndex, endIndex - startIndex);

        // Build up article to display, keeping pagenation in mind.
        var result = new StringBuilder();
        result.Append($"News Article Viewer - Page {currentPage}/{totalPages}\r\n");
        result.Append("[N]ext page  [P]revious page  [Q]uit\r\n\r\n");
        
        if (currentPage >= totalPages)
        {
            // Last page will have proper return on it.
            result.Append(pageText);
        }
        else
        {
            // Cannot be guranteed of this otherwise!
            result.Append($"{pageText}\r\n");
        }

        _text = result.ToString();
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
