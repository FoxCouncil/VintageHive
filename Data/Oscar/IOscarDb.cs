using VintageHive.Proxy.Oscar;

namespace VintageHive.Data.Oscar;

public interface IOscarDb
{
    public OscarSession GetSessionByCookie(string cookie);

    public void SetSession(OscarSession session);
}
