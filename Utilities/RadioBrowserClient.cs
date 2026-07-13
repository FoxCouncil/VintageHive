// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json.Serialization;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Utilities;

public class RadioBrowserClient
{
    readonly string apiUrl;

    readonly HttpClient httpClient = new();

    public RadioBrowserClient()
    {
        apiUrl = GetRadioBrowserApiUrl();

        httpClient = new();
        httpClient.DefaultRequestHeaders.Add(HttpHeaderName.UserAgent, $"VintageHive/{Mind.ApplicationVersion}");
    }

    public async Task Init()
    {
        string rawJson = await GetRaw("countries");

        if (rawJson == null)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(RadioBrowserClient), "Countries API RadioBrowser Not Available", "");
            return;
        }

        var countries = JsonSerializer.Deserialize<List<RadioBrowserCountry>>(rawJson);
        Mind.RadioBrowserDB.CountriesLoad(countries);

        var tags = JsonSerializer.Deserialize<List<RadioBrowserTag>>(await GetRaw("tags"));
        Mind.RadioBrowserDB.TagsLoad(tags);
    }

    public List<RadioBrowserCountry> ListGetCountries(int limit = default)
    {
        return Mind.RadioBrowserDB.CountriesGet(limit);
    }

    public List<RadioBrowserTag> ListGetTags(int limit = default)
    {
        return Mind.RadioBrowserDB.TagsGet(limit);
    }

    public async Task<List<RadioBrowserStation>> StationsByCountryCodePagedAsync(string countryCode, int offset = 0, int limit = 100)
    {
        return await Mind.Cache.Do("StationsByCountry" + countryCode + offset + limit, TimeSpan.FromHours(1), async () =>
        {
            return JsonSerializer.Deserialize<List<RadioBrowserStation>>(await GetRaw($"stations/search?countrycode={countryCode}&offset={offset}&limit={limit}&order=clickcount&reverse=true&hidebroken=true"));
        });
    }

    public async Task<List<RadioBrowserStation>> StationsByTagPagedAsync(string tag, int offset = 0, int limit = 100)
    {
        return await Mind.Cache.Do("StationsByTag" + tag + offset + limit, TimeSpan.FromHours(1), async () =>
        {
            return JsonSerializer.Deserialize<List<RadioBrowserStation>>(await GetRaw($"stations/search?tag={tag}&offset={offset}&limit={limit}&order=clickcount&reverse=true&hidebroken=true"));
        });
    }

    public async Task<List<RadioBrowserStation>> StationsBySearchPagedAsync(string searchTerm, int offset = 0, int limit = 100)
    {
        return await Mind.Cache.Do("StationsBySearch" + searchTerm + offset + limit, TimeSpan.FromHours(1), async () =>
        {
            return JsonSerializer.Deserialize<List<RadioBrowserStation>>(await GetRaw($"stations/search?name={searchTerm}&offset={offset}&limit={limit}&order=clickcount&reverse=true&hidebroken=true"));
        });
    }

    public async Task<List<RadioBrowserStation>> StationsGetByClicksAsync(int limit = 100)
    {
        return await Mind.Cache.Do("StationsGetByClicks", TimeSpan.FromHours(1), async () =>
        {
            return JsonSerializer.Deserialize<List<RadioBrowserStation>>(await GetRaw($"stations/topclick/{limit}?hidebroken=true"));
        });
    }

    public async Task<RadioBrowserStation> StationGetAsync(string id)
    {
        return await Mind.Cache.Do("StationGetAsync" + id, TimeSpan.FromMinutes(10), async () =>
        {
            return JsonSerializer.Deserialize<List<RadioBrowserStation>>(await GetRaw($"stations/byuuid/{id}?hidebroken=true"), GetJsonSerializerOptions()).FirstOrDefault();
        });
    }

    public async Task<RadioBrowserStats> ServerStatsAsync()
    {
        return await Mind.Cache.Do("ServerStatsAsync", TimeSpan.FromHours(1), async () =>
        {
            return JsonSerializer.Deserialize<RadioBrowserStats>(await GetRaw($"stats"));
        });
    }

    async Task<string> GetRaw(string url)
    {
        var response = await httpClient.GetAsync($"https://{apiUrl}/json/{url}", HttpCompletionOption.ResponseHeadersRead);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
    }

    static string GetRadioBrowserApiUrl()
    {
        // Best-effort selection of the fastest radio-browser mirror. This MUST NOT crash startup: DNS may
        // be unavailable (offline / walled-garden), and Ping is not usable everywhere - a container without
        // the ping utility throws PlatformNotSupportedException, not PingException, which previously escaped
        // and killed Mind.Bootstrap. Any failure falls back to the default mirror.
        const string defaultUrl = @"nl1.api.radio-browser.info";

        try
        {
            var ips = Dns.GetHostAddresses(@"all.api.radio-browser.info", AddressFamily.InterNetwork);

            var lastRoundTripTime = long.MaxValue;

            var searchUrl = defaultUrl;

            foreach (var ipAddress in ips)
            {
                try
                {
                    var reply = new Ping().Send(ipAddress, 1000);

                    if (reply == null || reply.Status != IPStatus.Success || reply.RoundtripTime >= lastRoundTripTime)
                    {
                        continue;
                    }

                    lastRoundTripTime = reply.RoundtripTime;

                    searchUrl = ipAddress.ToString();
                }
                catch (Exception ex) when (ex is PingException or PlatformNotSupportedException or SocketException)
                {
                    // Ping unavailable or blocked for this address; keep the current best.
                }
            }

            // Get clean name
            var hostEntry = Dns.GetHostEntry(searchUrl);

            if (!string.IsNullOrEmpty(hostEntry.HostName))
            {
                searchUrl = hostEntry.HostName;
            }

            return searchUrl;
        }
        catch (Exception ex)
        {
            Log.WriteLine(Log.LEVEL_WARN, nameof(RadioBrowserClient), $"Could not probe radio-browser mirrors, using the default: {ex.Message}", "");

            return defaultUrl;
        }
    }

    public JsonSerializerOptions GetJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions();

        options.Converters.Add(new DateTimeConverterUsingDateTimeParse());

        return options;
    }

    public class DateTimeConverterUsingDateTimeParse : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Debug.Assert(typeToConvert == typeof(DateTime));

            var dateString = reader.GetString();

            return DateTime.Parse(dateString ?? string.Empty);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
