// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Data;
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

    public List<string> Buddies { get; set; } = new();

    public string Profile { get; set; } = string.Empty;

    public string ProfileMimeType { get; set; } = string.Empty;

    public string AwayMessage { get; set; } = string.Empty;

    public string AwayMessageMimeType { get; set; } = string.Empty;

    public List<string> Capabilities { get; set; } = new();

    public string UserAgent { get; set; }

    public OscarSession() { }

    public OscarSession(IDataReader reader)
    {
        Cookie = reader.GetString(0);
        
        ScreenName = reader.GetString(1);
        
        Status = (OscarSessionOnlineStatus)reader.GetInt32(2);
        
        AwayMessageMimeType = reader.GetString(3);
        AwayMessage = reader.GetString(4);

        ProfileMimeType = reader.GetString(5);
        Profile = reader.GetString(6);

        Buddies = JsonSerializer.Deserialize<List<string>>(reader.GetString(7));

        Capabilities = JsonSerializer.Deserialize<List<string>>(reader.GetString(8));

        UserAgent = reader.GetString(9);
    }

    public OscarSession(ListenerSocket client)
    {
        Client = client;
    }

    public void LoadFromOtherSession(OscarSession otherSession)
    {
        Cookie = otherSession.Cookie;

        ScreenName = otherSession.ScreenName;

        Status = otherSession.Status;

        AwayMessageMimeType = otherSession.AwayMessageMimeType;
        AwayMessage = otherSession.AwayMessage;

        ProfileMimeType = otherSession.ProfileMimeType;
        Profile = otherSession.Profile;

        Buddies = otherSession.Buddies;

        Capabilities = otherSession.Capabilities;

        if (string.IsNullOrEmpty(UserAgent))
        {
            UserAgent = otherSession.UserAgent;
        }
    }

    public void Load(string screenName)
    {
        ScreenName = screenName;

        var otherSession = Mind.Db.OscarGetSessionByScreenameAndIp(screenName, Client.RemoteIP);

        if (otherSession != null)
        {
            LoadFromOtherSession(otherSession);
        }
        else
        {
            Cookie = Guid.NewGuid().ToString().ToUpper();
        }

        Save();
    }

    public void Save()
    {
        Mind.Db.OscarInsertOrUpdateSession(this);
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

        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"Recieved {read} bytes...", Client.TraceId.ToString());

        var flaps = OscarUtils.DecodeFlaps(buffer[..read]);

        Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"Total {flaps.Length} flaps", Client.TraceId.ToString());

        return flaps;
    }
}
