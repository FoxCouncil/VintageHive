// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar;

public enum FlapFrameType : byte
{
    SignOn = 0x01,
    Data,
    Error,
    SignOff,
    KeepAlive
}
