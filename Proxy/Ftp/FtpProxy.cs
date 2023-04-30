// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Data.Contexts;
using VintageHive.Network;
using VintageHiveFtpProcessDelegate = System.Func<VintageHive.Proxy.Ftp.FtpRequest, System.Threading.Tasks.Task<bool>>;

namespace VintageHive.Proxy.Ftp;

public class FtpProxy : Listener
{
    static TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(7);
    
    readonly List<VintageHiveFtpProcessDelegate> Handlers = new();

    internal CacheDbContext CacheDb { get; set; }

    public FtpProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp) { }

    public FtpProxy Use(VintageHiveFtpProcessDelegate delegateFunc)
    {
        Handlers.Add(delegateFunc);

        return this;
    }

    internal override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        await Task.Delay(0);

        return Encoding.ASCII.GetBytes("220 VintageHive FTP Proxy!\n");
    }

    internal override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var requestData = Encoding.ASCII.GetString(data, 0, read);

        var req = await FtpRequest.Parse(connection, Encoding, data, read);

        if (!req.IsValid)
        {
            return null;
        }
        
        var key = $"FPC-{req.Uri}";

        var cachedResponse = Mind.Cache.GetFtpProxy(key);

        if (cachedResponse == null)
        {
            bool handled;

            try
            {
                foreach (var handler in Handlers)
                {
                    handled = await handler(req);

                    if (handled)
                    {
                        Mind.Db.RequestsTrack(connection, "N/A", "FTP", req.Uri.ToString(), handler.Method.DeclaringType.Name);

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteException(GetType().Name, ex, connection.TraceId.ToString());

                return null;
            }

            if (connection == null)
            {
                // Nothing to send too..
                return null;
            }

            /* DO NOT FUCKING CACHE FTP YET */
            /* YOU ARE NOT GOOD ENOUGH YET */
            //if (responseData != null)
            //{
            //    CacheDb?.Set<string>(key, CacheTtl, Convert.ToBase64String(responseData));
            //}
        }
        else
        {
            try
            {
                // Log.WriteLine(Log.LEVEL_REQUEST, GetType().Name, $"({req.Uri}) [N/A]", connection.TraceId.ToString());

                return Convert.FromBase64String(cachedResponse);
            }
            catch (Exception) { }
        }

        // TODO: Keep-Alive respect?
        connection.RawSocket.Close();

        return null;
    }
}
