// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;

namespace VintageHive.Proxy.NetMeeting.H225;

/// <summary>
/// Thread-safe registry of H.323 endpoints for the RAS gatekeeper.
/// Endpoints are keyed by their gatekeeper-assigned EndpointIdentifier.
/// </summary>
internal class RasRegistry
{
    private readonly ConcurrentDictionary<string, RasEndpoint> _endpoints = new();
    private int _nextId;

    /// <summary>Number of currently registered endpoints.</summary>
    public int Count => _endpoints.Count;

    /// <summary>Generate a unique endpoint identifier.</summary>
    public string GenerateEndpointId()
    {
        var id = Interlocked.Increment(ref _nextId);
        return $"EP{id:D4}";
    }

    /// <summary>Register or update an endpoint.</summary>
    public void Register(RasEndpoint endpoint)
    {
        _endpoints[endpoint.EndpointId] = endpoint;
    }

    /// <summary>Remove an endpoint by its identifier.</summary>
    public bool Unregister(string endpointId)
    {
        return _endpoints.TryRemove(endpointId, out _);
    }

    /// <summary>Find an endpoint by its identifier.</summary>
    public RasEndpoint FindById(string endpointId)
    {
        return _endpoints.TryGetValue(endpointId, out var ep) ? ep : null;
    }

    /// <summary>Find an endpoint by alias (case-insensitive).</summary>
    public RasEndpoint FindByAlias(string alias)
    {
        foreach (var ep in _endpoints.Values)
        {
            if (ep.Aliases == null)
            {
                continue;
            }

            foreach (var a in ep.Aliases)
            {
                if (string.Equals(a, alias, StringComparison.OrdinalIgnoreCase))
                {
                    return ep;
                }
            }
        }

        return null;
    }

    /// <summary>Remove all expired registrations.</summary>
    public int CleanExpired()
    {
        var removed = 0;

        foreach (var kvp in _endpoints)
        {
            if (kvp.Value.IsExpired)
            {
                if (_endpoints.TryRemove(kvp.Key, out _))
                {
                    removed++;
                }
            }
        }

        return removed;
    }

    /// <summary>Get all registered endpoints (snapshot).</summary>
    public RasEndpoint[] GetAll()
    {
        return _endpoints.Values.ToArray();
    }
}
