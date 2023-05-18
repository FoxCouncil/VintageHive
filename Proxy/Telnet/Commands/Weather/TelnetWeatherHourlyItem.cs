// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Telnet.Commands.Weather;

public class TelnetWeatherItem
{
    public string WeatherCode { get; internal set; }
    public string CurrentTime { get; internal set; }
    public string Temperature { get; internal set; }
}
