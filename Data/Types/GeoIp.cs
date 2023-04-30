// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics.CodeAnalysis;

namespace VintageHive.Data.Types;

[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "External Dependency Data Name Contract")]
public class GeoIp
{
    public string status { get; set; }

    public string country { get; set; }

    public string countryCode { get; set; }

    public string region { get; set; }

    public string regionName { get; set; }

    public string city { get; set; }

    public string zip { get; set; }

    public float lat { get; set; }

    public float lon { get; set; }

    public string timezone { get; set; }

    public string isp { get; set; }

    public string org { get; set; }

    public string _as { get; set; }

    public string query { get; set; }

    public string fullname => $"{city}, {region ?? regionName ?? ""}, {country}";
}
