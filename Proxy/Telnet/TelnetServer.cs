using VintageHive.Network;
using System;

namespace VintageHive.Proxy.Telnet
{
    internal class TelnetServer : Listener
    {
        private static readonly List<TelnetSession> _sessions = new();

        public TelnetServer(IPAddress address, int port) : base(address, port, SocketType.Stream, ProtocolType.Tcp, false) { }

        internal override async Task<byte[]> ProcessConnection(ListenerSocket connection)
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(TelnetServer), $"Client {connection.RemoteIP} connected!", connection.TraceId.ToString());
            var session = new TelnetSession(connection);
            _sessions.Add(session);

            var buffer = new byte[1024];

            connection.Stream.ReadTimeout = 1000;

            var bufferedCommands = string.Empty;

            while (connection.IsConnected)
            {
                Thread.Sleep(1);

                var read = 0;

                try
                {
                    read = connection.Stream.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    // NOOP
                }

                bufferedCommands += Encoding.ASCII.GetString(buffer, 0, read);
                session.InputBuffer = bufferedCommands;

                if (bufferedCommands.Contains("\r\n"))
                {
                    var command = bufferedCommands[..bufferedCommands.IndexOf("\r\n")];

                    bufferedCommands = bufferedCommands.Remove(0, command.Length + 2);

                    await session.ProcessCommand(command);

                    // Clear command buffer AFTER processing.
                    session.InputBuffer = string.Empty;

                    // Last command could have been to exit, check if we should stop here.
                    if (!connection.IsConnected)
                    {
                        break;
                    }
                }
                else if (bufferedCommands.Contains('\b'))
                {
                    bufferedCommands = bufferedCommands.Replace("\b", string.Empty);

                    // Remove the last character but only if there is something to remove
                    if (!string.IsNullOrEmpty(bufferedCommands))
                    {
                        bufferedCommands = bufferedCommands.TrimEnd(bufferedCommands[^1]);
                    }

                    session.InputBuffer = bufferedCommands;
                }

                // Process logic for this sessions window manager.
                session.TickWindows();

                // Build up current TUI
                var screenOutput = new StringBuilder();
                //screenOutput.Append($"VintageHive Telnet {Mind.ApplicationVersion} {session.UpdateSpinner()}\r\n");
                screenOutput.Append($"VintageHive Telnet {Mind.ApplicationVersion}\r\n");
                screenOutput.Append(new string('-', session.TermWidth) + "\r\n");
                screenOutput.Append(session.TopWindowOutputBuffer);
                screenOutput.Append(new string('-', session.TermWidth) + "\r\n");
                screenOutput.Append($"Enter command (help, exit): {session.InputBuffer}");

                var finalOutput = screenOutput.ToString();
                session.CurrentOutput = finalOutput;

                if (session.CurrentOutput == session.LastOutput)
                {
                    // Skip because nothing has changed!
                    continue;
                }

                // Clear screen and move cursor to upper left corner
                await session.ClearScreen();

                session.LastOutput = finalOutput;

                // Write the updated output buffer to the client.
                var bytes = Encoding.ASCII.GetBytes(finalOutput);
                await connection.Stream.WriteAsync(bytes);
                await connection.Stream.FlushAsync();
            }

            Log.WriteLine(Log.LEVEL_INFO, nameof(TelnetServer), $"Client {connection.RemoteIP} disconnected!", connection.TraceId.ToString());
            _sessions.Remove(session);
            return null;
        }

        internal override Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
        {
            throw new ApplicationException("Telnet server does not use this override!");
        }
    }
}
