// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Printer;

/// <summary>
/// Raw TCP / JetDirect / AppSocket print server — default port 9100.
/// The simplest possible protocol: client connects, sends raw print data, closes.
/// No handshake, no metadata, just bytes.
/// </summary>
internal class RawPrintProxy : Listener
{
    public RawPrintProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var stream = connection.Stream;

        Log.WriteLine(Log.LEVEL_INFO, nameof(RawPrintProxy), $"Raw TCP connection from {connection.RemoteAddress}", connection.TraceId.ToString());

        using var memoryStream = new MemoryStream();
        var buffer = new byte[8192];

        try
        {
            while (connection.RawSocket.Connected)
            {
                int read;

                try
                {
                    read = await stream.ReadAsync(buffer);
                }
                catch (IOException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                if (read == 0)
                {
                    break; // Client closed connection — data transfer complete
                }

                memoryStream.Write(buffer, 0, read);
            }
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(RawPrintProxy), $"Error reading data: {ex.Message}", connection.TraceId.ToString());
        }

        var data = memoryStream.ToArray();

        if (data.Length > 0)
        {
            var jobName = $"Raw Print Job {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            var jobId = Mind.PrinterDb.CreateJob("Raw TCP");
            var docNewAttr = JsonSerializer.Serialize(new { JobName = jobName });
            Mind.PrinterDb.SetJobDocumentData(jobId, "{}", docNewAttr, data);

            Log.WriteLine(Log.LEVEL_INFO, nameof(RawPrintProxy), $"Raw TCP job created; Id={jobId}, Size={data.Length}", connection.TraceId.ToString());
        }
        else
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(RawPrintProxy), "Raw TCP connection closed with no data", connection.TraceId.ToString());
        }

        // Close our side
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
}
