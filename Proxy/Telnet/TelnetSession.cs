using VintageHive.Network;

namespace VintageHive.Proxy.Telnet
{
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

        private int _currentAnimationFrame;
        private readonly TelnetWindowManager _windowManager = new();

        public TelnetSession(ListenerSocket client)
        {
            Client = client;
        }

        public async Task TickWindows()
        {
            // Process logic for all loaded windows.
            _windowManager.TickWindows();

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

        /// <summary>
        /// Respects the terminal width and height variables to print long string over multiple lines.
        /// </summary>
        /// <param name="text">Text to be transformed into telenet compatible lines.</param>
        /// <returns>Formatted lines of text with proper returns at the end.</returns>
        public string WordWrapText(string text)
        {
            // Split the text into lines using whole words
            var lines = new List<string>();
            var currentLine = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (currentLine.Length + word.Length + 1 > TermWidth)
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                }

                if (lines.Count == TermHeight)
                {
                    break;
                }

                currentLine.Append(word).Append(' ');
            }

            if (currentLine.Length > 0 && lines.Count < TermHeight)
            {
                lines.Add(currentLine.ToString());
            }

            // Add a newline character at the end of each line that has been broken up for telnet.
            var result = new StringBuilder();
            foreach (var line in lines)
            {
                result.Append(line + '\r' + '\n');
            }

            return result.ToString();
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
                // Hidden window that will say what was typed was invalid.
                ForceAddWindow("invalid_cmd");
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
        public void ForceAddWindow(string windowName)
        {
            if (_windowManager.TryAddWindow(windowName))
            {
                _windowManager.GetTopWindow().OnAdd(this);
                return;
            }

            Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetSession), $"Client {Client.RemoteIP} session attempted to add a window {windowName} which doesn't exist!", Client.TraceId.ToString());
        }
    }
}
