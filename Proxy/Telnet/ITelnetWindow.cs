namespace VintageHive.Proxy.Telnet;

public interface ITelnetWindow
{
    bool ShouldRemoveNextCommand { get; }
    bool HiddenCommand { get; }
    string Title { get; }
    string Description { get; }
    string Text { get; }
    void OnAdd(TelnetSession session);
    void Tick();

    void Destroy();
}
