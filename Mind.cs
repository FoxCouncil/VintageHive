using LibFoxyProxy.Http;
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

        var ipAddress = ConfigDb.SettingGet<string>("ipaddress");

        var portNum = ConfigDb.SettingGet<int>("porthttp");

        _httpProxy = new(IPAddress.Parse(ipAddress), portNum);

        _httpProxy.CacheDb = _cacheDb;

        _httpProxy
            .Use(IntranetProcessor.ProcessRequest)
            .Use(InternetArchiveProcessor.ProcessRequest);
    }

    internal void Start()
    {
        _httpProxy.Start();

        _resetEvent.WaitOne();
    }
}

