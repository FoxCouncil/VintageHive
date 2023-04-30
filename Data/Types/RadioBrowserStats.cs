// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.Json.Serialization;

namespace VintageHive.Data.Types;

internal class RadioBrowserStats
{
    [JsonPropertyName("supported_version")]
    public int SupportedVersion { get; set; }

    [JsonPropertyName("software_version")]
    public string SoftwareVersion { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("stations")]
    public int Stations { get; set; }

    [JsonPropertyName("stations_broken")]
    public int StationsBroken { get; set; }

    [JsonPropertyName("tags")]
    public int Tags { get; set; }

    [JsonPropertyName("clicks_last_hour")]
    public int ClicksLastHour { get; set; }

    [JsonPropertyName("clicks_last_day")]
    public int ClicksLastDay { get; set; }

    [JsonPropertyName("languages")]
    public int Languages { get; set; }

    [JsonPropertyName("countries")]
    public int Countries { get; set; }
}
