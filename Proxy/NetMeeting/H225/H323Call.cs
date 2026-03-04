// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive.Proxy.NetMeeting.H245;
using VintageHive.Proxy.NetMeeting.Rtp;

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// Call state for H.323 call signaling.
/// </summary>
internal enum H323CallState
{
    /// <summary>No call in progress.</summary>
    Idle,

    /// <summary>Setup sent/received, waiting for response.</summary>
    Setup,

    /// <summary>CallProceeding received.</summary>
    Proceeding,

    /// <summary>Alerting received (callee ringing).</summary>
    Alerting,

    /// <summary>Connect received — call is active.</summary>
    Connected,

    /// <summary>ReleaseComplete sent/received — call ended.</summary>
    Released
}

/// <summary>
/// Represents an H.323 call in progress through the gatekeeper-routed signaling proxy.
/// Links the caller and callee TCP connections.
/// </summary>
internal class H323Call
{
    private static int _nextCallId;
    private H323CallState _state;

    public H323Call()
    {
        CallId = Interlocked.Increment(ref _nextCallId);
        _state = H323CallState.Idle;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Internal call identifier for logging.</summary>
    public int CallId { get; }

    /// <summary>Q.931 call reference value (15-bit).</summary>
    public int CallReference { get; set; }

    /// <summary>Current call state. Invalid transitions are ignored (logged at DEBUG).</summary>
    public H323CallState State
    {
        get => _state;
        set
        {
            if (value == _state)
            {
                return;
            }

            if (!IsValidTransition(_state, value))
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(H323Call),
                    $"Call#{CallId}: invalid state transition {_state}→{value}, ignoring", "");
                return;
            }

            _state = value;
        }
    }

    /// <summary>Conference ID from the Setup-UUIE (16 bytes).</summary>
    public byte[] ConferenceId { get; set; }

    /// <summary>Caller's registered endpoint ID.</summary>
    public string CallerEndpointId { get; set; }

    /// <summary>Callee's registered endpoint ID.</summary>
    public string CalleeEndpointId { get; set; }

    /// <summary>Caller's aliases from Setup.</summary>
    public string[] CallerAliases { get; set; }

    /// <summary>Callee's aliases from Setup.</summary>
    public string[] CalleeAliases { get; set; }

    /// <summary>Caller's call signal address.</summary>
    public IPEndPoint CallerSignalAddress { get; set; }

    /// <summary>Callee's call signal address (from registry).</summary>
    public IPEndPoint CalleeSignalAddress { get; set; }

    /// <summary>H.245 address offered by caller.</summary>
    public IPEndPoint CallerH245Address { get; set; }

    /// <summary>H.245 address offered by callee.</summary>
    public IPEndPoint CalleeH245Address { get; set; }

    /// <summary>When the call was created.</summary>
    public DateTime CreatedAt { get; }

    /// <summary>When the call was connected (null if never connected).</summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>When the call was released (null if still active).</summary>
    public DateTime? ReleasedAt { get; set; }

    /// <summary>H.245 control channel handler (set when call is connected).</summary>
    public H245Handler H245Handler { get; set; }

    /// <summary>RTP relay manager for this call (set when call is connected).</summary>
    public RtpRelayManager RelayManager { get; set; }

    public override string ToString()
    {
        var caller = CallerAliases != null ? string.Join(",", CallerAliases) : "?";
        var callee = CalleeAliases != null ? string.Join(",", CalleeAliases) : "?";
        return $"Call#{CallId}[{State} {caller}→{callee} CRV={CallReference}]";
    }

    /// <summary>
    /// Check whether a state transition is valid per the H.323 call model.
    /// </summary>
    internal static bool IsValidTransition(H323CallState from, H323CallState to)
    {
        return (from, to) switch
        {
            (H323CallState.Idle, H323CallState.Setup) => true,
            (H323CallState.Setup, H323CallState.Proceeding) => true,
            (H323CallState.Setup, H323CallState.Alerting) => true,
            (H323CallState.Setup, H323CallState.Connected) => true, // fast connect
            (H323CallState.Setup, H323CallState.Released) => true,
            (H323CallState.Proceeding, H323CallState.Alerting) => true,
            (H323CallState.Proceeding, H323CallState.Connected) => true,
            (H323CallState.Proceeding, H323CallState.Released) => true,
            (H323CallState.Alerting, H323CallState.Connected) => true,
            (H323CallState.Alerting, H323CallState.Released) => true,
            (H323CallState.Connected, H323CallState.Released) => true,
            (H323CallState.Released, H323CallState.Released) => true, // idempotent
            _ => false
        };
    }
}
