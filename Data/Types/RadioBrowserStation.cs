// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.Json.Serialization;

namespace VintageHive.Data.Types;

public class RadioBrowserStation
{
    [JsonPropertyName("changeuuid")]
    public string Changeuuid { get; set; }

    [JsonPropertyName("stationuuid")]
    public string Stationuuid { get; set; }

    [JsonPropertyName("serveruuid")]
    public object Serveruuid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("url_resolved")]
    public string UrlResolved { get; set; }

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; }

    [JsonPropertyName("favicon")]
    public string Favicon { get; set; }

    [JsonPropertyName("tags")]
    public string Tags { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; }

    [JsonPropertyName("countrycode")]
    public string Countrycode { get; set; }

    [JsonPropertyName("iso_3166_2")]
    public object Iso31662 { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; }

    [JsonPropertyName("languagecodes")]
    public string Languagecodes { get; set; }

    [JsonPropertyName("votes")]
    public int Votes { get; set; }

    [JsonPropertyName("lastchangetime")]
    public string Lastchangetime { get; set; }

    [JsonPropertyName("lastchangetime_iso8601")]
    public DateTime LastchangetimeIso8601 { get; set; }

    [JsonPropertyName("codec")]
    public string Codec { get; set; }

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    [JsonPropertyName("hls")]
    public int Hls { get; set; }

    [JsonPropertyName("lastcheckok")]
    public int Lastcheckok { get; set; }

    [JsonPropertyName("lastchecktime")]
    public string Lastchecktime { get; set; }

    [JsonPropertyName("lastchecktime_iso8601")]
    public DateTime LastchecktimeIso8601 { get; set; }

    [JsonPropertyName("lastcheckoktime")]
    public string Lastcheckoktime { get; set; }

    [JsonPropertyName("lastcheckoktime_iso8601")]
    public DateTime LastcheckoktimeIso8601 { get; set; }

    [JsonPropertyName("lastlocalchecktime")]
    public string Lastlocalchecktime { get; set; }

    [JsonPropertyName("lastlocalchecktime_iso8601")]
    public DateTime LastlocalchecktimeIso8601 { get; set; }

    [JsonPropertyName("clicktimestamp")]
    public string Clicktimestamp { get; set; }

    [JsonPropertyName("clicktimestamp_iso8601")]
    public DateTime ClicktimestampIso8601 { get; set; }

    [JsonPropertyName("clickcount")]
    public int Clickcount { get; set; }

    [JsonPropertyName("clicktrend")]
    public int Clicktrend { get; set; }

    [JsonPropertyName("ssl_error")]
    public int SslError { get; set; }

    [JsonPropertyName("geo_lat")]
    public object GeoLat { get; set; }

    [JsonPropertyName("geo_long")]
    public object GeoLong { get; set; }

    [JsonPropertyName("has_extended_info")]
    public bool HasExtendedInfo { get; set; }
}