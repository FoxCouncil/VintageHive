#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
# libgs-dev: PostScript print-to-PDF (PCL is preserved raw). ffmpeg: radio stream transcoding (MP3/WMA/RealAudio).
RUN apt-get update && apt-get install -y --no-install-recommends libgs-dev ffmpeg iputils-ping && rm -rf /var/lib/apt/lists/*
WORKDIR /app

# FTP
EXPOSE 1971

# HTTP
EXPOSE 1990

# SOCKS5
EXPOSE 1996

# TELNET
EXPOSE 1969

# OSCAR (AIM/ICQ)
EXPOSE 5190

# Finger
EXPOSE 79

# Gopher
EXPOSE 70

# Yahoo! Messenger
EXPOSE 5050

# MSN Messenger
EXPOSE 1863

# HTTPS
EXPOSE 9999

# DNS
EXPOSE 1953/udp

# SMTP
EXPOSE 1980

# POP3
EXPOSE 1984

# IMAP
EXPOSE 1985

# NNTP/Usenet
EXPOSE 1986

# IRC
EXPOSE 1988

# IPP Printing
EXPOSE 631

# LPD Printing
EXPOSE 515

# Raw Print
EXPOSE 9100

# ILS (NetMeeting directory)
EXPOSE 1002

# H.225 RAS Gatekeeper
EXPOSE 1719/udp

# H.225 Call Signaling
EXPOSE 1720

# T.120 Data Conferencing
EXPOSE 1503

# MMS (Windows Media)
EXPOSE 1755

# PNA (RealPlayer)
EXPOSE 7070

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["VintageHive.csproj", "."]
RUN dotnet restore "./VintageHive.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "VintageHive.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VintageHive.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# The container uses the distro's ffmpeg (installed above via PATH); drop the bundled Windows/macOS/amd64
# binaries so the correct architecture is always used and the image isn't carrying ~240MB of dead weight.
RUN rm -f libs/ffmpeg.exe libs/ffmpeg.osx.intel libs/ffmpeg.amd64
# Create a non-root user; pre-create the VFS root so a fresh named volume mounts writable
RUN useradd -m vintagehive && \
    mkdir -p /app/vfs && \
    chown -R vintagehive:vintagehive /app

# All persistent state (SQLite DBs, mail, print jobs, downloads) lives under the VFS root.
# Declared after the chown so the volume snapshot inherits vintagehive ownership.
VOLUME /app/vfs

USER vintagehive

ENTRYPOINT ["dotnet", "VintageHive.dll"]
