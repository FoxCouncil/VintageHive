using System.Net;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Processors;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Oscar;
using VintageHive.Utilities;

namespace VintageHive;

static class Mind
{
    public static readonly string ApplicationVersion = typeof(HttpProxy).Assembly.GetName().Version?.ToString() ?? "NA";

    static readonly ManualResetEvent _resetEvent = new(false);

    static HttpProxy _httpProxy;

    static HttpProxy _httpsProxy;

    static FtpProxy _ftpProxy;

    // static Socks5Proxy _socks5Proxy;

    static OscarServer _oscarServer;

    public static CacheDbContext Cache { get; private set; }

    public static HiveDbContext Db { get; private set; }

    public static GeonamesDbContext Geonames { get; private set; }

    public static async Task Init()
    {
        Resources.Initialize();

        Cache = new();
        Db = new();
        Geonames = new();

        VFS.Init();

        await GeoIpUtils.CheckGeoIp();

        _ = ProtoWebUtils.UpdateSiteLists();

        var ipAddressString = Db.ConfigGet<string>(ConfigNames.IpAddress);

        var ipAddress = IPAddress.Parse(ipAddressString);

        var httpPort = Db.ConfigGet<int>(ConfigNames.PortHttp);

        _httpProxy = new(ipAddress, httpPort, false);

        _httpProxy
            .Use(LocalServerProcessor.ProcessHttpRequest)
            .Use(ProtoWebProcessor.ProcessHttpRequest)
            .Use(InternetArchiveProcessor.ProcessRequest);

        var ftpPort = Db.ConfigGet<int>(ConfigNames.PortFtp);

        _ftpProxy = new(ipAddress, ftpPort)
        {
            CacheDb = Cache
        };

        _ftpProxy
            .Use(ProtoWebProcessor.ProcessFtpRequest)
            .Use(LocalServerProcessor.ProcessFtpRequest);

        _oscarServer = new(ipAddress);

        _httpsProxy = new(ipAddress, 9999, true);

        _httpsProxy
            .Use(LocalServerProcessor.ProcessHttpsRequest);

        // ==== TESTING AREA =====
#if DEBUG
        // var socks5Port = ConfigDb.SettingGet<int>(ConfigNames.PortSocks5);

        // _socks5Proxy = new(ipAddress, socks5Port);
#endif
    }

    internal static void Start()
    {
        _httpProxy.Start();

        _httpsProxy.Start();

        _ftpProxy.Start();

#if DEBUG
        // _socks5Proxy.Start();
#endif

        _oscarServer.Start();

        _resetEvent.WaitOne();
    }
}

