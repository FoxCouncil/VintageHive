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

    // Set only where a credential has actually been verified: a roasted or MD5 password, a stored sign-on
    // cookie, or a one-shot chat cookie. ScreenName cannot stand in for this - the client supplies it in a
    // TLV and both the auth-key request and the MD5 login assign it before anything is checked, so a session
    // can carry a name it never proved it owns.
    public bool IsAuthenticated { get; set; }

    public string Cookie { get; set; }

    public ushort SequenceNumber { get; set; } = 0;

    public string ScreenName { get; set; }

    // The wire identity and the account it authenticates as split when a numeric alias signs on:
    // ScreenName stays the presented number - contacts, ICBMs, presence and the session store all key on
    // it - while the credential, account email and any other user-row lookup follow the owning username.
    // Defaults to ScreenName, so a session with no alias in play behaves exactly as before.
    string accountUsername;

    public string AccountUsername
    {
        get => accountUsername ?? ScreenName;

        set => accountUsername = value;
    }

    public OscarSessionOnlineStatus Status { get; set; }

    // Buddy, permit and deny are mutated by the OWNER's handler thread while OTHER sessions' threads read
    // them during presence fan-out (BroadcastStatusToWatchers and IsVisibleTo both walk other sessions'
    // copies). As plain List<string> that meant a member editing their list while someone else signed on
    // threw "Collection was modified" mid-broadcast, aborting the fan-out so every watcher after the throw
    // was never told the user came online. Reads hand back an immutable snapshot and every mutation goes
    // through a method holding the same lock, so an edit can no longer land inside someone else's walk.
    private readonly object _listLock = new();

    private List<string> _buddies = new();

    private List<string> _permitList = new();

    private List<string> _denyList = new();

    public IReadOnlyList<string> Buddies
    {
        get
        {
            lock (_listLock)
            {
                return _buddies.ToArray();
            }
        }
    }

    public IReadOnlyList<string> PermitList
    {
        get
        {
            lock (_listLock)
            {
                return _permitList.ToArray();
            }
        }
    }

    public IReadOnlyList<string> DenyList
    {
        get
        {
            lock (_listLock)
            {
                return _denyList.ToArray();
            }
        }
    }

    public void ReplaceBuddies(IEnumerable<string> names)
    {
        lock (_listLock)
        {
            _buddies = names?.ToList() ?? new List<string>();
        }
    }

    public void ReplacePermitList(IEnumerable<string> names)
    {
        lock (_listLock)
        {
            _permitList = names?.ToList() ?? new List<string>();
        }
    }

    public void ReplaceDenyList(IEnumerable<string> names)
    {
        lock (_listLock)
        {
            _denyList = names?.ToList() ?? new List<string>();
        }
    }

    /// <summary>Adds the name unless an equal one (ignoring case) is already present. Returns whether it was added.</summary>
    public bool AddBuddy(string name) => AddUnique(_ => _buddies, name);

    public bool AddPermit(string name) => AddUnique(_ => _permitList, name);

    public bool AddDeny(string name) => AddUnique(_ => _denyList, name);

    public void RemoveBuddy(string name) => RemoveFrom(_ => _buddies, name);

    public void RemovePermit(string name) => RemoveFrom(_ => _permitList, name);

    public void RemoveDeny(string name) => RemoveFrom(_ => _denyList, name);

    // The check and the add have to be one atomic step, or two threads both see "not present" and both add.
    private bool AddUnique(Func<object, List<string>> pick, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        lock (_listLock)
        {
            var list = pick(null);

            if (list.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            list.Add(name);

            return true;
        }
    }

    private void RemoveFrom(Func<object, List<string>> pick, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        lock (_listLock)
        {
            pick(null).RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

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

        ReplaceBuddies(JsonSerializer.Deserialize<List<string>>(reader.GetString(7)));

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

        ReplaceBuddies(otherSession.Buddies);

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

        bool OnList(IReadOnlyList<string> list) => list != null && list.Any(x => x.Equals(otherScreenName, StringComparison.OrdinalIgnoreCase));

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
