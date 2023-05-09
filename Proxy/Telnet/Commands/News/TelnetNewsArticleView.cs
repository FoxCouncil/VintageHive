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

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        // The standard passage, used since the 1500s
        _text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.".WordWrapText(_session.TermWidth, _session.TermHeight);
    }

    public void Destroy() { }

    public void Refresh() { }

    public void ProcessCommand(string command)
    {

    }
}
