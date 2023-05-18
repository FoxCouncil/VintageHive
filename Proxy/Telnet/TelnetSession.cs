// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Telnet;

public class TelnetSession
{
    public Guid ID { get; } = new Guid();

    public ListenerSocket Client { get; }

    public int TermWidth { get; set; } = 80;

    public int TermHeight { get; set; } = 24;

    public string InputBuffer { get; set; } = string.Empty;

    public string TopWindowOutputBuffer { get; set; } = string.Empty;

    public string CurrentOutput { get; set;} = string.Empty;

    public string LastOutput { get; set; } = string.Empty;

    public TelnetWindowManager WindowManager { get => _windowManager; }

    private readonly TelnetWindowManager _windowManager = new();

    public TelnetSession(ListenerSocket client)
    {
        Client = client;
    }

    public async Task TickWindows()
    {
        // Update output that is sent to clients after each tick.
        var topWindow = _windowManager.GetTopWindow();
        if (topWindow != null && !string.IsNullOrEmpty(topWindow.Text))
        {
            TopWindowOutputBuffer = topWindow.Text;
        }
        else
        {
            TopWindowOutputBuffer = "No windows currently loaded!\r\n";
        }

        // Build up current TUI
        var screenOutput = new StringBuilder();
        screenOutput.Append($"VintageHive Telnet {Mind.ApplicationVersion}\r\n");
        screenOutput.Append(new string('-', TermWidth) + "\r\n");
        screenOutput.Append(TopWindowOutputBuffer);
        screenOutput.Append(new string('-', TermWidth) + "\r\n");
        screenOutput.Append($"Enter command (help, exit): {InputBuffer}");

        var finalOutput = screenOutput.ToString();
        CurrentOutput = finalOutput;

        if (CurrentOutput == LastOutput)
        {
            // Skip because nothing has changed!
            return;
        }

        // Clear screen and move cursor to upper left corner
        await ClearScreen();

        LastOutput = finalOutput;

        // Write the updated output buffer to the client.
        var bytes = Encoding.ASCII.GetBytes(finalOutput);
        await Client.Stream.WriteAsync(bytes);
        await Client.Stream.FlushAsync();
    }

    public async Task ClearScreen()
    {
        byte[] clearScreenCommand = Encoding.ASCII.GetBytes("\x1b[2J\x1b[H");
        await Client.Stream.WriteAsync(clearScreenCommand);
    }

    public async Task MoveCursor(int row, int column)
    {
        // send the cursor movement command to the client
        byte[] cursorCommand = Encoding.ASCII.GetBytes($"\x1b[{row};{column}H");
        await Client.Stream.WriteAsync(cursorCommand);
    }

    public async Task ProcessCommand(string command)
    {
        // Lowercase all characters and trim any trailing spaces.
        var cleanCmd = command.ToLower().Trim();

        // Remove any windows pending removal before attempting to process commands.
        _windowManager.RemoveDeadWindows();

        // Check if top level window will intercept commands instead.
        var topWindow = _windowManager.GetTopWindow();
        if (topWindow != null)
        {
            if (topWindow.AcceptsCommands && !topWindow.ShouldRemoveNextCommand)
            {
                // Exit will close that window, otherwise send it the command.
                if (cleanCmd == "exit" || cleanCmd == "close")
                {
                    _windowManager.CloseTopWindow();
                }
                else
                {
                    topWindow.ProcessCommand(cleanCmd);
                }
                
                // Normal processing of command will not occur when a window accepts commands.
                return;
            }
        }

        // Hard-coded commands to destroy the current session and show help.
        if (cleanCmd == "exit" || cleanCmd == "quit")
        {
            await Client.Stream.FlushAsync();
            Client.Stream.Close();
            Client.RawSocket.Shutdown(SocketShutdown.Both);
            return;
        }

        // Attempts to add the window by name.
        var result = _windowManager.TryAddWindow(command);
        if (result)
        {
            _windowManager.GetTopWindow().OnAdd(this);
        }
        else
        {
            // Only say invalid command if there are no windows attached.
            if (_windowManager.GetWindowCount() <= 0)
            {
                // Hidden window that will say what was typed was invalid.
                ForceAddWindow("invalid_cmd");
            }
        }
    }

    public void Destroy()
    {
        _windowManager.Destroy();
    }

    /// <summary>
    /// Forcefully adds a window even if hidden, throws error if window not found.
    /// Intended to be used by windows adding sub-windows!
    /// </summary>
    /// <remarks>This is special because it can pass arguments to added windows.</remarks>
    public void ForceAddWindow(string windowName, object args = null)
    {
        if (_windowManager.TryAddWindow(windowName))
        {
            _windowManager.GetTopWindow().OnAdd(this, args);
            return;
        }

        Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetSession), $"Client {Client.RemoteIP} session attempted ForceAddWindow({windowName}) which doesn't exist!", Client.TraceId.ToString());
    }

    /// <summary>
    /// Forcefully removes a window even if hidden, throws error if window not found.
    /// Intended to be used by window to close itself
    /// </summary>
    /// <param name="windowToClose">Window that has ShouldRemoveNextCommand set to true.</param>
    public void ForceCloseWindow(ITelnetWindow windowToClose)
    {
        if (!windowToClose.ShouldRemoveNextCommand)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetSession), $"Client {Client.RemoteIP} session attempted to ForceCloseWindow({windowToClose.Title}) which doesn't have ShouldRemoveNextCommand set to true!", Client.TraceId.ToString());
            return;
        }

        // Removes the dangling window from the window manager.
        _windowManager.RemoveDeadWindows();

        // Check that current top window is not null.
        var topWindow = _windowManager.GetTopWindow();
        if (topWindow == null) 
        {
            // Since there is no window below, we don't have to run a refresh on it.
            return;
        }

        // Complain if window titles match, the new top window title should NOT match incoming window to close!
        if (topWindow.Title == windowToClose.Title)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetSession), $"Client {Client.RemoteIP} session attempted to ForceCloseWindow({windowToClose.Title}) but it won't die!", Client.TraceId.ToString());
            return;
        }

        // Forces the window 
        topWindow.Refresh();
    }
}
