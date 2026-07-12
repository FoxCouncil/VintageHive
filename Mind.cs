// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;
using VintageHive.Data.Contexts;
using VintageHive.Processors;
using VintageHive.Proxy.Dns;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Gopher;
using VintageHive.Proxy.Http;
using VintageHive.Proxy.Imap;
using VintageHive.Proxy.Irc;
using VintageHive.Proxy.Mms;
using VintageHive.Proxy.NetMeeting.H225;
using VintageHive.Proxy.NetMeeting.ILS;
using VintageHive.Proxy.NetMeeting.T120;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Finger;
using VintageHive.Proxy.Presence;
using VintageHive.Proxy.Pna;
using VintageHive.Proxy.Pop3;
using VintageHive.Proxy.Printer;
using VintageHive.Proxy.Smtp;
using VintageHive.Proxy.Socks;
using VintageHive.Proxy.Telnet;
using VintageHive.Proxy.Msn;
using VintageHive.Proxy.Usenet;
using VintageHive.Proxy.Yahoo;

namespace VintageHive;

public static class Mind
{
    public static readonly string ApplicationVersion = "0.4.0-alpha";

    // Whitelabel product identity emitted in banners and page chrome. Config-overridable, falling back to
    // VintageHive's own name/version so default output is unchanged and pre-DB (test) contexts stay safe.
    public static string ProductName
    {
        get
        {
            var configured = Db?.ConfigGet<string>(ConfigNames.ProductName);

            return string.IsNullOrEmpty(configured) ? "VintageHive" : configured;
        }
    }

    public static string ProductVersion
    {
        get
        {
            var configured = Db?.ConfigGet<string>(ConfigNames.ProductVersion);

            return string.IsNullOrEmpty(configured) ? ApplicationVersion : configured;
        }
    }

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

    static YmsgServer yahooServer;

    static MsnServer msnServer;

    static MmsServer mmsServer;

    static PnaServer pnaServer;

    static PrinterProxy printerProxy;

    static LpdProxy lpdProxy;

    static RawPrintProxy rawPrintProxy;

    static NntpProxy nntpProxy;

    static FingerServer fingerServer;

    static GopherServer gopherServer;

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

    // Network-free initialization: text encodings, resources/VFS, the database contexts, and the periodic
    // maintenance sweep. Split out from Init so an external composition root can bring up the data layer
    // without the outward warmups or the built-in service set. Idempotent.
    public static void Bootstrap()
    {
        if (Db != null)
        {
            return;
        }

        // Codepage + Mac text encodings the archive/codepage paths resolve at runtime. MacEncodingProvider
        // is internal, so an embedding host cannot register it itself; do it here.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding.RegisterProvider(new MacEncodingProvider());
        Encoding.GetEncoding("ISO-8859-1");

        // Files
        Resources.Initialize();
        VFS.Init();

        // Data
        Cache = new();
        Db = new();
        PostOfficeDb = new();
        PrinterDb = new();
        IrcDb = new();

        // Services (network-free construction; RadioBrowser.Init in WarmUp is the outward part)
        Geonames = new();
        RadioBrowserDB = new();
        RadioBrowser = new();

        // Periodic DB maintenance - sweep expired cache/log/request/session rows and checkpoint the WAL
        _ = Task.Run(MaintenanceLoop);
    }

    // Outward network warmups. Separate from Bootstrap so a walled or offline host can skip them; each is
    // log-and-continue so degraded internet never crashes startup.
    public static async Task WarmUp()
    {
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
    }

    // Refuses to boot when walled-garden mode is on and any outward-reaching service is enabled, giving a
    // provable no-forward posture. Public so an embedding composition root can assert its own config too.
    public static void AssertWalledGarden()
    {
        if (!Db.ConfigGet<bool>(ConfigNames.ServiceWalledGarden))
        {
            return;
        }

        string[] egress =
        {
            ConfigNames.ServiceHttp,     // HelperProcessor forwards a hardcoded external allowlist
            ConfigNames.ServiceProtoWeb,
            ConfigNames.ServiceInternetArchive,
            ConfigNames.ServiceDialnine,
            ConfigNames.ServiceSocks,
            ConfigNames.ServiceMms,
            ConfigNames.ServicePna,
            ConfigNames.ServiceGopher,   // live gopherspace relay
        };

        var enabled = egress.Where(k => Db.ConfigGet<bool>(k)).ToArray();

        if (enabled.Length > 0)
        {
            throw new InvalidOperationException($"Walled-garden mode is enabled but these outward-reaching services are on: {string.Join(", ", enabled)}. Disable them to boot.");
        }
    }

    public static async Task Init()
    {
        Bootstrap();

        AssertWalledGarden();

        // Outward warmups are skipped in walled mode; AssertWalledGarden guarantees no egress service is on.
        if (!Db.ConfigGet<bool>(ConfigNames.ServiceWalledGarden))
        {
            await WarmUp();
        }

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

        // Feed OSCAR presence into the shared registry so Finger (and any future consumer) sees AIM/ICQ users.
        PresenceRegistry.Register(new OscarPresenceProvider());

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

        // Gopher protocol (RFC 1436, hive portal + live gopherspace relay)
        var gopherPort = Db.ConfigGet<int>(ConfigNames.PortGopher);

        gopherServer = new(ipAddress, gopherPort);

        // Yahoo! Messenger (YMSG, login + presence + IM)
        var yahooPort = Db.ConfigGet<int>(ConfigNames.PortYahoo);

        yahooServer = new(ipAddress, yahooPort);

        PresenceRegistry.Register(new YahooPresenceProvider());

        // MSN Messenger (MSNP, notification + switchboard on one port)
        var msnPort = Db.ConfigGet<int>(ConfigNames.PortMsn);

        msnServer = new(ipAddress, msnPort);

        PresenceRegistry.Register(new MsnPresenceProvider());
    }

    public static void Start()
    {
        // Every service is gated on its config flag; changes apply on next restart. All default to on, so
        // default behavior is unchanged.
        StartService(ConfigNames.ServiceHttp, "HTTP Web Proxy", () => httpProxy.Start());

        StartService(ConfigNames.ServiceHttps, "HTTPS Proxy", () => httpsProxy.Start());

        StartService(ConfigNames.ServiceFtp, "FTP Proxy", () => ftpProxy.Start());

        StartService(ConfigNames.ServiceTelnet, "Telnet", () => telnetServer.Start());

        StartService(ConfigNames.ServiceSocks, "SOCKS", () => socksProxy.Start());

        StartService(ConfigNames.ServiceOscar, "OSCAR (AIM/ICQ)", () => oscarServer.Start());

        StartService(ConfigNames.ServiceMms, "MMS", () => mmsServer.Start());

        StartService(ConfigNames.ServicePna, "PNA", () => pnaServer.Start());

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

        StartService(ConfigNames.ServiceGopher, "Gopher", () => gopherServer.Start());

        StartService(ConfigNames.ServiceYahoo, "Yahoo! Messenger", () => yahooServer.Start());

        StartService(ConfigNames.ServiceMsn, "MSN Messenger", () => msnServer.Start());

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
