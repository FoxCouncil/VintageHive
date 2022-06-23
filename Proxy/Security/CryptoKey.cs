namespace VintageHive.Proxy.Security;

public class CryptoKey : NativeRef
{
    public CryptoKey() : base(Native.EVP_PKEY_new()) { }

	public override void Dispose()
	{
		Native.EVP_PKEY_free(this);
	}
}
