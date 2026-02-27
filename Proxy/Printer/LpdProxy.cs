// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Printer;

/// <summary>
/// LPD (Line Printer Daemon) protocol server — RFC 1179, default port 515.
/// Accepts print jobs from LPR clients (DOS, Windows 3.x/95, Unix/Mac).
/// </summary>
internal class LpdProxy : Listener
{
    const byte LPD_CMD_PRINT_WAITING = 0x01;
    const byte LPD_CMD_RECEIVE_JOB = 0x02;
    const byte LPD_CMD_QUEUE_STATE_SHORT = 0x03;
    const byte LPD_CMD_QUEUE_STATE_LONG = 0x04;
    const byte LPD_CMD_REMOVE_JOBS = 0x05;

    const byte LPD_SUBCMD_ABORT = 0x01;
    const byte LPD_SUBCMD_CONTROL_FILE = 0x02;
    const byte LPD_SUBCMD_DATA_FILE = 0x03;

    const byte ACK = 0x00;
    const byte NAK = 0x01;

    public LpdProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var stream = connection.Stream;
        var buffer = new byte[1024];

        try
        {
            // Read the daemon command line
            int read = await stream.ReadAsync(buffer);

            if (read == 0)
            {
                return null;
            }

            byte command = buffer[0];

            switch (command)
            {
                case LPD_CMD_RECEIVE_JOB:
                {
                    var queueLine = Encoding.ASCII.GetString(buffer, 1, read - 1).TrimEnd('\n', '\r');

                    Log.WriteLine(Log.LEVEL_INFO, nameof(LpdProxy), $"Receive job for queue: {queueLine}", connection.TraceId.ToString());

                    // ACK the daemon command
                    await stream.WriteAsync(new byte[] { ACK });

                    await HandleReceiveJob(connection);
                }
                break;

                case LPD_CMD_QUEUE_STATE_SHORT:
                case LPD_CMD_QUEUE_STATE_LONG:
                {
                    await HandleSendQueueState(connection, command == LPD_CMD_QUEUE_STATE_LONG);
                }
                break;

                case LPD_CMD_REMOVE_JOBS:
                {
                    // Not implemented — just ACK
                    await stream.WriteAsync(new byte[] { ACK });
                }
                break;

                case LPD_CMD_PRINT_WAITING:
                {
                    // Not implemented — just ACK
                    await stream.WriteAsync(new byte[] { ACK });
                }
                break;

                default:
                {
                    Log.WriteLine(Log.LEVEL_ERROR, nameof(LpdProxy), $"Unknown LPD command: 0x{command:X2}", connection.TraceId.ToString());
                }
                break;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is SocketException)
        {
            // Connection closed by client — normal for LPD
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(LpdProxy), $"Error: {ex}", connection.TraceId.ToString());
        }

        // Close connection — LPD is one-shot per connection
        try
        {
            connection.RawSocket.Close();
        }
        catch
        {
            // Already closed
        }

        return null;
    }

    async Task HandleReceiveJob(ListenerSocket connection)
    {
        var stream = connection.Stream;
        var buffer = new byte[1024];

        string userName = null;
        string jobName = null;
        string hostName = null;
        string sourceFile = null;
        byte[] documentData = null;

        while (connection.RawSocket.Connected)
        {
            int read;

            try
            {
                read = await stream.ReadAsync(buffer);
            }
            catch
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            byte subCommand = buffer[0];

            switch (subCommand)
            {
                case LPD_SUBCMD_CONTROL_FILE:
                {
                    // Format: 0x02 <count> SP <name> LF
                    var subLine = Encoding.ASCII.GetString(buffer, 1, read - 1).TrimEnd('\n', '\r');
                    var spaceIdx = subLine.IndexOf(' ');

                    if (spaceIdx < 0)
                    {
                        await stream.WriteAsync(new byte[] { NAK });
                        continue;
                    }

                    if (!int.TryParse(subLine[..spaceIdx], out int count))
                    {
                        await stream.WriteAsync(new byte[] { NAK });
                        continue;
                    }

                    // ACK ready to receive
                    await stream.WriteAsync(new byte[] { ACK });

                    // Read control file data
                    var controlData = await ReadExactBytesAsync(stream, count);

                    // Read trailing zero byte
                    await ReadExactBytesAsync(stream, 1);

                    // Parse control file
                    ParseControlFile(controlData, ref userName, ref jobName, ref hostName, ref sourceFile);

                    // ACK control file received
                    await stream.WriteAsync(new byte[] { ACK });

                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(LpdProxy), $"Control file: user={userName}, job={jobName}, host={hostName}, file={sourceFile}", connection.TraceId.ToString());
                }
                break;

                case LPD_SUBCMD_DATA_FILE:
                {
                    // Format: 0x03 <count> SP <name> LF
                    var subLine = Encoding.ASCII.GetString(buffer, 1, read - 1).TrimEnd('\n', '\r');
                    var spaceIdx = subLine.IndexOf(' ');

                    if (spaceIdx < 0)
                    {
                        await stream.WriteAsync(new byte[] { NAK });
                        continue;
                    }

                    if (!int.TryParse(subLine[..spaceIdx], out int count))
                    {
                        await stream.WriteAsync(new byte[] { NAK });
                        continue;
                    }

                    // ACK ready to receive
                    await stream.WriteAsync(new byte[] { ACK });

                    // Read data file
                    documentData = await ReadExactBytesAsync(stream, count);

                    // Read trailing zero byte
                    await ReadExactBytesAsync(stream, 1);

                    // ACK data file received
                    await stream.WriteAsync(new byte[] { ACK });

                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(LpdProxy), $"Data file received: {count} bytes", connection.TraceId.ToString());
                }
                break;

                case LPD_SUBCMD_ABORT:
                {
                    Log.WriteLine(Log.LEVEL_INFO, nameof(LpdProxy), "Job aborted by client", connection.TraceId.ToString());
                    return;
                }

                default:
                {
                    // Unknown sub-command — might be end of job, try to finalize
                    Log.WriteLine(Log.LEVEL_DEBUG, nameof(LpdProxy), $"Unknown LPD sub-command: 0x{subCommand:X2}", connection.TraceId.ToString());
                }
                break;
            }
        }

        // Create print job if we received data
        if (documentData != null && documentData.Length > 0)
        {
            var name = userName ?? "LPD User";
            var jName = jobName ?? sourceFile ?? "LPD Job";

            var jobId = Mind.PrinterDb.CreateJob(name);
            var docNewAttr = JsonSerializer.Serialize(new { JobName = jName });
            Mind.PrinterDb.SetJobDocumentData(jobId, "{}", docNewAttr, documentData);

            Log.WriteLine(Log.LEVEL_INFO, nameof(LpdProxy), $"LPD job created; Id={jobId}, User={name}, Name={jName}, Size={documentData.Length}", connection.TraceId.ToString());
        }
    }

    async Task HandleSendQueueState(ListenerSocket connection, bool longFormat)
    {
        var stream = connection.Stream;
        var jobs = Mind.PrinterDb.GetAllJobs();

        var sb = new System.Text.StringBuilder();

        if (longFormat)
        {
            sb.AppendLine($"VintageHive LPD Printer ({Mind.PrinterDb.GetProcessingJobCount()} active)");
            sb.AppendLine();

            foreach (var job in jobs.Take(20))
            {
                sb.AppendLine($"  {job.Name,-10} {job.Id,6}  {job.State,-12}  {job.Created:yyyy-MM-dd HH:mm}");
            }
        }
        else
        {
            foreach (var job in jobs.Take(20))
            {
                sb.AppendLine($"{job.Name}\t{job.Id}\t{job.State}");
            }
        }

        var response = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(response);
    }

    static void ParseControlFile(byte[] data, ref string userName, ref string jobName, ref string hostName, ref string sourceFile)
    {
        var content = Encoding.ASCII.GetString(data);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Length < 2)
            {
                continue;
            }

            char code = line[0];
            string value = line[1..].TrimEnd('\r');

            switch (code)
            {
                case 'H': // Hostname
                {
                    hostName = value;
                }
                break;

                case 'P': // User name
                {
                    userName = value;
                }
                break;

                case 'J': // Job name
                {
                    jobName = value;
                }
                break;

                case 'N': // Source file name
                {
                    sourceFile = value;
                }
                break;

                case 'C': // Class (usually hostname)
                case 'L': // Banner page user name
                case 'M': // Mail when done
                case 'T': // Title
                case 'f': // Print formatted file
                case 'l': // Print file leaving control chars
                case 'o': // Print PostScript file
                case 'p': // Print with pr(1)
                {
                    // Consumed but not used
                }
                break;
            }
        }
    }

    static async Task<byte[]> ReadExactBytesAsync(NetworkStream stream, int count)
    {
        var result = new byte[count];
        int totalRead = 0;

        while (totalRead < count)
        {
            int read = await stream.ReadAsync(result.AsMemory(totalRead, count - totalRead));

            if (read == 0)
            {
                break; // Connection closed
            }

            totalRead += read;
        }

        if (totalRead < count)
        {
            // Truncated — return what we got
            return result[..totalRead];
        }

        return result;
    }
}
