Vintage Hive By @FoxCouncil
======

This project tries to help alter the modern internet to work on really old computers and systems, with a focus on cleaning the rough edges and bringing in multiple sources of archival internet data.

# Table Of Contents

- [Installation](#installation)
- [Usage](#usage)
- [Roadmap](#roadmap)
- [Help](#help)
- [Data Sources](#sources)

# Installation

> `Windows 10/11` _only_

- Download [Latest Release](https://github.com/FoxCouncil/VintageHive/releases/latest) ZIP package for your operating system.
- Place ZIP package in an empty folder
- Unzip package in place.
- Run the `VintageHive` executable
- Allow the service access through your firewall

# Usage

## Defaults

> Default HTTP Proxy Port `1990`

> ~~Default FTP Proxy Port `1971`~~ | **`Coming Soon`** |

Intranet & Settings
------

- Main Url: `http://hive`
- News Url: `http://hive/news`
- Weather Url: `http://hive/weather`
- Settings Url: `http://hive/settings`

Usage Guides
------

### <img src="https://docs.microsoft.com/en-us/windows/iot/iot-enterprise/kiosk-mode/media/ie11.png" alt="Internet Explore Logo" width="12"> Internet Explorer 6

<img src="https://docs.microsoft.com/en-us/troubleshoot/developer/browsers/connectivity-navigation/media/use-proxy-servers-with-ie/browser-setting-to-bypass-address.png">

- Open the Tools menu, and then select Internet Options
- Click on the Connections tab
- Select LAN Settings
- In the Local Area Network Settings dialog box, select the `Use a proxy server for your LAN` settings check box
- Input your host IP address and the port `1990` in the HTTP proxy field

### *More guides coming soon*

# Roadmap

- FTP Proxy Support
- HTTPS Proxy Support (with security downgrading SSL2)
- Emulated Services
  - ICQ
  - POP3/SMTP
  - IRC
  - MSN Messenger
  - NetMeeting
- Custom Hosted Pages
- Download Center
- Community Servers
- Gopher Support

# Help

### Q: `I can't connect to VintageHive from my guest computer`
### A: Check your hosts firewall settings, make sure both the guest and host are on the same network and subnet

# Sources

- Internet Archive
- ProtoWeb
- GoogleNews
- WeatherDB
- GeoIPService
