// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

public class SslCertificate
{
    public SslCertificate(string certificate, string key)
    {
        Certificate = certificate;
        Key = key;
    }

    public string Certificate { get; set; }

    public string Key { get; set; }
}
