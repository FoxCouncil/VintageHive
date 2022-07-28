using System.Net;
using VintageHive.Data.Cache;
using VintageHive.Data.Config;
using VintageHive.Data.Oscar;
using VintageHive.Processors;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Security;
using VintageHive.Proxy.Socks;
using VintageHive.Utilities;

namespace VintageHive;

class Mind
{
    static readonly object _lock = new();

    static Mind _instance;

    readonly ManualResetEvent _resetEvent = new(false);

    internal ConfigDbContext _configDb;

    internal CacheDbContext _cacheDb;

    internal OscarDbContext _oscarDb;

    HttpProxy _httpProxy;

    // HttpProxy _httpsProxy;

    FtpProxy _ftpProxy;

    // Socks5Proxy _socks5Proxy;

    OscarServer _oscarServer;

    public IConfigDb ConfigDb => _configDb;

    public ICacheDb CacheDb => _cacheDb;

    public IOscarDb OscarDb => _oscarDb;

    public static Mind Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new Mind();
                }

                return _instance;
            }
        }
    }

    private Mind() { }

    public async Task Init()
    {
        Resources.Initialize();

        _configDb = new ConfigDbContext("Data Source=config.db;Cache=Shared");

        _cacheDb = new CacheDbContext("Data Source=cache.db;Cache=Shared");

        _oscarDb = new OscarDbContext("Data Source=oscar.db;Cache=Shared");

        CertificateAuthority.Init(_configDb);

        await CheckGeoIp();

        var ipAddressString = ConfigDb.SettingGet<string>(ConfigNames.IpAddress);

        var ipAddress = IPAddress.Parse(ipAddressString);

        var httpPort = ConfigDb.SettingGet<int>(ConfigNames.PortHttp);

        _httpProxy = new(ipAddress, httpPort, false)
        {
            CacheDb = _cacheDb
        };

        _httpProxy
            .Use(IntranetProcessor.ProcessRequest)
            .Use(RedirectionHelper.ProcessRequest)
            .Use(ProtoWebProcessor.ProcessHttpRequest)
            .Use(InternetArchiveProcessor.ProcessRequest);

        var ftpPort = ConfigDb.SettingGet<int>(ConfigNames.PortFtp);

        _ftpProxy = new(ipAddress, ftpPort)
        {
            CacheDb = _cacheDb
        };

        _ftpProxy
            .Use(ProtoWebProcessor.ProcessFtpRequest);

        _oscarServer = new(ipAddress);

        // var socks5Port = ConfigDb.SettingGet<int>(ConfigNames.PortSocks5);

        // _socks5Proxy = new(ipAddress, socks5Port);

        // _httpsProxy = new(ipAddress, 9999, true);

        // ==== TESTING AREA =====
#if DEBUG
        using var rsaTest = new Rsa();

        rsaTest.GenerateKey(512, 3);

        var output = rsaTest.PEMPrivateKey();

        Console.WriteLine(output);
        Console.WriteLine();

        var rsa = Rsa.FromPEMPrivateKey(output);

        var output2 = rsa.PEMPrivateKey();

        Console.WriteLine(output2);
#endif
    }

    public async Task ResetGeoIP()
    {
        ConfigDb.SettingSet<string>(ConfigNames.Location, null);
        ConfigDb.SettingSet<string>(ConfigNames.RemoteAddress, null);

        await CheckGeoIp();
    }

    public async Task CheckGeoIp()
    {
        var location = ConfigDb.SettingGet<string>(ConfigNames.Location);
        var address = ConfigDb.SettingGet<string>(ConfigNames.RemoteAddress);

        if (location == null || address == null)
        {
            var geoIpData = await Clients.GetGeoIpData();

            location = $"{geoIpData.city}, {geoIpData.region}, {geoIpData.countryCode}";

            ConfigDb.SettingSet(ConfigNames.Location, location);

            ConfigDb.SettingSet(ConfigNames.RemoteAddress, geoIpData.query);
        }
    }

    internal void Start()
    {
        _httpProxy.Start();

        // _httpsProxy.Start();

        _ftpProxy.Start();

        // _socks5Proxy.Start();

        _oscarServer.Start();

        _resetEvent.WaitOne();
    }
}

