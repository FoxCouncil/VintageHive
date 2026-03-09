// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VintageHive.Network;
using VintageHive.Proxy.NetMeeting.H245;
using VintageHive.Proxy.NetMeeting.Rtp;

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// H.225.0 Call Signaling server — TCP listener on port 1720.
/// Gatekeeper-routed: receives Setup from callers, looks up the callee
/// in the RAS registry, opens an outbound TCP connection to the callee,
/// then proxies Q.931 messages bidirectionally.
/// </summary>
internal class H323Server : Listener
{
    private const string LOG_SRC = nameof(H323Server);

    private readonly RasRegistry _registry;
    private readonly ConcurrentDictionary<int, H323Call> _activeCalls = new();
    private int _nextCallRef;

    public H323Server(IPAddress address, int port, RasRegistry registry)
        : base(address, port, SocketType.Stream, ProtocolType.Tcp)
    {
        _registry = registry;
    }

    /// <summary>Access active calls for testing/inspection.</summary>
    internal ConcurrentDictionary<int, H323Call> ActiveCalls => _activeCalls;

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = false;
        var callerStream = connection.Stream;
        var callerEndpoint = connection.Remote;

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"Call signaling connection from {callerEndpoint}", "");

        TcpClient calleeClient = null;
        NetworkStream calleeStream = null;

        try
        {
            // Read first TPKT frame — must be a Q.931 Setup
            var setupPayload = await TpktFrame.ReadAsync(callerStream);
            if (setupPayload == null)
            {
                return null;
            }

            var setupQ931 = Q931Message.Parse(setupPayload);

            if (setupQ931.MessageType != Q931Message.MSG_SETUP)
            {
                Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Expected Setup, got {Q931Message.MessageTypeName(setupQ931.MessageType)}", "");

                await SendReleaseComplete(callerStream, setupQ931.CallReference, true, H225CallCodec.REL_UNDEFINED_REASON);

                return null;
            }

            // Decode the UUIE from the Setup
            var uuieData = setupQ931.GetUuieData();
            H225CallMessage setupUuie = null;

            if (uuieData != null)
            {
                try
                {
                    setupUuie = H225CallCodec.Decode(uuieData);
                }
                catch (Exception ex)
                {
                    Log.WriteException(LOG_SRC, ex, "");
                }
            }

            // Create call model
            var call = new H323Call
            {
                CallReference = setupQ931.CallReference,
                State = H323CallState.Setup,
                CallerSignalAddress = callerEndpoint
            };

            if (setupUuie?.Setup != null)
            {
                call.ConferenceId = setupUuie.Setup.ConferenceId;
                call.CallerAliases = setupUuie.Setup.SourceAliases;
                call.CalleeAliases = setupUuie.Setup.DestinationAliases;
                call.CallerH245Address = setupUuie.Setup.H245Address;
            }

            _activeCalls[call.CallId] = call;

            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{call}: received Setup", "");

            // Look up callee in RAS registry
            var calleeEp = FindCallee(call.CalleeAliases, setupUuie?.Setup?.DestCallSignalAddress);

            if (calleeEp == null)
            {
                Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{call}: callee not found, sending ReleaseComplete", "");

                await SendReleaseComplete(callerStream, call.CallReference, true, H225CallCodec.REL_UNREACHABLE_DEST);

                call.State = H323CallState.Released;
                call.ReleasedAt = DateTime.UtcNow;

                return null;
            }

            call.CalleeSignalAddress = calleeEp.CallSignalAddresses[0];
            call.CalleeEndpointId = calleeEp.EndpointId;

            // Send CallProceeding to caller while we connect to callee
            var cpUuie = H225CallCodec.EncodeCallProceeding(new CallProceedingUuie
            {
                ProtocolIdentifier = H225Constants.ProtocolOid
            });

            var cpMsg = Q931Message.CreateCallProceeding(call.CallReference, cpUuie);
            await TpktFrame.WriteAsync(callerStream, cpMsg.Build());
            call.State = H323CallState.Proceeding;

            // Connect to callee
            try
            {
                calleeClient = new TcpClient();
                await calleeClient.ConnectAsync(call.CalleeSignalAddress.Address, call.CalleeSignalAddress.Port);
                calleeStream = calleeClient.GetStream();
            }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{call}: failed to connect to callee at {call.CalleeSignalAddress}: {ex.Message}", "");

                await SendReleaseComplete(callerStream, call.CallReference, true, H225CallCodec.REL_UNREACHABLE_DEST);

                call.State = H323CallState.Released;
                call.ReleasedAt = DateTime.UtcNow;

                return null;
            }

            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{call}: connected to callee at {call.CalleeSignalAddress}", "");

            // Forward Setup to callee
            await TpktFrame.WriteAsync(calleeStream, setupPayload);

            // Proxy messages bidirectionally until call ends
            await ProxyCallSignaling(call, callerStream, calleeStream);
        }
        catch (IOException)
        {
            // Normal disconnect
        }
        catch (Exception ex)
        {
            Log.WriteException(LOG_SRC, ex, "");
        }
        finally
        {
            calleeStream?.Dispose();
            calleeClient?.Dispose();

            // Clean up call from active set
            foreach (var kvp in _activeCalls)
            {
                if (kvp.Value.State == H323CallState.Released)
                {
                    _activeCalls.TryRemove(kvp.Key, out _);
                }
            }
        }

        // Shut down caller connection
        try { connection.RawSocket.Shutdown(SocketShutdown.Both); } catch { }
        try { connection.RawSocket.Close(); } catch { }

        return null;
    }

    public override Task ProcessDisconnection(ListenerSocket connection)
    {
        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Call signaling disconnect from {connection.RemoteAddress}", "");

        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────
    //  Call signaling proxy
    // ──────────────────────────────────────────────────────────

    private async Task ProxyCallSignaling(H323Call call, NetworkStream callerStream, NetworkStream calleeStream)
    {
        using var cts = new CancellationTokenSource();

        // Two parallel tasks: caller→callee and callee→caller
        var callerToCallee = ProxyDirection(call, callerStream, calleeStream, "caller→callee", false, cts);
        var calleeToCaller = ProxyDirection(call, calleeStream, callerStream, "callee→caller", true, cts);

        // Wait for either direction to finish (call ended or disconnect)
        await Task.WhenAny(callerToCallee, calleeToCaller);

        // Cancel the other direction
        cts.Cancel();

        // Brief wait for graceful shutdown
        try
        {
            await Task.WhenAll(callerToCallee, calleeToCaller);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }

        if (call.State != H323CallState.Released)
        {
            call.State = H323CallState.Released;
            call.ReleasedAt = DateTime.UtcNow;
        }

        // Clean up H.245 handler and RTP relays
        if (call.H245Handler != null)
        {
            call.H245Handler.Dispose();
            call.H245Handler = null;
        }

        if (call.RelayManager != null)
        {
            try { await call.RelayManager.StopAllAsync(); } catch { }
            call.RelayManager.Dispose();
            call.RelayManager = null;
        }

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{call}: signaling ended", "");
    }

    private async Task ProxyDirection(H323Call call, NetworkStream source, NetworkStream dest, string direction, bool fromCallee, CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var payload = await TpktFrame.ReadAsync(source);
                if (payload == null)
                {
                    break; // Disconnect
                }

                // Parse Q.931 to track call state
                try
                {
                    var q931 = Q931Message.Parse(payload);
                    UpdateCallState(call, q931, fromCallee);

                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"{call}: {direction} {Q931Message.MessageTypeName(q931.MessageType)}", "");
                }
                catch
                {
                    // Parse failure — still forward the raw bytes
                }

                // Forward to other side
                await TpktFrame.WriteAsync(dest, payload);

                if (call.State == H323CallState.Released)
                {
                    break;
                }
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
    }

    private void UpdateCallState(H323Call call, Q931Message msg, bool fromCallee)
    {
        switch (msg.MessageType)
        {
            case Q931Message.MSG_ALERTING:
            {
                call.State = H323CallState.Alerting;

                var uuie = DecodeUuieSafe(msg);
                if (uuie?.Alerting?.H245Address != null)
                {
                    if (fromCallee)
                    {
                        call.CalleeH245Address = uuie.Alerting.H245Address;
                    }
                    else
                    {
                        call.CallerH245Address = uuie.Alerting.H245Address;
                    }
                }
            }
            break;

            case Q931Message.MSG_CONNECT:
            {
                call.State = H323CallState.Connected;
                call.ConnectedAt = DateTime.UtcNow;

                var uuie = DecodeUuieSafe(msg);
                if (uuie?.Connect?.H245Address != null)
                {
                    if (fromCallee)
                    {
                        call.CalleeH245Address = uuie.Connect.H245Address;
                    }
                    else
                    {
                        call.CallerH245Address = uuie.Connect.H245Address;
                    }
                }

                StartH245Relay(call);
            }
            break;

            case Q931Message.MSG_RELEASE_COMPLETE:
            {
                call.State = H323CallState.Released;
                call.ReleasedAt = DateTime.UtcNow;
            }
            break;
        }
    }

    private static H225CallMessage DecodeUuieSafe(Q931Message msg)
    {
        var data = msg.GetUuieData();
        if (data == null)
        {
            return null;
        }

        try
        {
            return H225CallCodec.Decode(data);
        }
        catch
        {
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  H.245 / RTP relay wiring
    // ──────────────────────────────────────────────────────────

    private void StartH245Relay(H323Call call)
    {
        // Guard: skip if H.245 addresses are missing or already started
        if (call.CallerH245Address == null || call.CalleeH245Address == null)
        {
            return;
        }

        if (call.H245Handler != null)
        {
            return;
        }

        try
        {
            var handler = new H245Handler(Address, 0);
            var relay = new RtpRelayManager(Address);

            call.H245Handler = handler;
            call.RelayManager = relay;

            handler.OnChannelOpened = (channelNumber, channelInfo) =>
            {
                if (channelInfo.SenderMediaChannel != null && channelInfo.ReceiverMediaChannel != null)
                {
                    var pair = relay.CreateRelay(channelNumber);
                    relay.StartRelay(channelNumber, channelInfo.SenderMediaChannel, channelInfo.SenderMediaControlChannel, channelInfo.ReceiverMediaChannel, channelInfo.ReceiverMediaControlChannel);
                }
            };

            handler.OnChannelClosed = (channelNumber) =>
            {
                _ = relay.StopRelayAsync(channelNumber);
            };

            // Run H.245 proxy in background
            _ = Task.Run(async () =>
            {
                try
                {
                    Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{call}: H.245 relay listening on port {handler.Port}", "");

                    await handler.RunAsync(call.CalleeH245Address);
                }
                catch (Exception ex)
                {
                    Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"{call}: H.245 relay error: {ex.Message}", "");
                }
            });

            Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"{call}: H.245 relay started", "");
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"{call}: failed to start H.245 relay: {ex.Message}", "");
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private RasEndpoint FindCallee(string[] aliases, IPEndPoint destCallSignal)
    {
        // First try by alias
        if (aliases != null)
        {
            foreach (var alias in aliases)
            {
                var ep = _registry.FindByAlias(alias);
                if (ep != null)
                {
                    return ep;
                }
            }
        }

        // Fall back to finding by call signal address
        if (destCallSignal != null)
        {
            foreach (var ep in _registry.GetAll())
            {
                if (ep.CallSignalAddresses == null)
                {
                    continue;
                }

                foreach (var addr in ep.CallSignalAddresses)
                {
                    if (addr.Address.Equals(destCallSignal.Address) &&
                        addr.Port == destCallSignal.Port)
                    {
                        return ep;
                    }
                }
            }
        }

        return null;
    }

    private static async Task SendReleaseComplete(NetworkStream stream, int callRef, bool fromDest, int reason)
    {
        var rcUuie = H225CallCodec.EncodeReleaseComplete(new ReleaseCompleteUuie
        {
            ProtocolIdentifier = H225Constants.ProtocolOid,
            Reason = reason
        });

        var msg = Q931Message.CreateReleaseComplete(callRef, fromDest, rcUuie);
        await TpktFrame.WriteAsync(stream, msg.Build());
    }
}
