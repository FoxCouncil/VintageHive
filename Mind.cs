using System.Net;
using VintageHive.Data.Contexts;
using VintageHive.Processors;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Security;
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

    public static async Task Init()
    {
        Resources.Initialize();

        Cache = new CacheDbContext();

        Db = new HiveDbContext();

        VFS.Init();

        // CertificateAuthority.Init();

        await CheckGeoIp();

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

        // ==== TESTING AREA =====
#if DEBUG
        // var socks5Port = ConfigDb.SettingGet<int>(ConfigNames.PortSocks5);

        // _socks5Proxy = new(ipAddress, socks5Port);

        _httpsProxy = new(ipAddress, 9999, true);

        using var rsaTest = new Rsa();

        rsaTest.GenerateKey(512, BigNumber.Rsa3);

        var output = rsaTest.PEMPrivateKey();

        Log.WriteLine(Log.LEVEL_INFO, nameof(Mind), output, "");
        Log.WriteLine();

        var rsa = Rsa.FromPEMPrivateKey(output);

        var output2 = rsa.PEMPrivateKey();

        Log.WriteLine(Log.LEVEL_INFO, nameof(Mind), output2, "");
#endif
    }

    public static async Task ResetGeoIP()
    {
        Db.ConfigSet<string>(ConfigNames.Location, null);
        Db.ConfigSet<string>(ConfigNames.RemoteAddress, null);

        await CheckGeoIp();
    }

    public static async Task CheckGeoIp()
    {
        var location = Db.ConfigGet<string>(ConfigNames.Location);
        var address = Db.ConfigGet<string>(ConfigNames.RemoteAddress);

        if (location == null || address == null)
        {
            var geoIpData = await Clients.GetGeoIpData();

            location = $"{geoIpData.city}, {geoIpData.region}, {geoIpData.countryCode}";

            Db.ConfigSet(ConfigNames.Location, location);

            Db.ConfigSet(ConfigNames.RemoteAddress, geoIpData.query);
        }
    }

    internal static void Start()
    {
        _httpProxy.Start();

#if DEBUG
        _httpsProxy.Start();
#endif

        _ftpProxy.Start();

        // _socks5Proxy.Start();

        _oscarServer.Start();

        _resetEvent.WaitOne();
    }
}

