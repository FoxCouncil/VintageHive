using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive.Data.Cache;
using VintageHive.Network;
using VintageHiveFtpProcessDelegate = System.Func<VintageHive.Proxy.Ftp.FtpRequest, System.Threading.Tasks.Task<byte[]>>;

namespace VintageHive.Proxy.Ftp;

public class FtpProxy : Listener
{
    static TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(7);
    
    readonly List<VintageHiveFtpProcessDelegate> Handlers = new();

    internal ICacheDb CacheDb { get; set; }

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

        var req = FtpRequest.ParseFtpOverHttp(connection, Encoding, data);
        
        var key = $"FPC-{req.Uri}";

        var cachedResponse = CacheDb?.Get<string>(key);

        if (cachedResponse == null)
        {
            byte[] responseData = null;

            try
            {
                foreach (var handler in Handlers)
                {
                    responseData = await handler(req);
                        
                    if (responseData != null)
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {
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

            return responseData;
        }
        else
        {
            try
            {
                Display.WriteLog($"[{"FTP Proxy Cached",15} Request] ({req.Uri}) [N/A]");

                return Convert.FromBase64String(cachedResponse);
            }
            catch (Exception) { }
        }

        // TODO: Keep-Alive respect?
        connection.RawSocket.Close();

        return null;
    }
}
