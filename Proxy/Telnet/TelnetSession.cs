using System.IO;
using VintageHive.Network;
using System;

namespace VintageHive.Proxy.Telnet
{
    public class TelnetSession
    {
        public ulong ID { get; } = _sessionID++;

        public ListenerSocket Client { get; }

        public int TermWidth { get; set; } = 80;

        public int TermHeight { get; set; } = 24;

        public string InputBuffer { get; set; } = string.Empty;

        public string OutputBuffer { get; set; } = string.Empty;

        private readonly char[] _spinnerAnimationFrames = new[] { '|', '/', '-', '\\' };
        private int _currentAnimationFrame;

        private static ulong _sessionID = 0;

        public TelnetSession()
        {

        }

        public TelnetSession(ListenerSocket client)
        {
            Client = client;
        }

        public async Task ClearScreen()
        {
            byte[] clearScreenCommand = Encoding.ASCII.GetBytes("\x1b[2J\x1b[H");
            await Client.Stream.WriteAsync(clearScreenCommand);
        }

        public async Task GetClientResolution()
        {
            // Enable NAWS option
            byte[] nawsOption = new byte[] { 0xff, 0xfb, 0x1f, 0xff, 0xfd, 0x1f };
            await Client.Stream.WriteAsync(nawsOption);
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

        public async Task SendText(string text)
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

            // Send each line to the client, adding a newline character at the end
            foreach (var line in lines)
            {
                var bytes = Encoding.ASCII.GetBytes(line + '\r' + '\n');
                await Client.Stream.WriteAsync(bytes);
            }

            await Client.Stream.FlushAsync();
        }

        public async Task ProcessCommand(string command)
        {
            if (command.StartsWith("\xff\xfa\x1f", StringComparison.Ordinal))
            {
                // Process NAWS (Negotiate About Window Size) option
                int width = command[3] * 256 + command[4];
                int height = command[5] * 256 + command[6];
                TermWidth = width;
                TermHeight = height;

                Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"NAWS option received for client {Client.RemoteIP}, extracted terminal resolution got {TermWidth}x{TermHeight}.", "");
            }

            switch (command.ToLower().Trim())
            {
                case "help":
                    OutputBuffer = PrintHelp();
                    break;

                case "version":
                    OutputBuffer = $"VintageHive TelnetServer {Mind.ApplicationVersion}\r\n";
                    break;

                case "exit":
                    OutputBuffer = "Goodbye!\r\n";
                    await Client.Stream.FlushAsync();
                    Client.Stream.Close();
                    Client.RawSocket.Shutdown(SocketShutdown.Both);
                    break;

                default:
                    OutputBuffer = $"Invalid command: {command}\r\n";
                    break;
            }
        }

        private string PrintHelp()
        {
            // The standard passage, used since the 1500s
            return "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.\r\n";
        }
    }
}
