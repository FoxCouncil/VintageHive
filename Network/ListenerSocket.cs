// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Security;

namespace VintageHive.Network;

public class ListenerSocket
{
    public Guid TraceId { get; } = Guid.NewGuid();

    public bool IsConnected => RawSocket.Connected;

    public IPEndPoint Local => (IPEndPoint)RawSocket.LocalEndPoint;

    public string LocalIP => Local.Address.MapToIPv4().ToString();

    public string LocalPort => Local.Port.ToString();

    public string LocalAddress => $"{LocalIP}:{LocalPort}";

    public IPEndPoint Remote => (IPEndPoint)RawSocket.RemoteEndPoint;

    public string RemoteIP => Remote.Address.MapToIPv4().ToString();

    public string RemotePort => Remote.Port.ToString();

    public string RemoteAddress => $"{RemoteIP}:{RemotePort}";

    public Socket RawSocket { get; set; }

    public bool IsSecure { get; set; } = false;

    public NetworkStream Stream { get; set; }

    public SslStream SecureStream { get; set; }

    public void Close() => RawSocket.Close();

}
