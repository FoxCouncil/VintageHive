namespace VintageHive.Proxy.Security;

internal static class CertificateAuthority
{
    const string CA_PRIVATE_KEY = "CA_PRIVATE_KEY";

    const int CA_PRIVATE_KEY_BITS = 1024;

    static readonly BigNumber CA_PRIVATE_KEY_E = BigNumber.Rsa65537;

    static readonly X509Name CAName = new("VintageHive Dialnine Cert Authority");

    private static CryptoKey _privateKey;

    private static CryptoKey _publicKey;
    
    internal static void Init()
    {
        //var privateKeyString = Mind.Db.ConfigGet<string>(CA_PRIVATE_KEY);

        //if (privateKeyString == null)
        //{
        //    Mind.Db.ConfigSet(CA_PRIVATE_KEY, privateKeyString);
        //}

        using var rsaTest = new Rsa();

        rsaTest.GenerateKey(CA_PRIVATE_KEY_BITS, CA_PRIVATE_KEY_E);

        //var rsaTestPrivatePem = rsaTest.PEMPrivateKey();

        //var rsaTestPublicPem = rsaTest.PEMPublicKey();

        _privateKey = new CryptoKey(rsaTest);

        //_publicKey = CryptoKey.FromRSAPublicKey(rsaTestPublicPem);

        var caCert = new X509Certificate(CAName, CAName, _publicKey, DateTime.Now, DateTime.Now.AddYears(100));

        caCert.Sign(_privateKey, MessageDigest.MD5);

        var certificateString = caCert.ToPEM();
    }
}
