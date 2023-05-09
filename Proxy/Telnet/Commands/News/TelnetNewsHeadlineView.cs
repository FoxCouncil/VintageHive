namespace VintageHive.Proxy.Telnet.Commands.News;

public  class TelnetNewsHeadlineView : ITelnetWindow
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

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        // Parse the argument as which news topic to work with.
        if (Enum.TryParse<GoogleNewsTopic>(args.ToString(), false, out var topic))
        {
            _selectedTopic = topic;
        }

        var articles = NewsUtils.GetGoogleTopicArticles(_selectedTopic).Result;

        var headlines = new StringBuilder();
        var count = 0;

        headlines.Append($"Today in {_selectedTopic.ToString()} news, type number to view article.\r\n");
        headlines.Append($"Or type exit or press enter to return to news menu...\r\n\r\n");

        foreach (var article in articles.Take(10))
        {
            var title = article.Title;

            // Split on - to save space on characters and length
            var splitTitle = title.Split('-');
            title = splitTitle.First().Trim().WordWrapText(_session.TermWidth, _session.TermHeight);

            // We want ten to become zero so it lines up with keyboard layouts.
            if (count == 10)
            {
                count = 0;
            }

            headlines.Append($"{count}. {title}");
            _articleMap.Add(++count, article);
        }

        _text = headlines.ToString();
    }

    public void Destroy() { }

    public void Refresh() { }

    public void ProcessCommand(string command)
    {
        if (int.TryParse(command, out var commandInt))
        {
            if (_articleMap.TryGetValue(commandInt, out var article))
            {
                //article.
            }
        }
    }
}
