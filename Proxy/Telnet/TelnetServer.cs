// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

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

            // First tick to force output to client before main session loop.
            await session.TickWindows();

            var read = 0;
            while (connection.IsConnected && (read = await connection.Stream.ReadAsync(buffer)) > 0)
            {
                Thread.Sleep(1);

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
                await session.TickWindows();
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
