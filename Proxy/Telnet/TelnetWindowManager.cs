using System.Reflection;

namespace VintageHive.Proxy.Telnet;

public class TelnetWindowManager : IDisposable
{
    private readonly Stack<ITelnetWindow> _activeWindows = new();
    private readonly List<string> _knownWindows = new();

    public TelnetWindowManager()
    {
        // Get all types in the assembly
        Type[] types = Assembly.GetExecutingAssembly().GetTypes();

        foreach (Type type in types)
        {
            if (typeof(ITelnetWindow).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
            {
                // Activate the type and then get it's title that way, making sure to cleanup afterwards.
                var castedType = (ITelnetWindow)Activator.CreateInstance(type);
                _knownWindows.Add(castedType.Title);
                castedType.Dispose();
            }
        }
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
            window?.Dispose();
        }
    }

    public bool IsWindowActive(string commandName)
    {
        foreach (var window in _activeWindows)
        {
            if (window.Title == commandName)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryAddWindow(string commandName)
    {
        // Check if a window by that name exists and is not already loaded.
        if (_knownWindows.Contains(commandName) && !IsWindowActive(commandName))
        {
            // Remove any pending windows that are waiting until next command.
            if (_activeWindows.Any())
            {
                while (_activeWindows.TryPeek(out var window))
                {
                    if (window.ShouldRemoveNextCommand)
                    {
                        CloseTopWindow();
                    }
                }
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
                    else
                    {
                        createdWindow.Dispose();
                    }
                }
            }
        }

        return false;
    }

    public void TickWindows()
    {
        if (_activeWindows.Any())
        {
            foreach (var window in _activeWindows)
            {
                window?.Tick();
            }
        }
    }

    public void Dispose()
    {
        foreach (var window in _activeWindows)
        {
            window.Dispose();
        }
    }
}
