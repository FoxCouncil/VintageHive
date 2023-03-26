using System.Text.Json;
using VintageHive.Data.Types;

namespace VintageHive.Utilities;

public static class WeatherUtils
{
    const string WeatherDataApiUrl = "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&timezone={2}&temperature_unit={3}&windspeed_unit={4}&precipitation_unit={5}&daily=weathercode,temperature_2m_max,temperature_2m_min&current_weather=true";

    internal static async Task<WeatherData> GetDataByGeoIp(GeoIp location, string tempratureUnits, string distanceUnits)
    {
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

        return JsonSerializer.Deserialize<WeatherData>(rawData);
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
}
