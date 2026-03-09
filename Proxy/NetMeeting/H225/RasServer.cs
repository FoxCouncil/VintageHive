// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive.Network;

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// H.225.0 RAS Gatekeeper — listens on UDP for PER-encoded RAS messages.
/// Handles endpoint discovery (GRQ), registration (RRQ), admission (ARQ),
/// unregistration (URQ), and disengage (DRQ).
/// </summary>
internal class RasServer : UdpListener
{
    private const string LOG_SRC = nameof(RasServer);
    private const string GK_ID = "VintageHive";
    private const int DEFAULT_TTL_SECONDS = 300;

    private readonly RasRegistry _registry = new();
    private readonly Timer _ttlTimer;

    public RasServer(IPAddress address, int port) : base(address, port)
    {
        _ttlTimer = new Timer(_ => CleanExpired(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    /// <summary>Access the registry for testing/inspection.</summary>
    internal RasRegistry Registry => _registry;

    public override Task<byte[]> ProcessDatagram(IPEndPoint remoteEndPoint, byte[] data, int length)
    {
        try
        {
            var msgData = data;
            if (length < data.Length)
            {
                msgData = new byte[length];
                Array.Copy(data, msgData, length);
            }

            var msg = RasCodec.Decode(msgData);

            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"RAS from {remoteEndPoint}: type={msg.Type} seq={msg.RequestSeqNum}", "");

            var response = msg.Type switch
            {
                H225Constants.RAS_GATEKEEPER_REQUEST => HandleGatekeeperRequest(msg, remoteEndPoint),
                H225Constants.RAS_REGISTRATION_REQUEST => HandleRegistrationRequest(msg),
                H225Constants.RAS_UNREGISTRATION_REQUEST => HandleUnregistrationRequest(msg),
                H225Constants.RAS_ADMISSION_REQUEST => HandleAdmissionRequest(msg),
                H225Constants.RAS_DISENGAGE_REQUEST => HandleDisengageRequest(msg),
                _ => HandleUnknown(msg)
            };

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Log.WriteException(LOG_SRC, ex, "");
            return Task.FromResult<byte[]>(null);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Message handlers
    // ──────────────────────────────────────────────────────────

    private byte[] HandleGatekeeperRequest(RasMessage msg, IPEndPoint remoteEndPoint)
    {
        var grq = msg.Grq;

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"GRQ from {remoteEndPoint}: aliases={FormatAliases(grq.Aliases)}", "");

        // Always confirm — we are the gatekeeper for this network
        var rasAddress = new IPEndPoint(Address, BoundPort);

        return RasCodec.EncodeGatekeeperConfirm(grq.RequestSeqNum, rasAddress, GK_ID);
    }

    private byte[] HandleRegistrationRequest(RasMessage msg)
    {
        var rrq = msg.Rrq;

        var endpointId = _registry.GenerateEndpointId();
        var ttl = rrq.TimeToLive > 0 ? rrq.TimeToLive : DEFAULT_TTL_SECONDS;

        // Truncate aliases exceeding 256 characters
        var aliases = rrq.Aliases;
        if (aliases != null)
        {
            for (var i = 0; i < aliases.Length; i++)
            {
                if (aliases[i] != null && aliases[i].Length > 256)
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC,
                        $"Truncating alias '{aliases[i][..32]}...' from {aliases[i].Length} to 256 chars", "");
                    aliases[i] = aliases[i][..256];
                }
            }
        }

        var endpoint = new RasEndpoint
        {
            EndpointId = endpointId,
            CallSignalAddresses = rrq.CallSignalAddresses,
            RasAddresses = rrq.RasAddresses,
            Aliases = aliases,
            ExpiresAt = DateTime.UtcNow.AddSeconds(ttl)
        };

        _registry.Register(endpoint);

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"RRQ → RCF: ep={endpointId} aliases={FormatAliases(rrq.Aliases)} ttl={ttl}s (registry has {_registry.Count} endpoints)", "");

        return RasCodec.EncodeRegistrationConfirm(rrq.RequestSeqNum, rrq.CallSignalAddresses, GK_ID, endpointId, ttl);
    }

    private byte[] HandleUnregistrationRequest(RasMessage msg)
    {
        var urq = msg.Urq;

        if (urq.EndpointIdentifier != null)
        {
            _registry.Unregister(urq.EndpointIdentifier);
        }

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"URQ → UCF: ep={urq.EndpointIdentifier} (registry has {_registry.Count} endpoints)", "");

        return RasCodec.EncodeUnregistrationConfirm(urq.RequestSeqNum);
    }

    private byte[] HandleAdmissionRequest(RasMessage msg)
    {
        var arq = msg.Arq;

        // Verify the calling endpoint is registered
        var callerEp = _registry.FindById(arq.EndpointIdentifier);
        if (callerEp == null)
        {
            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"ARQ → ARJ: caller ep={arq.EndpointIdentifier} not registered", "");
            return RasCodec.EncodeAdmissionReject(arq.RequestSeqNum, H225Constants.ARJ_CALLER_NOT_REGISTERED);
        }

        // Look up the destination by alias
        RasEndpoint destEp = null;
        if (arq.DestinationAliases != null && arq.DestinationAliases.Length > 0)
        {
            foreach (var alias in arq.DestinationAliases)
            {
                destEp = _registry.FindByAlias(alias);
                if (destEp != null)
                {
                    break;
                }
            }
        }

        if (destEp == null)
        {
            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"ARQ → ARJ: destination {FormatAliases(arq.DestinationAliases)} not found", "");
            return RasCodec.EncodeAdmissionReject(arq.RequestSeqNum, H225Constants.ARJ_CALLED_PARTY_NOT_REGISTERED);
        }

        // Direct call model — tell caller to connect to destination's call signaling address
        var destCallSignal = destEp.CallSignalAddresses[0];

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"ARQ → ACF: ep={arq.EndpointIdentifier} → {FormatAliases(arq.DestinationAliases)} at {destCallSignal}", "");

        return RasCodec.EncodeAdmissionConfirm(arq.RequestSeqNum, arq.BandWidth, destCallSignal);
    }

    private byte[] HandleDisengageRequest(RasMessage msg)
    {
        var drq = msg.Drq;

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"DRQ → DCF: ep={drq.EndpointIdentifier} reason={drq.DisengageReason}", "");

        return RasCodec.EncodeDisengageConfirm(drq.RequestSeqNum);
    }

    private byte[] HandleUnknown(RasMessage msg)
    {
        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Unknown RAS message type {msg.Type}, sending UnknownMessageResponse", "");

        return RasCodec.EncodeUnknownMessageResponse(msg.RequestSeqNum > 0 ? msg.RequestSeqNum : 1);
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private void CleanExpired()
    {
        var removed = _registry.CleanExpired();
        if (removed > 0)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Cleaned {removed} expired registrations (registry has {_registry.Count} endpoints)", "");
        }
    }

    private static string FormatAliases(string[] aliases)
    {
        if (aliases == null || aliases.Length == 0)
        {
            return "(none)";
        }

        return string.Join(", ", aliases);
    }
}
