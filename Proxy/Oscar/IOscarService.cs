// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar;

public interface IOscarService
{
    public OscarServer Server { get; }

    public ushort Family { get; }

    public Task ProcessSnac(OscarSession session, Snac snac);
}
