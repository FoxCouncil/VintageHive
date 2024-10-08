﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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

    private TelnetSession _session;
    private bool _shouldRemoveNextCommand;

    private string _tempUnits;
    private string _temp;

    private string _distUnits;
    private GeoIp _geoipLocation;
    private WeatherData _weatherData;
    private string _weatherFullname;

    public void OnAdd(TelnetSession session, object args = null)
    {
        _session = session;

        UpdateWeatherData();

        WeatherMenu();
    }

    private void WeatherMenu()
    {
        var weatherMenu = new StringBuilder();
        weatherMenu.Append("Weather Menu\r\n");
        weatherMenu.Append($"Weather location: {_geoipLocation.fullname}\r\n");
        weatherMenu.Append($"Temperature units: {_temp}\r\n\r\n");

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
        _tempUnits = Mind.Db.ConfigLocalGet<string>(_session.Client.RemoteIP, ConfigNames.TemperatureUnits);
        _distUnits = Mind.Db.ConfigLocalGet<string>(_session.Client.RemoteIP, ConfigNames.DistanceUnits);
        _geoipLocation = Mind.Db.ConfigGet<GeoIp>(ConfigNames.Location);

        _weatherData = WeatherUtils.GetDataByGeoIp(_geoipLocation, _tempUnits, _distUnits).Result;

        _temp = _tempUnits[..1].ToLower();

        _weatherFullname = _geoipLocation == null ? "N/A" : (_geoipLocation?.fullname);
    }

    public void Destroy() { }

    public void Refresh()
    {
        // When submenus of weather command close we just update and display the weather menu again.
        UpdateWeatherData();
        WeatherMenu();
    }

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
            _session.ForceCloseWindow(this);
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
        result.Append($"Current Weather Conditions\r\n");
        result.Append($"Press enter to return to weather menu...\r\n\r\n");

        result.Append($"Location: {_weatherFullname}\r\n");
        result.Append($"Date: {time.ToShortDateString()}\r\n");
        result.Append($"Condition: {WeatherUtils.ConvertWmoCodeToString(_weatherData.CurrentWeather.Weathercode)}\r\n");
        result.Append($"Temperature: {_weatherData.CurrentWeather.Temperature}{_temp}\r\n");
        result.Append($"Ascii Art of Weather Status\r\n");
        result.Append(asciiArt);

        _text = result.ToString();
    }

    private void ChangeLocation()
    {
        _session.ForceAddWindow("weather_change_location");
    }

    private void ChangeTempUnits()
    {
        _session.ForceAddWindow("weather_change_temp");
    }
}
