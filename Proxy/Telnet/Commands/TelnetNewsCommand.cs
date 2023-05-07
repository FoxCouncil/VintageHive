namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetNewsCommand : ITelnetWindow
{
    public string Text => _text;

    public string Title => "news";

    public bool ShouldRemoveNextCommand => false;

    public string Description => "Latest headlines from around the world";

    public bool HiddenCommand => false;

    public bool AcceptsCommands => true;

    private string _text;
    private readonly Dictionary<int, Headlines> _articleMap = new();

    public void OnAdd(TelnetSession session)
    {
        var region = "US";
        var articles = NewsUtils.GetGoogleArticles(region).Result;

        var headlines = new StringBuilder();
        var count = 0;

        headlines.Append($"Today in {region} news, type number to view article.\r\n");
        foreach (var article in articles.Take(10)) 
        {
            _articleMap.Add(count++, article);
            var title = article.Title;
            if (title.Length > session.TermWidth)
            {
                title = title.Substring(0, session.TermWidth);
            }

            headlines.Append($"{count}-{title}\r\n");
        }

        _text = headlines.ToString();
    }

    public void Destroy() { }

    public void Tick() { }

    public void ProcessCommand(string command)
    {
        // TODO: Process number to show article
    }
}
