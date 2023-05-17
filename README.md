<img src="Statics/assets/hive.gif" height="30"> Vintage Hive By @FoxCouncil
======

This project tries to help alter the modern internet to work on really old computers and systems, with a focus on cleaning the rough edges and bringing in multiple sources of archival internet data. Browse the web like you are back in 1999, or have access to modern encrypted websites from Windows 95 (_just don't login to your bank with this!_)

# Table Of Contents

- [Installation](#installation)
- [Usage](#usage)
- [Roadmap](#roadmap)
- [Help](#help)
- [Data Sources](#sources)

# Installation

> `Windows 10/11`

- Download [Latest Release](https://github.com/FoxCouncil/VintageHive/releases/latest) ZIP package for your operating system.
- Place ZIP package in an empty folder
- Unzip package in place.
- Run the `VintageHive` executable
- Allow the service access through your firewall

> `Linux`

- Instructions Coming Soon

> `MacOS`

- Instructions Coming Soon (Not Tested Either)

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

### *More guides coming soon*

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
