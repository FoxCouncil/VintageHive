﻿using System.Timers;

namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetCountCommand : ITelnetWindow
{
    private string _text;

    private int _count;

    private readonly System.Timers.Timer _timer = new(1000);
    public string Text => _text;

    public string Title => "count";

    public bool ShouldRemoveNextCommand => true;

    public string Description => "Counts upwards forever";

    public bool HiddenCommand => false;

    private void UpdateCount()
    {
        _count++;
        _text = $"Count: {_count:N0}\r\n";
    }

    public void OnAdd(TelnetSession session)
    {
        _timer.Elapsed += Timer_Elapsed;
        _timer.Start();

        _text = "Starting timer...\r\n";
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        UpdateCount();
    }

    public void Destroy()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    public void Tick()
    {
        //UpdateCount();
    }
}