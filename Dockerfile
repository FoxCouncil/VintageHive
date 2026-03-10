#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends libgs-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /app

# FTP PASSIVE MODE
EXPOSE 1900-1910

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

# Create data volume
VOLUME /app/data

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
# Create a non-root user
RUN useradd -m vintagehive && \
    chown -R vintagehive:vintagehive /app

USER vintagehive

ENTRYPOINT ["dotnet", "VintageHive.dll"]
