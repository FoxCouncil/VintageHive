// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Network;

public class Request
{
    public bool IsValid { get; internal set; }

    public Uri? Uri { get; set; }

    public string Type { get; internal set; } = "";

    public string Version { get; internal set; } = "";

    public string ProxyUsername { get; internal set; }

    public string ProxyPassword { get; internal set; }

    public string Username { get; internal set; }

    public string Password { get; internal set; }

    public ListenerSocket? ListenerSocket { get; set; }

    public Encoding? Encoding { get; set; }

    public IDictionary<string, string>? Headers { get; set; } = new Dictionary<string, string>();

    // The carry buffer lives on the socket rather than the request because FTP builds more than one
    // FtpRequest over the life of one control connection.
    private const string LineBufferKey = "_request_linebuf";

    private const int MaxLineBytes = 8 * 1024;

    public async Task SendRawResponse(string response)
    {
        EnsureValiditionOrThrow();

        var bytes = Encoding.GetBytes(response);

        // Both halves used to reach for ListenerSocket.Stream unconditionally, which would have written
        // plaintext into the middle of a TLS session the day a secure listener reused this type. FTP is the
        // only consumer today, so this was latent rather than live, but the branch costs nothing.
        if (ListenerSocket.IsSecure && ListenerSocket.SecureStream != null)
        {
            await ListenerSocket.SecureStream.WriteAsync(bytes);
        }
        else
        {
            await ListenerSocket.Stream.WriteAsync(bytes);
        }
    }

    private async Task<int> ReadRawAsync(Memory<byte> buffer)
    {
        if (ListenerSocket.IsSecure && ListenerSocket.SecureStream != null)
        {
            return await ListenerSocket.SecureStream.ReadAsync(buffer);
        }

        return await ListenerSocket.Stream.ReadAsync(buffer);
    }

    public async Task<string> ReadRawResponseAsync()
    {
        EnsureValiditionOrThrow();

        var readBuffer = new byte[512];

        var read = await ReadRawAsync(readBuffer);

        return Encoding.GetString(readBuffer, 0, read);
    }

    /// <summary>
    /// Reads exactly one CRLF-terminated command, buffering whatever arrived behind it.
    /// </summary>
    /// <remarks>
    /// FTP's control session is interactive: FtpRequest.Parse consumes the opening command and then drives the
    /// rest of the conversation itself through FetchCommand, so the per-read model the line-based mail and news
    /// proxies use never applied here. Both halves treated one TCP read as exactly one command, which broke two
    /// ways. A command split across reads was silently truncated. Worse, a client that pipelined
    /// "USER x\r\nPASS y\r\n" into a single packet had its username parsed as "x\r\nPASS" and then blocked
    /// forever inside FetchCommand waiting for a PASS that had already arrived and been discarded. Buffering the
    /// remainder on the socket fixes both, and is the same carry-buffer idea the sibling protocols already use.
    /// </remarks>
    public async Task<string> ReadCommandLineAsync()
    {
        EnsureValiditionOrThrow();

        var buffered = ListenerSocket.DataBag.TryGetValue(LineBufferKey, out var existing) ? existing as string ?? string.Empty : string.Empty;

        while (true)
        {
            var newline = buffered.IndexOf('\n');

            if (newline >= 0)
            {
                var line = buffered[..newline].TrimEnd('\r');

                ListenerSocket.DataBag[LineBufferKey] = buffered[(newline + 1)..];

                return line;
            }

            // A peer that never sends a terminator must not be able to grow this without bound.
            if (buffered.Length > MaxLineBytes)
            {
                ListenerSocket.DataBag[LineBufferKey] = string.Empty;

                return string.Empty;
            }

            var readBuffer = new byte[512];

            var read = await ReadRawAsync(readBuffer);

            if (read <= 0)
            {
                ListenerSocket.DataBag[LineBufferKey] = string.Empty;

                // Whatever arrived without a terminator before the peer hung up.
                return buffered;
            }

            buffered += Encoding.GetString(readBuffer, 0, read);
        }
    }

    /// <summary>Seeds the command buffer with bytes the listener already consumed, so the opening command and
    /// anything pipelined behind it are drawn from the same place as every later read.</summary>
    internal void SeedCommandBuffer(string data)
    {
        EnsureValiditionOrThrow();

        ListenerSocket.DataBag[LineBufferKey] = data ?? string.Empty;
    }

    private void EnsureValiditionOrThrow()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("Invalid Request");
        }

        if (ListenerSocket == null || Encoding == null)
        {
            throw new InvalidOperationException("Request doesn't have a socket and/or encoding objects assigned!");
        }
    }
}
