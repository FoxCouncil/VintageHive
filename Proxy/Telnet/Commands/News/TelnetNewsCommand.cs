namespace VintageHive.Proxy.Telnet.Commands.News;

public class TelnetNewsCommand : ITelnetWindow
{
    public string Text => _text;

    public string Title => "news";

    public bool ShouldRemoveNextCommand => _shouldRemoveNextCommand;

    public string Description => "Latest headlines from around the world";

    public bool HiddenCommand => false;

    public bool AcceptsCommands => true;

    private string _text;

    private TelnetSession _session;
    private bool _shouldRemoveNextCommand;

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        var newsMenu = new StringBuilder();

        newsMenu.Append("News Menu\r\n");
        newsMenu.Append("Type a number and press enter...\r\n\r\n");

        newsMenu.Append("1. Local news\r\n");
        newsMenu.Append("2. US news\r\n");
        newsMenu.Append("3. World news\r\n");
        newsMenu.Append("4. Technology\r\n");
        newsMenu.Append("5. Science\r\n");
        newsMenu.Append("6. Business\r\n");
        newsMenu.Append("7. Entertainment\r\n");
        newsMenu.Append("8. Sports\r\n");
        newsMenu.Append("9. Health\r\n");
        newsMenu.Append("0. Close news\r\n");

        _text = newsMenu.ToString();
    }

    public void Destroy() { }

    public void Refresh() { }

    public void ProcessCommand(string command)
    {
        switch (command) 
        {
            case "1":
                // Local news
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.Local);
                break;
            case "2":
                // US news
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.US);
                break;
            case "3":
                // World news
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.World);
                break;
            case "4":
                // Technology
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.Technology);
                break;
            case "5":
                // Science
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.Science);
                break;
            case "6":
                // Business
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.Business);
                break;
            case "7":
                // Entertainment
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.Entertainment);
                break;
            case "8":
                // Sports
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.Sports);
                break;
            case "9":
                // Health
                _session.ForceAddWindow("news_headline_view", GoogleNewsTopic.Health);
                break;
            case "0":
                // Close news
                _shouldRemoveNextCommand = true;
                _session.ForceCloseWindow(this);
                break;
        }
    }
}
