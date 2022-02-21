using LibFoxyProxy.Http;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using VintageHive.Processors;

namespace VintageHive;

internal class Mind
{
    const string ConfigFilePath = "config.json";

    const string GeoIPApiUri = "https://freegeoip.app/json/";

    internal static Config Config;

    ManualResetEvent _resetEvent = new(false);

    IPAddress _ip;

    HttpProxy _httpProxy;

    public Mind()
    {
        ProcessConfiguration();

        _httpProxy = new(_ip, Config.PortHttp);

        _httpProxy.Request += HttpProcessor.ProcessRequest;
    }

    private void ProcessConfiguration()
    {
        if (File.Exists(ConfigFilePath))
        {
            // _config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigFilePath));
        }
        
        if (Config == null)
        {
            Config = new Config();

            //using var httpClient = new HttpClient();            

            //var geoIpDataRaw = Task.Run(() => httpClient.GetStringAsync(GeoIPApiUri)).Result;

            //var geoIpData = JsonNode.Parse(geoIpDataRaw);

            //Config.PublicIPAddress = geoIpData["ip"].ToString();

            //Config.CountryCode = geoIpData["country_code"].ToString();

            //Config.RegionCode = geoIpData["region_code"].ToString();

            //Config.City = geoIpData["city"].ToString();

            //Config.PostalCode = geoIpData["zip_code"].ToString();

            //Config.Timezone = geoIpData["time_zone"].ToString();

            //Config.Latitude = (double)geoIpData["latitude"];

            //Config.Longitude = (double)geoIpData["logitude"];

            var jsonString = JsonSerializer.Serialize(Config, new JsonSerializerOptions() { WriteIndented = true });

            File.WriteAllText(ConfigFilePath, jsonString);
        }

        _ip = IPAddress.Parse(Config.IpAddress);
    }

    internal void Start()
    {
        _httpProxy.Start();

        _resetEvent.WaitOne();
    }
}

