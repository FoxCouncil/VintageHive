// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;
using VintageHive.Data.Contexts;
using VintageHive.Processors;
using VintageHive.Proxy.Dns;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Imap;
using VintageHive.Proxy.Irc;
using VintageHive.Proxy.Mms;
using VintageHive.Proxy.NetMeeting.H225;
using VintageHive.Proxy.NetMeeting.ILS;
using VintageHive.Proxy.NetMeeting.T120;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Finger;
using VintageHive.Proxy.Pna;
using VintageHive.Proxy.Pop3;
using VintageHive.Proxy.Printer;
using VintageHive.Proxy.Smtp;
using VintageHive.Proxy.Socks;
using VintageHive.Proxy.Telnet;
using VintageHive.Proxy.Usenet;

namespace VintageHive;

public static class Mind
{
    public static readonly string ApplicationVersion = "0.4.0-alpha";

    static DnsProxy dnsProxy;

    static IlsServer ilsServer;

    static RasServer rasServer;

    static H323Server h323Server;

    static T120Server t120Server;

    static SocksProxy socksProxy;

    static HttpProxy httpProxy;

    static HttpProxy httpsProxy;

    static FtpProxy ftpProxy;

    static TelnetServer telnetServer;

    static SmtpProxy smtpProxy;

    static Pop3Proxy pop3Proxy;

    static ImapProxy imapProxy;

    static OscarServer oscarServer;

    static MmsServer mmsServer;

    static PnaServer pnaServer;

    static PrinterProxy printerProxy;

    static LpdProxy lpdProxy;

    static RawPrintProxy rawPrintProxy;

    static NntpProxy nntpProxy;

    static FingerServer fingerServer;

    static readonly DateTime StartTimeUtc = DateTime.UtcNow;

    static readonly ManualResetEvent resetEvent = new(false);

    public static TimeSpan TotalRuntime => DateTime.UtcNow - StartTimeUtc;

    public static bool IsRunning { get; private set; } = true;

    public static bool IsDebug => Debugger.IsAttached;

    public static bool IsDocker => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    public static CacheDbContext Cache { get; private set; }

    public static HiveDbContext Db { get; private set; }

    public static GeonamesDbContext Geonames { get; private set; }

    public static PostOfficeDbContext PostOfficeDb { get; private set; }

    public static PrinterDbContext PrinterDb { get; private set; }

    public static RadioBrowserDbContext RadioBrowserDB { get; private set; }

    public static RadioBrowserClient RadioBrowser { get; private set; }

    public static IrcDbContext IrcDb { get; private set; }

    internal static IrcProxy IrcServer { get; private set; }

    public static SynchronizationContext MainThread { get; } = SynchronizationContext.Current;

    public static async Task Init()
    {
        // Files
        Resources.Initialize();
        VFS.Init();

        // Data
        Cache = new();
        Db = new();
        PostOfficeDb = new();
        PrinterDb = new();
        IrcDb = new();

        // Services
        Geonames = new();
        RadioBrowserDB = new();
        RadioBrowser = new();

        // These reach out to the network; an offline or degraded-internet start must NOT crash the process
        // before any service comes up. Log-and-continue - the affected feature degrades, the rest still runs.
        try
        {
            await RadioBrowser.Init();
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(Mind), $"RadioBrowser init failed (offline?): {ex.Message}", "");
        }

        try
        {
            await GeoIpUtils.CheckGeoIp();
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(Mind), $"GeoIP check failed (offline?): {ex.Message}", "");
        }

        try
        {
            await ProtoWebUtils.GetSites();
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(Mind), $"ProtoWeb sitelist fetch failed (offline?): {ex.Message}", "");
        }

        // Periodic DB maintenance - sweep expired cache/log/request/session rows and checkpoint the WAL
        _ = Task.Run(MaintenanceLoop);

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

        var httpsPort = Db.ConfigGet<int>(ConfigNames.PortHttps);

        httpsProxy = new(ipAddress, httpsPort, true);

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

        mmsServer = new(ipAddress);

        pnaServer = new(ipAddress);

        var ircProxyPort = Db.ConfigGet<int>(ConfigNames.PortIrc);

        IrcServer = new(ipAddress, ircProxyPort);

        IrcServer.InitChannels();

        // Print services (IPP, LPD, Raw TCP)
        var ippPort = Db.ConfigGet<int>(ConfigNames.PortIpp);

        printerProxy = new PrinterProxy(ipAddress, ippPort);

        var lpdPort = Db.ConfigGet<int>(ConfigNames.PortLpd);

        lpdProxy = new LpdProxy(ipAddress, lpdPort);

        var rawPrintPort = Db.ConfigGet<int>(ConfigNames.PortRawPrint);

        rawPrintProxy = new RawPrintProxy(ipAddress, rawPrintPort);

        // Email services
        var smtpProxyPort = Db.ConfigGet<int>(ConfigNames.PortSmtp);

        smtpProxy = new(ipAddress, smtpProxyPort);

        var pop3ProxyPort = Db.ConfigGet<int>(ConfigNames.PortPop3);

        pop3Proxy = new(ipAddress, pop3ProxyPort);

        var imapProxyPort = Db.ConfigGet<int>(ConfigNames.PortImap);

        imapProxy = new(ipAddress, imapProxyPort);

        // Usenet (NNTP) service
        var nntpProxyPort = Db.ConfigGet<int>(ConfigNames.PortUsenet);

        nntpProxy = new(ipAddress, nntpProxyPort);

        // SOCKS proxy (SOCKS4/4a + SOCKS5 on single port)
        var socksPort = Db.ConfigGet<int>(ConfigNames.PortSocks5);

        socksProxy = new(ipAddress, socksPort);

        // DNS proxy (UDP, intercepts all lookups -> VintageHive IP)
        var dnsPort = Db.ConfigGet<int>(ConfigNames.PortDns);

        dnsProxy = new(ipAddress, dnsPort, ipAddress);

        // ILS directory server (LDAP for NetMeeting user registration + lookup)
        var ilsPort = Db.ConfigGet<int>(ConfigNames.PortIls);

        ilsServer = new(ipAddress, ilsPort);

        // H.225.0 RAS Gatekeeper (UDP, endpoint registration + call admission)
        var rasPort = Db.ConfigGet<int>(ConfigNames.PortRas);

        rasServer = new(ipAddress, rasPort);

        // H.225.0 Call Signaling (TCP, gatekeeper-routed call proxy)
        var h323Port = Db.ConfigGet<int>(ConfigNames.PortH323);

        h323Server = new(ipAddress, h323Port, rasServer.Registry);

        // T.120 Data Conferencing (TCP, MCS domain server for chat/whiteboard/file transfer)
        var t120Port = Db.ConfigGet<int>(ConfigNames.PortT120);

        t120Server = new(ipAddress, t120Port);

        // Finger protocol (RFC 1288, user info lookup)
        var fingerPort = Db.ConfigGet<int>(ConfigNames.PortFinger);

        fingerServer = new(ipAddress, fingerPort);
    }

    public static void Start()
    {
        // Core services - the web proxy and always-on protocol servers.
        httpProxy.Start();

        httpsProxy.Start();

        ftpProxy.Start();

        telnetServer.Start();

        socksProxy.Start();

        oscarServer.Start();

        mmsServer.Start();

        pnaServer.Start();

        // Toggleable services - gated on their config flag. Changes apply on next restart.
        StartService(ConfigNames.ServiceIrc, "IRC", () => IrcServer.Start());

        StartService(ConfigNames.ServicePrinter, "Printer (IPP/LPD/Raw)", () =>
        {
            PrintSpooler.Init();

            printerProxy.Start();
            lpdProxy.Start();
            rawPrintProxy.Start();
        });

        StartService(ConfigNames.ServiceSmtp, "SMTP", () =>
        {
            smtpProxy.StartPostmaster();

            smtpProxy.Start();
        });

        StartService(ConfigNames.ServicePop3, "POP3", () => pop3Proxy.Start());

        StartService(ConfigNames.ServiceImap, "IMAP", () => imapProxy.Start());

        StartService(ConfigNames.ServiceUsenet, "Usenet (NNTP)", () => nntpProxy.Start());

        StartService(ConfigNames.ServiceDns, "DNS", () => dnsProxy.Start());

        StartService(ConfigNames.ServiceIls, "ILS", () => ilsServer.Start());

        StartService(ConfigNames.ServiceRas, "H.225 RAS", () => rasServer.Start());

        StartService(ConfigNames.ServiceH323, "H.323", () => h323Server.Start());

        StartService(ConfigNames.ServiceT120, "T.120", () => t120Server.Start());

        StartService(ConfigNames.ServiceFinger, "Finger", () => fingerServer.Start());

        resetEvent.WaitOne();

        IsRunning = false;
    }

    static async Task MaintenanceLoop()
    {
        while (IsRunning)
        {
            await Task.Delay(TimeSpan.FromHours(1));

            try
            {
                Cache?.RunMaintenance();
                Db?.RunMaintenance();
            }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_WARN, nameof(Mind), $"Maintenance sweep failed: {ex.Message}", "");
            }
        }
    }

    static void StartService(string serviceConfigName, string displayName, Action start)
    {
        if (Db.ConfigGet<bool>(serviceConfigName))
        {
            start();
        }
        else
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(Mind), $"Service '{displayName}' is disabled via config; not starting.", string.Empty);
        }
    }
}
