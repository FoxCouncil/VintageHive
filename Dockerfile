#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
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

# Create data volume
VOLUME /app/data

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
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
