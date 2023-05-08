namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetInvalidCommand : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => true;

    public string Title => "invalid_cmd";

    public string Description => "Shows user error";

    public string Text => _text;

    public bool HiddenCommand => true;

    public bool AcceptsCommands => false;

    private string _text;

    public void OnAdd(TelnetSession session)
    {
        var cleanInputBuffer = session.InputBuffer.ReplaceLineEndings(string.Empty).Trim();
        if (string.IsNullOrEmpty(cleanInputBuffer) || string.IsNullOrWhiteSpace(cleanInputBuffer))
        {
            _text = "Invalid command: Empty or whitespace\r\n";
        }
        else
        {
            _text = $"Invalid command: {cleanInputBuffer}\r\n";
        }
    }

    public void Destroy() { }

    public void Refresh() { }

    public void ProcessCommand(string command) { }
}
