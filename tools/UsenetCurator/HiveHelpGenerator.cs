// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using UsenetCurator.Sources;

namespace UsenetCurator;

/// <summary>
/// Generates the built-in alt.hive.help newsgroup content.
/// This group is always present in VintageHive's NNTP server and contains
/// help articles about using all VintageHive features.
/// </summary>
internal static class HiveHelpGenerator
{
    public const string GroupName = "alt.hive.help";

    public const string GroupDescription = "VintageHive help and documentation";

    public static List<RawArticle> Generate()
    {
        var articles = new List<RawArticle>();
        var now = new DateTimeOffset(1997, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var n = 0;

        articles.Add(MakeArticle(++n, "Welcome to VintageHive!", now.AddMinutes(n),
            "Welcome to VintageHive!\r\n" +
            "\r\n" +
            "VintageHive is a suite of network services that make vintage\r\n" +
            "computers useful on a modern network. It provides:\r\n" +
            "\r\n" +
            "  - Web Proxy      HTTP/HTTPS proxy with Internet Archive integration\r\n" +
            "  - E-Mail         SMTP, POP3, and IMAP mail server\r\n" +
            "  - IRC            Internet Relay Chat server\r\n" +
            "  - Usenet (NNTP)  News server with real historical archives\r\n" +
            "  - FTP            File server with ProtoWeb archive proxy\r\n" +
            "  - SOCKS Proxy    SOCKS4/4a/5 TCP tunneling proxy\r\n" +
            "  - DNS            Intercepts all lookups to VintageHive's IP\r\n" +
            "  - Telnet BBS     Text-mode bulletin board system\r\n" +
            "  - Printing       Network print server (IPP, LPD, Raw TCP)\r\n" +
            "\r\n" +
            "This newsgroup (alt.hive.help) contains help articles for every\r\n" +
            "VintageHive service. Browse the articles in this group for setup\r\n" +
            "instructions and tips.\r\n" +
            "\r\n" +
            "For the full help pages with screenshots and tables, visit:\r\n" +
            "  http://hive.com/help\r\n" +
            "\r\n" +
            "The admin panel is at:\r\n" +
            "  http://admin.hive.com\r\n" +
            "\r\n" +
            "Happy computing!\r\n" +
            "-- The VintageHive Team"
        ));

        articles.Add(MakeArticle(++n, "Accounts and Logins", now.AddMinutes(n),
            "VintageHive Accounts and Logins\r\n" +
            "===============================\r\n" +
            "\r\n" +
            "Create accounts at http://admin.hive.com\r\n" +
            "\r\n" +
            "USERNAME RULES:\r\n" +
            "  - 3 to 8 characters\r\n" +
            "  - Letters and numbers only\r\n" +
            "  - Case-insensitive\r\n" +
            "\r\n" +
            "WHICH SERVICES NEED A LOGIN?\r\n" +
            "\r\n" +
            "  No login required:\r\n" +
            "    - Web Browsing (HTTP/HTTPS proxy)\r\n" +
            "    - Telnet BBS\r\n" +
            "    - FTP (anonymous access)\r\n" +
            "    - Usenet (NNTP)\r\n" +
            "\r\n" +
            "  Login optional:\r\n" +
            "    - IRC (only if using a registered nickname)\r\n" +
            "\r\n" +
            "  Login required:\r\n" +
            "    - E-Mail (SMTP and POP3/IMAP)\r\n" +
            "    - AIM/ICQ (OSCAR protocol)"
        ));

        articles.Add(MakeArticle(++n, "Web Browsing - HTTP/HTTPS Proxy Setup", now.AddMinutes(n),
            "Web Browsing via VintageHive Proxy\r\n" +
            "==================================\r\n" +
            "\r\n" +
            "VintageHive acts as an HTTP/HTTPS proxy that serves archived web\r\n" +
            "pages from the Internet Archive Wayback Machine and ProtoWeb.\r\n" +
            "\r\n" +
            "PROXY SETTINGS:\r\n" +
            "  Address:    VintageHive's IP address\r\n" +
            "  HTTP Port:  1990  (default)\r\n" +
            "  HTTPS Port: 1991  (default, DialNine SSL downgrade)\r\n" +
            "\r\n" +
            "INTERNET EXPLORER 5.x / 6.x:\r\n" +
            "  1. Tools > Internet Options > Connections tab\r\n" +
            "  2. Click LAN Settings\r\n" +
            "  3. Check 'Use a proxy server'\r\n" +
            "  4. Set Address and Port to VintageHive's values\r\n" +
            "  5. Click OK\r\n" +
            "\r\n" +
            "NETSCAPE NAVIGATOR 4.x:\r\n" +
            "  1. Edit > Preferences > Advanced > Proxies\r\n" +
            "  2. Select 'Manual proxy configuration'\r\n" +
            "  3. Click View and fill in the proxy address and ports\r\n" +
            "\r\n" +
            "HOW IT WORKS:\r\n" +
            "  Pages are served from ProtoWeb first, then the Internet Archive\r\n" +
            "  Wayback Machine. The default year is 1999 but can be changed\r\n" +
            "  from 1997 to 2021 in the admin panel.\r\n" +
            "\r\n" +
            "  Some sites pass through directly: 68k.news, frogfind.com,\r\n" +
            "  razorback95.com, webtv.zone, windowsupdaterestored.com\r\n" +
            "\r\n" +
            "RECOMMENDED BROWSERS:\r\n" +
            "  Internet Explorer 5.x or 6.x, Netscape Navigator 4.x,\r\n" +
            "  Opera 3.x through 5.x"
        ));

        articles.Add(MakeArticle(++n, "E-Mail - SMTP, POP3, and IMAP Setup", now.AddMinutes(n),
            "E-Mail via VintageHive\r\n" +
            "======================\r\n" +
            "\r\n" +
            "VintageHive provides a local mail server with SMTP (sending),\r\n" +
            "POP3 (receiving), and IMAP (receiving) support.\r\n" +
            "\r\n" +
            "ACCOUNT SETTINGS:\r\n" +
            "  Incoming (POP3):  VintageHive IP, POP3 port\r\n" +
            "  Incoming (IMAP):  VintageHive IP, IMAP port\r\n" +
            "  Outgoing (SMTP):  VintageHive IP, SMTP port\r\n" +
            "  Email address:    username@hive.com\r\n" +
            "  Username/Password: Your VintageHive account credentials\r\n" +
            "\r\n" +
            "OUTLOOK EXPRESS:\r\n" +
            "  1. Tools > Accounts > Add > Mail\r\n" +
            "  2. Enter your name, then username@hive.com\r\n" +
            "  3. Set incoming mail server type (POP3 or IMAP)\r\n" +
            "  4. Set both incoming and outgoing to VintageHive's IP\r\n" +
            "  5. Enter your VintageHive username and password\r\n" +
            "\r\n" +
            "EUDORA:\r\n" +
            "  1. Tools > Options > Getting Started\r\n" +
            "  2. Set POP account to username@hive.com\r\n" +
            "  3. Set SMTP server to VintageHive's IP\r\n" +
            "\r\n" +
            "NETSCAPE MESSENGER:\r\n" +
            "  1. Edit > Preferences > Mail & Newsgroups > Mail Servers\r\n" +
            "  2. Add incoming server (POP3 or IMAP)\r\n" +
            "  3. Set outgoing server to VintageHive's IP\r\n" +
            "\r\n" +
            "NOTE: A VintageHive account is REQUIRED for email.\r\n" +
            "Create one at http://admin.hive.com"
        ));

        articles.Add(MakeArticle(++n, "IRC - Internet Relay Chat Setup", now.AddMinutes(n),
            "IRC via VintageHive\r\n" +
            "===================\r\n" +
            "\r\n" +
            "VintageHive includes a built-in IRC server.\r\n" +
            "\r\n" +
            "SERVER SETTINGS:\r\n" +
            "  Server:  VintageHive's IP address\r\n" +
            "  Port:    IRC port (see admin panel for configured port)\r\n" +
            "\r\n" +
            "DEFAULT CHANNELS:\r\n" +
            "  #hive      Main VintageHive channel\r\n" +
            "  #vintage   Vintage computing discussion\r\n" +
            "\r\n" +
            "mIRC SETUP:\r\n" +
            "  1. File > Options (or Alt+O)\r\n" +
            "  2. Under Connect > Servers, click Add\r\n" +
            "  3. Set the server address and port\r\n" +
            "  4. Set your nickname\r\n" +
            "  5. Click Connect\r\n" +
            "  6. Type: /join #hive\r\n" +
            "\r\n" +
            "PIRCH:\r\n" +
            "  1. File > New Server Entry\r\n" +
            "  2. Set server address and port\r\n" +
            "  3. Connect and /join #hive\r\n" +
            "\r\n" +
            "UNIX / COMMAND LINE:\r\n" +
            "  irc -c '#hive' nickname server port\r\n" +
            "\r\n" +
            "NOTES:\r\n" +
            "  - No login required (only needed if using a registered nick)\r\n" +
            "  - User-created channels persist while members are active\r\n" +
            "  - Standard IRC commands work: /join, /part, /msg, /nick, etc."
        ));

        articles.Add(MakeArticle(++n, "FTP - File Transfer Protocol Setup", now.AddMinutes(n),
            "FTP via VintageHive\r\n" +
            "===================\r\n" +
            "\r\n" +
            "VintageHive provides an FTP server with anonymous access.\r\n" +
            "\r\n" +
            "SERVER SETTINGS:\r\n" +
            "  Server:  VintageHive's IP address\r\n" +
            "  Port:    FTP port (see admin panel)\r\n" +
            "  Login:   Anonymous (no credentials required)\r\n" +
            "  Passive mode ports: 1900-1910\r\n" +
            "\r\n" +
            "WS_FTP / CuteFTP:\r\n" +
            "  1. Create a new connection profile\r\n" +
            "  2. Set host to VintageHive's IP\r\n" +
            "  3. Set port to VintageHive's FTP port\r\n" +
            "  4. Select Anonymous login\r\n" +
            "  5. Connect\r\n" +
            "\r\n" +
            "COMMAND LINE:\r\n" +
            "  ftp vintagehive-ip\r\n" +
            "  Name: anonymous\r\n" +
            "  Password: (anything)\r\n" +
            "\r\n" +
            "PROTOWEB FTP ARCHIVE:\r\n" +
            "  VintageHive integrates with the ProtoWeb FTP archive,\r\n" +
            "  providing access to historical FTP site mirrors from\r\n" +
            "  the 1990s and early 2000s."
        ));

        articles.Add(MakeArticle(++n, "Telnet BBS - Bulletin Board System", now.AddMinutes(n),
            "Telnet BBS via VintageHive\r\n" +
            "==========================\r\n" +
            "\r\n" +
            "VintageHive includes a text-mode BBS accessible via Telnet.\r\n" +
            "\r\n" +
            "SERVER SETTINGS:\r\n" +
            "  Server:  VintageHive's IP address\r\n" +
            "  Port:    Telnet port (see admin panel)\r\n" +
            "  Terminal: 80x24\r\n" +
            "  Login:   Not required\r\n" +
            "\r\n" +
            "WINDOWS:\r\n" +
            "  Start > Run > telnet vintagehive-ip port\r\n" +
            "\r\n" +
            "PuTTY:\r\n" +
            "  1. Set Connection type to Telnet\r\n" +
            "  2. Enter VintageHive's IP and port\r\n" +
            "  3. Click Open\r\n" +
            "\r\n" +
            "UNIX / MAC:\r\n" +
            "  telnet vintagehive-ip port\r\n" +
            "\r\n" +
            "AVAILABLE COMMANDS:\r\n" +
            "  help      Show available commands\r\n" +
            "  news      Read the latest news\r\n" +
            "  weather   Check the weather\r\n" +
            "  riddle    Get a riddle\r\n" +
            "  exit      Disconnect (also: quit)"
        ));

        articles.Add(MakeArticle(++n, "SOCKS Proxy - TCP Tunneling Setup", now.AddMinutes(n),
            "SOCKS Proxy via VintageHive\r\n" +
            "===========================\r\n" +
            "\r\n" +
            "VintageHive includes a SOCKS proxy that supports SOCKS4, SOCKS4a,\r\n" +
            "and SOCKS5 on a single port. SOCKS proxies work at the TCP level,\r\n" +
            "so any application that supports SOCKS can tunnel through VintageHive.\r\n" +
            "\r\n" +
            "SERVER SETTINGS:\r\n" +
            "  Server:  VintageHive's IP address\r\n" +
            "  Port:    SOCKS port (see admin panel)\r\n" +
            "  Auth:    None (no credentials required)\r\n" +
            "\r\n" +
            "SUPPORTED VERSIONS:\r\n" +
            "  SOCKS4   Basic TCP connect, client resolves DNS, IPv4 only\r\n" +
            "  SOCKS4a  Like SOCKS4, but the server resolves hostnames\r\n" +
            "  SOCKS5   TCP connect with server-side DNS, IPv4 and IPv6\r\n" +
            "\r\n" +
            "INTERNET EXPLORER:\r\n" +
            "  1. Tools > Internet Options > Connections > LAN Settings\r\n" +
            "  2. Check 'Use a proxy server', click Advanced\r\n" +
            "  3. In the Socks row, set Address and Port\r\n" +
            "  4. Leave HTTP/Secure/FTP rows empty for SOCKS-only mode\r\n" +
            "\r\n" +
            "NETSCAPE 4.x:\r\n" +
            "  1. Edit > Preferences > Advanced > Proxies\r\n" +
            "  2. Manual proxy configuration > View\r\n" +
            "  3. Set SOCKS Host and Port\r\n" +
            "\r\n" +
            "TRUMPET WINSOCK:\r\n" +
            "  1. File > Setup > SOCKS tab\r\n" +
            "  2. Enable SOCKS Firewall, set server and port\r\n" +
            "\r\n" +
            "SOCKSCAP / SOCKSCAP32:\r\n" +
            "  Wraps any Winsock application to use SOCKS5. Useful for\r\n" +
            "  programs without built-in SOCKS support. Set type to\r\n" +
            "  SOCKS5, no authentication, enter server and port.\r\n" +
            "\r\n" +
            "APPLICATION-LEVEL SOCKS:\r\n" +
            "  mIRC:    File > Options > Connect > Firewall (SOCKS5)\r\n" +
            "  CuteFTP: Edit > Settings > Connection > SOCKS\r\n" +
            "  WS_FTP:  Session Properties > Firewall tab\r\n" +
            "  Eudora:  Tools > Options > Advanced Network\r\n" +
            "\r\n" +
            "See http://hive.com/help/socks for detailed setup with screenshots."
        ));

        articles.Add(MakeArticle(++n, "DNS - Domain Name Configuration", now.AddMinutes(n),
            "DNS via VintageHive\r\n" +
            "===================\r\n" +
            "\r\n" +
            "VintageHive includes a DNS server that intercepts all domain\r\n" +
            "name lookups and resolves them to VintageHive's IP address.\r\n" +
            "This lets vintage computers find VintageHive services by\r\n" +
            "hostname without a real DNS server or hosts file entries.\r\n" +
            "\r\n" +
            "SERVER SETTINGS:\r\n" +
            "  Server:    VintageHive's IP address\r\n" +
            "  Port:      DNS port (see admin panel, default 1953)\r\n" +
            "  Protocol:  UDP\r\n" +
            "\r\n" +
            "HOW IT WORKS:\r\n" +
            "  Every A record query returns VintageHive's IP. The vintage\r\n" +
            "  computer connects to VintageHive, which proxies the request\r\n" +
            "  through its HTTP proxy, FTP proxy, SOCKS proxy, etc.\r\n" +
            "\r\n" +
            "  Non-A queries (AAAA, MX, CNAME, etc.) return an empty\r\n" +
            "  response. Only A records are needed for vintage networking.\r\n" +
            "\r\n" +
            "WINDOWS 95/98:\r\n" +
            "  1. Control Panel > Network\r\n" +
            "  2. Select TCP/IP for your adapter, click Properties\r\n" +
            "  3. DNS Configuration tab > Enable DNS\r\n" +
            "  4. Add VintageHive's IP under DNS Server Search Order\r\n" +
            "\r\n" +
            "WINDOWS 2000/XP:\r\n" +
            "  1. Control Panel > Network Connections\r\n" +
            "  2. Right-click connection > Properties\r\n" +
            "  3. Internet Protocol (TCP/IP) > Properties\r\n" +
            "  4. Set Preferred DNS server to VintageHive's IP\r\n" +
            "\r\n" +
            "MAC OS CLASSIC:\r\n" +
            "  1. Control Panels > TCP/IP\r\n" +
            "  2. Set Name server addr. to VintageHive's IP\r\n" +
            "\r\n" +
            "UNIX / LINUX:\r\n" +
            "  Edit /etc/resolv.conf:\r\n" +
            "    nameserver <VintageHive IP>\r\n" +
            "\r\n" +
            "NOTE ON PORT:\r\n" +
            "  Standard DNS uses port 53. VintageHive defaults to port 1953\r\n" +
            "  to avoid requiring administrator/root privileges. Most vintage\r\n" +
            "  operating systems only support port 53, so you may need to\r\n" +
            "  change the port in VintageHive's admin panel or use port\r\n" +
            "  forwarding.\r\n" +
            "\r\n" +
            "TESTING:\r\n" +
            "  nslookup www.yahoo.com <VintageHive IP>\r\n" +
            "  dig @<VintageHive IP> -p <port> www.yahoo.com A\r\n" +
            "\r\n" +
            "See http://hive.com/help/dns for detailed setup instructions."
        ));

        articles.Add(MakeArticle(++n, "Printing - Network Print Server", now.AddMinutes(n),
            "Network Printing via VintageHive\r\n" +
            "================================\r\n" +
            "\r\n" +
            "VintageHive captures print jobs from vintage computers and\r\n" +
            "converts them to PDF files.\r\n" +
            "\r\n" +
            "SUPPORTED PROTOCOLS:\r\n" +
            "  IPP (Internet Printing Protocol)\r\n" +
            "  LPD (Line Printer Daemon)\r\n" +
            "  Raw TCP / JetDirect (port 9100 style)\r\n" +
            "\r\n" +
            "FORMAT DETECTION:\r\n" +
            "  VintageHive automatically detects the print format:\r\n" +
            "  - PostScript\r\n" +
            "  - ESC/P (Epson)\r\n" +
            "  - IBM ProPrinter\r\n" +
            "  - PCL (HP LaserJet)\r\n" +
            "  - Plain text\r\n" +
            "  All formats are converted to PDF.\r\n" +
            "\r\n" +
            "RECOMMENDED PRINTER DRIVERS:\r\n" +
            "  DOS text printing:   Epson FX-80 or IBM ProPrinter\r\n" +
            "  Windows GUI apps:    Apple LaserWriter or HP LaserJet PS\r\n" +
            "  Mac OS Classic:      LaserWriter 8\r\n" +
            "  Mac OS X / Linux:    Generic PostScript\r\n" +
            "\r\n" +
            "SETUP VARIES BY OS:\r\n" +
            "  - DOS: redirect LPT1 to network using NET USE\r\n" +
            "  - Windows 95/98: Add Printer > Network, set the port\r\n" +
            "  - Windows 2000/XP: Add Printer > TCP/IP port\r\n" +
            "  - Mac OS Classic: Chooser > LaserWriter 8\r\n" +
            "  - Linux/Unix: CUPS with IPP backend\r\n" +
            "\r\n" +
            "See http://hive.com/help/printing for detailed per-OS steps."
        ));

        articles.Add(MakeArticle(++n, "Usenet (NNTP) - Newsreader Setup", now.AddMinutes(n),
            "Usenet via VintageHive\r\n" +
            "======================\r\n" +
            "\r\n" +
            "VintageHive includes an NNTP news server with real historical\r\n" +
            "Usenet posts from the 1980s through early 2000s.\r\n" +
            "\r\n" +
            "SERVER SETTINGS:\r\n" +
            "  Server:  VintageHive's IP address\r\n" +
            "  Port:    NNTP port (see admin panel)\r\n" +
            "  Login:   Not required (any credentials accepted)\r\n" +
            "\r\n" +
            "OUTLOOK EXPRESS:\r\n" +
            "  1. Tools > Accounts > Add > News\r\n" +
            "  2. Enter display name and email\r\n" +
            "  3. Set server to VintageHive's IP\r\n" +
            "  4. Finish, then download newsgroups when prompted\r\n" +
            "  5. For custom port: account Properties > Advanced tab\r\n" +
            "\r\n" +
            "FORTE AGENT / FREE AGENT:\r\n" +
            "  1. Options > General Preferences > System tab\r\n" +
            "  2. Set News Server to VintageHive's IP\r\n" +
            "  3. Options > News Server Properties for port\r\n" +
            "  4. F5 to refresh groups\r\n" +
            "\r\n" +
            "NETSCAPE NEWS:\r\n" +
            "  1. Edit > Preferences > Mail & Newsgroups > Newsgroup Servers\r\n" +
            "  2. Add VintageHive's IP and port\r\n" +
            "\r\n" +
            "WANT MORE GROUPS?\r\n" +
            "  Run UsenetCurator.exe (ships alongside VintageHive.exe)\r\n" +
            "  to download additional archives from the Internet Archive.\r\n" +
            "  See the 'How to Fetch Real Usenet Archives' article."
        ));

        articles.Add(MakeArticle(++n, "How to Fetch Real Usenet Archives", now.AddMinutes(n),
            "Fetching Additional Usenet Archives\r\n" +
            "====================================\r\n" +
            "\r\n" +
            "VintageHive ships with a companion tool called UsenetCurator\r\n" +
            "that downloads real historical Usenet posts from the Internet\r\n" +
            "Archive (archive.org) and prepares them for browsing.\r\n" +
            "\r\n" +
            "QUICK START\r\n" +
            "===========\r\n" +
            "\r\n" +
            "1. Find UsenetCurator.exe in the same folder as VintageHive.exe\r\n" +
            "\r\n" +
            "2. Run it from the command line:\r\n" +
            "\r\n" +
            "       UsenetCurator.exe\r\n" +
            "\r\n" +
            "   This downloads archives for all configured groups and places\r\n" +
            "   the data in the data\\usenet\\ folder next to VintageHive.exe.\r\n" +
            "\r\n" +
            "3. Restart VintageHive. The new groups will appear automatically.\r\n" +
            "\r\n" +
            "OPTIONS\r\n" +
            "=======\r\n" +
            "\r\n" +
            "  --output <path>       Where to write the output files\r\n" +
            "                        (default: data\\usenet\\)\r\n" +
            "\r\n" +
            "  --groups <list>       Comma-separated list of specific groups\r\n" +
            "                        to download (default: all configured)\r\n" +
            "\r\n" +
            "  --max-per-group <n>   Maximum articles per group\r\n" +
            "\r\n" +
            "NOTE: Downloads can be large (50-200 MB per group archive).\r\n" +
            "Downloaded files are cached, so subsequent runs are faster.\r\n" +
            "The final output is much smaller (compressed JSON)."
        ));

        articles.Add(MakeArticle(++n, "Client Settings - SSL Certificates", now.AddMinutes(n),
            "SSL Certificate Installation\r\n" +
            "============================\r\n" +
            "\r\n" +
            "VintageHive uses a custom CA certificate to handle HTTPS\r\n" +
            "connections through its DialNine SSL proxy. Installing the\r\n" +
            "certificate eliminates browser security warnings.\r\n" +
            "\r\n" +
            "DOWNLOAD THE CERTIFICATE:\r\n" +
            "  http://admin.hive.com/ca.crt\r\n" +
            "\r\n" +
            "INTERNET EXPLORER:\r\n" +
            "  1. Download ca.crt and double-click it\r\n" +
            "  2. Click Install Certificate\r\n" +
            "  3. Place in 'Trusted Root Certification Authorities'\r\n" +
            "\r\n" +
            "NETSCAPE NAVIGATOR 4.x:\r\n" +
            "  1. Navigate to http://admin.hive.com/ca.crt\r\n" +
            "  2. Netscape will prompt to accept the certificate\r\n" +
            "  3. Check all trust options and click OK\r\n" +
            "\r\n" +
            "OPERA:\r\n" +
            "  1. Download ca.crt\r\n" +
            "  2. File > Preferences > Security > Certificates\r\n" +
            "  3. Import the certificate\r\n" +
            "\r\n" +
            "NOTE: NCSA Mosaic and some very old browsers do not support\r\n" +
            "SSL at all. They will work fine for HTTP-only browsing."
        ));

        articles.Add(MakeArticle(++n, "About the Usenet Archives", now.AddMinutes(n),
            "About the Usenet Archives\r\n" +
            "=========================\r\n" +
            "\r\n" +
            "The articles in VintageHive's newsgroups are real historical\r\n" +
            "Usenet posts from the 1980s through the early 2000s.\r\n" +
            "\r\n" +
            "DATA SOURCE\r\n" +
            "===========\r\n" +
            "\r\n" +
            "Articles are sourced from the Internet Archive's Usenet\r\n" +
            "collections (archive.org). These are public archives of\r\n" +
            "historical Usenet posts preserved for posterity.\r\n" +
            "\r\n" +
            "WHAT YOU'LL FIND\r\n" +
            "================\r\n" +
            "\r\n" +
            "  comp.*   - Computer science, programming, hardware\r\n" +
            "  rec.*    - Recreation, games, movies, music\r\n" +
            "  alt.*    - Alternative topics, folklore, BBS culture\r\n" +
            "  sci.*    - Science, space, electronics\r\n" +
            "  news.*   - Usenet meta-discussions\r\n" +
            "  misc.*   - Miscellaneous (classifieds, etc.)\r\n" +
            "  soc.*    - Society and culture\r\n" +
            "  talk.*   - The bizarre and unusual\r\n" +
            "\r\n" +
            "This is read-only. Posting is not supported."
        ));

        articles.Add(MakeArticle(++n, "Frequently Asked Questions", now.AddMinutes(n),
            "VintageHive FAQ\r\n" +
            "===============\r\n" +
            "\r\n" +
            "Q: What is VintageHive?\r\n" +
            "A: A suite of network services that make vintage computers\r\n" +
            "   useful on modern networks. It provides web proxy, email,\r\n" +
            "   IRC, Usenet, FTP, SOCKS proxy, Telnet BBS, and printing.\r\n" +
            "\r\n" +
            "Q: Do I need a username and password?\r\n" +
            "A: Only for email and AIM/ICQ. Most services work without\r\n" +
            "   authentication. Create an account at http://admin.hive.com\r\n" +
            "\r\n" +
            "Q: What web pages can I browse?\r\n" +
            "A: Pages from the Internet Archive Wayback Machine and ProtoWeb.\r\n" +
            "   The default year is 1999 but you can change it in the admin\r\n" +
            "   panel (1997-2021).\r\n" +
            "\r\n" +
            "Q: Can I post to Usenet?\r\n" +
            "A: No. VintageHive's NNTP server is read-only.\r\n" +
            "\r\n" +
            "Q: Why do I only see alt.hive.help in my newsreader?\r\n" +
            "A: Run UsenetCurator.exe to download real Usenet archives.\r\n" +
            "\r\n" +
            "Q: Can I send real email to the internet?\r\n" +
            "A: Email is local to VintageHive. You can send between\r\n" +
            "   VintageHive users at username@hive.com.\r\n" +
            "\r\n" +
            "Q: What browsers work best?\r\n" +
            "A: Internet Explorer 5.x/6.x, Netscape Navigator 4.x,\r\n" +
            "   and Opera 3.x-5.x work best with the web proxy.\r\n" +
            "\r\n" +
            "Q: How do I print from my vintage computer?\r\n" +
            "A: VintageHive captures print jobs via IPP, LPD, or Raw TCP\r\n" +
            "   and converts them to PDF. Use a PostScript or ESC/P driver."
        ));

        return articles;
    }

    private static RawArticle MakeArticle(int number, string subject, DateTimeOffset date, string body)
    {
        return new RawArticle
        {
            MessageId = $"<hive-help-{number}@vintagehive.local>",
            From = "VintageHive <help@vintagehive.local>",
            Subject = subject,
            Date = date.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture),
            Newsgroups = GroupName,
            References = "",
            Body = body,
            ParsedDate = date,
        };
    }
}
