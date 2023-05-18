// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.Json.Serialization;

namespace VintageHive.Data.Types;

public class RadioBrowserCountry
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; }

    [JsonPropertyName("stationcount")]
    public int Stationcount { get; set; }
}
