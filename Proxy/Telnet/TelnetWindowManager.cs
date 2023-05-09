using System.Reflection;

namespace VintageHive.Proxy.Telnet;

public class TelnetWindowManager
{
    private readonly Stack<ITelnetWindow> _activeWindows = new();
    private readonly Dictionary<string, string> _windowDict = new();

    public TelnetWindowManager()
    {
        var commands = GetAllCommands(true);
        foreach (var cmd in commands) 
        {
            _windowDict.Add(cmd.Key, cmd.Value);
        }
    }

    public static Dictionary<string, string> GetAllCommands(bool showHidden)
    {
        // Get all types in the assembly
        Type[] types = Assembly.GetExecutingAssembly().GetTypes();

        var _tempDic = new Dictionary<string, string>();

        foreach (Type type in types)
        {
            if (typeof(ITelnetWindow).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
            {
                // Activate the type and then get it's title that way, making sure to cleanup afterwards.
                var castedType = Activator.CreateInstance(type) as ITelnetWindow;

                // Check if this is a hidden command.
                if (castedType.HiddenCommand && !showHidden)
                {
                    continue;
                }

                _tempDic.Add(castedType.Title, castedType.Description);
            }
        }

        return _tempDic;
    }

    public int GetWindowCount()
    {
        return _activeWindows.Count;
    }

    /// <summary>
    /// Returns the current window at the top of the stack, or null if there is none.
    /// </summary>
    public ITelnetWindow GetTopWindow()
    {
        if (_activeWindows.Any())
        {
            return _activeWindows?.Peek();
        }

        return null;
    }

    /// <summary>
    /// Pops top most window off the stack and destroys it.
    /// </summary>
    public void CloseTopWindow()
    {
        if (_activeWindows.Any())
        {
            var window = _activeWindows?.Pop();
            window?.Destroy();
        }
    }

    public bool TryAddWindow(string commandName)
    {
        // Check if a window by that name exists and is not already loaded.
        if (_windowDict.ContainsKey(commandName))
        {
            RemoveDeadWindows();

            // Check if the top window is the same as one we're adding.
            var topWindow = GetTopWindow();
            if (topWindow != null && topWindow.Title == commandName)
            {
                CloseTopWindow();
            }

            // Loop through all types and add any that implement ITelnetWindow to the list
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (Type type in types)
            {
                if (typeof(ITelnetWindow).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                {
                    var createdWindow = Activator.CreateInstance(type) as ITelnetWindow;
                    if (createdWindow.Title == commandName)
                    {
                        // Creates the window and adds it to list of active windows.
                        _activeWindows.Push(createdWindow);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public void RemoveDeadWindows()
    {
        // Remove any pending windows that are waiting until next command.
        if (_activeWindows.Any())
        {
            var lastWindowTitlePeek = string.Empty;
            while (_activeWindows.TryPeek(out var window))
            {
                // Prevent deadlock by breaking if last peek is same as current meaning no more work is needed.
                if (lastWindowTitlePeek == window.Title)
                {
                    break;
                }

                // If multiple windows need removed this will clear them out.
                if (window.ShouldRemoveNextCommand)
                {
                    CloseTopWindow();
                }

                // Keeps track of last window we looked at.
                lastWindowTitlePeek = window.Title;
            }
        }
    }

    public void Destroy()
    {
        foreach (var window in _activeWindows)
        {
            window.Destroy();
        }
    }
}
