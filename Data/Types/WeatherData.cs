using System.Text.Json.Serialization;
using VintageHive.Utilities;

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
    public List<string> Time { get; } = new List<string>();

    [JsonPropertyName("weathercode")]
    public List<int> Weathercode { get; } = new List<int>();

    [JsonPropertyName("temperature_2m_max")]
    public List<double> Temperature2mMax { get; } = new List<double>();

    [JsonPropertyName("temperature_2m_min")]
    public List<double> Temperature2mMin { get; } = new List<double>();
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

}



//public class CurrentConditions
//{
//    [JsonPropertyName("dayhour")]
//    public string Dayhour { get; set; }

//    [JsonPropertyName("temp")]
//    public Temp Temp { get; set; }

//    [JsonPropertyName("precip")]
//    public string Precip { get; set; }

//    [JsonPropertyName("humidity")]
//    public string Humidity { get; set; }

//    [JsonPropertyName("wind")]
//    public Wind Wind { get; set; }

//    [JsonPropertyName("iconURL")]
//    public string IconURL { get; set; }

//    public string IconURLInternal => IconURL
//        .Replace("https://ssl.gstatic.com/onebox/weather/64/", "/img/weather/")
//        .Replace("https://ssl.gstatic.com/onebox/weather/48/", "/img/weather/")
//        .Replace(".png", ".jpg");

//    [JsonPropertyName("comment")]
//    public string Comment { get; set; }
//}

//public class NextDay
//{
//    [JsonPropertyName("day")]
//    public string Day { get; set; }

//    [JsonPropertyName("comment")]
//    public string Comment { get; set; }

//    [JsonPropertyName("max_temp")]
//    public Temp MaxTemp { get; set; }

//    [JsonPropertyName("min_temp")]
//    public Temp MinTemp { get; set; }

//    [JsonPropertyName("iconURL")]
//    public string IconURL { get; set; }

//    public string IconURLInternal => IconURL
//        .Replace("https://ssl.gstatic.com/onebox/weather/64/", "/img/weather/")
//        .Replace("https://ssl.gstatic.com/onebox/weather/48/", "/img/weather/")
//        .Replace(".png", ".jpg");
//}

//public class Temp
//{
//    [JsonPropertyName("c")]
//    public int C { get; set; }

//    [JsonPropertyName("f")]
//    public int F { get; set; }
//}

//public class Wind
//{
//    [JsonPropertyName("km")]
//    public int Km { get; set; }

//    [JsonPropertyName("mile")]
//    public int Mile { get; set; }
//}

//public class ContactAuthor
//{
//    [JsonPropertyName("email")]
//    public string Email { get; set; }

//    [JsonPropertyName("auth_note")]
//    public string AuthNote { get; set; }
//}