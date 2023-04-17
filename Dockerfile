#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app
EXPOSE 1900-1910
EXPOSE 1971
EXPOSE 1990
EXPOSE 5190
EXPOSE 9999

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
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
ENTRYPOINT ["dotnet", "VintageHive.dll"]
