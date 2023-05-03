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

        public string OutputBuffer { get; set; } = string.Empty;

        public TelnetWindowManager WindowManager { get => _windowManager; }

        private readonly char[] _spinnerAnimationFrames = new[] { '|', '/', '-', '\\' };
        private int _currentAnimationFrame;
        private readonly TelnetWindowManager _windowManager = new();

        public TelnetSession(ListenerSocket client)
        {
            Client = client;
        }

        public void TickWindows()
        {
            // Process logic for all loaded windows.
            _windowManager.TickWindows();

            // Update output that is sent to clients after each tick.
            var topWindow = _windowManager.GetTopWindow();
            if (topWindow != null && !string.IsNullOrEmpty(topWindow.Text))
            {
                //var wrappedText = WordWrapText(topWindow.Text);
                OutputBuffer = topWindow.Text;
            }
            else
            {
                OutputBuffer = "No windows currently loaded!\r\n";
            }
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

        public string UpdateSpinner()
        {
            // Keep looping around all the animation frames
            _currentAnimationFrame++;
            if (_currentAnimationFrame == _spinnerAnimationFrames.Length)
            {
                _currentAnimationFrame = 0;
            }

            return _spinnerAnimationFrames[_currentAnimationFrame].ToString();
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

            // Hard-coded commands to destroy the current session and show help.
            if (cleanCmd == "exit" || cleanCmd == "quit")
            {
                await Client.Stream.FlushAsync();
                Client.Stream.Close();
                Client.RawSocket.Shutdown(SocketShutdown.Both);
                return;
            }

            var result = _windowManager.TryAddWindow(command);
            if (result)
            {
                _windowManager.GetTopWindow().OnAdd(this);
            }
            else if (_windowManager.TryAddWindow("invalid_cmd"))
            {
                // Attach hidden window that shows user error.
                _windowManager.GetTopWindow().OnAdd(this);
                Log.WriteLine(Log.LEVEL_ERROR, nameof(TelnetSession), $"Client {Client.RemoteIP} failed to load window {command}!", Client.TraceId.ToString());
            }
        }

        public void Dispose()
        {
            _windowManager.Dispose();
        }
    }
}
