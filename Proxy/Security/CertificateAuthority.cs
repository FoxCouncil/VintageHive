using VintageHive.Data.Config;

namespace VintageHive.Proxy.Security;

internal static class CertificateAuthority
{
    const string CA_PRIVATE_KEY = "CA_PRIVATE_KEY";

    const int CA_PRIVATE_KEY_BITS = 512;

    const int CA_PRIVATE_KEY_E = 3;
    
    static IConfigDb _db;

    static Rsa _privateKey;
    
    internal static void Init(IConfigDb configDb)
    {
        _db = configDb ?? throw new ArgumentNullException(nameof(configDb));

        var privateKeyString = _db.SettingGet<string>(CA_PRIVATE_KEY);

        if (privateKeyString == null)
        {
            using var rsaTest = new Rsa();

            rsaTest.GenerateKey(CA_PRIVATE_KEY_BITS, CA_PRIVATE_KEY_E);

            privateKeyString = rsaTest.PEMPrivateKey();

            _db.SettingSet(CA_PRIVATE_KEY, privateKeyString);
        }

        _privateKey = Rsa.FromPEMPrivateKey(privateKeyString);
    }
}
