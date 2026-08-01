// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Text;
using VintageHive.Network;

namespace VintageHive.Proxy.Telnet;

public class TelnetServer : Listener
{
    // Keyed by the per-connection trace id (TelnetSession.ID is unset), concurrent because connections add/remove from their own threads
    private static readonly ConcurrentDictionary<Guid, TelnetSession> _sessions = new();

    /// <summary>How long an idle connection waits before repainting timer-driven windows.</summary>
    private static readonly TimeSpan WindowTickInterval = TimeSpan.FromSeconds(1);

    public TelnetServer(IPAddress address, int port) : base(address, port, SocketType.Stream, ProtocolType.Tcp, false) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        Log.WriteLine(Log.LEVEL_INFO, nameof(TelnetServer), $"Client {connection.RemoteIP} connected!", connection.TraceId.ToString());

        var session = new TelnetSession(connection);
        _sessions[connection.TraceId] = session;

        try
        {
            var buffer = new byte[1024];

            // NetworkStream.ReadTimeout is only honoured by the SYNCHRONOUS Read, never by ReadAsync, so
            // setting it here bought nothing: the loop blocked until a keystroke arrived and TickWindows only
            // ran when the member typed. Timer-driven windows (weather, news) therefore never repainted on
            // their own. A cancellation token gives the async read a real deadline, so an idle connection
            // wakes up on schedule and ticks.
            var bufferedCommands = string.Empty;

            // First tick to force output to client before main session loop.
            await session.TickWindows();

            while (connection.IsConnected)
            {
                int read;

                try
                {
                    using var idleTimeout = new CancellationTokenSource(WindowTickInterval);

                    read = await connection.Stream.ReadAsync(buffer, idleTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    // Nothing typed within the interval: repaint any timer-driven windows and wait again.
                    await session.TickWindows();

                    continue;
                }

                if (read <= 0)
                {
                    break;
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
                    // Apply each backspace to the char before it (the old TrimEnd deleted ALL repeated trailing chars,
                    // and collapsing every \b first meant a run of backspaces only removed one character total).
                    var sb = new StringBuilder();

                    foreach (var ch in bufferedCommands)
                    {
                        if (ch == '\b')
                        {
                            if (sb.Length > 0)
                            {
                                sb.Length--;
                            }
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                    }

                    bufferedCommands = sb.ToString();
                    session.InputBuffer = bufferedCommands;
                }

                // Process logic for this sessions window manager.
                await session.TickWindows();
            }
        }
        finally
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(TelnetServer), $"Client {connection.RemoteIP} disconnected!", connection.TraceId.ToString());

            _sessions.TryRemove(connection.TraceId, out _);

            // Tears down the window manager, stopping per-window timers that would otherwise run forever
            session.Destroy();
        }

        return null;
    }

    public override Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        throw new ApplicationException("Telnet server does not use this override!");
    }
}
