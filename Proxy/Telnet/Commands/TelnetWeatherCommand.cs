using AngleSharp.Io;

namespace VintageHive.Proxy.Telnet.Commands;

public class TelnetWeatherCommand : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => true;

    public string Title => "weather";

    public string Text => _text;

    public string Description => "View local weather";

    public bool HiddenCommand => false;

    public bool AcceptsCommands => false;

    private string _text;

    private const string DEFAULT_LOCATION_PRIVACY = "Your Location";

    public void OnAdd(TelnetSession session)
    {
        // TODO: Let user specify a location
        string location = null;
        location = location == DEFAULT_LOCATION_PRIVACY ? null : location;

        var tempUnits = Mind.Db.ConfigLocalGet<string>(session.Client.RemoteIP, ConfigNames.TemperatureUnits);
        var distUnits = Mind.Db.ConfigLocalGet<string>(session.Client.RemoteIP, ConfigNames.DistanceUnits);

        var geoipLocation = location != null ? WeatherUtils.FindLocation(location) : Mind.Db.ConfigGet<GeoIp>(ConfigNames.Location);

        var weatherData = WeatherUtils.GetDataByGeoIp(geoipLocation, tempUnits, distUnits).Result;

        var temp = tempUnits[..1].ToLower();

        var weatherLocation = location ?? DEFAULT_LOCATION_PRIVACY;

        var weatherFullname = location == null ? DEFAULT_LOCATION_PRIVACY : geoipLocation?.fullname ?? "N/A";

        var result = new StringBuilder();
        result.Append($"{weatherData.CurrentWeather.Time}\r\n");
        result.Append($"{weatherData.CurrentWeather.Temperature}{temp}\r\n");
        result.Append($"{weatherLocation}\r\n");
        result.Append($"{weatherFullname}\r\n");

        _text = result.ToString();
    }

    public void Destroy() { }

    public void Tick() { }

    public void ProcessCommand(string command) { }
}
