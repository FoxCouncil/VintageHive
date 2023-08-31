// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;
using VintageHive.Data.Contexts;
using VintageHive.Processors;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Pop3;
using VintageHive.Proxy.Smtp;
using VintageHive.Proxy.Telnet;

namespace VintageHive;

public static class Mind
{
    public static readonly string ApplicationVersion = typeof(HttpProxy).Assembly.GetName().Version?.ToString() ?? "NA";

    static readonly ManualResetEvent resetEvent = new(false);

    // static DnsProxy dnsProxy;

    // static Socks5Proxy socks5Proxy;

    static HttpProxy httpProxy;

    static HttpProxy httpsProxy;

    static FtpProxy ftpProxy;

    static TelnetServer telnetServer;

    static SmtpProxy smtpProxy;

    static Pop3Proxy pop3Proxy;

    static OscarServer oscarServer;

    public static bool IsDebug => Debugger.IsAttached;

    public static bool IsDocker => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    public static CacheDbContext Cache { get; private set; }

    public static HiveDbContext Db { get; private set; }

    public static GeonamesDbContext Geonames { get; private set; }

    public static PostOfficeDbContext PostOfficeDb { get; private set; }

    public static RadioBrowserDbContext RadioBrowserDB { get; private set; }

    public static RadioBrowserClient RadioBrowser { get; private set; }

    public static async Task Init()
    {
        // Files
        Resources.Initialize();
        VFS.Init();

        // Data
        Cache = new();
        Db = new();
        PostOfficeDb = new();

        // Services
        Geonames = new();
        RadioBrowserDB = new();
        RadioBrowser = new();

        await RadioBrowser.Init();

        await GeoIpUtils.CheckGeoIp();
        await ProtoWebUtils.GetSites();

        // Proxies
        var ipAddressString = Db.ConfigGet<string>(ConfigNames.IpAddress);

        var ipAddress = IPAddress.Parse(ipAddressString);

        var httpPort = Db.ConfigGet<int>(ConfigNames.PortHttp);

        httpProxy = new(ipAddress, httpPort, false);

        httpProxy
            .Use(HelperProcessor.ProcessHttpRequest)
            .Use(LocalServerProcessor.ProcessHttpRequest)
            .Use(ProtoWebProcessor.ProcessHttpRequest)
            .Use(InternetArchiveProcessor.ProcessHttpRequest);

        // var httpsPort = Db.ConfigGet<int>(ConfigNames.PortHttps); // Soon?

        httpsProxy = new(ipAddress, 9999, true);

        httpsProxy
            .Use(LocalServerProcessor.ProcessHttpsRequest)
            .Use(DialNineProcessor.ProcessHttpsRequest);

        var ftpPort = Db.ConfigGet<int>(ConfigNames.PortFtp);

        ftpProxy = new(ipAddress, ftpPort)
        {
            CacheDb = Cache
        };

        ftpProxy
            .Use(ProtoWebProcessor.ProcessFtpRequest)
            .Use(LocalServerProcessor.ProcessFtpRequest);

        var telnetPort = Db.ConfigGet<int>(ConfigNames.PortTelnet);

        telnetServer = new(ipAddress, telnetPort);

        oscarServer = new(ipAddress);

#if DEBUG

        var smtpProxyPort = Db.ConfigGet<int>(ConfigNames.PortSmtp);

        smtpProxy = new(ipAddress, smtpProxyPort);


        var pop3ProxyPort = Db.ConfigGet<int>(ConfigNames.PortPop3);

        pop3Proxy = new(ipAddress, pop3ProxyPort);

        // ==== TESTING AREA =====
        // var socks5Port = ConfigDb.SettingGet<int>(ConfigNames.PortSocks5);

        // socks5Proxy = new(ipAddress, socks5Port);
#endif
    }

    public static void Start()
    {
        httpProxy.Start();

        httpsProxy.Start();

        ftpProxy.Start();

        telnetServer.Start();

#if DEBUG
        smtpProxy.Start();

        pop3Proxy.Start();

        // socks5Proxy.Start();
#endif

        oscarServer.Start();

        resetEvent.WaitOne();
    }
}

