namespace VintageHive.Proxy.Telnet;

public interface ITelnetWindow : IDisposable
{
    bool ShouldRemoveNextCommand { get; }
    string Title { get; }
    string Description { get; }
    string Text { get; }
    void OnAdd();
    void Tick();
}
