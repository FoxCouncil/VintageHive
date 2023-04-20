﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar;

public enum OscarSessionOnlineStatus : ushort
{
    Online,
    Away,
    DoNotDisturb,
    NotAvailable = 4,
    Occupied = 10,
    FreeToChat = 20,
    Invisible = 100
}
