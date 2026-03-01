// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Socks.Socks5;

internal enum Socks5CommandType : byte
{
    Connect = 0x01,
    Bind = 0x02,
    UdpAssociate = 0x03
}
