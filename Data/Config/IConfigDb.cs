namespace VintageHive.Data.Config;

internal interface IConfigDb
{
    public T SettingGet<T>(string key);

    public void SettingSet<T>(string key, T value);
}

