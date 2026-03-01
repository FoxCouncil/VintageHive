// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Socks.Socks5;

internal enum Socks5AddressType : byte
{
    IPv4 = 0x01,
    DomainName = 0x03,
    IPv6 = 0x04
}
