using System.Xml.Serialization;
using static System.Net.WebRequestMethods;

namespace VintageHive.Utilities;

public static class SCUtils
{
    const string Top500Api = "https://api.shoutcast.com/legacy/Top500?k=sh1t7hyn3Kh0jhlV&mt=audio/mpeg";

    const string StationApi = "https://yp.shoutcast.com/sbin/tunein-station.m3u?id={0}";

    const string M3uSig = "#EXTINF:-1,";

    public static async Task<SCTop500List> GetTop500()
    {
        string top500XmlRaw = await GetTop500FromCache();

        var serializer = new XmlSerializer(typeof(SCTop500List));

        using var reader = new StringReader(top500XmlRaw);

        return (SCTop500List)serializer.Deserialize(reader);
    }

    public static async Task<Tuple<string, Uri>> GetStationById(string id)
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

        return new Tuple<string, Uri>(stationName, new Uri(stationUrl));
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
    public class Station
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
    public class SCTop500List
    {
        [XmlElement(ElementName = "tunein")]
        public TuneIn Tunein { get; set; }

        [XmlElement(ElementName = "station")]
        public List<Station> Stations { get; set; }
    }

}
