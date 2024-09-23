// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Xml.Serialization;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Utilities;

public static class SCUtils
{
    const string Top500Api = "https://api.shoutcast.com/legacy/Top500?k=sh1t7hyn3Kh0jhlV";

    const string StationSearchApi = "https://api.shoutcast.com/legacy/stationsearch?k=sh1t7hyn3Kh0jhlV&limit=500&search=";

    const string StationSearchByGenreApi = "http://api.shoutcast.com/legacy/genresearch?k=sh1t7hyn3Kh0jhlV&limit=500&genre=";

    const string GenreListApi = "http://api.shoutcast.com/legacy/genrelist?k=sh1t7hyn3Kh0jhlV";

    const string StationApi = "https://yp.shoutcast.com/sbin/tunein-station.m3u?id={0}";

    const string M3uSig = "#EXTINF:-1,";

    readonly static Dictionary<string, int> genreCache = new();

    readonly static Dictionary<int, ShoutcastStation> stationCache = new();

    public static async Task<List<Genre>> GetGenres()
    {
        string genresXmlRaw = await GetGenreListFromCache();

        var serializer = new XmlSerializer(typeof(ShoutcastGenreList));

        using var reader = new StringReader(genresXmlRaw);

        var data = (ShoutcastGenreList)serializer.Deserialize(reader);

        foreach (var genre in data.Genre)
        {
            genreCache.Remove(genre.Name);
            genreCache.Add(genre.Name, genre.Count);
        }

        return data.Genre;
    }

    public static async Task<List<ShoutcastStation>> GetTop500()
    {
        string top500XmlRaw = await GetTop500FromCache();

        var serializer = new XmlSerializer(typeof(ShoutcastStationList));

        using var reader = new StringReader(top500XmlRaw);

        var data = (ShoutcastStationList)serializer.Deserialize(reader);

        foreach (var station in data.Stations)
        {
            stationCache.Remove(station.Id);

            station.Mt = GetFormatString(station.Mt);

            stationCache.Add(station.Id, station);
        }

        return data.Stations;
    }

    public static async Task<List<ShoutcastStation>> StationSearch(string query)
    {
        string stationSearchRawXml = await GetStationSearchFromCache(query);

        var serializer = new XmlSerializer(typeof(ShoutcastStationList));

        using var reader = new StringReader(stationSearchRawXml);

        var data = (ShoutcastStationList)serializer.Deserialize(reader);

        foreach (var station in data.Stations)
        {
            stationCache.Remove(station.Id);

            station.Mt = GetFormatString(station.Mt);

            stationCache.Add(station.Id, station);
        }

        return data.Stations;
    }

    public static async Task<List<ShoutcastStation>> StationSearchByGenre(string genreQuery)
    {
        string stationSearchByGenreRawXml = await GetStationSearchByGenreFromCache(genreQuery);

        var serializer = new XmlSerializer(typeof(ShoutcastStationList));

        using var reader = new StringReader(stationSearchByGenreRawXml);

        var data = (ShoutcastStationList)serializer.Deserialize(reader);

        foreach (var station in data.Stations)
        {
            stationCache.Remove(station.Id);

            station.Mt = GetFormatString(station.Mt);

            stationCache.Add(station.Id, station);
        }

        return data.Stations;
    }

    public static async Task<Tuple<ShoutcastStation, Uri>> GetStationById(string id)
    {
        var key = string.Format(StationApi, id).ToLowerInvariant();

        var stationString = Mind.Cache.GetData(key);

        if (string.IsNullOrEmpty(stationString))
        {
            stationString = await HttpClientUtils.GetHttpString(key);

            Mind.Cache.SetData(key, TimeSpan.FromHours(1), stationString);
        }

        var stationData = stationString.Split("\n", StringSplitOptions.RemoveEmptyEntries);

        var stationName = stationData.FirstOrDefault(x => x.StartsWith(M3uSig));

        if (string.IsNullOrEmpty(stationName))
        {
            stationName = "N/A";
        }
        else
        {
            stationName = stationName.Replace(M3uSig, string.Empty);
        }

        var stationUrl = stationData.LastOrDefault();

        stationUrl = stationUrl.Replace("https://", "http://");
        stationUrl = stationUrl.Replace(":443/", ":80/");

        var idInt = Convert.ToInt32(id);

        return new Tuple<ShoutcastStation, Uri>(stationCache.ContainsKey(idInt) ? stationCache[idInt] : null, new Uri(stationUrl));
    }

    public static string GetFormatString(string input)
    {
        return input.ToLower() switch
        {
            HttpContentTypeMimeType.Audio.Mpeg => "MP3",
            HttpContentTypeMimeType.Audio.Aac => "AAC",
            HttpContentTypeMimeType.Audio.Aacp => "AAC+",
            HttpContentTypeMimeType.Audio.Mp4 => "M4A",
            _ => input,
        };
    }

    private static async Task<string> GetStationSearchByGenreFromCache(string genreQuery)
    {
        var stationSearchByGenreRawXml = Mind.Cache.GetData(StationSearchByGenreApi + genreQuery);

        if (string.IsNullOrEmpty(stationSearchByGenreRawXml))
        {
            stationSearchByGenreRawXml = await HttpClientUtils.GetHttpString(StationSearchByGenreApi + genreQuery);

            Mind.Cache.SetData(StationSearchByGenreApi + genreQuery, TimeSpan.FromHours(1), stationSearchByGenreRawXml);
        }

        return stationSearchByGenreRawXml;
    }

    private static async Task<string> GetStationSearchFromCache(string query)
    {
        var stationSearchRawXml = Mind.Cache.GetData(StationSearchApi + query);

        if (string.IsNullOrEmpty(stationSearchRawXml))
        {
            stationSearchRawXml = await HttpClientUtils.GetHttpString(StationSearchApi + query);

            Mind.Cache.SetData(StationSearchApi + query, TimeSpan.FromHours(1), stationSearchRawXml);
        }

        return stationSearchRawXml;
    }

    private static async Task<string> GetGenreListFromCache()
    {
        var genreXmlRaw = Mind.Cache.GetData(GenreListApi);

        if (string.IsNullOrEmpty(genreXmlRaw))
        {
            genreXmlRaw = await HttpClientUtils.GetHttpString(GenreListApi);

            Mind.Cache.SetData(GenreListApi, TimeSpan.FromHours(24), genreXmlRaw);
        }

        return genreXmlRaw;
    }

    private static async Task<string> GetTop500FromCache()
    {
        var top500XmlRaw = Mind.Cache.GetData(Top500Api);

        if (string.IsNullOrEmpty(top500XmlRaw))
        {
            top500XmlRaw = await HttpClientUtils.GetHttpString(Top500Api);

            Mind.Cache.SetData(Top500Api, TimeSpan.FromHours(1), top500XmlRaw);
        }

        return top500XmlRaw;
    }

    [XmlRoot(ElementName = "tunein")]
    public class TuneIn
    {
        [XmlAttribute(AttributeName = "base")]
        public string Base { get; set; }

        [XmlAttribute(AttributeName = "base-m3u")]
        public string BaseM3u { get; set; }

        [XmlAttribute(AttributeName = "base-xspf")]
        public string BaseXspf { get; set; }
    }

    [XmlRoot(ElementName = "station")]
    public class ShoutcastStation
    {
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        [XmlAttribute(AttributeName = "mt")]
        public string Mt { get; set; }

        [XmlAttribute(AttributeName = "id")]
        public int Id { get; set; }

        [XmlAttribute(AttributeName = "br")]
        public int Br { get; set; }

        [XmlAttribute(AttributeName = "genre")]
        public string Genre { get; set; }

        [XmlAttribute(AttributeName = "ct")]
        public string Ct { get; set; }

        [XmlAttribute(AttributeName = "lc")]
        public int Lc { get; set; }

        [XmlAttribute(AttributeName = "genre2")]
        public string Genre2 { get; set; }

        [XmlAttribute(AttributeName = "genre3")]
        public string Genre3 { get; set; }

        [XmlAttribute(AttributeName = "genre4")]
        public string Genre4 { get; set; }

        [XmlAttribute(AttributeName = "logo")]
        public string Logo { get; set; }

        [XmlAttribute(AttributeName = "genre5")]
        public string Genre5 { get; set; }
    }

    [XmlRoot(ElementName = "stationlist")]
    public class ShoutcastStationList
    {
        [XmlElement(ElementName = "tunein")]
        public TuneIn Tunein { get; set; }

        [XmlElement(ElementName = "station")]
        public List<ShoutcastStation> Stations { get; set; }
    }

    [XmlRoot(ElementName = "genre")]
    public class Genre
    {
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        [XmlAttribute(AttributeName = "count")]
        public int Count { get; set; }
    }

    [XmlRoot(ElementName = "genrelist")]
    public class ShoutcastGenreList
    {

        [XmlElement(ElementName = "genre")]
        public List<Genre> Genre { get; set; }
    }


}
