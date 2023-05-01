using System;
using System.Collections.Generic;
using System.Linq;
using VintageHive.Network;

namespace VintageHive.Proxy.Telnet
{
    public class TelnetSession
    {
        static ulong SessionID = 0;

        public ulong ID { get; } = SessionID++;

        public ListenerSocket Client { get; }

        /// <summary>
        /// Default terminal width
        /// </summary>
        public int TermWidth { get; set; } = 80;

        /// <summary>
        /// Default terminal height
        /// </summary>
        public int TermHeight { get; set; } = 24;
        
        public TelnetSession()
        {

        }

        public TelnetSession(ListenerSocket client)
        {
            Client = client;
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
            switch (command.ToLower().Trim())
            {
                case "help":
                    await PrintHelp();
                    break;

                case "version":
                    await SendText("Telnet Server v1.0\r\n");
                    break;

                case "exit":
                    await SendText("Goodbye!\r\n");
                    Client.Close();
                    break;

                default:
                    await SendText($"Invalid command: {command}.\r\n");
                    break;
            }
        }

        private async Task PrintHelp()
        {
            // The standard passage, used since the 1500s
            await SendText("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.\r\n");
        }
    }
}
