// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

public static class ConfigNames
{
    public const string IpAddress = "ipaddress";

    public const string AdminUsername = "adminusername";

    public const string AdminPassword = "adminpassword";

    public const string PortHttp = "porthttp";

    public const string PortHttps = "porthttps";

    public const string PortFtp = "portftp";

    // FTP passive-mode data channel. Set the address to the host's LAN IP and the port range to a small
    // published range so passive FTP works from behind NAT or inside a Docker bridge network. An empty
    // address and a 0/0 range keep the original behavior (advertise the bound IP, OS-assigned data port).
    public const string FtpPassiveAddress = "ftppassiveaddress";

    public const string FtpPassivePortMin = "ftppassiveportmin";

    public const string FtpPassivePortMax = "ftppassiveportmax";

    public const string PortTelnet = "porttelnet";

    public const string PortSocks5 = "portsocks5";

    public const string PortSmtp = "portsmtp";

    public const string PortPop3 = "portpop3";

    public const string PortUsenet = "portusenet";

    public const string PortIrc = "portirc";

    public const string PortImap = "portimap";

    public const string PortIpp = "portipp";

    public const string PortLpd = "portlpd";

    public const string PortRawPrint = "portrawprint";

    public const string PortDns = "portdns";

    public const string PortIls = "portils";

    public const string PortRas = "portras";

    public const string PortH323 = "porth323";

    public const string PortT120 = "portt120";

    public const string PortFinger = "portfinger";

    public const string PortGopher = "portgopher";

    public const string PortYahoo = "portyahoo";

    public const string PortMsn = "portmsn";

    public const string PortOscar = "portoscar";

    public const string PortMms = "portmms";

    public const string PortPna = "portpna";

    public const string ServiceDns = "servicedns";

    public const string ServiceIls = "serviceils";

    public const string ServiceRas = "serviceras";

    public const string ServiceH323 = "serviceh323";

    public const string ServiceT120 = "servicet120";

    public const string ServiceInternetArchive = "serviceinternetarchive";

    public const string ServiceInternetArchiveYear = "serviceinternetarchiveyear";

    public const string ServiceInternetArchiveWorker = "serviceinternetarchiveworker";

    public const string ServiceInternetArchiveWorkerUrl = "serviceinternetarchiveworkerurl";

    public const string ServiceIntranet = "serviceintranet";

    public const string ServiceProtoWeb = "serviceprotoweb";

    public const string ServiceDialnine = "servicedialnine";

    public const string ServiceSmtp = "servicesmtp";

    public const string ServicePop3 = "servicepop3";

    public const string ServiceUsenet = "serviceusenet";

    public const string ServiceIrc = "serviceirc";

    public const string ServiceImap = "serviceimap";

    public const string ServicePrinter = "serviceprinter";

    public const string ServiceFinger = "servicefinger";

    public const string ServiceGopher = "servicegopher";

    public const string ServiceSocks5RequireAuth = "servicesocks5requireauth";

    public const string ServiceYahoo = "serviceyahoo";

    public const string ServiceYahooPager = "serviceyahoopager";

    public const string ServiceMsn = "servicemsn";

    public const string ServiceHttp = "servicehttp";

    public const string ServiceHttps = "servicehttps";

    public const string ServiceFtp = "serviceftp";

    public const string ServiceTelnet = "servicetelnet";

    public const string ServiceSocks = "servicesocks";

    public const string ServiceOscar = "serviceoscar";

    public const string ServiceMms = "servicemms";

    public const string ServicePna = "servicepna";

    // When true, refuse to boot if any outward-reaching service is enabled (a provable no-forward posture).
    public const string ServiceWalledGarden = "servicewalledgarden";

    // When false, clients cannot self-register accounts in-band (e.g. the OSCAR/AIM registration flow);
    // an embedding host that provisions accounts externally sets this off. Default true (today's behavior).
    public const string AllowSelfRegistration = "allowselfregistration";

    // Per-service switches for the server-built rosters. True (the default everywhere) is exactly today's
    // behavior: the service hands every member the full account list. False starts each member with an
    // empty roster - still well-formed, because an absent list hangs period clients where an empty one does
    // not - so an operator whose members are strangers can require them to add each other. Deliberately
    // independent with no global switch: a public finger directory with private messenger rosters (or the
    // reverse) are both legitimate deployments. A future service that grows a server-built roster takes
    // <Service>AutoRoster; services without one (OSCAR's member-built SSI tree, channel-scoped IRC) get no
    // key, because a key that gates nothing is a key that will be believed.
    public const string MsnAutoRoster = "msnautoroster";

    public const string YahooAutoRoster = "yahooautoroster";

    // When true, MSN serves each member's own persisted FL/AL/BL/RL (built with ADD/REM, surviving
    // restarts), reverse-list adds reach the target so the client can raise its authorization prompt,
    // and presence fans out list-scoped; MsnAutoRoster is ignored. False (the default) is exactly the
    // pre-persistence behavior: server-built or empty roster per MsnAutoRoster, ack-only ADD/REM,
    // broadcast-to-all presence. No existing deployment changes behavior until an operator opts in.
    public const string MsnPersistLists = "msnpersistlists";

    // When true, Yahoo! serves each member's own persisted buddy list (built with ADDBUDDY/REMBUDDY, group
    // names preserved verbatim, surviving restarts), a fresh add of an online member pushes their presence
    // to the adder immediately, and presence fans out list-scoped; YahooAutoRoster is ignored. False (the
    // default) is exactly the pre-persistence behavior: server-built or empty roster per YahooAutoRoster,
    // ack-only ADDBUDDY/REMBUDDY, broadcast-to-all presence. No existing deployment changes behavior until
    // an operator opts in.
    public const string YahooPersistLists = "yahoopersistlists";

    // When true, period IM file transfers (MSN/AIM/Yahoo) are brokered through a server-side relay so two
    // members behind different NATs can transfer even though every client advertises a LAN address and expects
    // a direct peer connection. Default false: invitations pass through untouched, exactly as today. Messaging,
    // presence and lists are unaffected either way. The relay allocates a short-lived listener per transfer on
    // the member-reachable address from the RelayPortLow..RelayPortHigh range; the host firewall must permit
    // that range, exactly as it must for the FTP passive range.
    public const string RelayTransfers = "relaytransfers";

    public const string RelayPortLow = "relayportlow";

    public const string RelayPortHigh = "relayporthigh";

    // When false, finger's no-query answer is the banner with no online-user table (Gopher's /users reuse follows).
    public const string FingerListOnline = "fingerlistonline";

    // When true, IRC registration requires PASS with valid account credentials (nickname = screen
    // name), and nick renames are refused. Default false (today's open behavior).
    public const string IrcRequireAuthentication = "ircrequireauthentication";

    // Custom IRC MOTD text, one line per \n-separated line of the blob. Empty = built-in MOTD.
    public const string IrcMotd = "ircmotd";

    // IRC server identity: reply-prefix hostname and the version string in 002/004. Empty = built-ins.
    public const string IrcHostname = "irchostname";

    public const string IrcVersion = "ircversion";

    // ICQ service-locator URLs answered from the CLI_REQ_XML_INFO meta request. The %d/%s tokens are the
    // CLIENT's substitution placeholders and must pass through verbatim, never via a format call. Banners
    // empty = built-in api.hive.com template; DataFiles/Channels empty = the key stays unanswered and is
    // logged as unknown, which is the pre-config behavior.
    public const string IcqBannersUrl = "icqbannersurl";

    public const string IcqDataFilesUrl = "icqdatafilesurl";

    public const string IcqChannelsUrl = "icqchannelsurl";

    // Comma-separated list of mail domains this host serves; governs POP3/IMAP/SMTP login, MAIL FROM,
    // RCPT TO, and postmaster routing. First entry is the primary domain (banner and postmaster identity).
    // Every check re-reads it, so an embedding host's ConfigSet takes effect without restart.
    public const string ValidMailDomains = "validmaildomains";

    // Whitelabel: product name/version emitted in banners and page chrome; default to VintageHive's own.
    public const string ProductName = "productname";

    public const string ProductVersion = "productversion";

    // Key size for newly generated TLS certificates. Global, not per host - see CertificateAuthority.KeySize.
    public const string CertificateKeySize = "certificatekeysize";

    public const string DownloadRepos = "downloadrepos";

    public const string Location = "location";

    // The stored key is misspelled ("tempratureunits"). Deliberately NOT corrected: it is the actual column
    // value in every deployed database, so renaming it without a migration would orphan every member's saved
    // preference and silently reset them all to the default. The constant is the single point everything
    // reads through, so the misspelling is contained here. Fix it WITH a migration or not at all.
    public const string TemperatureUnits = "tempratureunits";

    public const string DistanceUnits = "distanceunits";
}
