using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

internal class PrivateKey : NativeRef
{
    public PrivateKey() : base(EVP_PKEY_new()) { }

    public PrivateKey(Rsa rsaKey) : this()
    {
        CheckResultSuccess(EVP_PKEY_set1_RSA(this, rsaKey));
    }
}
