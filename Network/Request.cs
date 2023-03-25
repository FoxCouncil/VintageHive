using System.Text;

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

    public ListenerSocket? ListenerSocket { get; internal set; }

    public Encoding? Encoding { get; internal set; }

    public IReadOnlyDictionary<string, string>? Headers { get; internal set; }

    public async Task SendRawResponse(string response)
    {
        EnsureValiditionOrThrow();

        await ListenerSocket.Stream.WriteAsync(Encoding.GetBytes(response));
    }    

    public async Task<string> ReadRawResponseAsync()
    {
        EnsureValiditionOrThrow();

        var readBuffer = new byte[512];

        var read = await ListenerSocket.Stream.ReadAsync(readBuffer);

        return Encoding.GetString(readBuffer, 0, read);
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
