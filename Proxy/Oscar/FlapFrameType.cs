namespace VintageHive.Proxy.Oscar;

public enum FlapFrameType : byte
{
    SignOn = 0x01,
    Data,
    Error,
    SignOff,
    KeepAlive
}
