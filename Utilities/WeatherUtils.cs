// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.Json;
using VintageHive.Data.Types;

namespace VintageHive.Utilities;

public static class WeatherUtils
{
    const string WeatherDataApiUrl = "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&timezone={2}&temperature_unit={3}&windspeed_unit={4}&precipitation_unit={5}&daily=weathercode,temperature_2m_max,temperature_2m_min&hourly=weathercode,temperature_2m&current_weather=true";

    internal static async Task<WeatherData> GetDataByGeoIp(GeoIp location, string tempratureUnits, string distanceUnits)
    {
        if (location == null)
        {
            return null;
        }

        var tempUnits = tempratureUnits;

        var windUnits = distanceUnits == DistanceUnits.Metric ? "kmh" : "mph";

        var percUnits = distanceUnits == DistanceUnits.Metric ? "mm" : "inch";

        var url = string.Format(WeatherDataApiUrl, location.lat, location.lon, location.timezone, tempUnits, windUnits, percUnits);

        var cacheKey = $"WEA-{url}";

        string rawData = Mind.Cache.GetWeather(cacheKey);

        if (rawData == null)
        {
            var client = HttpClientUtils.GetHttpClient();

            try
            {
                var result = await client.GetStringAsync(url);

                if (result.Contains("\"error\":true"))
                {
                    Mind.Cache.SetWeather(cacheKey, TimeSpan.FromDays(1000), result);

                    return null;
                }

                Mind.Cache.SetWeather(cacheKey, TimeSpan.FromMinutes(15), result);

                rawData = result;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }
        
        var data = JsonSerializer.Deserialize<WeatherData>(rawData);

        data.Hourly.DeltaTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(location.timezone));

        return data;
    }

    public static double CelsiusToFahrenheit(double celsius)
    {
        double fahrenheit = (celsius * 9 / 5) + 32;
        return Math.Round(fahrenheit, 1);
    }

    public static class TemperatureUnits
    {
        public const string Celsius = "celsius";

        public const string Fahrenheit = "fahrenheit";
    }

    public static class DistanceUnits
    {
        public const string Imperial = "i";

        public const string Metric = "m";
    }

    public static string ConvertWmoCodeToString(int code)
    {
        if (WeatherCodes.TryGetValue(code, out string weatherDescription))
        {
            return weatherDescription;
        }
        else
        {
            return "N/A";
        }
    }

    internal static GeoIp FindLocation(string location)
    {
        var viableLocation = Mind.Geonames.GetLocationBySearch(location);

        return viableLocation;
    }

    private static readonly Dictionary<int, string> WeatherCodes = new()
    {
        {0, "Clear sky"},
        {1, "Mainly clear"},
        {2, "Partly cloudy"},
        {3, "Overcast"},
        {45, "Fog"},
        {48, "Depositing rime fog"},
        {51, "Light drizzle"},
        {53, "Moderate drizzle"},
        {55, "Dense drizzle"},
        {56, "Light freezing drizzle"},
        {57, "Dense freezing drizzle"},
        {61, "Slight rain"},
        {63, "Moderate rain"},
        {65, "Heavy rain"},
        {66, "Light freezing rain"},
        {67, "Heavy freezing rain"},
        {71, "Slight snow fall"},
        {73, "Moderate snow fall"},
        {75, "Heavy snow fall"},
        {77, "Snow grains"},
        {80, "Slight rain showers"},
        {81, "Moderate rain showers"},
        {82, "Violent rain showers"},
        {85, "Slight snow showers"},
        {86, "Heavy snow showers"},
        {95, "Slight or moderate thunderstorm"},
        {96, "Thunderstorm with slight hail"},
        {99, "Thunderstorm with heavy hail"},
    };
}
