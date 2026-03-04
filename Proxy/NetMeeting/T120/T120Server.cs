// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VintageHive.Network;
using VintageHive.Proxy.NetMeeting.GCC;

namespace VintageHive.Proxy.NetMeeting.T120;

/// <summary>
/// T.120 data conferencing server — TCP listener on port 1503.
///
/// Handles the full T.120 stack:
///   TPKT framing → X.224 Class 0 transport → MCS (T.125)
///
/// Acts as the MCS top provider: manages user IDs, channel joins,
/// and bridges SendData between connected participants.
/// </summary>
internal class T120Server : Listener
{
    private const string LOG_SRC = nameof(T120Server);

    private readonly ConcurrentDictionary<int, T120Session> _sessions = new();
    private int _nextUserId = 1001; // MCS user IDs start at 1001

    public T120Server(IPAddress address, int port)
        : base(address, port, SocketType.Stream, ProtocolType.Tcp)
    {
    }

    /// <summary>Active sessions for testing/inspection.</summary>
    internal ConcurrentDictionary<int, T120Session> ActiveSessions => _sessions;

    /// <summary>Peek at next user ID (for testing).</summary>
    internal int NextUserId => _nextUserId;

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = false;
        var stream = connection.Stream;
        var remote = connection.Remote;

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
            $"T.120 connection from {remote}", "");

        T120Session session = null;

        try
        {
            // ── Phase 1: X.224 Connection ──────────────────────────
            var x224Payload = await TpktFrame.ReadAsync(stream);
            if (x224Payload == null)
            {
                return null;
            }

            var x224 = X224Message.Parse(x224Payload);

            if (x224.Type != X224Message.TYPE_CR)
            {
                Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                    $"Expected X.224 CR, got {X224Message.TypeName(x224.Type)}", "");
                return null;
            }

            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                $"X.224 CR from {remote}: SrcRef=0x{x224.SrcRef:X4}", "");

            // Send X.224 Connection Confirm
            var cc = X224Message.BuildConnectionConfirm(
                dstRef: x224.SrcRef,
                srcRef: 1);
            await TpktFrame.WriteAsync(stream, cc);

            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                $"X.224 CC sent to {remote}", "");

            // ── Phase 2: MCS Connect ───────────────────────────────
            var mcsPayload = await ReadX224Data(stream);
            if (mcsPayload == null)
            {
                return null;
            }

            if (!McsCodec.IsConnectInitial(mcsPayload))
            {
                Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                    $"Expected MCS Connect-Initial, got 0x{mcsPayload[0]:X2}", "");
                return null;
            }

            var connectInitial = McsCodec.DecodeConnectInitial(mcsPayload);

            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                $"MCS Connect-Initial from {remote}: " +
                $"MaxChannels={connectInitial.CallingDomainParameters.MaxChannelIds}, " +
                $"MaxUsers={connectInitial.CallingDomainParameters.MaxUserIds}", "");

            // Negotiate domain parameters (use calling params as basis)
            var negotiatedParams = NegotiateDomainParameters(
                connectInitial.CallingDomainParameters,
                connectInitial.MinimumDomainParameters,
                connectInitial.MaximumDomainParameters);

            // Decode GCC ConferenceCreateRequest from MCS userData
            ConferenceCreateRequest gccRequest = null;
            byte[] gccResponseData;

            if (connectInitial.UserData != null && GccCodec.IsT124ConnectData(connectInitial.UserData))
            {
                try
                {
                    gccRequest = GccCodec.DecodeConferenceCreateRequest(connectInitial.UserData);

                    Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
                        $"GCC ConferenceCreateRequest from {remote}: " +
                        $"name=\"{gccRequest.ConferenceNameNumeric}\", " +
                        $"userData={gccRequest.UserData?.Length ?? 0} blocks", "");

                    // Assign a node ID for this participant
                    var nodeId = Interlocked.Increment(ref _nextUserId);

                    // Build proper GCC ConferenceCreateResponse
                    gccResponseData = GccCodec.EncodeConferenceCreateResponse(
                        new ConferenceCreateResponse
                        {
                            NodeId = nodeId,
                            Tag = 1,
                            Result = GccConstants.RESULT_SUCCESS,
                            UserData = gccRequest.UserData // Echo client's user data blocks
                        });
                }
                catch (Exception ex)
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                        $"GCC decode failed, echoing raw userData: {ex.Message}", "");
                    gccResponseData = connectInitial.UserData;
                }
            }
            else
            {
                // No GCC data — echo raw userData
                gccResponseData = connectInitial.UserData;
            }

            // Send MCS Connect-Response
            var connectResponse = McsCodec.EncodeConnectResponse(new McsConnectResponse
            {
                Result = McsConstants.RESULT_SUCCESSFUL,
                CalledConnectId = 0,
                DomainParameters = negotiatedParams,
                UserData = gccResponseData
            });

            var dtPayload = X224Message.BuildDataTransfer(connectResponse);
            await TpktFrame.WriteAsync(stream, dtPayload);

            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                $"MCS Connect-Response sent to {remote}", "");

            // ── Phase 3: MCS Domain ────────────────────────────────
            session = new T120Session
            {
                Remote = remote,
                ConnectedAt = DateTime.UtcNow,
                DomainParameters = negotiatedParams,
                GccUserData = connectInitial.UserData,
                GccRequest = gccRequest,
                Stream = stream
            };

            await HandleDomainPhase(stream, session);
        }
        catch (IOException)
        {
            // Normal disconnect
        }
        catch (InvalidDataException ex)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                $"Protocol error from {remote}: {ex.Message}", "");
        }
        catch (Exception ex)
        {
            Log.WriteException(LOG_SRC, ex, "");
        }
        finally
        {
            if (session != null)
            {
                RemoveSession(session);
                session.DisconnectedAt = DateTime.UtcNow;

                Log.WriteLine(Log.LEVEL_INFO, LOG_SRC,
                    $"T.120 session ended for {remote}: " +
                    $"UserId={session.UserId}, " +
                    $"Channels={session.JoinedChannels.Count}, " +
                    $"DataPdus={session.DataPduCount}", "");
            }
        }

        // Shut down the connection
        try { connection.RawSocket.Shutdown(SocketShutdown.Both); } catch { }
        try { connection.RawSocket.Close(); } catch { }

        return null;
    }

    public override Task ProcessDisconnection(ListenerSocket connection)
    {
        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
            $"T.120 disconnect from {connection.RemoteAddress}", "");

        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────
    //  MCS Domain phase
    // ──────────────────────────────────────────────────────────

    private async Task HandleDomainPhase(NetworkStream stream, T120Session session)
    {
        while (true)
        {
            var data = await ReadX224Data(stream);
            if (data == null)
            {
                break; // Disconnect
            }

            // Domain PDUs are PER-encoded
            McsDomainPdu pdu;

            try
            {
                pdu = McsCodec.DecodeDomainPdu(data);
            }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                    $"Failed to decode domain PDU: {ex.Message}", "");
                continue;
            }

            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                $"Domain PDU from {session.Remote}: {pdu}", "");

            switch (pdu.Type)
            {
                case McsConstants.DOMAIN_ERECT_DOMAIN_REQUEST:
                {
                    // Nothing to respond to — just acknowledge internally
                    session.DomainErected = true;
                }
                break;

                case McsConstants.DOMAIN_ATTACH_USER_REQUEST:
                {
                    var userId = Interlocked.Increment(ref _nextUserId);
                    session.UserId = userId;

                    // Register session
                    _sessions[userId] = session;

                    // Send AttachUserConfirm
                    var confirm = McsCodec.EncodeAttachUserConfirm(
                        McsConstants.RESULT_SUCCESSFUL, userId);
                    await WriteX224Data(stream, confirm);

                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                        $"Assigned UserId={userId} to {session.Remote}", "");
                }
                break;

                case McsConstants.DOMAIN_CHANNEL_JOIN_REQUEST:
                {
                    session.JoinedChannels.Add(pdu.ChannelId);

                    // Send ChannelJoinConfirm
                    var confirm = McsCodec.EncodeChannelJoinConfirm(
                        McsConstants.RESULT_SUCCESSFUL, pdu.UserId, pdu.ChannelId);
                    await WriteX224Data(stream, confirm);

                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                        $"UserId={pdu.UserId} joined channel {pdu.ChannelId}", "");
                }
                break;

                case McsConstants.DOMAIN_SEND_DATA_REQUEST:
                {
                    session.DataPduCount++;

                    // Relay as SendDataIndication to all other sessions on this channel
                    var indication = McsCodec.EncodeSendDataIndication(
                        pdu.Initiator, pdu.ChannelId, pdu.DataPriority, pdu.UserData);

                    await RelayToChannel(pdu.ChannelId, session.UserId, indication);
                }
                break;

                case McsConstants.DOMAIN_DISCONNECT_PROVIDER_ULTIMATUM:
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                        $"DisconnectProviderUltimatum from UserId={session.UserId}", "");
                    return; // End session
                }

                default:
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                        $"Unhandled domain PDU type {pdu.Type} from {session.Remote}", "");
                }
                break;
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  X.224 Data Transfer helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Read an X.224 DT PDU via TPKT and return the user data.
    /// </summary>
    internal static async Task<byte[]> ReadX224Data(NetworkStream stream)
    {
        var payload = await TpktFrame.ReadAsync(stream);
        if (payload == null)
        {
            return null;
        }

        var x224 = X224Message.Parse(payload);

        if (x224.Type == X224Message.TYPE_DR)
        {
            return null; // Disconnect request
        }

        if (x224.Type != X224Message.TYPE_DT)
        {
            throw new InvalidDataException(
                $"Expected X.224 DT, got {X224Message.TypeName(x224.Type)}");
        }

        return x224.Data;
    }

    /// <summary>
    /// Wrap data in an X.224 DT PDU and send via TPKT.
    /// </summary>
    internal static async Task WriteX224Data(NetworkStream stream, byte[] data)
    {
        var dt = X224Message.BuildDataTransfer(data);
        await TpktFrame.WriteAsync(stream, dt);
    }

    // ──────────────────────────────────────────────────────────
    //  Session management
    // ──────────────────────────────────────────────────────────

    private void RemoveSession(T120Session session)
    {
        if (session.UserId != 0)
        {
            _sessions.TryRemove(session.UserId, out _);
        }
    }

    /// <summary>
    /// Relay a SendDataIndication to all sessions that have joined the target channel,
    /// except the sender.
    /// </summary>
    private async Task RelayToChannel(int channelId, int senderUserId, byte[] indication)
    {
        foreach (var kvp in _sessions)
        {
            if (kvp.Key == senderUserId)
            {
                continue;
            }

            var target = kvp.Value;

            if (!target.JoinedChannels.Contains(channelId))
            {
                continue;
            }

            if (target.Stream == null)
            {
                continue;
            }

            try
            {
                await WriteX224Data(target.Stream, indication);
            }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                    $"Failed to relay to UserId={kvp.Key}: {ex.Message}", "");
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Domain parameter negotiation
    // ──────────────────────────────────────────────────────────

    private static McsDomainParameters NegotiateDomainParameters(
        McsDomainParameters target,
        McsDomainParameters minimum,
        McsDomainParameters maximum)
    {
        return new McsDomainParameters
        {
            MaxChannelIds = Clamp(target.MaxChannelIds, minimum.MaxChannelIds, maximum.MaxChannelIds),
            MaxUserIds = Clamp(target.MaxUserIds, minimum.MaxUserIds, maximum.MaxUserIds),
            MaxTokenIds = Clamp(target.MaxTokenIds, minimum.MaxTokenIds, maximum.MaxTokenIds),
            NumPriorities = Clamp(target.NumPriorities, minimum.NumPriorities, maximum.NumPriorities),
            MinThroughput = Clamp(target.MinThroughput, minimum.MinThroughput, maximum.MinThroughput),
            MaxHeight = Clamp(target.MaxHeight, minimum.MaxHeight, maximum.MaxHeight),
            MaxMcsPduSize = Clamp(target.MaxMcsPduSize, minimum.MaxMcsPduSize, maximum.MaxMcsPduSize),
            ProtocolVersion = Clamp(target.ProtocolVersion, minimum.ProtocolVersion, maximum.ProtocolVersion)
        };
    }

    private static int Clamp(int value, int min, int max)
    {
        if (min > max)
        {
            return value; // Invalid range — use target
        }

        return Math.Clamp(value, min, max);
    }
}

/// <summary>
/// Tracks one T.120/MCS participant session.
/// </summary>
internal class T120Session
{
    public IPEndPoint Remote { get; init; }
    public DateTime ConnectedAt { get; init; }
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>Negotiated MCS domain parameters.</summary>
    public McsDomainParameters DomainParameters { get; init; }

    /// <summary>GCC user data from Connect-Initial (raw bytes).</summary>
    public byte[] GccUserData { get; init; }

    /// <summary>Parsed GCC ConferenceCreateRequest (null if parsing failed).</summary>
    public ConferenceCreateRequest GccRequest { get; init; }

    /// <summary>MCS user ID assigned by AttachUserConfirm.</summary>
    public int UserId { get; set; }

    /// <summary>Whether ErectDomainRequest was received.</summary>
    public bool DomainErected { get; set; }

    /// <summary>Set of MCS channel IDs this user has joined.</summary>
    public HashSet<int> JoinedChannels { get; } = new();

    /// <summary>Count of SendDataRequest PDUs received.</summary>
    public long DataPduCount { get; set; }

    /// <summary>NetworkStream for relaying data to this session.</summary>
    public NetworkStream Stream { get; set; }
}
