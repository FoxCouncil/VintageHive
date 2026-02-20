<img src="Statics/controllers/hive.com/img/hive.gif" height="30"> Vintage Hive
======

[![Version](https://img.shields.io/badge/version-0.4.0--alpha-blue)](https://github.com/FoxCouncil/VintageHive/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)

## What Is VintageHive?

VintageHive is a retro internet proxy and service emulator that lets vintage computers browse the modern web. It intercepts HTTP, FTP, and Telnet traffic and routes it through a chain of processors — including [ProtoWeb](https://protoweb.org/) and the [Internet Archive Wayback Machine](https://web.archive.org/) — so you can browse the web like it's 1999. It also provides a full suite of period-accurate services: an intranet portal with weather and news, internet radio streaming, an AIM/ICQ-compatible chat server, a Telnet BBS, FTP hosting, and more.

Point your vintage browser's proxy settings at VintageHive and you're online.

# Table Of Contents

- [Features](#features)
- [Installation](#installation)
- [First Run](#first-run)
- [Service Ports](#service-ports)
- [Browser Setup Guides](#browser-setup-guides)
- [Docker](#docker)
- [Architecture Overview](#architecture-overview)
- [Roadmap](#roadmap)
- [Configuration](#configuration)
- [FAQ / Troubleshooting](#faq--troubleshooting)
- [Data Sources](#data-sources)
- [Contributing](#contributing)
- [License](#license)

# Features

### Web Browsing — HTTP Proxy (port 1990)

VintageHive processes every HTTP request through a multi-stage processor chain:

1. **Direct Passthrough** — A curated set of retro-friendly sites (68k.news, FrogFind, Old AltaVista, Razorback95, etc.) are fetched directly from the live web
2. **Intranet Portal** — Requests for `hive.com` subdomains are handled locally (see below)
3. **ProtoWeb** — Archived websites from [ProtoWeb.org](https://protoweb.org/) are served with a 1-year cache
4. **Internet Archive** — Anything not found on ProtoWeb is looked up in the Wayback Machine (configurable year, default 1999)
5. **404** — If nothing matches, a not-found page is returned

The proxy also includes an article reader that strips modern web clutter and renders clean HTML for vintage browsers, and a search engine powered by DuckDuckGo.

### Intranet Portal — hive.com

The built-in intranet at `http://hive.com` provides:

- **Weather** — Current conditions and forecast via Open-Meteo, with configurable location and temperature units
- **News** — Headlines from Google News across topics (Local, US, World, Tech, Science, Business, Entertainment, Sports, Health)
- **Directory** — Curated hot links to retro-friendly websites
- **ProtoWeb Browser** — Browse the ProtoWeb HTTP and FTP site archives
- **Search** — Web search powered by DuckDuckGo
- **User Accounts** — Per-user preferences and session management

### Radio — radio.hive.com

A full internet radio experience for vintage media players:

- Browse stations by **country**, **genre/tag**, or **keyword search**
- Top 100 stations, top countries, and top tags
- **Shoutcast** directory with genre browsing and search
- Streams in **MP3** and **ASF/WMA** formats with ICY metadata
- Automatic codec transcoding via FFmpeg (AAC, OGG, etc. converted to MP3)
- Generates **PLS** and **ASX** playlists for one-click playback

### Admin Panel — admin.hive.com

A web-based administration interface:

- **Dashboard** — Real-time service status and request logs
- **User Management** — Create and delete user accounts
- **Service Toggles** — Enable/disable ProtoWeb, Internet Archive, and other services
- **Internet Archive Year** — Set the Wayback Machine target year (1997–2021)
- **Cache Management** — View cache stats and clear all caches
- **Download Center** — Manage download repositories

### OSCAR / AIM / ICQ Server — port 5190

A fully functional OSCAR protocol server compatible with vintage AIM and ICQ clients:

- **Authentication** — MD5 login support
- **Buddy Lists** — Add buddies, online/offline notifications
- **Messaging** — Send and receive instant messages (ICBM)
- **Profiles** — User profiles and away messages
- **Privacy** — Permit/deny lists and visibility controls
- **ICQ Extensions** — User info queries, offline messages

### Telnet Server — port 1969

A text-mode BBS accessible from any Telnet client:

- `news` — Browse news headlines and read articles
- `weather` — Current weather with configurable location
- `riddle` — Interactive riddle game (3 attempts per riddle)
- `gallery` — ASCII art image gallery
- `help` — List all available commands

### FTP Server — port 1971

- Serves ProtoWeb FTP archives
- Local file hosting via the virtual filesystem
- Full FTP command support (LIST, RETR, STOR, CWD, MKD, DELE, PASV, etc.)
- Passive mode on ports 1900–1910

### Debug / Experimental Services

These services are functional but considered experimental:

- **Printer Proxy** (port 631) — IPP printing with PostScript-to-PDF conversion via GhostScript
- **SMTP Server** — Email sending with authentication and mail spooling
- **POP3 Server** — Email retrieval with message management
- **IRC Server** — Basic IRC with private messaging and channel support (`irc.hive.com`)

# Installation

## Windows

1. Download the latest `VintageHive-v*-win-x64.zip` from the [Releases](https://github.com/FoxCouncil/VintageHive/releases/latest) page
2. Right-click the ZIP file and select "Extract All..."
3. Choose a destination folder (e.g., `C:\VintageHive`)
4. Open the extracted folder
5. Run `VintageHive.exe`
6. When prompted by Windows Defender Firewall, click "Allow Access"
7. The server will start and be accessible at:
   - HTTP Proxy: `http://127.0.0.1:1990`
   - Admin Interface: `http://admin.hive.com:1990`

## Linux

1. Download the latest `VintageHive-v*-linux-x64.tar.gz` from the [Releases](https://github.com/FoxCouncil/VintageHive/releases/latest) page
2. Open a terminal and navigate to your download directory
3. Extract the archive:
   ```bash
   tar xzf VintageHive-v*-linux-x64.tar.gz
   cd VintageHive-v*-linux-x64
   ```
4. Make the binary executable:
   ```bash
   chmod +x VintageHive
   ```
5. Run VintageHive:
   ```bash
   ./VintageHive
   ```
6. The server will start and be accessible at:
   - HTTP Proxy: `http://127.0.0.1:1990`
   - Admin Interface: `http://admin.hive.com:1990`

Note: On Linux, you might need to run with sudo if you want to use privileged ports (< 1024)

## macOS

1. Download the latest `VintageHive-v*-osx-x64.tar.gz` from the [Releases](https://github.com/FoxCouncil/VintageHive/releases/latest) page
2. Open Terminal and navigate to your download directory
3. Extract the archive:
   ```bash
   tar xzf VintageHive-v*-osx-x64.tar.gz
   cd VintageHive-v*-osx-x64
   ```
4. Make the binary executable:
   ```bash
   chmod +x VintageHive
   ```
5. The first time you run VintageHive, macOS may block it. To allow it:
   - Try to run `./VintageHive`
   - When blocked, go to System Preferences > Security & Privacy
   - Click "Allow Anyway" for VintageHive
   - Run `./VintageHive` again and click "Open" when prompted
6. The server will start and be accessible at:
   - HTTP Proxy: `http://127.0.0.1:1990`
   - Admin Interface: `http://admin.hive.com:1990`

# First Run

On first run, VintageHive will:
1. Create necessary directories for data storage
2. Generate SSL certificates for HTTPS proxy
3. Initialize the local database
4. Start all enabled services

Visit `http://admin.hive.com:1990` to:
- Monitor proxy activity
- Configure services
- Manage certificates
- View system status

# Service Ports

| Service | Port | Protocol | Status |
|---------|------|----------|--------|
| HTTP Proxy | 1990 | HTTP | Stable |
| HTTPS Proxy | 9999 | HTTPS/SSL | Experimental |
| FTP Server | 1971 | FTP | Stable |
| Telnet Server | 1969 | Telnet | Stable |
| OSCAR (AIM/ICQ) | 5190 | OSCAR/FLAP | Stable |
| FTP Passive Range | 1900–1910 | FTP Data | Stable |
| SOCKS5 Proxy | 1996 | SOCKS5 | Experimental |
| SMTP Server | 1980 | SMTP | Debug |
| POP3 Server | 1984 | POP3 | Debug |
| IRC Server | 1988 | IRC | Debug |

# Browser Setup Guides

<img src="https://docs.microsoft.com/en-us/windows/iot/iot-enterprise/kiosk-mode/media/ie11.png" alt="Internet Explore Logo" width="12"> Internet Explorer 6
------
<img src="https://docs.microsoft.com/en-us/troubleshoot/developer/browsers/connectivity-navigation/media/use-proxy-servers-with-ie/browser-setting-to-bypass-address.png">

- Open the Tools menu, and then select Internet Options
- Click on the Connections tab
- Select LAN Settings
- In the Local Area Network Settings dialog box, select the `Use a proxy server for your LAN` settings check box
- Input your host IP address and the port `1990` in the HTTP proxy field

### <img src="Statics/controllers/hive.com/img/netscape.gif" height="16"> Netscape Navigator 3.x/4.x
1. Open `Edit > Preferences`
2. Navigate to `Advanced > Proxies`
3. Select `Manual Proxy Configuration`
4. Set:
   - HTTP Proxy: `127.0.0.1` Port: `1990`
   - Security Proxy (SSL): `127.0.0.1` Port: `9999`
   - FTP Proxy: `127.0.0.1` Port: `1971`
5. Click `OK`

### <img src="Statics/controllers/hive.com/img/ie.gif" height="16"> Internet Explorer 3.x/4.x/5.x
1. Open `View > Options` (or `Tools > Internet Options` in IE5)
2. Click `Connection` tab
3. Click `Proxy Settings` or `LAN Settings`
4. Check `Use a proxy server`
5. Click `Advanced`
6. Set:
   - HTTP: `127.0.0.1:1990`
   - Secure (HTTPS): `127.0.0.1:9999`
   - FTP: `127.0.0.1:1971`
7. Click `OK` on all dialogs

### <img src="Statics/controllers/hive.com/img/mosaic.gif" height="16"> NCSA Mosaic
1. Open `Options > Network Preferences`
2. Enable `Use Proxy Server`
3. Set:
   - HTTP Proxy: `127.0.0.1` Port: `1990`
   - FTP Proxy: `127.0.0.1` Port: `1971`
4. Click `OK`

### <img src="Statics/controllers/hive.com/img/opera.gif" height="16"> Opera 3.x/4.x
1. Open `File > Preferences`
2. Click `Network` tab
3. Click `Proxy Servers`
4. Set:
   - HTTP: `127.0.0.1:1990`
   - HTTPS: `127.0.0.1:9999`
   - FTP: `127.0.0.1:1971`
5. Click `OK`

### <img src="Statics/controllers/hive.com/img/aol.gif" height="16"> AOL Browser
1. Open `My AOL > Preferences`
2. Click `WWW` icon
3. Select `Connection` tab
4. Click `Setup`
5. Enable `Connect through proxy server`
6. Set:
   - HTTP Proxy: `127.0.0.1` Port: `1990`
7. Click `OK`

### Compatibility Notes

- **Windows 3.1/95 Users**:
  - If you can't type the `.` in `127.0.0.1`, use the numeric keypad
  - Some browsers may require `localhost` instead of `127.0.0.1`

- **SSL/HTTPS Support**:
  - IE3: Limited HTTPS support, may need additional patches
  - Netscape 3+: Full SSL support with certificate import
  - Mosaic: No SSL support, will only work with HTTP
  - Opera 3+: SSL support varies by version

- **Certificate Installation**:
  1. Visit `http://admin.hive.com:1990/ca.crt` in your browser
  2. For Netscape: Select `Accept this Certificate Authority for Certifying network sites`
  3. For IE: Click `Install Certificate` and follow the wizard
  4. For Opera: Save the file and import via `File > Preferences > Security > Certificates`

- **Known Issues**:
  - Some browsers may show certificate warnings for HTTPS sites
  - FTP passive mode may not work in older browsers
  - JavaScript support varies significantly between browsers
  - Some browsers may require system proxy settings instead of browser settings

# Docker

You can quickly run VintageHive using Docker:

```bash
docker run -d \
  --name vintagehive \
  -p 1990:1990 \
  -p 9999:9999 \
  -p 1971:1971 \
  -p 1969:1969 \
  -p 5190:5190 \
  -p 1900-1910:1900-1910 \
  -v vintagehive_data:/app/data \
  foxcouncil/vintagehive:latest
```
The container exposes the following ports:
- 1990: HTTP Proxy
- 9999: HTTPS Proxy
- 1971: FTP Server
- 1969: TELNET Server
- 5190: OSCAR (AIM/ICQ) Server
- 1900-1910: FTP Passive Mode Range

Data is persisted in the `vintagehive_data` volume.

# Architecture Overview

VintageHive is built on .NET 8 and follows a processor chain pattern for request handling.

### Processor Chain

Every incoming HTTP request passes through a chain of processors in order. The first processor that can handle a request returns a response; if none match, a 404 is returned.

```
HTTP Request → HelperProcessor → LocalServerProcessor → ProtoWebProcessor → InternetArchiveProcessor → 404
```

### Controller Routing

The `LocalServerProcessor` uses domain-based routing to dispatch requests to controllers:

| Domain | Controller |
|--------|-----------|
| `hive.com` | HiveController — portal, weather, news, search |
| `admin.hive.com` | AdminController — admin panel |
| `radio.hive.com` | RadioController — internet radio |
| `api.hive.com` | ApiController — image proxy and API |

### Template Engine

HTML pages are rendered using the [Fluid](https://github.com/sebastienros/fluid) template engine (Liquid-compatible) with custom filters for URL encoding, byte formatting, and content processing.

### Database

VintageHive uses SQLite for persistent storage, with separate database contexts for general data (`HiveDbContext`), printer jobs (`PrinterDbContext`), and caching (`CacheDbContext`).

# Roadmap

### Done
- ~~FTP Proxy Support~~
- ~~HTTPS Proxy Support (with security downgrading SSL2)~~
- ~~Custom Hosted Pages~~
- ~~Download Center~~
- ~~AIM / OSCAR Server~~
- ~~ICQ Support~~
- ~~Internet Radio (RadioBrowser + Shoutcast)~~
- ~~Telnet BBS~~
- ~~Printer Proxy (IPP)~~
- ~~POP3 / SMTP / IRC (debug)~~

### Planned
- HTTPS re-enablement and certificate improvements
- Gopher protocol support
- FTP authentication
- USENET / NNTP server
- SOCKS5 proxy improvements
- Yahoo! IM and MSN Messenger
- NetMeeting support
- Community servers

# Configuration

Most settings are managed through the **Admin Panel** at `http://admin.hive.com:1990`. Per-IP local settings allow different vintage machines on the same network to have independent preferences (location, temperature units, archive year, etc.).

Key configuration options:
- **Internet Archive Year** — Which year to fetch archived pages from (1997–2021)
- **Service Toggles** — Enable or disable ProtoWeb, Internet Archive, Intranet, SMTP, POP3, IRC
- **Temperature Units** — Celsius or Fahrenheit
- **Distance Units** — Metric or Imperial

# FAQ / Troubleshooting

### Q: I can't connect to VintageHive from my guest computer
**A:** Check your host's firewall settings. Make sure both the guest and host are on the same network and subnet. On Windows, allow VintageHive through Windows Defender Firewall.

### Q: HTTPS sites don't work or show certificate errors
**A:** HTTPS support is experimental. Visit `http://admin.hive.com:1990/ca.crt` to download and install VintageHive's CA certificate in your browser. Note that some very old browsers have limited or no SSL support.

### Q: How do I change the Internet Archive year?
**A:** Log in to the admin panel at `http://admin.hive.com:1990` and change the Internet Archive year setting. Valid years are 1997–2021.

### Q: Can I use VintageHive with real vintage hardware?
**A:** Yes! Run VintageHive on a modern machine on the same network as your vintage computer. Configure the vintage browser to use your host machine's IP address as the proxy (e.g., `192.168.1.100:1990`). Make sure your host's firewall allows incoming connections on the service ports.

### Q: Radio stations aren't playing
**A:** VintageHive requires FFmpeg for transcoding some radio streams. Make sure the appropriate FFmpeg binary is available in the application directory. On Windows, this is `ffmpeg.exe`.

### Q: How do I use AIM/ICQ?
**A:** Configure your AIM or ICQ client to connect to your VintageHive host IP on port 5190. Create a user account through the admin panel first, then log in with those credentials.

# Data Sources

- [Internet Archive / Wayback Machine](https://web.archive.org/)
- [ProtoWeb](https://protoweb.org/)
- [RadioBrowser](https://www.radio-browser.info/)
- [Shoutcast](https://directory.shoutcast.com/)
- [Geonames](https://www.geonames.org/)
- [Open-Meteo](https://open-meteo.com/)
- [DuckDuckGo](https://duckduckgo.com/)
- [Google News](https://news.google.com/)

# Contributing

### Building from Source

```bash
git clone https://github.com/FoxCouncil/VintageHive.git
cd VintageHive
dotnet build
dotnet run
```

To build in debug mode with additional logging and experimental services:
```bash
dotnet build -c Debug
dotnet run -c Debug
```

### Reporting Issues

Found a bug or have a feature request? [Open an issue](https://github.com/FoxCouncil/VintageHive/issues) on GitHub.

# License

VintageHive is released under the [MIT License](LICENSE).
