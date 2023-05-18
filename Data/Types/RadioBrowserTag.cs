// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.Json.Serialization;

namespace VintageHive.Data.Types;

public class RadioBrowserTag
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("stationcount")]
    public int Stationcount { get; set; }
}