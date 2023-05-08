namespace VintageHive.Proxy.Telnet.Commands.Weather;

public class TelnetWeatherCommand : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => _shouldRemoveNextCommand;

    public string Title => "weather";

    public string Text => _text;

    public string Description => "View local weather";

    public bool HiddenCommand => false;

    public bool AcceptsCommands => true;

    private string _text;

    private const string DEFAULT_LOCATION_PRIVACY = "Your Location";

    private TelnetSession _session;
    private bool _shouldRemoveNextCommand;

    private string _tempUnits;
    private string _temp;

    private string _distUnits;
    private GeoIp _geoipLocation;
    private WeatherData _weatherData;
    private string _weatherLocation;
    private string _weatherFullname;

    public void OnAdd(TelnetSession session)
    {
        _session = session;

        UpdateWeatherData();

        WeatherMenu();
    }

    public void WeatherMenu()
    {
        var weatherMenu = new StringBuilder();
        weatherMenu.Append("Weather Menu\r\n");
        weatherMenu.Append($"Weather location: {_geoipLocation.fullname}\r\n");
        weatherMenu.Append($"Temperature units: {_temp}\r\n");
        weatherMenu.Append($"Distance units: {_distUnits}\r\n\r\n");

        weatherMenu.Append("Weather Options, type a number and press enter...\r\n");
        weatherMenu.Append("1. Change temperature units\r\n");
        weatherMenu.Append("2. Change location\r\n");
        weatherMenu.Append("3. Get current conditions\r\n");
        weatherMenu.Append("4. Get daily report\r\n");
        weatherMenu.Append("5. Get hourly report\r\n");
        weatherMenu.Append("6. Close weather\r\n");

        _text = weatherMenu.ToString();
    }

    private void UpdateWeatherData()
    {
        // TODO: Let user specify a location
        string location = null;
        location = location == DEFAULT_LOCATION_PRIVACY ? null : location;

        _tempUnits = Mind.Db.ConfigLocalGet<string>(_session.Client.RemoteIP, ConfigNames.TemperatureUnits);
        _distUnits = Mind.Db.ConfigLocalGet<string>(_session.Client.RemoteIP, ConfigNames.DistanceUnits);

        _geoipLocation = location != null ? WeatherUtils.FindLocation(location) : Mind.Db.ConfigGet<GeoIp>(ConfigNames.Location);

        _weatherData = WeatherUtils.GetDataByGeoIp(_geoipLocation, _tempUnits, _distUnits).Result;

        _temp = _tempUnits[..1].ToLower();

        _weatherLocation = location ?? DEFAULT_LOCATION_PRIVACY;

        _weatherFullname = location == null ? DEFAULT_LOCATION_PRIVACY : _geoipLocation?.fullname ?? "N/A";
    }

    public void Destroy() { }

    public void Tick() { }

    public void ProcessCommand(string command)
    {
        switch (command)
        {
            case "1":
                // Change temperature units
                ChangeTempUnits();
                break;
            case "2":
                // Change location
                ChangeLocation();
                break;
            case "3":
                // Get current conditions
                GetCurrentConditions();
                break;
            case "4":
                // Get daily report
                GetDailyReport();
                break;
            case "5":
                // Get hourly report
                GetHourlyReport();
                break;
            case "6":
                // Forces weather window to close.
                _shouldRemoveNextCommand = true;
                break;
            default:
                // Default menu options
                WeatherMenu();
                break;
        }
    }

    private void GetHourlyReport()
    {
        var result = new StringBuilder();
        result.Append($"Hourly Weather Report\r\n");
        result.Append($"Press enter to return to weather menu...\r\n\r\n");

        // Build up hourly data in a form we can present in text table.
        var hourlyWeather = new List<TelnetWeatherItem>();
        for (int i = 0; i < 7; i++)
        {
            var hWeather = new TelnetWeatherItem()
            {
                CurrentTime = DateTime.Parse(_weatherData.Hourly.CurrentTime[i]).ToShortTimeString(),
                WeatherCode = WeatherUtils.ConvertWmoCodeToString(_weatherData.Hourly.CurrentWeathercode[i]),
                Temperature = $"{_weatherData.Hourly.CurrentTemperature2m[i]}{_temp}"
            };

            hourlyWeather.Add(hWeather);
        }

        var table = hourlyWeather.ToStringTable(
            new[] { "Time", "Condition", "Temperature" },
            u => u.CurrentTime,
            u => u.WeatherCode,
            u => u.Temperature
        );

        result.Append(table);

        _text = result.ToString();
    }

    private void GetDailyReport()
    {
        var result = new StringBuilder();
        result.Append($"Daily Weather Report\r\n");
        result.Append($"Press enter to return to weather menu...\r\n\r\n");

        // Build up daily data in a form we can present in text table.
        var daily = new List<TelnetWeatherItem>();
        for (int i = 0; i < 7; i++)
        {
            var hWeather = new TelnetWeatherItem()
            {
                CurrentTime = DateTime.Parse(_weatherData.Daily.Time[i]).ToShortDateString(),
                WeatherCode = WeatherUtils.ConvertWmoCodeToString(_weatherData.Daily.Weathercode[i]),
                Temperature = $"{_weatherData.Daily.Temperature2mMin[i]}{_temp} / {_weatherData.Daily.Temperature2mMax[i]}{_temp}"
            };

            daily.Add(hWeather);
        }

        var table = daily.ToStringTable(
            new[] { "Date", "Condition", "Temperature" },
            u => u.CurrentTime,
            u => u.WeatherCode,
            u => u.Temperature
        );

        result.Append(table);

        _text = result.ToString();
    }

    private void GetCurrentConditions()
    {
        var time = DateTime.Parse(_weatherData.CurrentWeather.Time);
        var imagePath = $"{VFS.StaticsPath}controllers/hive.com/img/weather/{_weatherData.CurrentWeather.Weathercode}.jpg";
        var asciiArt = string.Empty;

        if (VFS.FileExists(imagePath))
        {
            var imageData = VFS.FileReadDataAsync(imagePath).Result;
            Image<Rgba32> image = Image.Load<Rgba32>(imageData);
            asciiArt = AsciiUtils.ConvertToAsciiArt(image, 40, 20);
        }

        var result = new StringBuilder();
        result.Append($"-----[CURRENT CONDITIONS]-----\r\n");
        result.Append($"Location: {_weatherFullname}\r\n");
        result.Append($"Date: {time.ToShortDateString()}\r\n");
        result.Append($"Condition: {WeatherUtils.ConvertWmoCodeToString(_weatherData.CurrentWeather.Weathercode)}\r\n");
        result.Append($"Temperature: {_weatherData.CurrentWeather.Temperature}{_temp}\r\n");
        result.Append($"-----[ASCII ART]-----\r\n");
        result.Append(asciiArt);

        _text = result.ToString();
    }

    private void ChangeLocation()
    {
        var result = new StringBuilder();
        result.Append($"-----[CHANGE LOCATION]-----\r\n");

        _text = result.ToString();
    }

    private void ChangeDistUnits()
    {
        var result = new StringBuilder();
        result.Append($"-----[CHANGE DISTANCE UNITS]-----\r\n");

        _text = result.ToString();
    }

    private void ChangeTempUnits()
    {
        _session.ForceAddWindow("weather_change_temp");
    }
}
