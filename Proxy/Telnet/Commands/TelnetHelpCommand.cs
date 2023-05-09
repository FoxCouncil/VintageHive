namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetHelpCommand : ITelnetWindow
{
    private string _text;
    public string Text => _text;

    public string Title => "help";

    public bool ShouldRemoveNextCommand => true;

    public string Description => "Show all commands";

    public bool HiddenCommand => false;

    public bool AcceptsCommands => false;

    public void OnAdd(TelnetSession session, object args = null)
    {
        var result = new StringBuilder();
        foreach (var item in TelnetWindowManager.GetAllCommands(false))
        {
            result.Append($"{item.Key} - {item.Value}\r\n");
        }

        _text = result.ToString();
    }

    public void Destroy() { }

    public void Refresh() { }

    public void ProcessCommand(string command) { }
}
