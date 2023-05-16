namespace VintageHive.Proxy.Telnet.Commands.News;

public class TelnetNewsHeadlineView : ITelnetWindow
{
    public string Text => _text;

    public string Title => "news_headline_view";

    public bool ShouldRemoveNextCommand => _shouldRemoveNextCommand;

    public string Description => "Views a specific topic of headlines";

    public bool HiddenCommand => true;

    public bool AcceptsCommands => true;

    private TelnetSession _session;
    private bool _shouldRemoveNextCommand;

    private string _text;
    private readonly Dictionary<int, Headlines> _articleMap = new();
    private GoogleNewsTopic _selectedTopic = GoogleNewsTopic.World;

    private int itemsPerPage = 10;
    private int currentPage = 1;
    private int totalPages = 1;

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        // Parse the argument as which news topic to work with.
        if (Enum.TryParse<GoogleNewsTopic>(args.ToString(), false, out var topic))
        {
            _selectedTopic = topic;
        }

        // Get a list of raw article data from the web, map them to dictionary with numbers.
        var articles = NewsUtils.GetGoogleTopicArticles(_selectedTopic).Result;
        var count = 0;
        foreach (var article in articles)
        {
            _articleMap.Add(++count, article);
        }

        // Calculate total number of pages.
        totalPages = (int)Math.Ceiling(_articleMap.Count / (double)itemsPerPage);

        UpdateHeadlines();
    }

    private void UpdateHeadlines()
    {
        // Build up presentation of headline data to user.
        var headlines = new StringBuilder();
        headlines.Append($"Today in {_selectedTopic.ToString()} news, type number to view article.\r\n");
        headlines.Append($"Page {currentPage} of {totalPages}\r\n");
        headlines.Append("N) Next Page  P) Previous Page  Q) Quit\r\n\r\n");

        // Display items for the current page
        int startIndex = (currentPage - 1) * itemsPerPage;
        int endIndex = Math.Min(startIndex + itemsPerPage, _articleMap.Count);
        for (int i = startIndex; i < endIndex; i++)
        {
            if (_articleMap.TryGetValue(i, out var article))
            {
                // Subtract the length which first part of string takes up.
                var countText = $"{i}. ";
                var wrappedTitle = article.Title.Trim().WordWrapText((_session.TermWidth - countText.Length), _session.TermHeight);

                headlines.Append($"{countText}{wrappedTitle}");
            }
        }

        _text = headlines.ToString();
    }

    public void Destroy() { }

    public void Refresh()
    {
        //UpdateHeadlines();
    }

    public void ProcessCommand(string command)
    {
        // Navigation keys for moving between pages of headlines.
        switch (command)
        {
            case "n":
                if (currentPage < totalPages)
                {
                    currentPage++;
                }
                break;
            case "p":
                if (currentPage > 1)
                {
                    currentPage--;
                }
                break;
            case "q":
                _shouldRemoveNextCommand = true;
                _session.ForceCloseWindow(this);
                break;
        }

        // Attempt to parse user input into a number which can be an article they want to read.
        if (int.TryParse(command, out var commandInt))
        {
            if (_articleMap.TryGetValue(commandInt, out var article))
            {
                _session.ForceAddWindow("news_article_view", article);
            }
        }

        // Forces redraw of current list of headlines on specified page.
        UpdateHeadlines();
    }
}
