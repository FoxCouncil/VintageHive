<img src="Statics/controllers/hive.com/img/hive.gif" height="30"> Vintage Hive By @FoxCouncil
======

This project tries to help alter the modern internet to work on really old computers and systems, with a focus on cleaning the rough edges and bringing in multiple sources of archival internet data. Browse the web like you are back in 1999, or have access to modern encrypted websites from Windows 95 (_just don't login to your bank with this!_)

# Table Of Contents

- [Installation](#installation)
- [Usage](#usage)
- [Roadmap](#roadmap)
- [Help](#help)
- [Data Sources](#sources)
- [Docker](#docker)

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

# Usage

## Defaults

| Service | Port | Documentation  |
|---------|------|----------------|
|    HTTP | 1990 | [**`Docs`**](#http) |
|   HTTPS | 9999 | [**`Docs`**](#https) |
|     FTP | 1971 | [**`Docs`**](#ftp) |
|  TELNET | 1969 | [**`Docs`**](#telnet) |

Intranet & Settings
------

- Main Url: `http://hive.com`
- 🧪 Shoutcast: `http://radio.hive.com/`

# HTTP

When using this proxy, it will parse any `http` request through several processors, in decending order

- [HelperProcessor.ProcessHttpRequest](https://github.com/FoxCouncil/VintageHive/blob/main/Processors/HelperProcessor.cs#L17)
- [LocalServerProcessor.ProcessHttpRequest](https://github.com/FoxCouncil/VintageHive/blob/main/Processors/LocalServerProcessor.cs#L523)
- [ProtoWebProcessor.ProcessHttpRequest](https://github.com/FoxCouncil/VintageHive/blob/main/Processors/LocalServerProcessor.cs#L80)
- [InternetArchiveProcessor.ProcessHttpRequest](https://github.com/FoxCouncil/VintageHive/blob/main/Processors/InternetArchiveProcessor.cs#L49)
- **404**

# HTTPS

When using this proxy, it will parse any `https` request, create a certificate authority and generate certificates for processing through several processors, in decending order

- [LocalServerProcessor.ProcessHttpsRequest](https://github.com/FoxCouncil/VintageHive/blob/main/Processors/LocalServerProcessor.cs#L516)
- [DialNineProcessor.ProcessHttpsRequest](https://github.com/FoxCouncil/VintageHive/blob/main/Processors/DialNineProcessor.cs#L20)
- **404**

# FTP

When using this proxy, it will parse any `ftp` request through several processors, in decending order

- [ProtoWebProcessor.ProcessFtpRequest](https://github.com/FoxCouncil/VintageHive/blob/main/Processors/ProtoWebProcessor.cs#L99)
- [LocalServerProcessor.ProcessFtpRequest](https://github.com/FoxCouncil/VintageHive/blob/main/Processors/LocalServerProcessor.cs#L80)
- **Exception**

# TELNET

This is a pure C# implementation of a Telnet server intended to allow really old machines like 386's and 80's Macs to access the same data they could using the HTTP proxy and Lynx (text only browser) or something similar. For cases where this is not possible this Telnet server seeks to provide an alternative way to view modern data through old software since Telnet clients were very popular and avaliable on every platform. Once connected type help to get a list of commands.

# Usage Guides

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

# Roadmap

- ~~FTP Proxy Support~~
- ~~HTTPS Proxy Support (with security downgrading SSL2)~~
- ~~Custom Hosted Pages~~
- ~~Download Center~~
- Emulated Services
  - ICQ - `Coming Soon`
  - AIM - `Coming Soon`
  - Yahoo! IM
  - MSN Messenger
  - POP3/SMTP
  - IRC
  - NetMeeting
  - etc
- Community Servers
- Gopher Support

# Help

### Q: `I can't connect to VintageHive from my guest computer`
### A: Check your hosts firewall settings, make sure both the guest and host are on the same network and subnet

# Sources

- [Internet Archive](https://web.archive.org/)
- [ProtoWeb](https://protoweb.org/)
- [Geonames](https://www.geonames.org/)
- [Open-Meteo](https://github.com/open-meteo/open-meteo)
- [DuckDuckGo](https://duckduckgo.com/)
- GoogleNews
- GeoIPService
