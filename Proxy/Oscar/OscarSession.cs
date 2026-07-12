// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Data;
using VintageHive.Network;

namespace VintageHive.Proxy.Oscar;

public class OscarSession
{
    static ulong SessionID = 0;

    // Serializes sequence-number assignment + the socket write so a broadcast from another session can't
    // interleave with this session's own reply (which corrupts FLAP framing and duplicates sequence numbers).
    readonly SemaphoreSlim writeLock = new(1, 1);

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

    public ushort WarningLevel { get; set; }

    public uint IdleTime { get; set; }

    public DateTimeOffset IdleSince { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset SignOnTime { get; set; } = DateTimeOffset.UtcNow;

    public List<string> PermitList { get; set; } = new();

    public List<string> DenyList { get; set; } = new();

    public byte PrivacyMode { get; set; } = 1; // 1=allow all, 2=deny all, 3=permit only, 4=deny list, 5=allow buddy list only

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

        SignOnTime = DateTimeOffset.UtcNow;

        Save();
    }

    public void Save()
    {
        Mind.Db.OscarInsertOrUpdateSession(this);
    }

    public void SetIdle(uint seconds)
    {
        IdleTime = seconds;

        if (seconds > 0)
        {
            IdleSince = DateTimeOffset.UtcNow;
        }
        else
        {
            IdleSince = DateTimeOffset.MinValue;
        }
    }

    public uint GetCurrentIdleSeconds()
    {
        if (IdleSince == DateTimeOffset.MinValue)
        {
            return 0;
        }

        return (uint)(DateTimeOffset.UtcNow - IdleSince).TotalSeconds;
    }

    public void ApplyWarning(bool isAnonymous)
    {
        // AIM warning formula: anonymous warnings add less
        var increment = isAnonymous ? (ushort)33 : (ushort)100;

        WarningLevel = (ushort)Math.Min(WarningLevel + increment, 9990);
    }

    public void DecayWarning()
    {
        // Warning level decays over time - roughly 1 point per minute
        if (WarningLevel > 0)
        {
            WarningLevel = (ushort)Math.Max(0, WarningLevel - 1);
        }
    }

    public async Task SendSnac(Snac snac)
    {
        Log.WriteLine(Log.LEVEL_INFO, nameof(OscarSession), $"<- {snac}", Client.TraceId.ToString());

        await writeLock.WaitAsync();

        try
        {
            var snacDataFlap = new Flap(FlapFrameType.Data)
            {
                Data = snac.Encode(),
                Sequence = SequenceNumber++
            };

            await Client.Stream.WriteAsync(snacDataFlap.Encode());
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task SendFlap(Flap flap)
    {
        await writeLock.WaitAsync();

        try
        {
            flap.Sequence = SequenceNumber++;

            await Client.Stream.WriteAsync(flap.Encode());
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<Flap[]> ReceiveFlaps()
    {
        // A FLAP header is 6 bytes: 0x2A, type(1), sequence(2), payloadLength(2). Reading one whole frame at a
        // time (header, then the exact declared payload) instead of assuming a single ReadAsync returns a full
        // frame fixes deterministic disconnects on frames split across reads or larger than the old 4096 buffer.
        var header = new byte[6];

        if (!await ReadExactAsync(header))
        {
            return null; // disconnected
        }

        if (header[0] != (byte)'*')
        {
            // Framing desync - treat as a disconnect rather than misparsing the rest of the stream
            return null;
        }

        var payloadLength = (header[4] << 8) | header[5];

        var frame = new byte[6 + payloadLength];

        Array.Copy(header, frame, 6);

        if (payloadLength > 0 && !await ReadExactAsync(frame.AsMemory(6, payloadLength)))
        {
            return null;
        }

        return OscarUtils.DecodeFlaps(frame);
    }

    async Task<bool> ReadExactAsync(Memory<byte> buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await Client.Stream.ReadAsync(buffer[total..]);

            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    /// <summary>
    /// Privacy-list gate. Returns whether the user identified by <paramref name="otherScreenName"/> is allowed to
    /// see this user's presence and to reach them with messages, typing notifications and warnings, per THIS user's
    /// <see cref="PrivacyMode"/>/<see cref="PermitList"/>/<see cref="DenyList"/>. The same predicate governs both
    /// directions: for delivery the subject is the recipient (does the recipient permit the sender), and for
    /// presence the subject is the user whose status changed (does that user permit the watcher). A denied party
    /// therefore both fails to receive and sees the subject as offline - no more one-shot "offline" contradicted by
    /// the next presence broadcast.
    /// </summary>
    public bool IsVisibleTo(string otherScreenName)
    {
        if (string.IsNullOrEmpty(otherScreenName))
        {
            return false;
        }

        bool OnList(List<string> list) => list != null && list.Any(x => x.Equals(otherScreenName, StringComparison.OrdinalIgnoreCase));

        return PrivacyMode switch
        {
            2 => false,                 // deny all
            3 => OnList(PermitList),     // allow only users on the permit list
            4 => !OnList(DenyList),      // block only users on the deny list
            5 => OnList(Buddies),        // allow only users on the buddy list
            _ => true                    // 1 (allow all) and any unknown mode stay open
        };
    }

    public async Task BroadcastStatusToWatchers()
    {
        foreach (var session in OscarServer.Sessions.Values)
        {
            if (session == this || session.Client == null || !session.Client.IsConnected)
            {
                continue;
            }

            if (!session.Buddies.Any(b => b.Equals(ScreenName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Respect this user's privacy list - a denied/non-permitted watcher must not see us come online.
            if (!IsVisibleTo(session.ScreenName))
            {
                continue;
            }

            Snac statusSnac;

            if (Status == OscarSessionOnlineStatus.Invisible)
            {
                // An invisible user must appear OFFLINE to watchers (mirrors the messengers hiding from
                // peers), not online carrying an invisible flag: send a buddy-departed (SRV_USER_OFFLINE).
                statusSnac = new Snac(0x03, 0x0C); // Family 0x03, SRV_USER_OFFLINE

                statusSnac.WriteUInt8((byte)ScreenName.Length);
                statusSnac.WriteString(ScreenName);
                statusSnac.WriteUInt16(WarningLevel);
                statusSnac.WriteUInt16(1);
                statusSnac.Write(new Tlv(0x01, OscarUtils.GetBytes(0)).Encode());
            }
            else
            {
                statusSnac = new Snac(0x03, 0x0B); // Family 0x03, SRV_USER_ONLINE

                statusSnac.WriteUInt8((byte)ScreenName.Length);
                statusSnac.WriteString(ScreenName);
                statusSnac.WriteUInt16(WarningLevel);

                var tlvs = new List<Tlv>
                {
                    new Tlv(0x01, OscarUtils.GetBytes(0)),
                    new Tlv(0x06, OscarUtils.GetBytes((uint)Status)),
                    new Tlv(0x0F, OscarUtils.GetBytes((uint)SignOnTime.ToUnixTimeSeconds())),
                    new Tlv(0x03, OscarUtils.GetBytes((uint)OscarServer.ServerTime.ToUnixTimeSeconds())),
                    new Tlv(0x05, OscarUtils.GetBytes((uint)SignOnTime.ToUnixTimeSeconds()))
                };

                if (GetCurrentIdleSeconds() > 0)
                {
                    tlvs.Add(new Tlv(0x04, OscarUtils.GetBytes((ushort)GetCurrentIdleSeconds())));
                }

                statusSnac.WriteUInt16((ushort)tlvs.Count);

                foreach (Tlv tlv in tlvs)
                {
                    statusSnac.Write(tlv.Encode());
                }
            }

            try
            {
                await session.SendSnac(statusSnac);
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(OscarSession), ex, session.Client.TraceId.ToString());
            }
        }
    }
}
