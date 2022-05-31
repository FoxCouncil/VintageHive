namespace VintageHive.Data.Cache;

public interface ICacheDb
{
    public T Get<T>(string key);

    public void Set<T>(string key, TimeSpan ttl, T value);
}