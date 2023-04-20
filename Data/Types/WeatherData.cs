// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.Json.Serialization;

namespace VintageHive.Data.Types;

public class CurrentWeather
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("windspeed")]
    public double Windspeed { get; set; }

    [JsonPropertyName("winddirection")]
    public double Winddirection { get; set; }

    [JsonPropertyName("weathercode")]
    public int Weathercode { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; }
}

public class Daily
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; }

    [JsonPropertyName("weathercode")]
    public List<int> Weathercode { get; set; }

    [JsonPropertyName("temperature_2m_max")]
    public List<double> Temperature2mMax { get; set; }

    [JsonPropertyName("temperature_2m_min")]
    public List<double> Temperature2mMin { get; set; }
}

public class DailyUnits
{
    [JsonPropertyName("time")]
    public string Time { get; set; }

    [JsonPropertyName("weathercode")]
    public string Weathercode { get; set; }

    [JsonPropertyName("temperature_2m_max")]
    public string Temperature2mMax { get; set; }

    [JsonPropertyName("temperature_2m_min")]
    public string Temperature2mMin { get; set; }
}
public class Hourly
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; }

    [JsonPropertyName("weathercode")]
    public List<int> Weathercode { get; set; }

    [JsonPropertyName("temperature_2m")]
    public List<double> Temperature2m { get; set; }

    public List<string> CurrentTime => Time.Skip(Time.IndexOf(DeltaTime.ToString("yyyy-MM-ddTHH:00"))).Take(8).ToList();

    public List<int> CurrentWeathercode => Weathercode.Skip(Time.IndexOf(DeltaTime.ToString("yyyy-MM-ddTHH:00"))).Take(8).ToList();

    public List<double> CurrentTemperature2m => Temperature2m.Skip(Time.IndexOf(DeltaTime.ToString("yyyy-MM-ddTHH:00"))).Take(8).ToList();

    public DateTime DeltaTime { get; set; }
}

public class HourlyUnits
{
    [JsonPropertyName("time")]
    public string Time { get; set; }

    [JsonPropertyName("weathercode")]
    public string Weathercode { get; set; }

    [JsonPropertyName("temperature_2m")]
    public string Temperature2m { get; set; }
}

public class WeatherData
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("generationtime_ms")]
    public double GenerationtimeMs { get; set; }

    [JsonPropertyName("utc_offset_seconds")]
    public int UtcOffsetSeconds { get; set; }

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; }

    [JsonPropertyName("timezone_abbreviation")]
    public string TimezoneAbbreviation { get; set; }

    [JsonPropertyName("elevation")]
    public double Elevation { get; set; }

    [JsonPropertyName("current_weather")]
    public CurrentWeather CurrentWeather { get; set; }

    [JsonPropertyName("daily_units")]
    public DailyUnits DailyUnits { get; set; }

    [JsonPropertyName("daily")]
    public Daily Daily { get; set; }

    [JsonPropertyName("hourly_units")]
    public HourlyUnits HourlyUnits { get; set; }

    [JsonPropertyName("hourly")]
    public Hourly Hourly { get; set; }

    public string TempUnits => HourlyUnits.Temperature2m;
}