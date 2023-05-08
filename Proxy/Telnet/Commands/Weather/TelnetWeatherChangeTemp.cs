namespace VintageHive.Proxy.Telnet.Commands.Weather;

public class TelnetWeatherChangeTemp : ITelnetWindow
{
    public bool ShouldRemoveNextCommand => _shouldRemoveNextCommand;

    public string Title => "weather_change_temp";

    public string Text => _text;

    public string Description => "Change temp units for weather";

    public bool HiddenCommand => true;

    public bool AcceptsCommands => true;

    private string _text;
    private string _tempUnits;
    private string _temp;

    private TelnetSession _session;
    private bool _shouldRemoveNextCommand;

    public void OnAdd(TelnetSession session)
    {
        _session = session;

        UpdateTempData(session);
    }

    private void UpdateTempData(TelnetSession session)
    {
        _tempUnits = Mind.Db.ConfigLocalGet<string>(session.Client.RemoteIP, ConfigNames.TemperatureUnits);
        _temp = _tempUnits[..1].ToLower();

        var result = new StringBuilder();
        result.Append($"Weather Change Temperature Units\r\n");
        result.Append($"Current units: {_temp}\r\n\r\n");

        result.Append("1. Change to fahrenheit\r\n");
        result.Append("2. Change to celsius\r\n\r\n");

        result.Append("Press any other key to close and return\r\n");
        result.Append("to the weather menu...\r\n");

        _text = result.ToString();
    }

    public void Destroy() { }

    public void ProcessCommand(string command)
    {
        switch (command) 
        {
            case "1":
                Mind.Db.ConfigLocalSet(_session.Client.RemoteIP, ConfigNames.TemperatureUnits, "fahrenheit");
                break;
            case "2":
                Mind.Db.ConfigLocalSet(_session.Client.RemoteIP, ConfigNames.TemperatureUnits, "celsius");
                break;
        }

        UpdateTempData(_session);
        _shouldRemoveNextCommand = true;

        // Forcefully tell weather command to reprint itself.
        _session.WindowManager.RemoveDeadWindows();
        var weatherCommandBelowMe = _session.WindowManager.GetTopWindow() as TelnetWeatherCommand;
        weatherCommandBelowMe.WeatherMenu();
    }
}
