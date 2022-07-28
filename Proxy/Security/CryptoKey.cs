using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

public class CryptoKey : NativeRef
{
    public CryptoKey() : base(EVP_PKEY_new()) { }

	public CryptoKey(Rsa rsaKey) : this()
	{
		CheckResultSuccess(EVP_PKEY_set1_RSA(this, rsaKey));
	}

	public override void Dispose()
	{
		EVP_PKEY_free(this);
	}
}
