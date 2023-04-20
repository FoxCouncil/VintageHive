// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Processors;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Oscar;

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

    public static bool IsDebug => Debugger.IsAttached;

    public static bool IsDocker => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    public static CacheDbContext Cache { get; private set; }

    public static HiveDbContext Db { get; private set; }

    public static GeonamesDbContext Geonames { get; private set; }

    public static async Task Init()
    {
        Resources.Initialize();

        VFS.Init();

        Cache = new();
        Db = new();
        Geonames = new();

        await GeoIpUtils.CheckGeoIp();

        _ = ProtoWebUtils.UpdateSiteLists();

        var ipAddressString = Db.ConfigGet<string>(ConfigNames.IpAddress);

        var ipAddress = IPAddress.Parse(ipAddressString);

        var httpPort = Db.ConfigGet<int>(ConfigNames.PortHttp);

        _httpProxy = new(ipAddress, httpPort, false);

        _httpProxy
            .Use(HelperProcessor.ProcessHttpRequest)
            .Use(LocalServerProcessor.ProcessHttpRequest)
            .Use(ProtoWebProcessor.ProcessHttpRequest)
            .Use(InternetArchiveProcessor.ProcessRequest);

        // var httpsPort = Db.ConfigGet<int>(ConfigNames.PortHttps); // Soon?

        _httpsProxy = new(ipAddress, 9999, true);

        _httpsProxy
            .Use(LocalServerProcessor.ProcessHttpsRequest)
            .Use(DialNineProcessor.ProcessHttpsRequest);

        var ftpPort = Db.ConfigGet<int>(ConfigNames.PortFtp);

        _ftpProxy = new(ipAddress, ftpPort)
        {
            CacheDb = Cache
        };

        _ftpProxy
            .Use(ProtoWebProcessor.ProcessFtpRequest)
            .Use(LocalServerProcessor.ProcessFtpRequest);

        _oscarServer = new(ipAddress);

#if DEBUG
        // ==== TESTING AREA =====
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

