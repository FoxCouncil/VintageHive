namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetLoremCommand : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => true;

    public string Title => "lorem";

    public string Text => _text;

    public string Description => "The 1500s classic!";

    public bool HiddenCommand => false;

    public bool AcceptsCommands => false;

    private string _text;

    public void OnAdd(TelnetSession session)
    {
        // The standard passage, used since the 1500s
        _text = session.WordWrapText("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.");
    }

    public void Destroy() { }

    public void Refresh() { }

    public void ProcessCommand(string command) { }
}
