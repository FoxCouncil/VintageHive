using VintageHive.Network;

namespace VintageHive.Proxy.Oscar;

public class OscarSession
{
    static ulong SessionID = 0;

    readonly byte[] buffer = new byte[4096];

    public ulong ID { get; } = SessionID++;

    public ListenerSocket Client { get; }

    public bool SentHello { get; set; } = false;

    public bool IsReady { get; set; }

    public string Cookie { get; set; }

    public ushort SequenceNumber { get; set; } = 0;

    public string ScreenName { get; set; }

    public OscarSessionOnlineStatus Status { get; set; }

    public List<string> Buddies { get; set;  } = new();

    public string Profile { get; set; }

    public string ProfileMimeType { get; set; }

    public string AwayMessage { get; set; }

    public string AwayMessageMimeType { get; set; }

    public List<string> Capabilities { get; set; }

    public string UserAgent { get; set; }

    public OscarSession()
    {

    }

    public OscarSession(ListenerSocket client)
    {
        Client = client;
    }

    public async Task SendSnac(Snac snac)
    {
        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"<- {snac}", Client.TraceId.ToString());

        var snacDataFlap = new Flap(FlapFrameType.Data)
        {
            Data = snac.Encode(),
            Sequence = SequenceNumber++
        };

        var encodedFlap = snacDataFlap.Encode();

        await Client.Stream.WriteAsync(encodedFlap);
    }

    public async Task SendFlap(Flap flap)
    {
        flap.Sequence = SequenceNumber++;

        var encodedFlap = flap.Encode();

        await Client.Stream.WriteAsync(encodedFlap);
    }

    public async Task<Flap[]> ReceiveFlaps()
    {
        var read = await Client.Stream.ReadAsync(buffer);

        if (read == 0)
        {
            return null;
        }

        Console.Write($"Recieved {read} bytes...");

        var flaps = OscarUtils.DecodeFlaps(buffer[..read]);

        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"Total {flaps.Length} flaps", Client.TraceId.ToString());

        return flaps;
    }
}
