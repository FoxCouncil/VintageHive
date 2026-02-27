// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using static VintageHive.Utilities.SCUtils;

namespace VintageHive.Processors.LocalServer.Streaming;

internal record RadioStationInfo(
    string Name, string Codec, string StreamUrl,
    string Favicon = null, string Homepage = null,
    string Tags = null, string Country = null,
    string CurrentTrack = null, int Bitrate = 0);

internal static class RadioStationResolver
{
    public static async Task<RadioStationInfo> ResolveStation(string id)
    {
        if (id.Contains('-'))
        {
            var station = await Mind.RadioBrowser.StationGetAsync(id);
            return new RadioStationInfo(
                station.Name, station.Codec.ToUpperInvariant(), station.UrlResolved,
                Favicon: station.Favicon, Homepage: station.Homepage,
                Tags: station.Tags, Country: station.Country,
                Bitrate: station.Bitrate);
        }
        else
        {
            var station = await GetStationById(id);
            var codec = GetFormatString(station.Item1.Mt);
            return new RadioStationInfo(
                station.Item1.Name, codec, station.Item2.ToString(),
                Tags: station.Item1.Genre, CurrentTrack: station.Item1.Ct,
                Bitrate: station.Item1.Br);
        }
    }
}
