using LibFoxyProxy.Ftp;
using LibFoxyProxy.Http;
using LibFoxyProxy.Security;
using System.Net;
using System.Text.Json;
using VintageHive.Data.Cache;
using VintageHive.Data.Config;
using VintageHive.Processors;
using VintageHive.Utilities;

namespace VintageHive;

internal class Mind
{
    const string ConfigFilePath = "config.json";

    const string GeoIPApiUri = "https://freegeoip.app/json/";

    static readonly object _lock = new();

    static Mind _instance;

    readonly ManualResetEvent _resetEvent = new(false);

    internal ConfigDbContext _configDb;

    internal CacheDbContext _cacheDb;

    IPAddress _ip;

    HttpProxy _httpProxy;

    HttpProxy _httpsProxy;

    FtpProxy _ftpProxy;

    public IConfigDb ConfigDb => _configDb;

    public ICacheDb CacheDb => _cacheDb;

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

    public void Init()
    {
        Resources.Initialize();

        _configDb = new ConfigDbContext("Data Source=config.db;Cache=Shared");

        _cacheDb = new CacheDbContext("Data Source=cache.db;Cache=Shared");

        var ipAddressString = ConfigDb.SettingGet<string>("ipaddress");

        var ipAddress = IPAddress.Parse(ipAddressString);

        var httpPort = ConfigDb.SettingGet<int>(ConfigNames.PortHttp);        

        _httpProxy = new(ipAddress, httpPort, false);

        _httpProxy.CacheDb = _cacheDb;

        _httpProxy
            .Use(IntranetProcessor.ProcessRequest)
            .Use(ProtoWebProcessor.ProcessRequest)
            .Use(InternetArchiveProcessor.ProcessRequest);

        var ftpPort = ConfigDb.SettingGet<int>(ConfigNames.PortFtp);

        _ftpProxy = new(ipAddress, ftpPort);

        _httpsProxy = new(ipAddress, 9999, true);

        using var rsaTest = new Rsa();

        rsaTest.GenerateKey(512, 3);

        var output = rsaTest.PEMPrivateKey();

        rsaTest.GenerateKey(4096, 3);

        var output2 = rsaTest.PEMPrivateKey();

        Console.WriteLine(output);
    }

    internal void Start()
    {
        _httpProxy.Start();

        _httpsProxy.Start();

        _ftpProxy.Start();

        _resetEvent.WaitOne();
    }
}

