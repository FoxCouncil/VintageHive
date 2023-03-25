using System.Text.Json.Serialization;

namespace VintageHive.Data.Types;

public class WeatherData
{
    [JsonPropertyName("region")]
    public string Region { get; set; }

    [JsonPropertyName("currentConditions")]
    public CurrentConditions CurrentConditions { get; set; }

    [JsonPropertyName("next_days")]
    public List<NextDay> NextDays { get; set; }

    [JsonPropertyName("contact_author")]
    public ContactAuthor ContactAuthor { get; set; }

    [JsonPropertyName("data_source")]
    public string DataSource { get; set; }
}

public class CurrentConditions
{
    [JsonPropertyName("dayhour")]
    public string Dayhour { get; set; }

    [JsonPropertyName("temp")]
    public Temp Temp { get; set; }

    [JsonPropertyName("precip")]
    public string Precip { get; set; }

    [JsonPropertyName("humidity")]
    public string Humidity { get; set; }

    [JsonPropertyName("wind")]
    public Wind Wind { get; set; }

    [JsonPropertyName("iconURL")]
    public string IconURL { get; set; }

    public string IconURLInternal => IconURL
        .Replace("https://ssl.gstatic.com/onebox/weather/64/", "/img/weather/")
        .Replace("https://ssl.gstatic.com/onebox/weather/48/", "/img/weather/")
        .Replace(".png", ".jpg");

    [JsonPropertyName("comment")]
    public string Comment { get; set; }
}

public class NextDay
{
    [JsonPropertyName("day")]
    public string Day { get; set; }

    [JsonPropertyName("comment")]
    public string Comment { get; set; }

    [JsonPropertyName("max_temp")]
    public Temp MaxTemp { get; set; }

    [JsonPropertyName("min_temp")]
    public Temp MinTemp { get; set; }

    [JsonPropertyName("iconURL")]
    public string IconURL { get; set; }

    public string IconURLInternal => IconURL
        .Replace("https://ssl.gstatic.com/onebox/weather/64/", "/img/weather/")
        .Replace("https://ssl.gstatic.com/onebox/weather/48/", "/img/weather/")
        .Replace(".png", ".jpg");
}

public class Temp
{
    [JsonPropertyName("c")]
    public int C { get; set; }

    [JsonPropertyName("f")]
    public int F { get; set; }
}

public class Wind
{
    [JsonPropertyName("km")]
    public int Km { get; set; }

    [JsonPropertyName("mile")]
    public int Mile { get; set; }
}

public class ContactAuthor
{
    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("auth_note")]
    public string AuthNote { get; set; }
}