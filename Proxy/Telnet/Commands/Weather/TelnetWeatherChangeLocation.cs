namespace VintageHive.Proxy.Telnet.Commands.Weather;

public class TelnetWeatherChangeLocation : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => _shouldRemoveNextCommand;

    public string Title => "weather_change_location";

    public string Text => _text;

    public string Description => "Change weather target location";

    public bool HiddenCommand => true;

    public bool AcceptsCommands => true;

    private string _text;
    private string _tempUnits;
    private string _temp;
    private string _distUnits;
    private GeoIp _geoipLocation;
    private string _weatherFullname;

    private TelnetSession _session;
    private bool _shouldRemoveNextCommand;
    private GeoIp _potentialLocation;

    public void OnAdd(TelnetSession session)
    {
        _session = session;

        UpdateLocationData();
    }

    private void UpdateLocationData()
    {
        _tempUnits = Mind.Db.ConfigLocalGet<string>(_session.Client.RemoteIP, ConfigNames.TemperatureUnits);
        _distUnits = Mind.Db.ConfigLocalGet<string>(_session.Client.RemoteIP, ConfigNames.DistanceUnits);
        _temp = _tempUnits[..1].ToLower();
        _geoipLocation = Mind.Db.ConfigGet<GeoIp>(ConfigNames.Location);
        _weatherFullname = _geoipLocation == null ? "N/A" : (_geoipLocation?.fullname);

        var result = new StringBuilder();
        result.Append($"Weather Change Location\r\n");

        // No current search so show initial prompt.
        if (_potentialLocation == null)
        {
            result.Append($"Current location: {_weatherFullname}\r\n\r\n");

            result.Append($"Type location name to see suggestions,\r\n");
            result.Append($"Or type exit to return to weather menu...\r\n\r\n");
        }
        else
        {
            // Show search result and ask user to confirm if correct.
            result.Append($"Found location: {_potentialLocation.fullname}\r\n\r\n");

            result.Append($"If correct type Y otherwise search again,\r\n");
            result.Append($"Or type exit to return to weather menu...\r\n\r\n");
        }

        _text = result.ToString();
    }

    public void Destroy() { }

    public void Refresh() { }

    public void ProcessCommand(string command)
    {
        switch (command)
        {
            case "exit":
                _shouldRemoveNextCommand = true;
                _session.ForceCloseWindow(this);
                break;
            case "y":
                if (_potentialLocation != null)
                {
                    // Saves the potential location as actual location!
                    Mind.Db.ConfigSet(ConfigNames.Location, _potentialLocation);

                    // We close this window, purpose served!
                    _shouldRemoveNextCommand = true;
                    _session.ForceCloseWindow(this);
                }
                else
                {
                    // Otherwise we treat the Y as just a search query as anything else would.
                    _potentialLocation = WeatherUtils.FindLocation(command);
                }
                break;
            default:
                _potentialLocation = WeatherUtils.FindLocation(command);
                break;
        }

        UpdateLocationData();
    }
}
