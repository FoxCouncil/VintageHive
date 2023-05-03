namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetInvalidCommand : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => true;

    public string Title => "invalid_cmd";

    public string Description => "Shows user error";

    public string Text => _text;

    public bool HiddenCommand => true;

    private string _text;

    public void OnAdd(TelnetSession session)
    {
        _text = $"Invalid command: {session.InputBuffer.ReplaceLineEndings(string.Empty)}\r\n";
    }

    public void Destroy() { }

    public void Tick() { }
}
