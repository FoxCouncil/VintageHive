namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetHelpCommand : ITelnetWindow
{
    private string _text;
    public string Text => _text;

    public string Title => "help";

    public bool ShouldRemoveNextCommand => true;

    public string Description => "Show all commands";

    public void OnAdd()
    {
        // The standard passage, used since the 1500s
        _text = "I am the help command believe it or not!\r\n";
    }

    public void Dispose() { }

    public void Tick() { }
}
