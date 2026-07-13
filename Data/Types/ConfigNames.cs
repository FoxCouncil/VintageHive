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

    // Whitelabel: product name/version emitted in banners and page chrome; default to VintageHive's own.
    public const string ProductName = "productname";

    public const string ProductVersion = "productversion";

    public const string DownloadRepos = "downloadrepos";

    public const string Location = "location";

    public const string TemperatureUnits = "tempratureunits";

    public const string DistanceUnits = "distanceunits";
}
